using Microsoft.EntityFrameworkCore;
using SchemaDoc.Core.Interfaces;
using SchemaDoc.Core.Models;
using SchemaDoc.Infrastructure.Data;
using SchemaDoc.Infrastructure.Security;

namespace SchemaDoc.Infrastructure.Repositories;

public class ConnectionRepository(AppDbContext db) : IConnectionRepository
{
    public async Task<IReadOnlyList<ConnectionProfile>> GetAllAsync()
    {
        // Tag column never null in DB (normalised on write); convert "" → null for callers
        // so existing UI checks like "if (Tag is not null)" continue to work.
        return await db.Connections
            .OrderBy(c => c.Name)
            .Select(c => new ConnectionProfile(
                c.Id, c.Name, c.Provider, c.LastConnectedAt, c.LastDatabaseName,
                c.Tag == "" ? null : c.Tag,
                c.TagColor))
            .ToListAsync();
    }

    public async Task<string?> GetConnectionStringAsync(int connectionId)
    {
        var conn = await db.Connections.FindAsync(connectionId);
        if (conn is null) return null;
        return ConnectionStringProtector.Unprotect(conn.EncryptedConnectionString);
    }

    public async Task<int> AddAsync(string name, DatabaseProvider provider, string connectionString, string? tag = null, string? tagColor = null)
    {
        var normalisedTag = tag ?? "";
        var trimmedName = name.Trim();

        // Application-level uniqueness check (also enforced by DB unique index on (Name, Tag))
        if (await db.Connections.AnyAsync(c => c.Name == trimmedName && c.Tag == normalisedTag))
        {
            var displayTag = string.IsNullOrEmpty(normalisedTag) ? "(no tag)" : $"[{normalisedTag}]";
            throw new InvalidOperationException(
                $"A connection named '{trimmedName}' with tag {displayTag} already exists. " +
                $"Pick a different name or a different tag.");
        }

        var entity = new SavedConnection
        {
            Name = trimmedName,
            Provider = provider,
            EncryptedConnectionString = ConnectionStringProtector.Protect(connectionString),
            LastConnectedAt = DateTime.UtcNow,
            Tag = normalisedTag,
            TagColor = tagColor
        };
        db.Connections.Add(entity);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    public async Task UpdateAsync(int id, string name, DatabaseProvider provider, string connectionString)
    {
        var entity = await db.Connections.FindAsync(id)
            ?? throw new KeyNotFoundException($"Connection {id} not found");
        entity.Name = name;
        entity.Provider = provider;
        entity.EncryptedConnectionString = ConnectionStringProtector.Protect(connectionString);
        await db.SaveChangesAsync();
    }

    public async Task UpdateTagAsync(int id, string? tag, string? tagColor)
    {
        var entity = await db.Connections.FindAsync(id);
        if (entity is not null)
        {
            entity.Tag = tag ?? "";
            entity.TagColor = tagColor;
            await db.SaveChangesAsync();
        }
    }

    public async Task DeleteAsync(int id)
    {
        var entity = await db.Connections.FindAsync(id);
        if (entity is not null)
        {
            db.Connections.Remove(entity);
            await db.SaveChangesAsync();
        }
    }

    public async Task TouchLastConnectedAsync(int id, string databaseName)
    {
        var entity = await db.Connections.FindAsync(id);
        if (entity is not null)
        {
            entity.LastConnectedAt = DateTime.UtcNow;
            entity.LastDatabaseName = databaseName;
            await db.SaveChangesAsync();
        }
    }

    public async Task<IReadOnlyDictionary<string, (string? Tag, string? TagColor)>> GetDatabaseTagsAsync(int connectionId)
    {
        var rows = await db.DatabaseTags
            .Where(x => x.ConnectionId == connectionId)
            .Select(x => new { x.DatabaseName, x.Tag, x.TagColor })
            .ToListAsync();
        return rows.ToDictionary(x => x.DatabaseName, x => (x.Tag, x.TagColor));
    }

    public async Task UpsertDatabaseTagAsync(int connectionId, string databaseName, string? tag, string? tagColor)
    {
        var existing = await db.DatabaseTags.FirstOrDefaultAsync(
            x => x.ConnectionId == connectionId && x.DatabaseName == databaseName);
        if (existing is null)
        {
            db.DatabaseTags.Add(new DatabaseTag
            {
                ConnectionId = connectionId,
                DatabaseName = databaseName,
                Tag = tag,
                TagColor = tagColor
            });
        }
        else
        {
            existing.Tag = tag;
            existing.TagColor = tagColor;
        }
        await db.SaveChangesAsync();
    }

    public async Task ClearDatabaseTagAsync(int connectionId, string databaseName)
    {
        var existing = await db.DatabaseTags.FirstOrDefaultAsync(
            x => x.ConnectionId == connectionId && x.DatabaseName == databaseName);
        if (existing is not null)
        {
            db.DatabaseTags.Remove(existing);
            await db.SaveChangesAsync();
        }
    }
}
