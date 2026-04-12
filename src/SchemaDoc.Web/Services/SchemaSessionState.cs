using SchemaDoc.Core.Models;

namespace SchemaDoc.Web.Services;

/// <summary>
/// Holds the active connection + loaded schemas for the session.
/// Caches schemas per connection so switching is instant.
/// Injected as Scoped so each browser tab/session has its own state.
/// </summary>
public class SchemaSessionState
{
    public ConnectionProfile? ActiveConnection { get; private set; }
    public DatabaseSchema? CurrentSchema { get; private set; }

    // Cached schemas keyed by connection ID — enables instant switching
    private readonly Dictionary<int, DatabaseSchema> _schemaCache = new();

    public event Action? OnChanged;

    public void SetActiveConnection(ConnectionProfile connection, DatabaseSchema schema)
    {
        ActiveConnection = connection;
        CurrentSchema = schema;
        _schemaCache[connection.Id] = schema;
        OnChanged?.Invoke();
    }

    /// <summary>Returns the cached schema for a connection, or null if not yet loaded.</summary>
    public DatabaseSchema? GetCachedSchema(int connectionId) =>
        _schemaCache.TryGetValue(connectionId, out var s) ? s : null;

    public bool IsCached(int connectionId) => _schemaCache.ContainsKey(connectionId);

    public void Clear()
    {
        ActiveConnection = null;
        CurrentSchema = null;
        OnChanged?.Invoke();
    }
}
