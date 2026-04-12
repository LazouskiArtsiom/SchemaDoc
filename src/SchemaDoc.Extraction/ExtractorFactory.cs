using SchemaDoc.Core.Interfaces;
using SchemaDoc.Core.Models;
using SchemaDoc.Extraction.Extractors;

namespace SchemaDoc.Extraction;

public class ExtractorFactory
{
    public ISchemaExtractor GetExtractor(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.SqlServer => new SqlServerExtractor(),
        DatabaseProvider.PostgreSql => new PostgreSqlExtractor(),
        DatabaseProvider.MySql     => new MySqlExtractor(),
        DatabaseProvider.CosmosDb  => new CosmosDbExtractor(),
        _                          => throw new ArgumentOutOfRangeException(nameof(provider))
    };
}
