using Dapper;
using Npgsql;
using SchemaDoc.Core.Interfaces;
using SchemaDoc.Core.Models;

namespace SchemaDoc.Extraction.Extractors;

public class PostgreSqlExtractor : ISchemaExtractor
{
    public async Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<string>> ListDatabasesAsync(string connectionString, CancellationToken ct = default)
    {
        // Connect to "postgres" admin DB which is usually accessible to any user.
        var rerouted = SwitchDatabase(connectionString, "postgres");
        try
        {
            await using var conn = new NpgsqlConnection(rerouted);
            await conn.OpenAsync(ct);
            var dbs = (await conn.QueryAsync<string>(
                """
                SELECT datname FROM pg_database
                WHERE datistemplate = false
                  AND datallowconn = true
                  AND has_database_privilege(datname, 'CONNECT')
                ORDER BY datname
                """)).ToList();
            return dbs;
        }
        catch
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return string.IsNullOrEmpty(builder.Database) ? [] : [builder.Database];
        }
    }

    public string SwitchDatabase(string connectionString, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName };
        return builder.ConnectionString;
    }

    public async Task<DatabaseSchema> ExtractAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        var dbName = conn.Database;

        var columns = (await conn.QueryAsync<RawColumn>(ColumnsQuery)).ToList();
        var fks = (await conn.QueryAsync<RawForeignKey>(ForeignKeysQuery)).ToList();
        var views = (await conn.QueryAsync<RawView>(ViewsQuery)).ToList();
        var procs = (await conn.QueryAsync<RawProc>(ProcsQuery)).ToList();
        var procParams = (await conn.QueryAsync<RawProcParam>(ProcParamsQuery)).ToList();

        // Step 1 enhanced diff data
        var primaryKeys = (await conn.QueryAsync<RawPkColumn>(PrimaryKeysQuery)).ToList();
        var uniqueConstraints = (await conn.QueryAsync<RawUniqueColumn>(UniqueConstraintsQuery)).ToList();
        var checkConstraints = (await conn.QueryAsync<RawCheck>(CheckConstraintsQuery)).ToList();
        var indexes = (await conn.QueryAsync<RawIndexColumn>(IndexesQuery)).ToList();
        var triggers = (await conn.QueryAsync<RawTrigger>(TriggersQuery)).ToList();

        var tables = BuildTables(columns, fks, primaryKeys, uniqueConstraints, checkConstraints, indexes);
        var schemaViews = BuildViews(views);
        var storedProcs = BuildProcs(procs, procParams);
        var foreignKeys = BuildForeignKeys(fks);
        var triggerList = BuildTriggers(triggers);

        return new DatabaseSchema(
            DatabaseName: dbName,
            Provider: DatabaseProvider.PostgreSql,
            ExtractedAt: DateTime.UtcNow,
            Tables: tables,
            Views: schemaViews,
            StoredProcedures: storedProcs,
            ForeignKeys: foreignKeys,
            Triggers: triggerList
        );
    }

    // ── Build helpers ────────────────────────────────────────────────────────

    private static List<SchemaTable> BuildTables(
        List<RawColumn> columns,
        List<RawForeignKey> fks,
        List<RawPkColumn> primaryKeys,
        List<RawUniqueColumn> uniqueConstraints,
        List<RawCheck> checkConstraints,
        List<RawIndexColumn> indexes)
    {
        var fkSet = fks
            .Select(f => (f.ParentSchema, f.ParentTable, f.ParentColumn))
            .ToHashSet();

        var pksByTable = primaryKeys
            .GroupBy(p => (p.SchemaName, p.TableName))
            .ToDictionary(g => g.Key, g => new PrimaryKeyInfo(
                Name: g.First().ConstraintName,
                Columns: g.OrderBy(c => c.KeyOrdinal).Select(c => c.ColumnName).ToList(),
                IndexType: "BTREE"));

        var uqByTable = uniqueConstraints
            .GroupBy(u => (u.SchemaName, u.TableName))
            .ToDictionary(g => g.Key, g => g
                .GroupBy(u => u.ConstraintName)
                .Select(cg => new UniqueConstraint(
                    Name: cg.Key,
                    Columns: cg.OrderBy(c => c.KeyOrdinal).Select(c => c.ColumnName).ToList()))
                .ToList());

        var checksByTable = checkConstraints
            .GroupBy(c => (c.SchemaName, c.TableName))
            .ToDictionary(g => g.Key, g => g
                .Select(c => new CheckConstraint(c.ConstraintName, c.Expression))
                .ToList());

        // PG: indexes already filter PK/UQ via is_primary/is_unique_constraint
        var indexesByTable = indexes
            .Where(i => !i.IsPrimaryKey && !i.IsUniqueConstraint)
            .GroupBy(i => (i.SchemaName, i.TableName))
            .ToDictionary(g => g.Key, g => g
                .GroupBy(i => i.IndexName)
                .Select(ig =>
                {
                    var ordered = ig.OrderBy(c => c.KeyOrdinal).ToList();
                    return new SchemaIndex(
                        Name: ig.Key,
                        Columns: ordered.Select(c => new IndexColumn(c.ColumnName, c.IsDescending)).ToList(),
                        IncludedColumns: null,
                        IsUnique: ig.First().IsUnique,
                        IndexType: ig.First().IndexType,
                        FilterExpression: ig.First().FilterDefinition);
                })
                .ToList());

        return columns
            .GroupBy(c => (c.SchemaName, c.TableName))
            .Select(g =>
            {
                var key = (g.Key.SchemaName, g.Key.TableName);
                return new SchemaTable(
                    Schema: g.Key.SchemaName,
                    Name: g.Key.TableName,
                    RowCount: null,
                    Columns: g.OrderBy(c => c.OrdinalPosition).Select(c => new SchemaColumn(
                        Name: c.ColumnName,
                        OrdinalPosition: c.OrdinalPosition,
                        DataType: c.DataType,
                        MaxLength: c.CharacterMaximumLength > 0 ? c.CharacterMaximumLength.ToString() : null,
                        NumericPrecision: c.NumericPrecision > 0 ? c.NumericPrecision : null,
                        NumericScale: c.NumericScale >= 0 ? c.NumericScale : null,
                        IsNullable: c.IsNullable,
                        IsPrimaryKey: c.IsPrimaryKey,
                        IsForeignKey: fkSet.Contains((c.SchemaName, c.TableName, c.ColumnName)),
                        IsIdentity: c.IsIdentity,
                        IsComputed: c.IsComputed,
                        DefaultValue: c.ColumnDefault,
                        DbNativeComment: c.ColumnComment
                    )).ToList(),
                    PrimaryKey: pksByTable.TryGetValue(key, out var pk) ? pk : null,
                    UniqueConstraints: uqByTable.TryGetValue(key, out var uqs) ? uqs : null,
                    CheckConstraints: checksByTable.TryGetValue(key, out var cks) ? cks : null,
                    Indexes: indexesByTable.TryGetValue(key, out var ixs) ? ixs : null
                );
            })
            .OrderBy(t => t.Schema).ThenBy(t => t.Name)
            .ToList();
    }

    private static List<SchemaTrigger> BuildTriggers(List<RawTrigger> triggers) =>
        triggers.Select(t => new SchemaTrigger(
            Schema: t.TriggerSchema,
            Name: t.TriggerName,
            TableSchema: t.TableSchema,
            TableName: t.TableName,
            Event: t.EventType,
            Timing: t.Timing,
            Definition: t.Definition
        ))
        .OrderBy(t => t.TableSchema).ThenBy(t => t.TableName).ThenBy(t => t.Name)
        .ToList();

    private static List<SchemaView> BuildViews(List<RawView> views)
    {
        return views
            .GroupBy(v => (v.SchemaName, v.ViewName, v.Definition))
            .Select(g => new SchemaView(
                Schema: g.Key.SchemaName,
                Name: g.Key.ViewName,
                Definition: g.Key.Definition,
                Columns: g.OrderBy(v => v.OrdinalPosition).Select(v => new SchemaColumn(
                    Name: v.ColumnName,
                    OrdinalPosition: v.OrdinalPosition,
                    DataType: v.DataType,
                    MaxLength: null,
                    NumericPrecision: null,
                    NumericScale: null,
                    IsNullable: v.IsNullable,
                    IsPrimaryKey: false,
                    IsForeignKey: false,
                    IsIdentity: false,
                    IsComputed: false,
                    DefaultValue: null,
                    DbNativeComment: null
                )).ToList()
            ))
            .OrderBy(v => v.Schema).ThenBy(v => v.Name)
            .ToList();
    }

    private static List<StoredProcedure> BuildProcs(List<RawProc> procs, List<RawProcParam> procParams)
    {
        var paramsByProc = procParams
            .GroupBy(p => (p.SchemaName, p.ProcName))
            .ToDictionary(g => g.Key, g => g.ToList());

        return procs.Select(p =>
        {
            var key = (p.SchemaName, p.ProcName);
            var parameters = paramsByProc.TryGetValue(key, out var ps)
                ? ps.Select(param => new ProcParameter(
                    Name: param.ParamName,
                    DataType: param.DataType,
                    Direction: param.ParameterMode switch
                    {
                        "OUT" => "OUT",
                        "INOUT" => "INOUT",
                        _ => "IN"
                    },
                    IsOptional: param.HasDefault
                )).ToList()
                : new List<ProcParameter>();

            return new StoredProcedure(p.SchemaName, p.ProcName, p.Definition, parameters);
        })
        .OrderBy(p => p.Schema).ThenBy(p => p.Name)
        .ToList();
    }

    private static List<ForeignKeyRelation> BuildForeignKeys(List<RawForeignKey> fks) =>
        fks.Select(f => new ForeignKeyRelation(
            ConstraintName: f.ConstraintName,
            ParentSchema: f.ParentSchema,
            ParentTable: f.ParentTable,
            ParentColumn: f.ParentColumn,
            ReferencedSchema: f.ReferencedSchema,
            ReferencedTable: f.ReferencedTable,
            ReferencedColumn: f.ReferencedColumn,
            OnDelete: f.OnDelete,
            OnUpdate: f.OnUpdate
        )).ToList();

    // ── Raw DTOs (classes for Dapper compatibility) ────────────────────────

    private class RawColumn
    {
        public string SchemaName { get; set; } = "";
        public string TableName { get; set; } = "";
        public int OrdinalPosition { get; set; }
        public string ColumnName { get; set; } = "";
        public string DataType { get; set; } = "";
        public int CharacterMaximumLength { get; set; }
        public int NumericPrecision { get; set; }
        public int NumericScale { get; set; }
        public bool IsNullable { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsIdentity { get; set; }
        public bool IsComputed { get; set; }
        public string? ColumnDefault { get; set; }
        public string? ColumnComment { get; set; }
    }

    private class RawForeignKey
    {
        public string ConstraintName { get; set; } = "";
        public string ParentSchema { get; set; } = "";
        public string ParentTable { get; set; } = "";
        public string ParentColumn { get; set; } = "";
        public string ReferencedSchema { get; set; } = "";
        public string ReferencedTable { get; set; } = "";
        public string ReferencedColumn { get; set; } = "";
        public string OnDelete { get; set; } = "";
        public string OnUpdate { get; set; } = "";
    }

    private class RawView
    {
        public string SchemaName { get; set; } = "";
        public string ViewName { get; set; } = "";
        public string? Definition { get; set; }
        public int OrdinalPosition { get; set; }
        public string ColumnName { get; set; } = "";
        public string DataType { get; set; } = "";
        public bool IsNullable { get; set; }
    }

    private class RawProc
    {
        public string SchemaName { get; set; } = "";
        public string ProcName { get; set; } = "";
        public string? Definition { get; set; }
    }

    private class RawProcParam
    {
        public string SchemaName { get; set; } = "";
        public string ProcName { get; set; } = "";
        public string ParamName { get; set; } = "";
        public string DataType { get; set; } = "";
        public string ParameterMode { get; set; } = "";
        public bool HasDefault { get; set; }
    }

    private class RawPkColumn
    {
        public string SchemaName { get; set; } = "";
        public string TableName { get; set; } = "";
        public string ConstraintName { get; set; } = "";
        public string ColumnName { get; set; } = "";
        public int KeyOrdinal { get; set; }
    }

    private class RawUniqueColumn
    {
        public string SchemaName { get; set; } = "";
        public string TableName { get; set; } = "";
        public string ConstraintName { get; set; } = "";
        public string ColumnName { get; set; } = "";
        public int KeyOrdinal { get; set; }
    }

    private class RawCheck
    {
        public string SchemaName { get; set; } = "";
        public string TableName { get; set; } = "";
        public string ConstraintName { get; set; } = "";
        public string Expression { get; set; } = "";
    }

    private class RawIndexColumn
    {
        public string SchemaName { get; set; } = "";
        public string TableName { get; set; } = "";
        public string IndexName { get; set; } = "";
        public string ColumnName { get; set; } = "";
        public int KeyOrdinal { get; set; }
        public bool IsDescending { get; set; }
        public bool IsUnique { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsUniqueConstraint { get; set; }
        public string? IndexType { get; set; }
        public string? FilterDefinition { get; set; }
    }

    private class RawTrigger
    {
        public string TriggerSchema { get; set; } = "";
        public string TriggerName { get; set; } = "";
        public string TableSchema { get; set; } = "";
        public string TableName { get; set; } = "";
        public string EventType { get; set; } = "";
        public string Timing { get; set; } = "";
        public string? Definition { get; set; }
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    private const string ColumnsQuery = """
        SELECT
            c.table_schema                              AS SchemaName,
            c.table_name                                AS TableName,
            c.ordinal_position                          AS OrdinalPosition,
            c.column_name                               AS ColumnName,
            c.udt_name                                  AS DataType,
            COALESCE(c.character_maximum_length, 0)     AS CharacterMaximumLength,
            COALESCE(c.numeric_precision, 0)            AS NumericPrecision,
            COALESCE(c.numeric_scale, 0)                AS NumericScale,
            CASE c.is_nullable WHEN 'YES' THEN true ELSE false END AS IsNullable,
            CASE WHEN pk.column_name IS NOT NULL THEN true ELSE false END AS IsPrimaryKey,
            CASE
                WHEN c.is_identity = 'YES' THEN true
                WHEN c.column_default LIKE 'nextval%' THEN true
                ELSE false
            END AS IsIdentity,
            CASE WHEN c.is_generated = 'ALWAYS' THEN true ELSE false END AS IsComputed,
            c.column_default                            AS ColumnDefault,
            col_description(
                (c.table_schema || '.' || c.table_name)::regclass,
                c.ordinal_position
            )                                           AS ColumnComment
        FROM information_schema.columns c
        JOIN information_schema.tables t
            ON t.table_schema = c.table_schema
            AND t.table_name = c.table_name
            AND t.table_type = 'BASE TABLE'
        LEFT JOIN (
            SELECT kcu.table_schema, kcu.table_name, kcu.column_name
            FROM information_schema.key_column_usage kcu
            JOIN information_schema.table_constraints tc
                ON tc.constraint_name = kcu.constraint_name
                AND tc.table_schema = kcu.table_schema
                AND tc.constraint_type = 'PRIMARY KEY'
        ) pk
            ON pk.table_schema = c.table_schema
            AND pk.table_name = c.table_name
            AND pk.column_name = c.column_name
        WHERE c.table_schema NOT IN ('pg_catalog', 'information_schema')
        ORDER BY c.table_schema, c.table_name, c.ordinal_position
        """;

    private const string ForeignKeysQuery = """
        SELECT
            rc.constraint_name                          AS ConstraintName,
            kcu_p.table_schema                          AS ParentSchema,
            kcu_p.table_name                            AS ParentTable,
            kcu_p.column_name                           AS ParentColumn,
            kcu_r.table_schema                          AS ReferencedSchema,
            kcu_r.table_name                            AS ReferencedTable,
            kcu_r.column_name                           AS ReferencedColumn,
            rc.delete_rule                              AS OnDelete,
            rc.update_rule                              AS OnUpdate
        FROM information_schema.referential_constraints rc
        JOIN information_schema.key_column_usage kcu_p
            ON kcu_p.constraint_name = rc.constraint_name
            AND kcu_p.constraint_schema = rc.constraint_schema
        JOIN information_schema.key_column_usage kcu_r
            ON kcu_r.constraint_name = rc.unique_constraint_name
            AND kcu_r.constraint_schema = rc.unique_constraint_schema
            AND kcu_r.ordinal_position = kcu_p.ordinal_position
        WHERE kcu_p.table_schema NOT IN ('pg_catalog', 'information_schema')
        """;

    private const string ViewsQuery = """
        SELECT
            v.table_schema                              AS SchemaName,
            v.table_name                                AS ViewName,
            pgv.definition                              AS Definition,
            c.ordinal_position                          AS OrdinalPosition,
            c.column_name                               AS ColumnName,
            c.udt_name                                  AS DataType,
            CASE c.is_nullable WHEN 'YES' THEN true ELSE false END AS IsNullable
        FROM information_schema.views v
        JOIN information_schema.columns c
            ON c.table_schema = v.table_schema
            AND c.table_name = v.table_name
        LEFT JOIN pg_catalog.pg_views pgv
            ON pgv.schemaname = v.table_schema
            AND pgv.viewname = v.table_name
        WHERE v.table_schema NOT IN ('pg_catalog', 'information_schema')
        ORDER BY v.table_schema, v.table_name, c.ordinal_position
        """;

    private const string ProcsQuery = """
        SELECT
            r.routine_schema                            AS SchemaName,
            r.routine_name                              AS ProcName,
            r.routine_definition                        AS Definition
        FROM information_schema.routines r
        WHERE r.routine_schema NOT IN ('pg_catalog', 'information_schema')
          AND r.routine_type IN ('PROCEDURE', 'FUNCTION')
        ORDER BY r.routine_schema, r.routine_name
        """;

    private const string ProcParamsQuery = """
        SELECT
            r.routine_schema                            AS SchemaName,
            r.routine_name                              AS ProcName,
            p.parameter_name                            AS ParamName,
            p.udt_name                                  AS DataType,
            p.parameter_mode                            AS ParameterMode,
            CASE WHEN p.parameter_default IS NOT NULL THEN true ELSE false END AS HasDefault
        FROM information_schema.routines r
        JOIN information_schema.parameters p
            ON p.specific_schema = r.specific_schema
            AND p.specific_name = r.specific_name
        WHERE r.routine_schema NOT IN ('pg_catalog', 'information_schema')
          AND r.routine_type IN ('PROCEDURE', 'FUNCTION')
          AND p.ordinal_position > 0
        ORDER BY r.routine_schema, r.routine_name, p.ordinal_position
        """;

    private const string PrimaryKeysQuery = """
        SELECT
            tc.table_schema                             AS SchemaName,
            tc.table_name                               AS TableName,
            tc.constraint_name                          AS ConstraintName,
            kcu.column_name                             AS ColumnName,
            kcu.ordinal_position                        AS KeyOrdinal
        FROM information_schema.table_constraints tc
        JOIN information_schema.key_column_usage kcu
            ON kcu.constraint_name = tc.constraint_name
            AND kcu.table_schema = tc.table_schema
        WHERE tc.constraint_type = 'PRIMARY KEY'
          AND tc.table_schema NOT IN ('pg_catalog', 'information_schema')
        ORDER BY tc.table_schema, tc.table_name, kcu.ordinal_position
        """;

    private const string UniqueConstraintsQuery = """
        SELECT
            tc.table_schema                             AS SchemaName,
            tc.table_name                               AS TableName,
            tc.constraint_name                          AS ConstraintName,
            kcu.column_name                             AS ColumnName,
            kcu.ordinal_position                        AS KeyOrdinal
        FROM information_schema.table_constraints tc
        JOIN information_schema.key_column_usage kcu
            ON kcu.constraint_name = tc.constraint_name
            AND kcu.table_schema = tc.table_schema
        WHERE tc.constraint_type = 'UNIQUE'
          AND tc.table_schema NOT IN ('pg_catalog', 'information_schema')
        ORDER BY tc.table_schema, tc.table_name, tc.constraint_name, kcu.ordinal_position
        """;

    private const string CheckConstraintsQuery = """
        SELECT
            n.nspname                                   AS SchemaName,
            cl.relname                                  AS TableName,
            con.conname                                 AS ConstraintName,
            pg_catalog.pg_get_constraintdef(con.oid, true) AS Expression
        FROM pg_catalog.pg_constraint con
        JOIN pg_catalog.pg_class cl   ON cl.oid = con.conrelid
        JOIN pg_catalog.pg_namespace n ON n.oid = cl.relnamespace
        WHERE con.contype = 'c'
          AND n.nspname NOT IN ('pg_catalog', 'information_schema')
        ORDER BY n.nspname, cl.relname, con.conname
        """;

    private const string IndexesQuery = """
        SELECT
            n.nspname                                   AS SchemaName,
            cl.relname                                  AS TableName,
            ic.relname                                  AS IndexName,
            att.attname                                 AS ColumnName,
            (array_position(ix.indkey, att.attnum))::int AS KeyOrdinal,
            false                                       AS IsDescending,
            ix.indisunique                              AS IsUnique,
            ix.indisprimary                             AS IsPrimaryKey,
            CASE WHEN con.contype = 'u' THEN true ELSE false END AS IsUniqueConstraint,
            am.amname                                   AS IndexType,
            pg_catalog.pg_get_expr(ix.indpred, ix.indrelid) AS FilterDefinition
        FROM pg_catalog.pg_index ix
        JOIN pg_catalog.pg_class cl      ON cl.oid = ix.indrelid
        JOIN pg_catalog.pg_class ic      ON ic.oid = ix.indexrelid
        JOIN pg_catalog.pg_namespace n   ON n.oid = cl.relnamespace
        JOIN pg_catalog.pg_am am         ON am.oid = ic.relam
        JOIN pg_catalog.pg_attribute att
            ON att.attrelid = cl.oid
            AND att.attnum = ANY(ix.indkey)
        LEFT JOIN pg_catalog.pg_constraint con
            ON con.conindid = ix.indexrelid
        WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
        ORDER BY n.nspname, cl.relname, ic.relname, KeyOrdinal
        """;

    private const string TriggersQuery = """
        SELECT
            trg.trigger_schema                          AS TriggerSchema,
            trg.trigger_name                            AS TriggerName,
            trg.event_object_schema                     AS TableSchema,
            trg.event_object_table                      AS TableName,
            string_agg(DISTINCT trg.event_manipulation, ',')  AS EventType,
            trg.action_timing                           AS Timing,
            trg.action_statement                        AS Definition
        FROM information_schema.triggers trg
        WHERE trg.trigger_schema NOT IN ('pg_catalog', 'information_schema')
        GROUP BY trg.trigger_schema, trg.trigger_name, trg.event_object_schema, trg.event_object_table, trg.action_timing, trg.action_statement
        ORDER BY trg.event_object_schema, trg.event_object_table, trg.trigger_name
        """;
}
