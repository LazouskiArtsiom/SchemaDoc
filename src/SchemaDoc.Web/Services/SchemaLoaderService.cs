using SchemaDoc.Core.Interfaces;
using SchemaDoc.Core.Models;
using SchemaDoc.Extraction;

namespace SchemaDoc.Web.Services;

/// <summary>
/// Orchestrates loading a schema by live extraction. Persisted snapshots are user-managed
/// artifacts (created via Save Snapshot) and are not used as a transparent read-through cache.
/// </summary>
public class SchemaLoaderService(
    ExtractorFactory extractorFactory,
    IConnectionRepository connectionRepo)
{
    public async Task<bool> TestConnectionAsync(DatabaseProvider provider, string connectionString)
    {
        var extractor = extractorFactory.GetExtractor(provider);
        return await extractor.TestConnectionAsync(connectionString);
    }

    public async Task<IReadOnlyList<string>> ListDatabasesAsync(int connectionId, DatabaseProvider provider)
    {
        var connStr = await connectionRepo.GetConnectionStringAsync(connectionId)
            ?? throw new InvalidOperationException("Connection not found.");
        var extractor = extractorFactory.GetExtractor(provider);
        return await extractor.ListDatabasesAsync(connStr);
    }

    /// <summary>
    /// Loads schema for a specific database under a server connection. Always extracts live
    /// — snapshots are now user-managed artifacts (see <c>Save Snapshot</c>) and not used as
    /// a transparent cache. Session-level in-memory caching is the responsibility of
    /// <c>SchemaSessionState</c>; this service does not persist anything to the DB.
    /// </summary>
    public async Task<DatabaseSchema> LoadSchemaAsync(int connectionId, DatabaseProvider provider, string? databaseName = null, bool forceRefresh = false)
    {
        _ = forceRefresh; // kept for source compat; behaviour is always "live extract"
        var connStr = await connectionRepo.GetConnectionStringAsync(connectionId)
            ?? throw new InvalidOperationException("Connection not found.");

        var extractor = extractorFactory.GetExtractor(provider);

        // Reroute the connection string to the requested database (no-op for Cosmos).
        var effectiveCs = string.IsNullOrEmpty(databaseName)
            ? connStr
            : extractor.SwitchDatabase(connStr, databaseName);

        return await extractor.ExtractAsync(effectiveCs);
    }

    private async Task<string> GetDatabaseNameAsync(DatabaseProvider provider, string connectionString)
    {
        // Quick way to get DB name without full extraction
        if (provider == DatabaseProvider.SqlServer)
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
            return conn.Database;
        }
        // Cosmos DB: account name is the key for snapshot lookup
        if (provider == DatabaseProvider.CosmosDb)
        {
            return await Task.FromResult(ExtractCosmosAccountName(connectionString));
        }
        // For PG/MySQL we just do a quick connect — extend later
        return "default";
    }

    private static string ExtractCosmosAccountName(string connectionString)
    {
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("AccountEndpoint=", StringComparison.OrdinalIgnoreCase))
            {
                var endpoint = trimmed["AccountEndpoint=".Length..].Trim();
                if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
                {
                    var host = uri.Host;
                    if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                        host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
                        return "CosmosDB Emulator";
                    return host.Replace(".documents.azure.com", "");
                }
            }
        }
        return "CosmosDB";
    }
}
