using Dapper;
using MySqlConnector;
using SchemaDoc.Core.Interfaces;
using SchemaDoc.Core.Models;

namespace SchemaDoc.Extraction.Extractors;

public class MySqlExtractor : ISchemaExtractor
{
    public async Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(connectionString);
        await conn.OpenAsync(ct);
        return true;
    }

    public async Task<DatabaseSchema> ExtractAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(connectionString);
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
            Provider: DatabaseProvider.MySql,
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
                        FilterExpression: null);
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
                    IsOptional: false
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
        public long CharacterMaximumLength { get; set; }
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
            c.TABLE_SCHEMA                              AS SchemaName,
            c.TABLE_NAME                                AS TableName,
            c.ORDINAL_POSITION                          AS OrdinalPosition,
            c.COLUMN_NAME                               AS ColumnName,
            c.DATA_TYPE                                 AS DataType,
            COALESCE(c.CHARACTER_MAXIMUM_LENGTH, 0)     AS CharacterMaximumLength,
            COALESCE(c.NUMERIC_PRECISION, 0)            AS NumericPrecision,
            COALESCE(c.NUMERIC_SCALE, 0)                AS NumericScale,
            CASE c.IS_NULLABLE WHEN 'YES' THEN 1 ELSE 0 END AS IsNullable,
            CASE WHEN c.COLUMN_KEY = 'PRI' THEN 1 ELSE 0 END AS IsPrimaryKey,
            CASE WHEN c.EXTRA LIKE '%auto_increment%' THEN 1 ELSE 0 END AS IsIdentity,
            CASE WHEN c.GENERATION_EXPRESSION IS NOT NULL AND c.GENERATION_EXPRESSION <> '' THEN 1 ELSE 0 END AS IsComputed,
            c.COLUMN_DEFAULT                            AS ColumnDefault,
            c.COLUMN_COMMENT                            AS ColumnComment
        FROM INFORMATION_SCHEMA.COLUMNS c
        JOIN INFORMATION_SCHEMA.TABLES t
            ON t.TABLE_SCHEMA = c.TABLE_SCHEMA
            AND t.TABLE_NAME = c.TABLE_NAME
            AND t.TABLE_TYPE = 'BASE TABLE'
        WHERE c.TABLE_SCHEMA = DATABASE()
        ORDER BY c.TABLE_SCHEMA, c.TABLE_NAME, c.ORDINAL_POSITION
        """;

    private const string ForeignKeysQuery = """
        SELECT
            kcu.CONSTRAINT_NAME                         AS ConstraintName,
            kcu.TABLE_SCHEMA                            AS ParentSchema,
            kcu.TABLE_NAME                              AS ParentTable,
            kcu.COLUMN_NAME                             AS ParentColumn,
            kcu.REFERENCED_TABLE_SCHEMA                 AS ReferencedSchema,
            kcu.REFERENCED_TABLE_NAME                   AS ReferencedTable,
            kcu.REFERENCED_COLUMN_NAME                  AS ReferencedColumn,
            rc.DELETE_RULE                               AS OnDelete,
            rc.UPDATE_RULE                              AS OnUpdate
        FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
        JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
            ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
            AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA
            AND tc.CONSTRAINT_TYPE = 'FOREIGN KEY'
        JOIN INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
            ON rc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
            AND rc.CONSTRAINT_SCHEMA = kcu.TABLE_SCHEMA
        WHERE kcu.TABLE_SCHEMA = DATABASE()
          AND kcu.REFERENCED_TABLE_NAME IS NOT NULL
        """;

    private const string ViewsQuery = """
        SELECT
            v.TABLE_SCHEMA                              AS SchemaName,
            v.TABLE_NAME                                AS ViewName,
            v.VIEW_DEFINITION                           AS Definition,
            c.ORDINAL_POSITION                          AS OrdinalPosition,
            c.COLUMN_NAME                               AS ColumnName,
            c.DATA_TYPE                                 AS DataType,
            CASE c.IS_NULLABLE WHEN 'YES' THEN 1 ELSE 0 END AS IsNullable
        FROM INFORMATION_SCHEMA.VIEWS v
        JOIN INFORMATION_SCHEMA.COLUMNS c
            ON c.TABLE_SCHEMA = v.TABLE_SCHEMA
            AND c.TABLE_NAME = v.TABLE_NAME
        WHERE v.TABLE_SCHEMA = DATABASE()
        ORDER BY v.TABLE_SCHEMA, v.TABLE_NAME, c.ORDINAL_POSITION
        """;

    private const string ProcsQuery = """
        SELECT
            r.ROUTINE_SCHEMA                            AS SchemaName,
            r.ROUTINE_NAME                              AS ProcName,
            r.ROUTINE_DEFINITION                        AS Definition
        FROM INFORMATION_SCHEMA.ROUTINES r
        WHERE r.ROUTINE_SCHEMA = DATABASE()
          AND r.ROUTINE_TYPE IN ('PROCEDURE', 'FUNCTION')
        ORDER BY r.ROUTINE_SCHEMA, r.ROUTINE_NAME
        """;

    private const string ProcParamsQuery = """
        SELECT
            r.ROUTINE_SCHEMA                            AS SchemaName,
            r.ROUTINE_NAME                              AS ProcName,
            p.PARAMETER_NAME                            AS ParamName,
            p.DATA_TYPE                                 AS DataType,
            p.PARAMETER_MODE                            AS ParameterMode
        FROM INFORMATION_SCHEMA.ROUTINES r
        JOIN INFORMATION_SCHEMA.PARAMETERS p
            ON p.SPECIFIC_SCHEMA = r.ROUTINE_SCHEMA
            AND p.SPECIFIC_NAME = r.ROUTINE_NAME
        WHERE r.ROUTINE_SCHEMA = DATABASE()
          AND r.ROUTINE_TYPE IN ('PROCEDURE', 'FUNCTION')
          AND p.ORDINAL_POSITION > 0
        ORDER BY r.ROUTINE_SCHEMA, r.ROUTINE_NAME, p.ORDINAL_POSITION
        """;

    private const string PrimaryKeysQuery = """
        SELECT
            s.TABLE_SCHEMA                              AS SchemaName,
            s.TABLE_NAME                                AS TableName,
            s.INDEX_NAME                                AS ConstraintName,
            s.COLUMN_NAME                               AS ColumnName,
            s.SEQ_IN_INDEX                              AS KeyOrdinal
        FROM INFORMATION_SCHEMA.STATISTICS s
        WHERE s.TABLE_SCHEMA = DATABASE()
          AND s.INDEX_NAME = 'PRIMARY'
        ORDER BY s.TABLE_SCHEMA, s.TABLE_NAME, s.SEQ_IN_INDEX
        """;

    private const string UniqueConstraintsQuery = """
        SELECT
            s.TABLE_SCHEMA                              AS SchemaName,
            s.TABLE_NAME                                AS TableName,
            s.INDEX_NAME                                AS ConstraintName,
            s.COLUMN_NAME                               AS ColumnName,
            s.SEQ_IN_INDEX                              AS KeyOrdinal
        FROM INFORMATION_SCHEMA.STATISTICS s
        WHERE s.TABLE_SCHEMA = DATABASE()
          AND s.NON_UNIQUE = 0
          AND s.INDEX_NAME <> 'PRIMARY'
          AND EXISTS (
              SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
              WHERE tc.TABLE_SCHEMA = s.TABLE_SCHEMA
                AND tc.TABLE_NAME = s.TABLE_NAME
                AND tc.CONSTRAINT_NAME = s.INDEX_NAME
                AND tc.CONSTRAINT_TYPE = 'UNIQUE'
          )
        ORDER BY s.TABLE_SCHEMA, s.TABLE_NAME, s.INDEX_NAME, s.SEQ_IN_INDEX
        """;

    private const string CheckConstraintsQuery = """
        SELECT
            tc.TABLE_SCHEMA                             AS SchemaName,
            tc.TABLE_NAME                               AS TableName,
            cc.CONSTRAINT_NAME                          AS ConstraintName,
            cc.CHECK_CLAUSE                             AS Expression
        FROM INFORMATION_SCHEMA.CHECK_CONSTRAINTS cc
        JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
            ON tc.CONSTRAINT_NAME = cc.CONSTRAINT_NAME
            AND tc.CONSTRAINT_SCHEMA = cc.CONSTRAINT_SCHEMA
            AND tc.CONSTRAINT_TYPE = 'CHECK'
        WHERE tc.TABLE_SCHEMA = DATABASE()
        ORDER BY tc.TABLE_SCHEMA, tc.TABLE_NAME, cc.CONSTRAINT_NAME
        """;

    private const string IndexesQuery = """
        SELECT
            s.TABLE_SCHEMA                              AS SchemaName,
            s.TABLE_NAME                                AS TableName,
            s.INDEX_NAME                                AS IndexName,
            s.COLUMN_NAME                               AS ColumnName,
            s.SEQ_IN_INDEX                              AS KeyOrdinal,
            CASE WHEN s.COLLATION = 'D' THEN 1 ELSE 0 END AS IsDescending,
            CASE WHEN s.NON_UNIQUE = 0 THEN 1 ELSE 0 END AS IsUnique,
            CASE WHEN s.INDEX_NAME = 'PRIMARY' THEN 1 ELSE 0 END AS IsPrimaryKey,
            CASE WHEN s.NON_UNIQUE = 0 AND s.INDEX_NAME <> 'PRIMARY' THEN 1 ELSE 0 END AS IsUniqueConstraint,
            s.INDEX_TYPE                                AS IndexType
        FROM INFORMATION_SCHEMA.STATISTICS s
        WHERE s.TABLE_SCHEMA = DATABASE()
        ORDER BY s.TABLE_SCHEMA, s.TABLE_NAME, s.INDEX_NAME, s.SEQ_IN_INDEX
        """;

    private const string TriggersQuery = """
        SELECT
            t.TRIGGER_SCHEMA                            AS TriggerSchema,
            t.TRIGGER_NAME                              AS TriggerName,
            t.EVENT_OBJECT_SCHEMA                       AS TableSchema,
            t.EVENT_OBJECT_TABLE                        AS TableName,
            t.EVENT_MANIPULATION                        AS EventType,
            t.ACTION_TIMING                             AS Timing,
            t.ACTION_STATEMENT                          AS Definition
        FROM INFORMATION_SCHEMA.TRIGGERS t
        WHERE t.TRIGGER_SCHEMA = DATABASE()
        ORDER BY t.EVENT_OBJECT_SCHEMA, t.EVENT_OBJECT_TABLE, t.TRIGGER_NAME
        """;
}
