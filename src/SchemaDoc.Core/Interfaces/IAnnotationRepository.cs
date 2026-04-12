using SchemaDoc.Core.Models;

namespace SchemaDoc.Core.Interfaces;

public interface IAnnotationRepository
{
    Task<TableAnnotationDto?> GetTableAnnotationAsync(
        string connectionName, string databaseName, string schemaName, string tableName);

    Task UpsertTableAnnotationAsync(TableAnnotationDto annotation);

    Task UpsertColumnAnnotationAsync(
        string connectionName, string databaseName, string schemaName,
        string tableName, string columnName, string? description);

    Task<IReadOnlyList<TableAnnotationDto>> GetAllForConnectionAsync(
        string connectionName, string databaseName);
}
