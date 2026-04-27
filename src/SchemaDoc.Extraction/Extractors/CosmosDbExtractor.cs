using Microsoft.Azure.Cosmos;
using SchemaDoc.Core.Interfaces;
using SchemaDoc.Core.Models;

namespace SchemaDoc.Extraction.Extractors;

public class CosmosDbExtractor : ISchemaExtractor
{
    public async Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        using var client = CreateClient(connectionString);
        var iter = client.GetDatabaseQueryIterator<DatabaseProperties>();
        await iter.ReadNextAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<string>> ListDatabasesAsync(string connectionString, CancellationToken ct = default)
    {
        using var client = CreateClient(connectionString);
        var dbs = new List<string>();
        var iter = client.GetDatabaseQueryIterator<DatabaseProperties>();
        while (iter.HasMoreResults)
        {
            var page = await iter.ReadNextAsync(ct);
            foreach (var d in page) dbs.Add(d.Id);
        }
        dbs.Sort();
        return dbs;
    }

    public string SwitchDatabase(string connectionString, string databaseName)
    {
        // Cosmos connection strings don't carry a single "database" — the account hosts many.
        // For Cosmos we keep the original connection string; database selection happens at query time.
        return connectionString;
    }

    public async Task<DatabaseSchema> ExtractAsync(string connectionString, CancellationToken ct = default)
    {
        using var client = CreateClient(connectionString);

        var tables = new List<SchemaTable>();

        // List all databases in the account
        var dbIter = client.GetDatabaseQueryIterator<DatabaseProperties>();
        var databases = new List<DatabaseProperties>();
        while (dbIter.HasMoreResults)
        {
            var page = await dbIter.ReadNextAsync(ct);
            databases.AddRange(page);
        }

        // For each database, list all containers
        foreach (var dbProps in databases)
        {
            var database = client.GetDatabase(dbProps.Id);
            var containerIter = database.GetContainerQueryIterator<ContainerProperties>();

            while (containerIter.HasMoreResults)
            {
                var page = await containerIter.ReadNextAsync(ct);
                foreach (var containerProps in page)
                {
                    var columns = BuildColumns(containerProps);
                    tables.Add(new SchemaTable(
                        Schema: dbProps.Id,
                        Name: containerProps.Id,
                        RowCount: null,
                        Columns: columns
                    ));
                }
            }
        }

        var accountName = ExtractAccountName(connectionString);

        return new DatabaseSchema(
            DatabaseName: accountName,
            Provider: DatabaseProvider.CosmosDb,
            ExtractedAt: DateTime.UtcNow,
            Tables: tables,
            Views: [],
            StoredProcedures: [],
            ForeignKeys: []
        );
    }

    // Each Cosmos DB container exposes `id` (always present) and its partition key path(s).
    private static IReadOnlyList<SchemaColumn> BuildColumns(ContainerProperties container)
    {
        var cols = new List<SchemaColumn>();
        int ordinal = 0;

        // `id` is always the document identifier / logical PK
        cols.Add(new SchemaColumn(
            Name: "id",
            OrdinalPosition: ordinal++,
            DataType: "ID",
            MaxLength: null,
            NumericPrecision: null,
            NumericScale: null,
            IsNullable: false,
            IsPrimaryKey: false,
            IsForeignKey: false,
            IsIdentity: false,
            IsComputed: false,
            DefaultValue: null,
            DbNativeComment: "Document identifier"
        ));

        // Partition key paths, e.g. "/categoryId" → field name "categoryId"
        foreach (var pkPath in container.PartitionKeyPaths)
        {
            var fieldName = pkPath.TrimStart('/');
            if (string.Equals(fieldName, "id", StringComparison.OrdinalIgnoreCase))
                continue; // already added above

            cols.Add(new SchemaColumn(
                Name: fieldName,
                OrdinalPosition: ordinal++,
                DataType: "PartitionKey",
                MaxLength: null,
                NumericPrecision: null,
                NumericScale: null,
                IsNullable: false,
                IsPrimaryKey: false,
                IsForeignKey: false,
                IsIdentity: false,
                IsComputed: false,
                DefaultValue: null,
                DbNativeComment: "Partition key"
            ));
        }

        return cols;
    }

    // Parse the account name from the connection string endpoint.
    // "AccountEndpoint=https://myaccount.documents.azure.com:443/;AccountKey=...;"
    // "AccountEndpoint=https://localhost:8081/;AccountKey=...;"
    private static string ExtractAccountName(string connectionString)
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
                    // Azure: "myaccount.documents.azure.com" → "myaccount"
                    // Emulator: "localhost" → "CosmosDB Emulator"
                    if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                        host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
                        return "CosmosDB Emulator";

                    return host.Replace(".documents.azure.com", "");
                }
            }
        }
        return "CosmosDB";
    }

    private static CosmosClient CreateClient(string connectionString)
    {
        // The emulator uses a self-signed certificate — bypass SSL validation for localhost
        bool isEmulator = connectionString.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                       || connectionString.Contains("127.0.0.1");

        if (isEmulator)
        {
            var options = new CosmosClientOptions
            {
                // Gateway mode avoids direct TCP which can also fail on the emulator
                ConnectionMode = ConnectionMode.Gateway,
                HttpClientFactory = () => new HttpClient(new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                })
            };
            return new CosmosClient(connectionString, options);
        }

        return new CosmosClient(connectionString);
    }
}
