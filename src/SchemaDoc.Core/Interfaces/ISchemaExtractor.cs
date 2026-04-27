using SchemaDoc.Core.Models;

namespace SchemaDoc.Core.Interfaces;

public interface ISchemaExtractor
{
    Task<DatabaseSchema> ExtractAsync(string connectionString, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default);

    /// <summary>
    /// Lists all user-databases the credentials in the connection string can see.
    /// Used by server-level connections so the sidebar can render a tree of databases.
    /// Returns at minimum the database the connection string targets.
    /// </summary>
    Task<IReadOnlyList<string>> ListDatabasesAsync(string connectionString, CancellationToken ct = default);

    /// <summary>
    /// Returns the connection string with its database/initial-catalog clause swapped to <paramref name="databaseName"/>.
    /// Caller uses this to reuse one server connection across multiple databases.
    /// </summary>
    string SwitchDatabase(string connectionString, string databaseName);
}
