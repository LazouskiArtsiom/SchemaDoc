using Dapper;
using Microsoft.Data.SqlClient;
using SchemaDoc.Core.Interfaces;
using SchemaDoc.Core.Models;

namespace SchemaDoc.Extraction.Extractors;

public class SqlServerExtractor : ISchemaExtractor
{
    public async Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(connectionString);
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
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        var dbName = conn.Database;

        var columns = (await conn.QueryAsync<RawColumn>(ColumnsQuery)).ToList();
        var fks = (await conn.QueryAsync<RawForeignKey>(ForeignKeysQuery)).ToList();
        var views = (await conn.QueryAsync<RawView>(ViewsQuery)).ToList();
        var procs = (await conn.QueryAsync<RawProc>(ProcsQuery)).ToList();
        var procParams = (await conn.QueryAsync<RawProcParam>(ProcParamsQuery)).ToList();
        var rowCounts = (await conn.QueryAsync<RawRowCount>(RowCountsQuery))
            .ToDictionary(r => (r.SchemaName, r.TableName), r => r.RowCount);

        var tables = BuildTables(columns, fks, rowCounts);
        var schemaViews = BuildViews(views);
        var storedProcs = BuildProcs(procs, procParams);
        var foreignKeys = BuildForeignKeys(fks);

        return new DatabaseSchema(
            DatabaseName: dbName,
            Provider: DatabaseProvider.SqlServer,
            ExtractedAt: DateTime.UtcNow,
            Tables: tables,
            Views: schemaViews,
            StoredProcedures: storedProcs,
            ForeignKeys: foreignKeys
        );
    }

    // ── Build helpers ────────────────────────────────────────────────────────

    private static List<SchemaTable> BuildTables(List<RawColumn> columns, List<RawForeignKey> fks, Dictionary<(string, string), long> rowCounts)
    {
        var fkSet = fks
            .Select(f => (f.ParentSchema, f.ParentTable, f.ParentColumn))
            .ToHashSet();

        return columns
            .GroupBy(c => (c.SchemaName, c.TableName))
            .Select(g => new SchemaTable(
                Schema: g.Key.SchemaName,
                Name: g.Key.TableName,
                RowCount: rowCounts.TryGetValue((g.Key.SchemaName, g.Key.TableName), out var rc) ? rc : null,
                Columns: g.OrderBy(c => c.ColumnId).Select(c => new SchemaColumn(
                    Name: c.ColumnName,
                    OrdinalPosition: c.ColumnId,
                    DataType: c.DataType,
                    MaxLength: c.MaxLength > 0 && c.MaxLength != -1 ? c.MaxLength.ToString() : (c.MaxLength == -1 ? "MAX" : null),
                    NumericPrecision: c.NumericPrecision > 0 ? c.NumericPrecision : null,
                    NumericScale: c.NumericScale >= 0 ? c.NumericScale : null,
                    IsNullable: c.IsNullable,
                    IsPrimaryKey: c.IsPrimaryKey,
                    IsForeignKey: fkSet.Contains((c.SchemaName, c.TableName, c.ColumnName)),
                    IsIdentity: c.IsIdentity,
                    IsComputed: c.IsComputed,
                    DefaultValue: c.DefaultValue,
                    DbNativeComment: c.MsDescription
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
                Columns: g.OrderBy(v => v.ColumnId).Select(v => new SchemaColumn(
                    Name: v.ColumnName,
                    OrdinalPosition: v.ColumnId,
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
                    Direction: param.IsOutput ? "OUT" : "IN",
                    IsOptional: param.HasDefaultValue
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
        public int ColumnId { get; set; }
        public string ColumnName { get; set; } = "";
        public string DataType { get; set; } = "";
        public short MaxLength { get; set; }
        public byte NumericPrecision { get; set; }
        public int NumericScale { get; set; }
        public bool IsNullable { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsIdentity { get; set; }
        public bool IsComputed { get; set; }
        public string? DefaultValue { get; set; }
        public string? MsDescription { get; set; }
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
        public int ColumnId { get; set; }
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
        public bool IsOutput { get; set; }
        public bool HasDefaultValue { get; set; }
    }

    private class RawRowCount
    {
        public string SchemaName { get; set; } = "";
        public string TableName { get; set; } = "";
        public long RowCount { get; set; }
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    private const string ColumnsQuery = """
        SELECT
            s.name                                          AS SchemaName,
            t.name                                          AS TableName,
            c.column_id                                     AS ColumnId,
            c.name                                          AS ColumnName,
            tp.name                                         AS DataType,
            c.max_length                                    AS MaxLength,
            c.precision                                     AS NumericPrecision,
            c.scale                                         AS NumericScale,
            c.is_nullable                                   AS IsNullable,
            CAST(ISNULL(pk.is_primary_key, 0) AS BIT)      AS IsPrimaryKey,
            c.is_identity                                   AS IsIdentity,
            c.is_computed                                   AS IsComputed,
            dc.definition                                   AS DefaultValue,
            CAST(ep.value AS NVARCHAR(4000))                AS MsDescription
        FROM sys.tables t
        JOIN sys.schemas s       ON t.schema_id = s.schema_id
        JOIN sys.columns c       ON t.object_id = c.object_id
        JOIN sys.types tp        ON c.user_type_id = tp.user_type_id
        LEFT JOIN sys.default_constraints dc
                                 ON dc.parent_object_id = t.object_id
                                 AND dc.parent_column_id = c.column_id
        LEFT JOIN (
            SELECT ic.object_id, ic.column_id, idx.is_primary_key
            FROM sys.index_columns ic
            JOIN sys.indexes idx ON idx.object_id = ic.object_id
                                AND idx.index_id = ic.index_id
                                AND idx.is_primary_key = 1
        ) pk ON pk.object_id = t.object_id AND pk.column_id = c.column_id
        LEFT JOIN sys.extended_properties ep
                                 ON ep.major_id = t.object_id
                                 AND ep.minor_id = c.column_id
                                 AND ep.name = 'MS_Description'
                                 AND ep.class = 1
        ORDER BY s.name, t.name, c.column_id
        """;

    private const string ForeignKeysQuery = """
        SELECT
            fk.name                                 AS ConstraintName,
            ps.name                                 AS ParentSchema,
            pt.name                                 AS ParentTable,
            pc.name                                 AS ParentColumn,
            rs.name                                 AS ReferencedSchema,
            rt.name                                 AS ReferencedTable,
            rc.name                                 AS ReferencedColumn,
            fk.delete_referential_action_desc       AS OnDelete,
            fk.update_referential_action_desc       AS OnUpdate
        FROM sys.foreign_keys fk
        JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
        JOIN sys.tables pt  ON fkc.parent_object_id = pt.object_id
        JOIN sys.schemas ps ON pt.schema_id = ps.schema_id
        JOIN sys.columns pc ON fkc.parent_object_id = pc.object_id AND fkc.parent_column_id = pc.column_id
        JOIN sys.tables rt  ON fkc.referenced_object_id = rt.object_id
        JOIN sys.schemas rs ON rt.schema_id = rs.schema_id
        JOIN sys.columns rc ON fkc.referenced_object_id = rc.object_id AND fkc.referenced_column_id = rc.column_id
        """;

    private const string ViewsQuery = """
        SELECT
            s.name          AS SchemaName,
            v.name          AS ViewName,
            sm.definition   AS Definition,
            c.column_id     AS ColumnId,
            c.name          AS ColumnName,
            tp.name         AS DataType,
            c.is_nullable   AS IsNullable
        FROM sys.views v
        JOIN sys.schemas s  ON v.schema_id = s.schema_id
        JOIN sys.columns c  ON v.object_id = c.object_id
        JOIN sys.types tp   ON c.user_type_id = tp.user_type_id
        LEFT JOIN sys.sql_modules sm ON v.object_id = sm.object_id
        ORDER BY s.name, v.name, c.column_id
        """;

    private const string ProcsQuery = """
        SELECT
            s.name          AS SchemaName,
            p.name          AS ProcName,
            sm.definition   AS Definition
        FROM sys.procedures p
        JOIN sys.schemas s      ON p.schema_id = s.schema_id
        LEFT JOIN sys.sql_modules sm ON p.object_id = sm.object_id
        ORDER BY s.name, p.name
        """;

    private const string RowCountsQuery = """
        SELECT
            s.name          AS SchemaName,
            t.name          AS TableName,
            SUM(p.[rows])   AS [RowCount]
        FROM sys.tables t
        JOIN sys.schemas s    ON t.schema_id = s.schema_id
        JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
        GROUP BY s.name, t.name
        """;

    private const string ProcParamsQuery = """
        SELECT
            s.name              AS SchemaName,
            p.name              AS ProcName,
            param.name          AS ParamName,
            tp.name             AS DataType,
            param.is_output     AS IsOutput,
            param.has_default_value AS HasDefaultValue
        FROM sys.procedures p
        JOIN sys.schemas s          ON p.schema_id = s.schema_id
        JOIN sys.parameters param   ON p.object_id = param.object_id
        JOIN sys.types tp           ON param.user_type_id = tp.user_type_id
        WHERE param.parameter_id > 0
        ORDER BY s.name, p.name, param.parameter_id
        """;
}
