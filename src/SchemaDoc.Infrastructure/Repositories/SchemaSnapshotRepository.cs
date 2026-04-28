using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchemaDoc.Core.Interfaces;
using SchemaDoc.Core.Models;
using SchemaDoc.Infrastructure.Data;

namespace SchemaDoc.Infrastructure.Repositories;

public class SchemaSnapshotRepository(AppDbContext db) : ISchemaSnapshotRepository
{
    /// <summary>
    /// Cap on UNNAMED snapshots per (connection, database). Named snapshots are durable
    /// artifacts (used for offline cross-server diffs) and are never auto-rotated.
    /// </summary>
    private const int MaxUnnamedSnapshotsPerDb = 10;

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

    public async Task<int> SaveAsync(int connectionId, DatabaseSchema schema, string? name = null, string? notes = null)
    {
        // Rotate only the UNNAMED snapshots; named snapshots survive forever.
        if (string.IsNullOrWhiteSpace(name))
        {
            var unnamed = await db.SchemaSnapshots
                .Where(s => s.ConnectionId == connectionId
                            && s.DatabaseName == schema.DatabaseName
                            && s.Name == null)
                .OrderByDescending(s => s.ExtractedAt)
                .ToListAsync();

            if (unnamed.Count >= MaxUnnamedSnapshotsPerDb)
            {
                var toRemove = unnamed.Skip(MaxUnnamedSnapshotsPerDb - 1).ToList();
                db.SchemaSnapshots.RemoveRange(toRemove);
            }
        }

        var entity = new SchemaSnapshot
        {
            ConnectionId = connectionId,
            DatabaseName = schema.DatabaseName,
            ExtractedAt = schema.ExtractedAt,
            SchemaJson = JsonSerializer.Serialize(schema, JsonOptions),
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
        db.SchemaSnapshots.Add(entity);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    public async Task<IReadOnlyList<SnapshotSummary>> GetSnapshotListAsync(int connectionId, string databaseName)
    {
        var rows = await db.SchemaSnapshots
            .Where(s => s.ConnectionId == connectionId && s.DatabaseName == databaseName)
            .OrderByDescending(s => s.ExtractedAt)
            .Select(s => new
            {
                s.Id,
                s.ConnectionId,
                ConnectionName = s.Connection.Name,
                s.DatabaseName,
                s.ExtractedAt,
                s.Name,
                s.Notes,
                s.SchemaJson
            })
            .ToListAsync();

        return rows.Select(r => new SnapshotSummary(
            r.Id, r.ConnectionId, r.ConnectionName, r.DatabaseName,
            r.ExtractedAt, r.Name, r.Notes,
            CountTables(r.SchemaJson))).ToList();
    }

    public async Task<IReadOnlyList<SnapshotSummary>> GetAllAsync()
    {
        var rows = await db.SchemaSnapshots
            .OrderByDescending(s => s.ExtractedAt)
            .Select(s => new
            {
                s.Id,
                s.ConnectionId,
                ConnectionName = s.Connection.Name,
                s.DatabaseName,
                s.ExtractedAt,
                s.Name,
                s.Notes,
                s.SchemaJson
            })
            .ToListAsync();

        return rows.Select(r => new SnapshotSummary(
            r.Id, r.ConnectionId, r.ConnectionName, r.DatabaseName,
            r.ExtractedAt, r.Name, r.Notes,
            CountTables(r.SchemaJson))).ToList();
    }

    public async Task<DatabaseSchema?> GetByIdAsync(int snapshotId)
    {
        var snapshot = await db.SchemaSnapshots.FindAsync(snapshotId);
        if (snapshot is null) return null;
        return JsonSerializer.Deserialize<DatabaseSchema>(snapshot.SchemaJson, JsonOptions);
    }

    public async Task<SnapshotSummary?> GetSummaryByIdAsync(int snapshotId)
    {
        var row = await db.SchemaSnapshots
            .Where(s => s.Id == snapshotId)
            .Select(s => new
            {
                s.Id,
                s.ConnectionId,
                ConnectionName = s.Connection.Name,
                s.DatabaseName,
                s.ExtractedAt,
                s.Name,
                s.Notes,
                s.SchemaJson
            })
            .FirstOrDefaultAsync();

        return row is null ? null : new SnapshotSummary(
            row.Id, row.ConnectionId, row.ConnectionName, row.DatabaseName,
            row.ExtractedAt, row.Name, row.Notes,
            CountTables(row.SchemaJson));
    }

    public async Task RenameAsync(int snapshotId, string? name, string? notes)
    {
        var entity = await db.SchemaSnapshots.FindAsync(snapshotId);
        if (entity is null) return;
        entity.Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        entity.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int snapshotId)
    {
        var entity = await db.SchemaSnapshots.FindAsync(snapshotId);
        if (entity is null) return;
        db.SchemaSnapshots.Remove(entity);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Cheap table count without fully deserializing the schema JSON.
    /// Falls back to 0 if the document shape is unexpected.
    /// </summary>
    private static int CountTables(string schemaJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(schemaJson);
            return doc.RootElement.TryGetProperty("Tables", out var tables) && tables.ValueKind == JsonValueKind.Array
                ? tables.GetArrayLength()
                : 0;
        }
        catch
        {
            return 0;
        }
    }
}
