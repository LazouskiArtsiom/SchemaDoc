using SchemaDoc.Core.Models;

namespace SchemaDoc.Core.Interfaces;

public interface ISchemaSnapshotRepository
{
    Task<DatabaseSchema?> GetLatestAsync(int connectionId, string databaseName);
    Task SaveAsync(int connectionId, DatabaseSchema schema);
    Task<IReadOnlyList<SnapshotSummary>> GetSnapshotListAsync(int connectionId, string databaseName);
    Task<DatabaseSchema?> GetByIdAsync(int snapshotId);
}
