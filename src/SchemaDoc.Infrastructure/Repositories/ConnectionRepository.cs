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
        return await db.Connections
            .OrderBy(c => c.Name)
            .Select(c => new ConnectionProfile(c.Id, c.Name, c.Provider, c.LastConnectedAt, c.LastDatabaseName, c.Tag, c.TagColor))
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
        var entity = new SavedConnection
        {
            Name = name,
            Provider = provider,
            EncryptedConnectionString = ConnectionStringProtector.Protect(connectionString),
            LastConnectedAt = DateTime.UtcNow,
            Tag = tag,
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
            entity.Tag = tag;
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
}
