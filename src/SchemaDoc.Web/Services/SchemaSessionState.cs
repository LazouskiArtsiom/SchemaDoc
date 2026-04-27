using SchemaDoc.Core.Models;

namespace SchemaDoc.Web.Services;

/// <summary>
/// Holds the active connection + database + loaded schemas for the session.
/// A connection now represents a SERVER, so the (ConnectionId, DatabaseName)
/// pair identifies a specific database. Cached schemas use the same key,
/// enabling instant switching between databases on the same server.
/// Injected as Scoped so each browser tab/session has its own state.
/// </summary>
public class SchemaSessionState
{
    public ConnectionProfile? ActiveConnection { get; private set; }
    public string? ActiveDatabase { get; private set; }
    public DatabaseSchema? CurrentSchema { get; private set; }

    // Effective tag for the currently active database (override or inherited from server)
    public string? ActiveDbTag { get; private set; }
    public string? ActiveDbTagColor { get; private set; }

    // Cached schemas keyed by (connection ID, database name)
    private readonly Dictionary<(int ConnectionId, string Db), DatabaseSchema> _schemaCache = new();

    // Cached database lists per connection — populated on first connect to a server
    private readonly Dictionary<int, IReadOnlyList<string>> _databaseListCache = new();

    public event Action? OnChanged;

    public void SetActiveDatabase(
        ConnectionProfile connection,
        string databaseName,
        DatabaseSchema schema,
        string? effectiveTag = null,
        string? effectiveTagColor = null)
    {
        ActiveConnection = connection;
        ActiveDatabase = databaseName;
        CurrentSchema = schema;
        ActiveDbTag = effectiveTag ?? connection.Tag;
        ActiveDbTagColor = effectiveTagColor ?? connection.TagColor;
        _schemaCache[(connection.Id, databaseName)] = schema;
        OnChanged?.Invoke();
    }

    /// <summary>Returns cached schema for (connection, db), or null if not yet loaded.</summary>
    public DatabaseSchema? GetCachedSchema(int connectionId, string databaseName) =>
        _schemaCache.TryGetValue((connectionId, databaseName), out var s) ? s : null;

    /// <summary>Cache the list of databases enumerated on a server, so we don't re-query every render.</summary>
    public void CacheDatabaseList(int connectionId, IReadOnlyList<string> databases)
    {
        _databaseListCache[connectionId] = databases;
        OnChanged?.Invoke();
    }

    public IReadOnlyList<string>? GetCachedDatabaseList(int connectionId) =>
        _databaseListCache.TryGetValue(connectionId, out var list) ? list : null;

    public void InvalidateDatabaseList(int connectionId)
    {
        _databaseListCache.Remove(connectionId);
    }

    public void Clear()
    {
        ActiveConnection = null;
        ActiveDatabase = null;
        CurrentSchema = null;
        ActiveDbTag = null;
        ActiveDbTagColor = null;
        OnChanged?.Invoke();
    }
}
