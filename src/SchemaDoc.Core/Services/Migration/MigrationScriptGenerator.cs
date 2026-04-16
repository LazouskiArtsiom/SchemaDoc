using System.Text;
using SchemaDoc.Core.Models;

namespace SchemaDoc.Core.Services.Migration;

/// <summary>
/// Converts a SchemaDiffResult into a list of individually togglable MigrationActions,
/// then assembles them into a final SQL script.
///
/// Direction semantics:
///   - baseline = source schema (the one you're running the script AGAINST)
///   - target   = the schema you want baseline to look like
/// </summary>
public class MigrationScriptGenerator
{
    /// <summary>
    /// Build all migration actions needed to transform <paramref name="baseline"/> into <paramref name="target"/>.
    /// Callers can filter the returned list (e.g. remove actions via checkbox) and pass to <see cref="AssembleScript"/>.
    /// </summary>
    public static List<MigrationAction> BuildActions(
        DatabaseSchema baseline,
        DatabaseSchema target,
        SchemaDiffResult diff,
        ISqlDialect dialect)
    {
        var actions = new List<MigrationAction>();
        var targetFksByTable = SchemaDiffService.GroupForeignKeys(target.ForeignKeys);
        var baselineFksByTable = SchemaDiffService.GroupForeignKeys(baseline.ForeignKeys);

        // ─── REMOVED TABLES ───────────────────────────────────────
        // Dropping a table: drop all its FKs from other tables first (handled generically by drop-first ordering)
        foreach (var t in diff.RemovedTables)
        {
            // Drop FKs pointing TO this table first (they'll be regenerated after if needed)
            foreach (var pair in baselineFksByTable.Values.SelectMany(v => v))
            {
                if (pair.ReferencedSchema == t.Schema && pair.ReferencedTable == t.Name)
                {
                    actions.Add(new MigrationAction(
                        $"fk:drop:{pair.ParentSchema}.{pair.ParentTable}:{pair.ConstraintName}:for-removed-{t.FullName}",
                        MigrationActionType.DropForeignKey,
                        "Foreign Keys",
                        $"{pair.ParentSchema}.{pair.ParentTable}",
                        $"Drop FK {pair.ConstraintName} (references dropped table {t.FullName})",
                        dialect.DropForeignKey(pair)));
                }
            }
            actions.Add(new MigrationAction(
                $"table:drop:{t.FullName}",
                MigrationActionType.DropTable,
                "Tables",
                t.FullName,
                $"Drop table {t.FullName}",
                dialect.DropTable(t)));
        }

        // ─── ADDED TABLES ─────────────────────────────────────────
        foreach (var t in diff.AddedTables)
        {
            var tableFks = targetFksByTable.TryGetValue(t.FullName, out var fks) ? fks : new List<ForeignKeyGroup>();
            actions.Add(new MigrationAction(
                $"table:add:{t.FullName}",
                MigrationActionType.CreateTable,
                "Tables",
                t.FullName,
                $"Create table {t.FullName} ({t.Columns.Count} columns)",
                dialect.CreateTable(t, tableFks)));

            // Add FKs for the new table (after all tables are created)
            foreach (var fk in tableFks)
            {
                actions.Add(new MigrationAction(
                    $"fk:add:{fk.ParentSchema}.{fk.ParentTable}:{fk.ConstraintName}",
                    MigrationActionType.AddForeignKey,
                    "Foreign Keys",
                    $"{fk.ParentSchema}.{fk.ParentTable}",
                    $"Add FK {fk.ConstraintName} → {fk.ReferencedSchema}.{fk.ReferencedTable}",
                    dialect.AddForeignKey(fk)));
            }
        }

        // ─── MODIFIED TABLES ──────────────────────────────────────
        foreach (var td in diff.ModifiedTables)
        {
            var baseTable = baseline.Tables.First(tt => tt.FullName == td.FullName);
            var curTable = target.Tables.First(tt => tt.FullName == td.FullName);

            // FK drops (always before any structural change)
            if (td.RemovedForeignKeys is not null)
                foreach (var fk in td.RemovedForeignKeys)
                    actions.Add(new MigrationAction(
                        $"fk:drop:{td.FullName}:{fk.ConstraintName}",
                        MigrationActionType.DropForeignKey,
                        "Foreign Keys",
                        td.FullName,
                        $"Drop FK {fk.ConstraintName}",
                        dialect.DropForeignKey(fk)));

            if (td.ModifiedForeignKeys is not null)
                foreach (var fkDiff in td.ModifiedForeignKeys)
                {
                    // Drop + readd
                    var oldFk = baselineFksByTable[td.FullName].First(f => f.ConstraintName.Equals(fkDiff.Name, StringComparison.OrdinalIgnoreCase));
                    var newFk = targetFksByTable[td.FullName].First(f => f.ConstraintName.Equals(fkDiff.Name, StringComparison.OrdinalIgnoreCase));
                    actions.Add(new MigrationAction(
                        $"fk:alter:{td.FullName}:{fkDiff.Name}:drop",
                        MigrationActionType.DropForeignKey,
                        "Foreign Keys",
                        td.FullName,
                        $"Recreate FK {fkDiff.Name} (drop step)",
                        dialect.DropForeignKey(oldFk)));
                    actions.Add(new MigrationAction(
                        $"fk:alter:{td.FullName}:{fkDiff.Name}:add",
                        MigrationActionType.AddForeignKey,
                        "Foreign Keys",
                        td.FullName,
                        $"Recreate FK {fkDiff.Name} (add step): {string.Join("; ", fkDiff.Changes)}",
                        dialect.AddForeignKey(newFk)));
                }

            if (td.AddedForeignKeys is not null)
                foreach (var fk in td.AddedForeignKeys)
                    actions.Add(new MigrationAction(
                        $"fk:add:{td.FullName}:{fk.ConstraintName}",
                        MigrationActionType.AddForeignKey,
                        "Foreign Keys",
                        td.FullName,
                        $"Add FK {fk.ConstraintName} → {fk.ReferencedSchema}.{fk.ReferencedTable}",
                        dialect.AddForeignKey(fk)));

            // Index drops
            if (td.RemovedIndexes is not null)
                foreach (var ix in td.RemovedIndexes)
                    actions.Add(new MigrationAction(
                        $"index:drop:{td.FullName}:{ix.Name}",
                        MigrationActionType.DropIndex,
                        "Indexes",
                        td.FullName,
                        $"Drop index {ix.Name}",
                        dialect.DropIndex(baseTable, ix)));

            if (td.ModifiedIndexes is not null)
                foreach (var ixDiff in td.ModifiedIndexes)
                {
                    var oldIx = baseTable.Indexes!.First(i => i.Name.Equals(ixDiff.Name, StringComparison.OrdinalIgnoreCase));
                    var newIx = curTable.Indexes!.First(i => i.Name.Equals(ixDiff.Name, StringComparison.OrdinalIgnoreCase));
                    actions.Add(new MigrationAction(
                        $"index:alter:{td.FullName}:{ixDiff.Name}:drop",
                        MigrationActionType.DropIndex,
                        "Indexes",
                        td.FullName,
                        $"Recreate index {ixDiff.Name} (drop step)",
                        dialect.DropIndex(baseTable, oldIx)));
                    actions.Add(new MigrationAction(
                        $"index:alter:{td.FullName}:{ixDiff.Name}:add",
                        MigrationActionType.CreateIndex,
                        "Indexes",
                        td.FullName,
                        $"Recreate index {ixDiff.Name} (add step): {string.Join("; ", ixDiff.Changes)}",
                        dialect.CreateIndex(curTable, newIx)));
                }

            if (td.AddedIndexes is not null)
                foreach (var ix in td.AddedIndexes)
                    actions.Add(new MigrationAction(
                        $"index:add:{td.FullName}:{ix.Name}",
                        MigrationActionType.CreateIndex,
                        "Indexes",
                        td.FullName,
                        $"Create index {ix.Name}",
                        dialect.CreateIndex(curTable, ix)));

            // Check constraint changes
            if (td.RemovedCheckConstraints is not null)
                foreach (var ck in td.RemovedCheckConstraints)
                    actions.Add(new MigrationAction(
                        $"check:drop:{td.FullName}:{ck.Name}",
                        MigrationActionType.DropCheckConstraint,
                        "Check Constraints",
                        td.FullName,
                        $"Drop check {ck.Name}",
                        dialect.DropCheckConstraint(baseTable, ck)));

            if (td.ModifiedCheckConstraints is not null)
                foreach (var ckDiff in td.ModifiedCheckConstraints)
                {
                    var oldCk = baseTable.CheckConstraints!.First(c => c.Name.Equals(ckDiff.Name, StringComparison.OrdinalIgnoreCase));
                    var newCk = curTable.CheckConstraints!.First(c => c.Name.Equals(ckDiff.Name, StringComparison.OrdinalIgnoreCase));
                    actions.Add(new MigrationAction(
                        $"check:alter:{td.FullName}:{ckDiff.Name}:drop",
                        MigrationActionType.DropCheckConstraint,
                        "Check Constraints",
                        td.FullName,
                        $"Recreate check {ckDiff.Name} (drop)",
                        dialect.DropCheckConstraint(baseTable, oldCk)));
                    actions.Add(new MigrationAction(
                        $"check:alter:{td.FullName}:{ckDiff.Name}:add",
                        MigrationActionType.AddCheckConstraint,
                        "Check Constraints",
                        td.FullName,
                        $"Recreate check {ckDiff.Name} (add)",
                        dialect.AddCheckConstraint(curTable, newCk)));
                }

            if (td.AddedCheckConstraints is not null)
                foreach (var ck in td.AddedCheckConstraints)
                    actions.Add(new MigrationAction(
                        $"check:add:{td.FullName}:{ck.Name}",
                        MigrationActionType.AddCheckConstraint,
                        "Check Constraints",
                        td.FullName,
                        $"Add check {ck.Name}",
                        dialect.AddCheckConstraint(curTable, ck)));

            // Unique constraint changes
            if (td.RemovedUniqueConstraints is not null)
                foreach (var uq in td.RemovedUniqueConstraints)
                    actions.Add(new MigrationAction(
                        $"unique:drop:{td.FullName}:{uq.Name}",
                        MigrationActionType.DropUniqueConstraint,
                        "Unique Constraints",
                        td.FullName,
                        $"Drop unique {uq.Name}",
                        dialect.DropUniqueConstraint(baseTable, uq)));

            if (td.ModifiedUniqueConstraints is not null)
                foreach (var uqDiff in td.ModifiedUniqueConstraints)
                {
                    var oldUq = baseTable.UniqueConstraints!.First(u => u.Name.Equals(uqDiff.Name, StringComparison.OrdinalIgnoreCase));
                    var newUq = curTable.UniqueConstraints!.First(u => u.Name.Equals(uqDiff.Name, StringComparison.OrdinalIgnoreCase));
                    actions.Add(new MigrationAction(
                        $"unique:alter:{td.FullName}:{uqDiff.Name}:drop",
                        MigrationActionType.DropUniqueConstraint,
                        "Unique Constraints",
                        td.FullName,
                        $"Recreate unique {uqDiff.Name} (drop)",
                        dialect.DropUniqueConstraint(baseTable, oldUq)));
                    actions.Add(new MigrationAction(
                        $"unique:alter:{td.FullName}:{uqDiff.Name}:add",
                        MigrationActionType.AddUniqueConstraint,
                        "Unique Constraints",
                        td.FullName,
                        $"Recreate unique {uqDiff.Name} (add)",
                        dialect.AddUniqueConstraint(curTable, newUq)));
                }

            if (td.AddedUniqueConstraints is not null)
                foreach (var uq in td.AddedUniqueConstraints)
                    actions.Add(new MigrationAction(
                        $"unique:add:{td.FullName}:{uq.Name}",
                        MigrationActionType.AddUniqueConstraint,
                        "Unique Constraints",
                        td.FullName,
                        $"Add unique {uq.Name}",
                        dialect.AddUniqueConstraint(curTable, uq)));

            // Primary Key (if changed, drop + add)
            if (td.PrimaryKeyChanges is not null && td.PrimaryKeyChanges.Count > 0)
            {
                if (baseTable.PrimaryKey is not null)
                    actions.Add(new MigrationAction(
                        $"pk:drop:{td.FullName}",
                        MigrationActionType.DropPrimaryKey,
                        "Primary Key",
                        td.FullName,
                        $"Drop primary key {baseTable.PrimaryKey.Name}",
                        dialect.DropPrimaryKey(baseTable, baseTable.PrimaryKey)));
                if (curTable.PrimaryKey is not null)
                    actions.Add(new MigrationAction(
                        $"pk:add:{td.FullName}",
                        MigrationActionType.AddPrimaryKey,
                        "Primary Key",
                        td.FullName,
                        $"Add primary key {curTable.PrimaryKey.Name} on ({string.Join(", ", curTable.PrimaryKey.Columns)})",
                        dialect.AddPrimaryKey(curTable, curTable.PrimaryKey)));
            }

            // Column changes
            foreach (var col in td.RemovedColumns)
                actions.Add(new MigrationAction(
                    $"column:drop:{td.FullName}:{col.Name}",
                    MigrationActionType.DropColumn,
                    "Columns",
                    td.FullName,
                    $"Drop column {col.Name}",
                    dialect.DropColumn(baseTable, col)));

            foreach (var col in td.AddedColumns)
                actions.Add(new MigrationAction(
                    $"column:add:{td.FullName}:{col.Name}",
                    MigrationActionType.AddColumn,
                    "Columns",
                    td.FullName,
                    $"Add column {col.Name} ({col.DataType})",
                    dialect.AddColumn(curTable, col)));

            foreach (var colDiff in td.ModifiedColumns)
            {
                var oldCol = baseTable.Columns.First(c => c.Name.Equals(colDiff.ColumnName, StringComparison.OrdinalIgnoreCase));
                var newCol = curTable.Columns.First(c => c.Name.Equals(colDiff.ColumnName, StringComparison.OrdinalIgnoreCase));
                actions.Add(new MigrationAction(
                    $"column:alter:{td.FullName}:{colDiff.ColumnName}",
                    MigrationActionType.AlterColumn,
                    "Columns",
                    td.FullName,
                    $"Alter column {colDiff.ColumnName}: {string.Join("; ", colDiff.Changes)}",
                    dialect.AlterColumn(curTable, oldCol, newCol)));
            }
        }

        // ─── TRIGGERS (DB-level) ──────────────────────────────────
        if (diff.RemovedTriggers is not null)
            foreach (var trg in diff.RemovedTriggers)
                actions.Add(new MigrationAction(
                    $"trigger:drop:{trg.FullName}",
                    MigrationActionType.DropTrigger,
                    "Triggers",
                    $"{trg.TableSchema}.{trg.TableName}",
                    $"Drop trigger {trg.FullName}",
                    dialect.DropTrigger(trg)));

        if (diff.ModifiedTriggers is not null)
            foreach (var trgDiff in diff.ModifiedTriggers)
            {
                var oldTrg = baseline.Triggers!.First(t => t.FullName.Equals(trgDiff.FullName, StringComparison.OrdinalIgnoreCase));
                var newTrg = target.Triggers!.First(t => t.FullName.Equals(trgDiff.FullName, StringComparison.OrdinalIgnoreCase));
                actions.Add(new MigrationAction(
                    $"trigger:alter:{trgDiff.FullName}:drop",
                    MigrationActionType.DropTrigger,
                    "Triggers",
                    $"{oldTrg.TableSchema}.{oldTrg.TableName}",
                    $"Recreate trigger {trgDiff.FullName} (drop)",
                    dialect.DropTrigger(oldTrg)));
                actions.Add(new MigrationAction(
                    $"trigger:alter:{trgDiff.FullName}:add",
                    MigrationActionType.CreateTrigger,
                    "Triggers",
                    $"{newTrg.TableSchema}.{newTrg.TableName}",
                    $"Recreate trigger {trgDiff.FullName} (add)",
                    dialect.CreateTrigger(newTrg)));
            }

        if (diff.AddedTriggers is not null)
            foreach (var trg in diff.AddedTriggers)
                actions.Add(new MigrationAction(
                    $"trigger:add:{trg.FullName}",
                    MigrationActionType.CreateTrigger,
                    "Triggers",
                    $"{trg.TableSchema}.{trg.TableName}",
                    $"Create trigger {trg.FullName}",
                    dialect.CreateTrigger(trg)));

        return actions;
    }

    /// <summary>
    /// Assembles the final SQL script from selected actions with transaction wrapping,
    /// correct ordering (drops → creates), and a header comment warning.
    /// </summary>
    public static string AssembleScript(
        IEnumerable<MigrationAction> selectedActions,
        ISqlDialect dialect,
        string baselineLabel,
        string targetLabel)
    {
        var ordered = selectedActions.OrderBy(a => (int)a.Type).ToList();
        if (ordered.Count == 0) return "-- No actions selected.\r\n";

        var sb = new StringBuilder();
        // Header
        sb.AppendLine("-- ═══════════════════════════════════════════════════════════════════");
        sb.AppendLine("-- SchemaDoc Migration Script");
        sb.AppendLine($"-- Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"-- Direction: apply to [{baselineLabel}] to match [{targetLabel}]");
        sb.AppendLine($"-- Dialect:   {dialect.Provider}");
        sb.AppendLine("--");
        sb.AppendLine("-- ⚠ WARNING: Review every statement before running.");
        sb.AppendLine("--            Back up your database first.");
        sb.AppendLine("--            Some changes may be destructive (DROP COLUMN, DROP TABLE).");
        sb.AppendLine("-- ═══════════════════════════════════════════════════════════════════");
        sb.AppendLine();

        // Transaction begin
        if (dialect.BeginTransaction is not null)
        {
            sb.AppendLine(dialect.BeginTransaction);
            if (!string.IsNullOrEmpty(dialect.StatementTerminator)) sb.AppendLine(dialect.StatementTerminator);
            sb.AppendLine();
        }

        // Group by category header
        string? lastCategory = null;
        foreach (var action in ordered)
        {
            if (action.Category != lastCategory)
            {
                sb.AppendLine($"-- ── {action.Category} ─────────────────────────────────");
                lastCategory = action.Category;
            }
            sb.AppendLine($"-- {action.Description}");

            // CREATE TRIGGER on SQL Server stores the entire batch text (including
            // leading comments) as the trigger definition in sys.sql_modules.
            // Emit an early terminator so the comment ends up in a separate batch.
            var isCreateTrigger = action.Type == MigrationActionType.CreateTrigger;
            if (isCreateTrigger && !string.IsNullOrEmpty(dialect.StatementTerminator))
                sb.AppendLine(dialect.StatementTerminator);

            sb.AppendLine(action.Sql);
            if (!string.IsNullOrEmpty(dialect.StatementTerminator))
                sb.AppendLine(dialect.StatementTerminator);
            sb.AppendLine();
        }

        // Transaction commit
        if (dialect.CommitTransaction is not null)
        {
            sb.AppendLine(dialect.CommitTransaction);
            if (!string.IsNullOrEmpty(dialect.StatementTerminator)) sb.AppendLine(dialect.StatementTerminator);
        }

        return sb.ToString();
    }

    public static ISqlDialect DialectFor(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.SqlServer => new SqlServerDialect(),
        DatabaseProvider.PostgreSql => new PostgreSqlDialect(),
        DatabaseProvider.MySql => new MySqlDialect(),
        _ => throw new NotSupportedException($"Migration script generation not supported for {provider}")
    };
}
