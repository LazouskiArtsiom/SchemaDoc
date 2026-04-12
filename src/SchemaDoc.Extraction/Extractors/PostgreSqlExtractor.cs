using Dapper;
using Npgsql;
using SchemaDoc.Core.Interfaces;
using SchemaDoc.Core.Models;

namespace SchemaDoc.Extraction.Extractors;

public class PostgreSqlExtractor : ISchemaExtractor
{
    public async Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
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

        var tables = BuildTables(columns, fks);
        var schemaViews = BuildViews(views);
        var storedProcs = BuildProcs(procs, procParams);
        var foreignKeys = BuildForeignKeys(fks);

        return new DatabaseSchema(
            DatabaseName: dbName,
            Provider: DatabaseProvider.PostgreSql,
            ExtractedAt: DateTime.UtcNow,
            Tables: tables,
            Views: schemaViews,
            StoredProcedures: storedProcs,
            ForeignKeys: foreignKeys
        );
    }

    // ── Build helpers ────────────────────────────────────────────────────────

    private static List<SchemaTable> BuildTables(List<RawColumn> columns, List<RawForeignKey> fks)
    {
        var fkSet = fks
            .Select(f => (f.ParentSchema, f.ParentTable, f.ParentColumn))
            .ToHashSet();

        return columns
            .GroupBy(c => (c.SchemaName, c.TableName))
            .Select(g => new SchemaTable(
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
                )).ToList()
            ))
            .OrderBy(t => t.Schema).ThenBy(t => t.Name)
            .ToList();
    }

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
}
