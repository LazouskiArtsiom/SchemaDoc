using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchemaDoc.Core.Interfaces;
using SchemaDoc.Core.Models;
using SchemaDoc.Infrastructure.Data;

namespace SchemaDoc.Infrastructure.Repositories;

public class SchemaSnapshotRepository(AppDbContext db) : ISchemaSnapshotRepository
{
    private const int MaxSnapshotsPerDb = 5;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public async Task<DatabaseSchema?> GetLatestAsync(int connectionId, string databaseName)
    {
        var snapshot = await db.SchemaSnapshots
            .Where(s => s.ConnectionId == connectionId && s.DatabaseName == databaseName)
            .OrderByDescending(s => s.ExtractedAt)
            .FirstOrDefaultAsync();

        if (snapshot is null) return null;

        return JsonSerializer.Deserialize<DatabaseSchema>(snapshot.SchemaJson, JsonOptions);
    }

    public async Task SaveAsync(int connectionId, DatabaseSchema schema)
    {
        // Keep only the latest MaxSnapshotsPerDb snapshots, remove older ones
        var existing = await db.SchemaSnapshots
            .Where(s => s.ConnectionId == connectionId && s.DatabaseName == schema.DatabaseName)
            .OrderByDescending(s => s.ExtractedAt)
            .ToListAsync();

        if (existing.Count >= MaxSnapshotsPerDb)
        {
            var toRemove = existing.Skip(MaxSnapshotsPerDb - 1).ToList();
            db.SchemaSnapshots.RemoveRange(toRemove);
        }

        db.SchemaSnapshots.Add(new SchemaSnapshot
        {
            ConnectionId = connectionId,
            DatabaseName = schema.DatabaseName,
            ExtractedAt = schema.ExtractedAt,
            SchemaJson = JsonSerializer.Serialize(schema, JsonOptions)
        });

        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<SnapshotSummary>> GetSnapshotListAsync(int connectionId, string databaseName)
    {
        return await db.SchemaSnapshots
            .Where(s => s.ConnectionId == connectionId && s.DatabaseName == databaseName)
            .OrderByDescending(s => s.ExtractedAt)
            .Select(s => new SnapshotSummary(s.Id, s.DatabaseName, s.ExtractedAt))
            .ToListAsync();
    }

    public async Task<DatabaseSchema?> GetByIdAsync(int snapshotId)
    {
        var snapshot = await db.SchemaSnapshots.FindAsync(snapshotId);
        if (snapshot is null) return null;
        return JsonSerializer.Deserialize<DatabaseSchema>(snapshot.SchemaJson, JsonOptions);
    }
}
