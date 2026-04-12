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
            .Select(c => new ConnectionProfile(c.Id, c.Name, c.Provider, c.LastConnectedAt, c.LastDatabaseName))
            .ToListAsync();
    }

    public async Task<string?> GetConnectionStringAsync(int connectionId)
    {
        var conn = await db.Connections.FindAsync(connectionId);
        if (conn is null) return null;
        return ConnectionStringProtector.Unprotect(conn.EncryptedConnectionString);
    }

    public async Task<int> AddAsync(string name, DatabaseProvider provider, string connectionString)
    {
        var entity = new SavedConnection
        {
            Name = name,
            Provider = provider,
            EncryptedConnectionString = ConnectionStringProtector.Protect(connectionString),
            LastConnectedAt = DateTime.UtcNow
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
