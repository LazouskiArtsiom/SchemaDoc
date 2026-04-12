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
    IReadOnlyList<ForeignKeyRelation> ForeignKeys
);

public record SchemaTable(
    string Schema,
    string Name,
    long? RowCount,
    IReadOnlyList<SchemaColumn> Columns
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
    string? DbNativeComment
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
