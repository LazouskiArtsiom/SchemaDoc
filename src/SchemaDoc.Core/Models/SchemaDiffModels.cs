namespace SchemaDoc.Core.Models;

public record SchemaDiffResult(
    IReadOnlyList<SchemaTable> AddedTables,
    IReadOnlyList<SchemaTable> RemovedTables,
    IReadOnlyList<TableDiff> ModifiedTables,
    IReadOnlyList<SchemaTrigger>? AddedTriggers = null,
    IReadOnlyList<SchemaTrigger>? RemovedTriggers = null,
    IReadOnlyList<TriggerDiff>? ModifiedTriggers = null,
    IReadOnlyList<StoredProcedure>? AddedProcedures = null,
    IReadOnlyList<StoredProcedure>? RemovedProcedures = null,
    IReadOnlyList<RoutineDiff>? ModifiedProcedures = null,
    IReadOnlyList<SchemaFunction>? AddedFunctions = null,
    IReadOnlyList<SchemaFunction>? RemovedFunctions = null,
    IReadOnlyList<RoutineDiff>? ModifiedFunctions = null
)
{
    public bool IsIdentical =>
        AddedTables.Count == 0 &&
        RemovedTables.Count == 0 &&
        ModifiedTables.Count == 0 &&
        (AddedTriggers is null || AddedTriggers.Count == 0) &&
        (RemovedTriggers is null || RemovedTriggers.Count == 0) &&
        (ModifiedTriggers is null || ModifiedTriggers.Count == 0) &&
        (AddedProcedures is null || AddedProcedures.Count == 0) &&
        (RemovedProcedures is null || RemovedProcedures.Count == 0) &&
        (ModifiedProcedures is null || ModifiedProcedures.Count == 0) &&
        (AddedFunctions is null || AddedFunctions.Count == 0) &&
        (RemovedFunctions is null || RemovedFunctions.Count == 0) &&
        (ModifiedFunctions is null || ModifiedFunctions.Count == 0);
}

/// <summary>
/// Describes what changed about a procedure or function. Used for both procs
/// and functions because the diff shape is the same: signature/body changes.
/// </summary>
public record RoutineDiff(
    string FullName,
    IReadOnlyList<string> Changes
);

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
    IReadOnlyList<IndexDiff>? ModifiedIndexes = null,
    IReadOnlyList<ForeignKeyGroup>? AddedForeignKeys = null,
    IReadOnlyList<ForeignKeyGroup>? RemovedForeignKeys = null,
    IReadOnlyList<ConstraintDiff>? ModifiedForeignKeys = null
)
{
    public string FullName => $"{Schema}.{Name}";

    public int TotalChanges =>
        AddedColumns.Count + RemovedColumns.Count + ModifiedColumns.Count +
        (PrimaryKeyChanges?.Count ?? 0) +
        (AddedUniqueConstraints?.Count ?? 0) + (RemovedUniqueConstraints?.Count ?? 0) + (ModifiedUniqueConstraints?.Count ?? 0) +
        (AddedCheckConstraints?.Count ?? 0) + (RemovedCheckConstraints?.Count ?? 0) + (ModifiedCheckConstraints?.Count ?? 0) +
        (AddedIndexes?.Count ?? 0) + (RemovedIndexes?.Count ?? 0) + (ModifiedIndexes?.Count ?? 0) +
        (AddedForeignKeys?.Count ?? 0) + (RemovedForeignKeys?.Count ?? 0) + (ModifiedForeignKeys?.Count ?? 0);
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
