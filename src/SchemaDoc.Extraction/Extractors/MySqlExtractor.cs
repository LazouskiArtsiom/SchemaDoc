using Dapper;
using MySqlConnector;
using SchemaDoc.Core.Interfaces;
using SchemaDoc.Core.Models;

namespace SchemaDoc.Extraction.Extractors;

public class MySqlExtractor : ISchemaExtractor
{
    public async Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new MySqlConnection(connectionString);
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
        await using var conn = new MySqlConnection(connectionString);
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
            Provider: DatabaseProvider.MySql,
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
}
