namespace SchemaDoc.Core.Models;

public record SchemaDiffResult(
    IReadOnlyList<SchemaTable> AddedTables,
    IReadOnlyList<SchemaTable> RemovedTables,
    IReadOnlyList<TableDiff> ModifiedTables
)
{
    public bool IsIdentical =>
        AddedTables.Count == 0 &&
        RemovedTables.Count == 0 &&
        ModifiedTables.Count == 0;
}

public record TableDiff(
    string Schema,
    string Name,
    IReadOnlyList<SchemaColumn> AddedColumns,
    IReadOnlyList<SchemaColumn> RemovedColumns,
    IReadOnlyList<ColumnDiff> ModifiedColumns
)
{
    public string FullName => $"{Schema}.{Name}";
    public int TotalChanges => AddedColumns.Count + RemovedColumns.Count + ModifiedColumns.Count;
}

public record ColumnDiff(
    string ColumnName,
    IReadOnlyList<string> Changes
);
