using SchemaDoc.Core.Models;
using SchemaDoc.Core.Services;
using SchemaDoc.Core.Services.Migration;

namespace SchemaDoc.Extraction.Tests;

/// <summary>
/// DB-free, deterministic checks that generated SQL Server CREATE INDEX statements
/// carry WITH (ONLINE = ON), both from the dialect directly and through the full
/// diff -> BuildActions -> AssembleScript path.
/// </summary>
public class IndexScriptTests
{
    private static SchemaColumn Col(string name, int ord) =>
        new(name, ord, "int", null, null, null,
            IsNullable: false, IsPrimaryKey: false, IsForeignKey: false,
            IsIdentity: false, IsComputed: false, DefaultValue: null, DbNativeComment: null);

    [Fact]
    public void CreateIndex_emits_online_on()
    {
        var dialect = new SqlServerDialect();
        var table = new SchemaTable("app", "Tasks", null, new[] { Col("Status", 1) });
        var ix = new SchemaIndex("IX_Tasks_Status",
            new[] { new IndexColumn("Status", false) }, null, IsUnique: false,
            IndexType: "NONCLUSTERED", FilterExpression: null);

        var sql = dialect.CreateIndex(table, ix);

        Assert.Contains("WITH (ONLINE = ON)", sql);
        Assert.EndsWith(";", sql.TrimEnd());
        // ONLINE clause must come after the column/INCLUDE/WHERE body, right before the terminator.
        Assert.EndsWith("WITH (ONLINE = ON);", sql.TrimEnd());
    }

    [Fact]
    public void CreateIndex_online_on_with_included_and_filtered()
    {
        var dialect = new SqlServerDialect();
        var table = new SchemaTable("app", "Tasks", null, new[] { Col("Status", 1) });
        var ix = new SchemaIndex("IX_Tasks_Filtered",
            new[] { new IndexColumn("Status", true) },
            IncludedColumns: new[] { "Priority" },
            IsUnique: true, IndexType: "NONCLUSTERED",
            FilterExpression: "[Status] IS NOT NULL");

        var sql = dialect.CreateIndex(table, ix);

        Assert.Contains("INCLUDE ([Priority])", sql);
        Assert.Contains("WHERE [Status] IS NOT NULL", sql);
        Assert.EndsWith("WITH (ONLINE = ON);", sql.TrimEnd());
    }

    [Theory]
    [InlineData("CLUSTERED COLUMNSTORE")]
    [InlineData("NONCLUSTERED COLUMNSTORE")]
    [InlineData("XML")]
    [InlineData("SPATIAL")]
    [InlineData("NONCLUSTERED HASH")]
    public void CreateIndex_omits_online_on_for_non_rowstore_types(string indexType)
    {
        // ONLINE = ON is rejected by SQL Server for columnstore / XML / spatial / hash
        // indexes, so it must never be appended for those.
        var dialect = new SqlServerDialect();
        var table = new SchemaTable("app", "Tasks", null, new[] { Col("Status", 1) });
        var ix = new SchemaIndex("IX_Tasks_Special",
            new[] { new IndexColumn("Status", false) }, null, IsUnique: false,
            IndexType: indexType, FilterExpression: null);

        var sql = dialect.CreateIndex(table, ix);

        Assert.DoesNotContain("ONLINE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generated_migration_for_added_index_contains_online_on()
    {
        var cols = new[] { Col("Id", 1), Col("Status", 2) };
        var pk = new PrimaryKeyInfo("PK_Tasks", new[] { "Id" }, "CLUSTERED");
        var baseTable = new SchemaTable("app", "Tasks", null, cols, pk, null, null, Indexes: null);
        var ix = new SchemaIndex("IX_Tasks_Status",
            new[] { new IndexColumn("Status", false) }, null, false, "NONCLUSTERED", null);
        var targetTable = baseTable with { Indexes = new[] { ix } };

        var baseline = new DatabaseSchema("db", DatabaseProvider.SqlServer, DateTime.UtcNow,
            new[] { baseTable }, Array.Empty<SchemaView>(), Array.Empty<StoredProcedure>(), Array.Empty<ForeignKeyRelation>());
        var target = new DatabaseSchema("db", DatabaseProvider.SqlServer, DateTime.UtcNow,
            new[] { targetTable }, Array.Empty<SchemaView>(), Array.Empty<StoredProcedure>(), Array.Empty<ForeignKeyRelation>());

        var diff = new SchemaDiffService().Compare(baseline, target);
        var dialect = MigrationScriptGenerator.DialectFor(DatabaseProvider.SqlServer);
        var actions = MigrationScriptGenerator.BuildActions(baseline, target, diff, dialect);
        var script = MigrationScriptGenerator.AssembleScript(actions, dialect, "baseline", "target");

        Assert.Contains("IX_Tasks_Status", script);
        Assert.Contains("WITH (ONLINE = ON)", script);
    }
}
