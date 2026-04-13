namespace SchemaDoc.Core.Models;

public record ConnectionProfile(
    int Id,
    string Name,
    DatabaseProvider Provider,
    DateTime LastConnectedAt,
    string? LastDatabaseName,
    string? Tag = null,
    string? TagColor = null
);

public record TableAnnotationDto(
    string ConnectionName,
    string DatabaseName,
    string SchemaName,
    string TableName,
    string? Description,
    string? Tags,
    IReadOnlyList<ColumnAnnotationDto> ColumnAnnotations
);

public record ColumnAnnotationDto(
    string ColumnName,
    string? Description
);
