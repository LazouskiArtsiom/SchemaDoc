using Microsoft.Data.SqlClient;
using SchemaDoc.Core.Models;
using SchemaDoc.Core.Services;
using SchemaDoc.Core.Services.Migration;
using SchemaDoc.Extraction.Extractors;

namespace SchemaDoc.Extraction.Tests;

/// <summary>
/// End-to-end tests: extract schemas, generate a migration script,
/// apply it to a fresh copy, verify it produces the target shape.
/// </summary>
public class MigrationScriptTests
{
    private const string MasterCs = "Server=localhost;Database=master;Integrated Security=True;TrustServerCertificate=True;";
    private const string ProdCs = "Server=localhost;Database=SchemaDoc_Prod;Integrated Security=True;TrustServerCertificate=True;";
    private const string DevCs  = "Server=localhost;Database=SchemaDoc_Dev;Integrated Security=True;TrustServerCertificate=True;";
    private const string TargetDbName = "SchemaDoc_MigrationTest";

    [Fact]
    public async Task Prod_to_Dev_script_is_idempotent_and_correct()
        => await RunScriptTest(sourceIsProd: true);

    [Fact]
    public async Task Dev_to_Prod_script_drops_things_correctly()
        => await RunScriptTest(sourceIsProd: false);

    private async Task RunScriptTest(bool sourceIsProd)
    {
        // 1. Create fresh copy of source schema
        if (sourceIsProd) await RecreateTargetFromProd();
        else await RecreateTargetFromDev();

        // 2. Extract source (baseline) and dest (target)
        var extractor = new SqlServerExtractor();
        var prod = await extractor.ExtractAsync(ProdCs);
        var dev = await extractor.ExtractAsync(DevCs);
        var source = sourceIsProd ? prod : dev;
        var dest = sourceIsProd ? dev : prod;
        var sourceLabel = sourceIsProd ? "Prod" : "Dev";
        var destLabel = sourceIsProd ? "Dev" : "Prod";

        // 3. Compute diff and generate migration script
        var diffSvc = new SchemaDiffService();
        var diff = diffSvc.Compare(source, dest);

        var dialect = MigrationScriptGenerator.DialectFor(DatabaseProvider.SqlServer);
        var actions = MigrationScriptGenerator.BuildActions(source, dest, diff, dialect);
        var script = MigrationScriptGenerator.AssembleScript(actions, dialect, sourceLabel, destLabel);

        // Print for visibility in test output
        Console.WriteLine($"=== Generated Script ({actions.Count} actions) ===");
        Console.WriteLine(script);

        // 4. Execute against the fresh target DB
        var targetCs = $"Server=localhost;Database={TargetDbName};Integrated Security=True;TrustServerCertificate=True;";
        var (success, errorMsg) = await TryExecuteScript(targetCs, script);
        Assert.True(success, $"Script execution failed:\n{errorMsg}\n\n--- Script ---\n{script}");

        // 5. Re-extract target and compare with destination — should be identical
        var targetAfter = await extractor.ExtractAsync(targetCs);
        var residualDiff = diffSvc.Compare(targetAfter, dest);

        // Assert no remaining diffs for tables we care about
        var failures = new List<string>();
        if (residualDiff.AddedTables.Count > 0)
            failures.Add($"Still missing {residualDiff.AddedTables.Count} tables: {string.Join(", ", residualDiff.AddedTables.Select(t => t.FullName))}");
        if (residualDiff.RemovedTables.Count > 0)
            failures.Add($"Extra {residualDiff.RemovedTables.Count} tables: {string.Join(", ", residualDiff.RemovedTables.Select(t => t.FullName))}");
        foreach (var td in residualDiff.ModifiedTables)
            failures.Add($"Table {td.FullName}: {td.TotalChanges} changes still remain: " +
                string.Join("; ", EnumerateChanges(td)));

        Assert.True(failures.Count == 0,
            $"After applying migration script ({sourceLabel} → {destLabel}), target does not match {destLabel}:\n" +
            string.Join("\n", failures) + "\n\n--- Script ---\n" + script);
    }

    private static async Task RecreateTargetFromDev()
    {
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
        var devSchema = await extractor.ExtractAsync(DevCs);
        var dialect = MigrationScriptGenerator.DialectFor(DatabaseProvider.SqlServer);

        var setupSb = new System.Text.StringBuilder();
        foreach (var sch in devSchema.Tables.Select(t => t.Schema).Distinct().Where(s => s != "dbo"))
        {
            setupSb.AppendLine($"IF SCHEMA_ID('{sch}') IS NULL EXEC('CREATE SCHEMA [{sch}]');");
            setupSb.AppendLine("GO");
        }

        var fksByTable = SchemaDiffService.GroupForeignKeys(devSchema.ForeignKeys);
        var sb = new System.Text.StringBuilder();
        foreach (var t in devSchema.Tables)
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

        foreach (var fk in devSchema.ForeignKeys
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

        // Triggers
        if (devSchema.Triggers is not null)
            foreach (var trg in devSchema.Triggers)
            {
                sb.AppendLine(dialect.CreateTrigger(trg));
                sb.AppendLine("GO");
            }

        var targetCs = $"Server=localhost;Database={TargetDbName};Integrated Security=True;TrustServerCertificate=True;";
        var (ok, err) = await TryExecuteScript(targetCs, setupSb.ToString() + sb.ToString());
        Assert.True(ok, $"Failed to set up target DB from Dev schema:\n{err}");
    }

    private static IEnumerable<string> EnumerateChanges(TableDiff td)
    {
        foreach (var c in td.AddedColumns) yield return $"column+{c.Name}";
        foreach (var c in td.RemovedColumns) yield return $"column-{c.Name}";
        foreach (var c in td.ModifiedColumns) yield return $"column~{c.ColumnName}[{string.Join(",",c.Changes)}]";
        if (td.PrimaryKeyChanges != null) foreach (var c in td.PrimaryKeyChanges) yield return $"PK: {c}";
        if (td.AddedIndexes != null) foreach (var i in td.AddedIndexes) yield return $"index+{i.Name}";
        if (td.RemovedIndexes != null) foreach (var i in td.RemovedIndexes) yield return $"index-{i.Name}";
        if (td.ModifiedIndexes != null) foreach (var i in td.ModifiedIndexes) yield return $"index~{i.Name}[{string.Join(",",i.Changes)}]";
        if (td.AddedUniqueConstraints != null) foreach (var i in td.AddedUniqueConstraints) yield return $"uq+{i.Name}";
        if (td.RemovedUniqueConstraints != null) foreach (var i in td.RemovedUniqueConstraints) yield return $"uq-{i.Name}";
        if (td.AddedCheckConstraints != null) foreach (var i in td.AddedCheckConstraints) yield return $"check+{i.Name}";
        if (td.RemovedCheckConstraints != null) foreach (var i in td.RemovedCheckConstraints) yield return $"check-{i.Name}";
        if (td.ModifiedCheckConstraints != null) foreach (var i in td.ModifiedCheckConstraints) yield return $"check~{i.Name}";
        if (td.AddedForeignKeys != null) foreach (var i in td.AddedForeignKeys) yield return $"fk+{i.ConstraintName}";
        if (td.RemovedForeignKeys != null) foreach (var i in td.RemovedForeignKeys) yield return $"fk-{i.ConstraintName}";
        if (td.ModifiedForeignKeys != null) foreach (var i in td.ModifiedForeignKeys) yield return $"fk~{i.Name}";
    }

    private static async Task RecreateTargetFromProd()
    {
        // Drop and recreate the target DB, then copy the Prod schema using DDL
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

        // Build a script from Prod schema using MigrationScriptGenerator
        var extractor = new SqlServerExtractor();
        var prodSchema = await extractor.ExtractAsync(ProdCs);
        var dialect = MigrationScriptGenerator.DialectFor(DatabaseProvider.SqlServer);

        // Create all Prod tables + their FKs in the fresh target DB
        var sb = new System.Text.StringBuilder();
        var fksByTable = SchemaDiffService.GroupForeignKeys(prodSchema.ForeignKeys);

        foreach (var t in prodSchema.Tables)
        {
            var tableFks = fksByTable.TryGetValue(t.FullName, out var fks) ? fks : new List<ForeignKeyGroup>();
            sb.AppendLine(dialect.CreateTable(t, tableFks));
            sb.AppendLine("GO");

            // Also create indexes (not inline in CREATE TABLE)
            if (t.Indexes is not null)
                foreach (var ix in t.Indexes)
                {
                    sb.AppendLine(dialect.CreateIndex(t, ix));
                    sb.AppendLine("GO");
                }
        }

        // Create schemas first if needed
        var schemas = prodSchema.Tables.Select(t => t.Schema).Distinct().Where(s => s != "dbo").ToList();
        var setupSb = new System.Text.StringBuilder();
        foreach (var sch in schemas)
        {
            setupSb.AppendLine($"IF SCHEMA_ID('{sch}') IS NULL EXEC('CREATE SCHEMA [{sch}]');");
            setupSb.AppendLine("GO");
        }

        // Apply all FKs after all tables exist
        foreach (var fk in prodSchema.ForeignKeys
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

        // Triggers
        if (prodSchema.Triggers is not null)
            foreach (var trg in prodSchema.Triggers)
            {
                sb.AppendLine(dialect.CreateTrigger(trg));
                sb.AppendLine("GO");
            }

        var fullSetup = setupSb.ToString() + sb.ToString();

        var targetCs = $"Server=localhost;Database={TargetDbName};Integrated Security=True;TrustServerCertificate=True;";
        var (ok, err) = await TryExecuteScript(targetCs, fullSetup);
        Assert.True(ok, $"Failed to set up target DB from Prod schema:\n{err}\n\n{fullSetup}");
    }

    private static async Task<(bool, string?)> TryExecuteScript(string connectionString, string script)
    {
        // Split on "GO" (SQL Server batch separator) — a simple line-based split is fine here
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
