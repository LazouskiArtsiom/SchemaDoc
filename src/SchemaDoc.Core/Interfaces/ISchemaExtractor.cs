using SchemaDoc.Core.Models;

namespace SchemaDoc.Core.Interfaces;

public interface ISchemaExtractor
{
    Task<DatabaseSchema> ExtractAsync(string connectionString, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default);
}
