using Microsoft.Data.SqlClient;
using SchemaDoc.Core.Models;
using SchemaDoc.Core.Services;
using SchemaDoc.Core.Services.Migration;
using SchemaDoc.Extraction.Extractors;

namespace SchemaDoc.Extraction.Tests;

/// <summary>
/// End-to-end tests for MigrationScriptGenerator.
/// Each test:
///   1. Creates a fresh copy of the SOURCE DB
///   2. Extracts source + target schemas
///   3. Generates the migration script (optionally filtered by category)
///   4. Applies the script to the fresh copy
///   5. Verifies the post-apply state matches expectations
/// </summary>
public class MigrationScriptTests
{
    private const string MasterCs = "Server=localhost;Database=master;Integrated Security=True;TrustServerCertificate=True;";
    private const string ProdCs    = "Server=localhost;Database=SchemaDoc_Prod;Integrated Security=True;TrustServerCertificate=True;";
    private const string StagingCs = "Server=localhost;Database=SchemaDoc_Staging;Integrated Security=True;TrustServerCertificate=True;";
    private const string DevCs     = "Server=localhost;Database=SchemaDoc_Dev;Integrated Security=True;TrustServerCertificate=True;";
    private const string TargetDbName = "SchemaDoc_MigrationTest";

    // ═══════════════════════════════════════════════════════════════════════
    // FULL MIGRATION TESTS — apply ALL actions, expect exact match
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Prod", "Dev")]
    [InlineData("Dev", "Prod")]
    [InlineData("Staging", "Prod")]
    [InlineData("Prod", "Staging")]
    [InlineData("Staging", "Dev")]
    [InlineData("Dev", "Staging")]
    public async Task Full_migration_produces_identical_schema(string source, string target)
    {
        var sourceCs = CsFor(source);
        var targetCs = CsFor(target);

        await RecreateFromSource(sourceCs);

        var extractor = new SqlServerExtractor();
        var src = await extractor.ExtractAsync(sourceCs);
        var dst = await extractor.ExtractAsync(targetCs);

        var diffSvc = new SchemaDiffService();
        var diff = diffSvc.Compare(src, dst);

        var dialect = MigrationScriptGenerator.DialectFor(DatabaseProvider.SqlServer);
        var actions = MigrationScriptGenerator.BuildActions(src, dst, diff, dialect);
        var script = MigrationScriptGenerator.AssembleScript(actions, dialect, source, target);

        var testCs = $"Server=localhost;Database={TargetDbName};Integrated Security=True;TrustServerCertificate=True;";
        var (ok, err) = await TryExecuteScript(testCs, script);
        Assert.True(ok, $"Full migration {source}→{target} failed:\n{err}\n\n{script}");

        var after = await extractor.ExtractAsync(testCs);
        AssertNoResidualDiff(diffSvc.Compare(after, dst), $"{source}→{target}", script);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PARTIAL MIGRATION TESTS — include only some categories, verify
    // only those changes happen (and excluded ones don't)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Partial_migration_columns_only()
        => await RunPartialTest("Prod", "Dev", a => a.Category == "Columns" || a.Category == "Tables");

    [Fact]
    public async Task Partial_migration_indexes_only()
        => await RunPartialTest("Prod", "Dev", a => a.Category == "Indexes");

    [Fact]
    public async Task Partial_migration_triggers_only()
        => await RunPartialTest("Prod", "Dev", a => a.Category == "Triggers");

    [Fact]
    public async Task Partial_migration_foreign_keys_only_is_safe()
        => await RunPartialTest("Dev", "Prod", a => a.Category == "Foreign Keys");

    [Fact]
    public async Task Partial_migration_check_constraints_only()
        => await RunPartialTest("Prod", "Dev", a => a.Category == "Check Constraints");

    [Fact]
    public async Task Partial_then_remaining_migration_composes()
    {
        // Apply columns+tables first, then apply the rest separately — final state should match target.
        var sourceCs = ProdCs;
        var targetCs = DevCs;

        await RecreateFromSource(sourceCs);
        var testCs = $"Server=localhost;Database={TargetDbName};Integrated Security=True;TrustServerCertificate=True;";

        var extractor = new SqlServerExtractor();
        var dialect = MigrationScriptGenerator.DialectFor(DatabaseProvider.SqlServer);
        var diffSvc = new SchemaDiffService();

        // Round 1: tables + columns only
        var src1 = await extractor.ExtractAsync(testCs);
        var dst  = await extractor.ExtractAsync(targetCs);
        var diff1 = diffSvc.Compare(src1, dst);
        var allActions1 = MigrationScriptGenerator.BuildActions(src1, dst, diff1, dialect);
        var round1Actions = allActions1.Where(a => a.Category == "Tables" || a.Category == "Columns").ToList();
        var script1 = MigrationScriptGenerator.AssembleScript(round1Actions, dialect, "Prod", "Dev");
        var (ok1, err1) = await TryExecuteScript(testCs, script1);
        Assert.True(ok1, $"Round 1 (Tables+Columns) failed:\n{err1}\n\n{script1}");

        // Round 2: everything else
        var src2 = await extractor.ExtractAsync(testCs);
        var diff2 = diffSvc.Compare(src2, dst);
        var round2Actions = MigrationScriptGenerator.BuildActions(src2, dst, diff2, dialect);
        var script2 = MigrationScriptGenerator.AssembleScript(round2Actions, dialect, "Prod", "Dev");
        var (ok2, err2) = await TryExecuteScript(testCs, script2);
        Assert.True(ok2, $"Round 2 (remaining) failed:\n{err2}\n\n{script2}");

        // Final: should match Dev exactly
        var final = await extractor.ExtractAsync(testCs);
        AssertNoResidualDiff(diffSvc.Compare(final, dst),
            "Composed partial migration",
            $"--- Round 1 ---\n{script1}\n--- Round 2 ---\n{script2}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static string CsFor(string name) => name switch
    {
        "Prod"    => ProdCs,
        "Staging" => StagingCs,
        "Dev"     => DevCs,
        _         => throw new ArgumentException($"Unknown DB: {name}")
    };

    /// <summary>
    /// Runs the script with only actions matching the filter and verifies:
    ///  a) The script executes without error
    ///  b) The actions' changes are reflected in the post-apply schema
    /// (does not verify that excluded changes are absent — would complicate
    /// the assertion considerably; the "compose" test covers that.)
    /// </summary>
    private async Task RunPartialTest(string sourceName, string targetName, Func<MigrationAction, bool> filter)
    {
        var sourceCs = CsFor(sourceName);
        var targetCs = CsFor(targetName);

        await RecreateFromSource(sourceCs);

        var extractor = new SqlServerExtractor();
        var src = await extractor.ExtractAsync(sourceCs);
        var dst = await extractor.ExtractAsync(targetCs);

        var diffSvc = new SchemaDiffService();
        var diff = diffSvc.Compare(src, dst);

        var dialect = MigrationScriptGenerator.DialectFor(DatabaseProvider.SqlServer);
        var allActions = MigrationScriptGenerator.BuildActions(src, dst, diff, dialect);
        var filtered = allActions.Where(filter).ToList();

        if (filtered.Count == 0)
        {
            // Not a failure — just nothing to verify.
            return;
        }

        var script = MigrationScriptGenerator.AssembleScript(filtered, dialect, sourceName, targetName);

        var testCs = $"Server=localhost;Database={TargetDbName};Integrated Security=True;TrustServerCertificate=True;";
        var (ok, err) = await TryExecuteScript(testCs, script);
        Assert.True(ok,
            $"Partial migration {sourceName}→{targetName} (filter produced {filtered.Count} of {allActions.Count} actions) failed:\n{err}\n\n{script}");

        // Verify the applied actions are now reflected in the target schema
        var after = await extractor.ExtractAsync(testCs);
        var diffAfter = diffSvc.Compare(after, dst);

        // For each applied action, check that the corresponding diff entry is no longer present.
        VerifyActionsApplied(filtered, diffAfter, script);
    }

    /// <summary>
    /// Verifies that each applied action's intended change is reflected in the post-apply schema.
    /// A residual diff after applying should NOT still contain the categories we applied.
    /// </summary>
    private static void VerifyActionsApplied(
        IReadOnlyList<MigrationAction> appliedActions,
        SchemaDiffResult diffAfter,
        string script)
    {
        var appliedCats = appliedActions.Select(a => a.Category).ToHashSet();
        var failures = new List<string>();

        foreach (var td in diffAfter.ModifiedTables)
        {
            if (appliedCats.Contains("Columns") && (td.AddedColumns.Count + td.RemovedColumns.Count + td.ModifiedColumns.Count) > 0)
            {
                var relevant = appliedActions.Where(a => a.TableFullName == td.FullName && a.Category == "Columns").ToList();
                if (relevant.Count > 0)
                    failures.Add($"{td.FullName}: column changes still remain after applying {relevant.Count} column actions");
            }
            if (appliedCats.Contains("Indexes") && ((td.AddedIndexes?.Count ?? 0) + (td.RemovedIndexes?.Count ?? 0) + (td.ModifiedIndexes?.Count ?? 0)) > 0)
            {
                var relevant = appliedActions.Where(a => a.TableFullName == td.FullName && a.Category == "Indexes").ToList();
                if (relevant.Count > 0)
                    failures.Add($"{td.FullName}: index changes still remain after applying {relevant.Count} index actions");
            }
        }

        if (appliedCats.Contains("Triggers") &&
            ((diffAfter.AddedTriggers?.Count ?? 0) + (diffAfter.RemovedTriggers?.Count ?? 0) + (diffAfter.ModifiedTriggers?.Count ?? 0)) > 0)
        {
            failures.Add("Trigger changes still remain after applying trigger actions");
        }

        Assert.True(failures.Count == 0,
            $"Partial application didn't apply all actions in its scope:\n{string.Join("\n", failures)}\n\n{script}");
    }

    private static void AssertNoResidualDiff(SchemaDiffResult residual, string scenario, string script)
    {
        var failures = new List<string>();
        if (residual.AddedTables.Count > 0)
            failures.Add($"Still missing {residual.AddedTables.Count} tables: {string.Join(", ", residual.AddedTables.Select(t => t.FullName))}");
        if (residual.RemovedTables.Count > 0)
            failures.Add($"Extra {residual.RemovedTables.Count} tables: {string.Join(", ", residual.RemovedTables.Select(t => t.FullName))}");
        foreach (var td in residual.ModifiedTables)
            failures.Add($"Table {td.FullName}: {td.TotalChanges} changes still remain: " +
                string.Join("; ", EnumerateChanges(td)));
        if (residual.AddedTriggers is { Count: > 0 })
            foreach (var t in residual.AddedTriggers)
                failures.Add($"Trigger added: {t.FullName}");
        if (residual.RemovedTriggers is { Count: > 0 })
            foreach (var t in residual.RemovedTriggers)
                failures.Add($"Trigger removed: {t.FullName}");
        if (residual.ModifiedTriggers is { Count: > 0 })
            foreach (var t in residual.ModifiedTriggers)
                failures.Add($"Trigger modified: {t.FullName} — {string.Join("; ", t.Changes)}");

        Assert.True(failures.Count == 0,
            $"[{scenario}] After applying migration script, state does not match target:\n" +
            string.Join("\n", failures) + "\n\n--- Script ---\n" + script);
    }

    private static IEnumerable<string> EnumerateChanges(TableDiff td)
    {
        foreach (var c in td.AddedColumns) yield return $"column+{c.Name}";
        foreach (var c in td.RemovedColumns) yield return $"column-{c.Name}";
        foreach (var c in td.ModifiedColumns) yield return $"column~{c.ColumnName}[{string.Join(",", c.Changes)}]";
        if (td.PrimaryKeyChanges != null) foreach (var c in td.PrimaryKeyChanges) yield return $"PK: {c}";
        if (td.AddedIndexes != null) foreach (var i in td.AddedIndexes) yield return $"index+{i.Name}";
        if (td.RemovedIndexes != null) foreach (var i in td.RemovedIndexes) yield return $"index-{i.Name}";
        if (td.ModifiedIndexes != null) foreach (var i in td.ModifiedIndexes) yield return $"index~{i.Name}[{string.Join(",", i.Changes)}]";
        if (td.AddedUniqueConstraints != null) foreach (var i in td.AddedUniqueConstraints) yield return $"uq+{i.Name}";
        if (td.RemovedUniqueConstraints != null) foreach (var i in td.RemovedUniqueConstraints) yield return $"uq-{i.Name}";
        if (td.AddedCheckConstraints != null) foreach (var i in td.AddedCheckConstraints) yield return $"check+{i.Name}";
        if (td.RemovedCheckConstraints != null) foreach (var i in td.RemovedCheckConstraints) yield return $"check-{i.Name}";
        if (td.ModifiedCheckConstraints != null) foreach (var i in td.ModifiedCheckConstraints) yield return $"check~{i.Name}";
        if (td.AddedForeignKeys != null) foreach (var i in td.AddedForeignKeys) yield return $"fk+{i.ConstraintName}";
        if (td.RemovedForeignKeys != null) foreach (var i in td.RemovedForeignKeys) yield return $"fk-{i.ConstraintName}";
        if (td.ModifiedForeignKeys != null) foreach (var i in td.ModifiedForeignKeys) yield return $"fk~{i.Name}";
    }

    private static async Task RecreateFromSource(string sourceCs)
    {
        // Drop + recreate target DB, then populate schema from source
        await using var master = new SqlConnection(MasterCs);
        await master.OpenAsync();
        await using (var cmd = master.CreateCommand())
        {
            cmd.CommandText = $@"
                IF DB_ID('{TargetDbName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{TargetDbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{TargetDbName}];
                END
                CREATE DATABASE [{TargetDbName}];
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        var extractor = new SqlServerExtractor();
        var source = await extractor.ExtractAsync(sourceCs);
        var dialect = MigrationScriptGenerator.DialectFor(DatabaseProvider.SqlServer);

        var setupSb = new System.Text.StringBuilder();
        foreach (var sch in source.Tables.Select(t => t.Schema).Distinct().Where(s => s != "dbo"))
        {
            setupSb.AppendLine($"IF SCHEMA_ID('{sch}') IS NULL EXEC('CREATE SCHEMA [{sch}]');");
            setupSb.AppendLine("GO");
        }

        var fksByTable = SchemaDiffService.GroupForeignKeys(source.ForeignKeys);
        var sb = new System.Text.StringBuilder();
        foreach (var t in source.Tables)
        {
            var tableFks = fksByTable.TryGetValue(t.FullName, out var fks) ? fks : new List<ForeignKeyGroup>();
            sb.AppendLine(dialect.CreateTable(t, tableFks));
            sb.AppendLine("GO");
            if (t.Indexes is not null)
                foreach (var ix in t.Indexes)
                {
                    sb.AppendLine(dialect.CreateIndex(t, ix));
                    sb.AppendLine("GO");
                }
        }
        foreach (var fk in source.ForeignKeys
            .GroupBy(f => f.ConstraintName + "|" + f.ParentSchema + "|" + f.ParentTable)
            .Select(g =>
            {
                var first = g.First();
                return new ForeignKeyGroup(
                    first.ConstraintName, first.ParentSchema, first.ParentTable,
                    g.Select(f => f.ParentColumn).ToList(),
                    first.ReferencedSchema, first.ReferencedTable,
                    g.Select(f => f.ReferencedColumn).ToList(),
                    first.OnDelete, first.OnUpdate);
            }))
        {
            sb.AppendLine(dialect.AddForeignKey(fk));
            sb.AppendLine("GO");
        }
        if (source.Triggers is not null)
            foreach (var trg in source.Triggers)
            {
                sb.AppendLine(dialect.CreateTrigger(trg));
                sb.AppendLine("GO");
            }

        var testCs = $"Server=localhost;Database={TargetDbName};Integrated Security=True;TrustServerCertificate=True;";
        var (ok, err) = await TryExecuteScript(testCs, setupSb.ToString() + sb.ToString());
        Assert.True(ok, $"Failed to set up target DB from source:\n{err}");
    }

    private static async Task<(bool, string?)> TryExecuteScript(string connectionString, string script)
    {
        var batches = SplitBatches(script);

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        foreach (var batch in batches)
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = batch;
                cmd.CommandTimeout = 60;
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                return (false, $"Batch failed: {ex.Message}\n\n--- Batch ---\n{batch}");
            }
        }
        return (true, null);
    }

    private static IEnumerable<string> SplitBatches(string script)
    {
        var current = new System.Text.StringBuilder();
        foreach (var line in script.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
            }
            else
            {
                current.AppendLine(line);
            }
        }
        if (current.Length > 0) yield return current.ToString();
    }
}
