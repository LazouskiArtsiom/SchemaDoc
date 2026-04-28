using SchemaDoc.Core.Models;

namespace SchemaDoc.Core.Interfaces;

public interface ISchemaSnapshotRepository
{
    /// <summary>
    /// Returns the most recent snapshot for (connection, database) regardless of name.
    /// Used as the cache lookup before live extraction.
    /// </summary>
    Task<DatabaseSchema?> GetLatestAsync(int connectionId, string databaseName);

    /// <summary>
    /// Persists a snapshot. If <paramref name="name"/> is null, the snapshot is "unnamed"
    /// (Refresh-Schema cache); unnamed snapshots auto-rotate per (connection, database)
    /// when the cap is exceeded. Named snapshots are durable and never auto-rotate.
    /// </summary>
    Task<int> SaveAsync(int connectionId, DatabaseSchema schema, string? name = null, string? notes = null);

    Task<IReadOnlyList<SnapshotSummary>> GetSnapshotListAsync(int connectionId, string databaseName);

    /// <summary>All snapshots across all connections + databases. For the management page and the diff picker.</summary>
    Task<IReadOnlyList<SnapshotSummary>> GetAllAsync();

    Task<DatabaseSchema?> GetByIdAsync(int snapshotId);
    Task<SnapshotSummary?> GetSummaryByIdAsync(int snapshotId);

    Task RenameAsync(int snapshotId, string? name, string? notes);
    Task DeleteAsync(int snapshotId);
}
