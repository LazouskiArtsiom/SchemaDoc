using SchemaDoc.Core.Interfaces;
using SchemaDoc.Core.Models;
using SchemaDoc.Extraction;

namespace SchemaDoc.Web.Services;

/// <summary>
/// Orchestrates loading a schema: checks snapshot cache first, falls back to live extraction.
/// </summary>
public class SchemaLoaderService(
    ExtractorFactory extractorFactory,
    IConnectionRepository connectionRepo,
    ISchemaSnapshotRepository snapshotRepo)
{
    public async Task<bool> TestConnectionAsync(DatabaseProvider provider, string connectionString)
    {
        var extractor = extractorFactory.GetExtractor(provider);
        return await extractor.TestConnectionAsync(connectionString);
    }

    public async Task<DatabaseSchema> LoadSchemaAsync(int connectionId, DatabaseProvider provider, bool forceRefresh = false)
    {
        var connStr = await connectionRepo.GetConnectionStringAsync(connectionId)
            ?? throw new InvalidOperationException("Connection not found.");

        if (!forceRefresh)
        {
            var dbName = await GetDatabaseNameAsync(provider, connStr);
            var cached = await snapshotRepo.GetLatestAsync(connectionId, dbName);
            if (cached is not null)
                return cached;
        }

        var extractor = extractorFactory.GetExtractor(provider);
        var schema = await extractor.ExtractAsync(connStr);
        await snapshotRepo.SaveAsync(connectionId, schema);

        return schema;
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
