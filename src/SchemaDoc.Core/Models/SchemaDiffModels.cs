namespace SchemaDoc.Core.Models;

public record SchemaDiffResult(
    IReadOnlyList<SchemaTable> AddedTables,
    IReadOnlyList<SchemaTable> RemovedTables,
    IReadOnlyList<TableDiff> ModifiedTables,
    IReadOnlyList<SchemaTrigger>? AddedTriggers = null,
    IReadOnlyList<SchemaTrigger>? RemovedTriggers = null,
    IReadOnlyList<TriggerDiff>? ModifiedTriggers = null
)
{
    public bool IsIdentical =>
        AddedTables.Count == 0 &&
        RemovedTables.Count == 0 &&
        ModifiedTables.Count == 0 &&
        (AddedTriggers is null || AddedTriggers.Count == 0) &&
        (RemovedTriggers is null || RemovedTriggers.Count == 0) &&
        (ModifiedTriggers is null || ModifiedTriggers.Count == 0);
}

public record TableDiff(
    string Schema,
    string Name,
    IReadOnlyList<SchemaColumn> AddedColumns,
    IReadOnlyList<SchemaColumn> RemovedColumns,
    IReadOnlyList<ColumnDiff> ModifiedColumns,
    // New for Step 1:
    IReadOnlyList<string>? PrimaryKeyChanges = null,
    IReadOnlyList<UniqueConstraint>? AddedUniqueConstraints = null,
    IReadOnlyList<UniqueConstraint>? RemovedUniqueConstraints = null,
    IReadOnlyList<ConstraintDiff>? ModifiedUniqueConstraints = null,
    IReadOnlyList<CheckConstraint>? AddedCheckConstraints = null,
    IReadOnlyList<CheckConstraint>? RemovedCheckConstraints = null,
    IReadOnlyList<ConstraintDiff>? ModifiedCheckConstraints = null,
    IReadOnlyList<SchemaIndex>? AddedIndexes = null,
    IReadOnlyList<SchemaIndex>? RemovedIndexes = null,
    IReadOnlyList<IndexDiff>? ModifiedIndexes = null
)
{
    public string FullName => $"{Schema}.{Name}";

    public int TotalChanges =>
        AddedColumns.Count + RemovedColumns.Count + ModifiedColumns.Count +
        (PrimaryKeyChanges?.Count ?? 0) +
        (AddedUniqueConstraints?.Count ?? 0) + (RemovedUniqueConstraints?.Count ?? 0) + (ModifiedUniqueConstraints?.Count ?? 0) +
        (AddedCheckConstraints?.Count ?? 0) + (RemovedCheckConstraints?.Count ?? 0) + (ModifiedCheckConstraints?.Count ?? 0) +
        (AddedIndexes?.Count ?? 0) + (RemovedIndexes?.Count ?? 0) + (ModifiedIndexes?.Count ?? 0);
}

public record ColumnDiff(
    string ColumnName,
    IReadOnlyList<string> Changes
);

public record ConstraintDiff(
    string Name,
    IReadOnlyList<string> Changes
);

public record IndexDiff(
    string Name,
    IReadOnlyList<string> Changes
);

public record TriggerDiff(
    string FullName,
    IReadOnlyList<string> Changes
);
