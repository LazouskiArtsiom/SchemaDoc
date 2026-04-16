using System.Text;
using SchemaDoc.Core.Models;

namespace SchemaDoc.Core.Services.Migration;

public class SqlServerDialect : ISqlDialect
{
    public DatabaseProvider Provider => DatabaseProvider.SqlServer;
    public string StatementTerminator => "GO";
    public string? BeginTransaction => "BEGIN TRANSACTION;";
    public string? CommitTransaction => "COMMIT TRANSACTION;";

    public string QuoteId(string identifier) => $"[{identifier.Replace("]", "]]")}]";
    public string QuoteSchemaTable(string schema, string table) => $"{QuoteId(schema)}.{QuoteId(table)}";

    public string RenderColumnType(SchemaColumn col)
    {
        var t = col.DataType.ToLowerInvariant();

        // Length-parameterized types
        if (t is "nvarchar" or "varchar" or "char" or "nchar" or "varbinary" or "binary")
        {
            var len = col.MaxLength == "MAX" ? "MAX" : col.MaxLength ?? "255";
            return $"{col.DataType.ToUpperInvariant()}({len})";
        }

        // Decimal/numeric
        if (t is "decimal" or "numeric" && col.NumericPrecision.HasValue)
        {
            var scale = col.NumericScale ?? 0;
            return $"{col.DataType.ToUpperInvariant()}({col.NumericPrecision},{scale})";
        }

        return col.DataType.ToUpperInvariant();
    }

    private string RenderColumnDefinition(SchemaColumn col)
    {
        var sb = new StringBuilder();
        sb.Append(QuoteId(col.Name)).Append(' ').Append(RenderColumnType(col));

        if (col.IsIdentity)
            sb.Append(" IDENTITY(1,1)");

        sb.Append(col.IsNullable ? " NULL" : " NOT NULL");

        if (!string.IsNullOrEmpty(col.DefaultValue))
        {
            // SQL Server default is stored with surrounding parens, keep as-is if already wrapped
            var def = col.DefaultValue.Trim();
            sb.Append(" DEFAULT ").Append(def);
        }

        return sb.ToString();
    }

    public string CreateTable(SchemaTable table, IEnumerable<ForeignKeyGroup> foreignKeys)
    {
        var sb = new StringBuilder();
        sb.Append("CREATE TABLE ").Append(QuoteSchemaTable(table.Schema, table.Name)).AppendLine(" (");

        var lines = new List<string>();
        foreach (var c in table.Columns.OrderBy(c => c.OrdinalPosition))
            lines.Add("    " + RenderColumnDefinition(c));

        if (table.PrimaryKey is not null)
        {
            var cols = string.Join(", ", table.PrimaryKey.Columns.Select(QuoteId));
            lines.Add($"    CONSTRAINT {QuoteId(table.PrimaryKey.Name)} PRIMARY KEY ({cols})");
        }

        if (table.UniqueConstraints is not null)
            foreach (var u in table.UniqueConstraints)
            {
                var cols = string.Join(", ", u.Columns.Select(QuoteId));
                lines.Add($"    CONSTRAINT {QuoteId(u.Name)} UNIQUE ({cols})");
            }

        if (table.CheckConstraints is not null)
            foreach (var ck in table.CheckConstraints)
                lines.Add($"    CONSTRAINT {QuoteId(ck.Name)} CHECK {ck.Expression}");

        sb.AppendLine(string.Join(",\r\n", lines));
        sb.Append(");");
        return sb.ToString();
    }

    public string DropTable(SchemaTable table)
        => $"DROP TABLE {QuoteSchemaTable(table.Schema, table.Name)};";

    public string AddColumn(SchemaTable table, SchemaColumn column)
        => $"ALTER TABLE {QuoteSchemaTable(table.Schema, table.Name)} ADD {RenderColumnDefinition(column)};";

    public string DropColumn(SchemaTable table, SchemaColumn column)
        => $"ALTER TABLE {QuoteSchemaTable(table.Schema, table.Name)} DROP COLUMN {QuoteId(column.Name)};";

    public string AlterColumn(SchemaTable table, SchemaColumn baseline, SchemaColumn current)
    {
        var nullability = current.IsNullable ? "NULL" : "NOT NULL";
        return $"ALTER TABLE {QuoteSchemaTable(table.Schema, table.Name)} ALTER COLUMN {QuoteId(current.Name)} {RenderColumnType(current)} {nullability};";
    }

    public string AddPrimaryKey(SchemaTable table, PrimaryKeyInfo pk)
    {
        var cols = string.Join(", ", pk.Columns.Select(QuoteId));
        return $"ALTER TABLE {QuoteSchemaTable(table.Schema, table.Name)} ADD CONSTRAINT {QuoteId(pk.Name)} PRIMARY KEY ({cols});";
    }

    public string DropPrimaryKey(SchemaTable table, PrimaryKeyInfo pk)
        => $"ALTER TABLE {QuoteSchemaTable(table.Schema, table.Name)} DROP CONSTRAINT {QuoteId(pk.Name)};";

    public string AddUniqueConstraint(SchemaTable table, UniqueConstraint uq)
    {
        var cols = string.Join(", ", uq.Columns.Select(QuoteId));
        return $"ALTER TABLE {QuoteSchemaTable(table.Schema, table.Name)} ADD CONSTRAINT {QuoteId(uq.Name)} UNIQUE ({cols});";
    }

    public string DropUniqueConstraint(SchemaTable table, UniqueConstraint uq)
        => $"ALTER TABLE {QuoteSchemaTable(table.Schema, table.Name)} DROP CONSTRAINT {QuoteId(uq.Name)};";

    public string AddCheckConstraint(SchemaTable table, CheckConstraint ck)
        => $"ALTER TABLE {QuoteSchemaTable(table.Schema, table.Name)} ADD CONSTRAINT {QuoteId(ck.Name)} CHECK {ck.Expression};";

    public string DropCheckConstraint(SchemaTable table, CheckConstraint ck)
        => $"ALTER TABLE {QuoteSchemaTable(table.Schema, table.Name)} DROP CONSTRAINT {QuoteId(ck.Name)};";

    public string CreateIndex(SchemaTable table, SchemaIndex ix)
    {
        var unique = ix.IsUnique ? "UNIQUE " : "";
        var kind = (ix.IndexType ?? "").ToUpperInvariant() switch
        {
            "CLUSTERED" => "CLUSTERED ",
            "NONCLUSTERED" => "NONCLUSTERED ",
            _ => ""
        };
        var cols = string.Join(", ", ix.Columns.Select(c => c.IsDescending ? $"{QuoteId(c.Name)} DESC" : QuoteId(c.Name)));
        var include = ix.IncludedColumns is { Count: > 0 }
            ? " INCLUDE (" + string.Join(", ", ix.IncludedColumns.Select(QuoteId)) + ")"
            : "";
        var filter = !string.IsNullOrEmpty(ix.FilterExpression) ? $" WHERE {ix.FilterExpression}" : "";
        return $"CREATE {unique}{kind}INDEX {QuoteId(ix.Name)} ON {QuoteSchemaTable(table.Schema, table.Name)} ({cols}){include}{filter};";
    }

    public string DropIndex(SchemaTable table, SchemaIndex ix)
        => $"DROP INDEX {QuoteId(ix.Name)} ON {QuoteSchemaTable(table.Schema, table.Name)};";

    public string AddForeignKey(ForeignKeyGroup fk)
    {
        var parentCols = string.Join(", ", fk.ParentColumns.Select(QuoteId));
        var refCols = string.Join(", ", fk.ReferencedColumns.Select(QuoteId));
        var onDelete = MapAction(fk.OnDelete);
        var onUpdate = MapAction(fk.OnUpdate);
        var clauses = "";
        if (onDelete is not null) clauses += $" ON DELETE {onDelete}";
        if (onUpdate is not null) clauses += $" ON UPDATE {onUpdate}";
        return $"ALTER TABLE {QuoteSchemaTable(fk.ParentSchema, fk.ParentTable)} " +
               $"ADD CONSTRAINT {QuoteId(fk.ConstraintName)} FOREIGN KEY ({parentCols}) " +
               $"REFERENCES {QuoteSchemaTable(fk.ReferencedSchema, fk.ReferencedTable)} ({refCols}){clauses};";
    }

    public string DropForeignKey(ForeignKeyGroup fk)
        => $"ALTER TABLE {QuoteSchemaTable(fk.ParentSchema, fk.ParentTable)} DROP CONSTRAINT {QuoteId(fk.ConstraintName)};";

    public string CreateTrigger(SchemaTrigger trg)
    {
        // SQL Server already provides the full CREATE TRIGGER definition.
        if (!string.IsNullOrWhiteSpace(trg.Definition))
            return trg.Definition.Trim().TrimEnd(';') + ";";

        return $"-- Trigger {trg.FullName}: original definition unavailable";
    }

    public string DropTrigger(SchemaTrigger trg)
        => $"DROP TRIGGER {QuoteId(trg.Schema)}.{QuoteId(trg.Name)};";

    private static string? MapAction(string? action) => action?.ToUpperInvariant() switch
    {
        null or "" or "NO_ACTION" or "NO ACTION" => null,
        "CASCADE" => "CASCADE",
        "SET_NULL" or "SET NULL" => "SET NULL",
        "SET_DEFAULT" or "SET DEFAULT" => "SET DEFAULT",
        _ => null
    };
}
