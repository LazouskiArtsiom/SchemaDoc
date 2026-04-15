namespace SchemaDoc.Core.Models;

public enum DatabaseProvider
{
    SqlServer,
    PostgreSql,
    MySql,
    CosmosDb
}

public record DatabaseSchema(
    string DatabaseName,
    DatabaseProvider Provider,
    DateTime ExtractedAt,
    IReadOnlyList<SchemaTable> Tables,
    IReadOnlyList<SchemaView> Views,
    IReadOnlyList<StoredProcedure> StoredProcedures,
    IReadOnlyList<ForeignKeyRelation> ForeignKeys,
    IReadOnlyList<SchemaTrigger>? Triggers = null
);

public record SchemaTable(
    string Schema,
    string Name,
    long? RowCount,
    IReadOnlyList<SchemaColumn> Columns,
    PrimaryKeyInfo? PrimaryKey = null,
    IReadOnlyList<UniqueConstraint>? UniqueConstraints = null,
    IReadOnlyList<CheckConstraint>? CheckConstraints = null,
    IReadOnlyList<SchemaIndex>? Indexes = null
)
{
    public string FullName => $"{Schema}.{Name}";
}

public record SchemaColumn(
    string Name,
    int OrdinalPosition,
    string DataType,
    string? MaxLength,
    int? NumericPrecision,
    int? NumericScale,
    bool IsNullable,
    bool IsPrimaryKey,
    bool IsForeignKey,
    bool IsIdentity,
    bool IsComputed,
    string? DefaultValue,
    string? DbNativeComment,
    string? ComputedExpression = null
);

public record ForeignKeyRelation(
    string ConstraintName,
    string ParentSchema,
    string ParentTable,
    string ParentColumn,
    string ReferencedSchema,
    string ReferencedTable,
    string ReferencedColumn,
    string? OnDelete,
    string? OnUpdate
);

/// <summary>Composite primary key information for a table.</summary>
public record PrimaryKeyInfo(
    string Name,
    IReadOnlyList<string> Columns,
    string? IndexType = null        // CLUSTERED / NONCLUSTERED / BTREE / etc.
);

public record UniqueConstraint(
    string Name,
    IReadOnlyList<string> Columns
);

public record CheckConstraint(
    string Name,
    string Expression
);

public record SchemaIndex(
    string Name,
    IReadOnlyList<IndexColumn> Columns,
    IReadOnlyList<string>? IncludedColumns,
    bool IsUnique,
    string? IndexType,              // CLUSTERED/NONCLUSTERED/BTREE/HASH/GIN/GIST/BRIN/FULLTEXT
    string? FilterExpression = null // Filtered indexes (SQL Server WHERE, PG partial)
);

public record IndexColumn(
    string Name,
    bool IsDescending = false
);

public record SchemaTrigger(
    string Schema,
    string Name,
    string TableSchema,
    string TableName,
    string Event,                   // INSERT / UPDATE / DELETE (or combination)
    string Timing,                  // BEFORE / AFTER / INSTEAD OF
    string? Definition
)
{
    public string FullName => $"{Schema}.{Name}";
}

public record SchemaView(
    string Schema,
    string Name,
    string? Definition,
    IReadOnlyList<SchemaColumn> Columns
)
{
    public string FullName => $"{Schema}.{Name}";
}

public record StoredProcedure(
    string Schema,
    string Name,
    string? Definition,
    IReadOnlyList<ProcParameter> Parameters
)
{
    public string FullName => $"{Schema}.{Name}";
}

public record ProcParameter(
    string Name,
    string DataType,
    string Direction,
    bool IsOptional
);
