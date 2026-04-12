using SchemaDoc.Core.Models;

namespace SchemaDoc.Core.Services;

public class SchemaDiffService
{
    /// <summary>
    /// Compares two database schemas (baseline vs. current) and returns what changed.
    /// </summary>
    public SchemaDiffResult Compare(DatabaseSchema baseline, DatabaseSchema current)
    {
        var baselineTables = baseline.Tables.ToDictionary(t => t.FullName, t => t);
        var currentTables  = current.Tables.ToDictionary(t => t.FullName, t => t);

        var addedTables = currentTables.Values
            .Where(t => !baselineTables.ContainsKey(t.FullName))
            .OrderBy(t => t.FullName)
            .ToList();

        var removedTables = baselineTables.Values
            .Where(t => !currentTables.ContainsKey(t.FullName))
            .OrderBy(t => t.FullName)
            .ToList();

        var modifiedTables = new List<TableDiff>();

        foreach (var key in baselineTables.Keys.Where(k => currentTables.ContainsKey(k)))
        {
            var baseTable    = baselineTables[key];
            var currentTable = currentTables[key];
            var diff         = DiffTable(baseTable, currentTable);
            if (diff.TotalChanges > 0)
                modifiedTables.Add(diff);
        }

        return new SchemaDiffResult(addedTables, removedTables, modifiedTables.OrderBy(t => t.FullName).ToList());
    }

    private static TableDiff DiffTable(SchemaTable baseline, SchemaTable current)
    {
        var baselineCols = baseline.Columns.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
        var currentCols  = current.Columns.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);

        var addedCols = currentCols.Values
            .Where(c => !baselineCols.ContainsKey(c.Name))
            .OrderBy(c => c.OrdinalPosition)
            .ToList();

        var removedCols = baselineCols.Values
            .Where(c => !currentCols.ContainsKey(c.Name))
            .OrderBy(c => c.OrdinalPosition)
            .ToList();

        var modifiedCols = new List<ColumnDiff>();
        foreach (var name in baselineCols.Keys.Where(n => currentCols.ContainsKey(n)))
        {
            var changes = DiffColumn(baselineCols[name], currentCols[name]);
            if (changes.Count > 0)
                modifiedCols.Add(new ColumnDiff(name, changes));
        }

        return new TableDiff(baseline.Schema, baseline.Name, addedCols, removedCols, modifiedCols);
    }

    private static List<string> DiffColumn(SchemaColumn baseline, SchemaColumn current)
    {
        var changes = new List<string>();

        string BaseType(SchemaColumn c) => c.MaxLength is not null
            ? $"{c.DataType}({c.MaxLength})" : c.DataType;

        if (!string.Equals(BaseType(baseline), BaseType(current), StringComparison.OrdinalIgnoreCase))
            changes.Add($"Type: {BaseType(baseline)} → {BaseType(current)}");

        if (baseline.IsNullable != current.IsNullable)
            changes.Add($"Nullable: {baseline.IsNullable} → {current.IsNullable}");

        if (baseline.IsPrimaryKey != current.IsPrimaryKey)
            changes.Add($"Primary key: {baseline.IsPrimaryKey} → {current.IsPrimaryKey}");

        if (baseline.IsIdentity != current.IsIdentity)
            changes.Add($"Identity: {baseline.IsIdentity} → {current.IsIdentity}");

        return changes;
    }
}
