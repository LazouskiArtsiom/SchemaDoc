using System.Text;
using SchemaDoc.Core.Models;

namespace SchemaDoc.Core.Services.Migration;

public class PostgreSqlDialect : ISqlDialect
{
    public DatabaseProvider Provider => DatabaseProvider.PostgreSql;
    public string StatementTerminator => "";
    public string? BeginTransaction => "BEGIN;";
    public string? CommitTransaction => "COMMIT;";

    public string QuoteId(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";
    public string QuoteSchemaTable(string schema, string table) =>
        string.IsNullOrEmpty(schema) || schema.Equals("public", StringComparison.OrdinalIgnoreCase)
            ? QuoteId(table)
            : $"{QuoteId(schema)}.{QuoteId(table)}";

    public string RenderColumnType(SchemaColumn col)
    {
        var t = col.DataType.ToLowerInvariant();
        if (t is "varchar" or "char" or "bpchar" && !string.IsNullOrEmpty(col.MaxLength))
            return $"{col.DataType}({col.MaxLength})";
        if (t is "numeric" or "decimal" && col.NumericPrecision.HasValue)
        {
            var scale = col.NumericScale ?? 0;
            return $"{col.DataType}({col.NumericPrecision},{scale})";
        }
        return col.DataType;
    }

    private string RenderColumnDefinition(SchemaColumn col)
    {
        var sb = new StringBuilder();
        sb.Append(QuoteId(col.Name)).Append(' ').Append(RenderColumnType(col));
        sb.Append(col.IsNullable ? " NULL" : " NOT NULL");
        if (!string.IsNullOrEmpty(col.DefaultValue))
            sb.Append(" DEFAULT ").Append(col.DefaultValue);
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
                lines.Add($"    CONSTRAINT {QuoteId(u.Name)} UNIQUE ({string.Join(", ", u.Columns.Select(QuoteId))})");
        if (table.CheckConstraints is not null)
            foreach (var ck in table.CheckConstraints)
                lines.Add($"    CONSTRAINT {QuoteId(ck.Name)} CHECK ({ck.Expression})");
        sb.AppendLine(string.Join(",\r\n", lines));
        sb.Append(");");
        return sb.ToString();
    }

    public string DropTable(SchemaTable table)
        => $"DROP TABLE {QuoteSchemaTable(table.Schema, table.Name)};";

    public string AddColumn(SchemaTable table, SchemaColumn column)
        => $"ALTER TABLE {QuoteSchemaTable(table.Schema, table.Name)} ADD COLUMN {RenderColumnDefinition(column)};";

    public string DropColumn(SchemaTable table, SchemaColumn column)
        => $"ALTER TABLE {QuoteSchemaTable(table.Schema, table.Name)} DROP COLUMN {QuoteId(column.Name)};";

    public string AlterColumn(SchemaTable table, SchemaColumn baseline, SchemaColumn current)
    {
        var sb = new StringBuilder();
        var qTable = QuoteSchemaTable(table.Schema, table.Name);
        var qCol = QuoteId(current.Name);

        if (!string.Equals(RenderColumnType(baseline), RenderColumnType(current), StringComparison.OrdinalIgnoreCase))
            sb.AppendLine($"ALTER TABLE {qTable} ALTER COLUMN {qCol} TYPE {RenderColumnType(current)};");
        if (baseline.IsNullable != current.IsNullable)
            sb.AppendLine(current.IsNullable
                ? $"ALTER TABLE {qTable} ALTER COLUMN {qCol} DROP NOT NULL;"
                : $"ALTER TABLE {qTable} ALTER COLUMN {qCol} SET NOT NULL;");
        if (baseline.DefaultValue != current.DefaultValue)
            sb.AppendLine(string.IsNullOrEmpty(current.DefaultValue)
                ? $"ALTER TABLE {qTable} ALTER COLUMN {qCol} DROP DEFAULT;"
                : $"ALTER TABLE {qTable} ALTER COLUMN {qCol} SET DEFAULT {current.DefaultValue};");
        return sb.ToString().TrimEnd();
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
        => $"ALTER TABLE {QuoteSchemaTable(table.Schema, table.Name)} ADD CONSTRAINT {QuoteId(ck.Name)} CHECK ({ck.Expression});";

    public string DropCheckConstraint(SchemaTable table, CheckConstraint ck)
        => $"ALTER TABLE {QuoteSchemaTable(table.Schema, table.Name)} DROP CONSTRAINT {QuoteId(ck.Name)};";

    public string CreateIndex(SchemaTable table, SchemaIndex ix)
    {
        var unique = ix.IsUnique ? "UNIQUE " : "";
        var method = !string.IsNullOrEmpty(ix.IndexType) && !ix.IndexType.Equals("btree", StringComparison.OrdinalIgnoreCase)
            ? $" USING {ix.IndexType.ToLowerInvariant()}" : "";
        var cols = string.Join(", ", ix.Columns.Select(c => c.IsDescending ? $"{QuoteId(c.Name)} DESC" : QuoteId(c.Name)));
        var filter = !string.IsNullOrEmpty(ix.FilterExpression) ? $" WHERE ({ix.FilterExpression})" : "";
        return $"CREATE {unique}INDEX {QuoteId(ix.Name)} ON {QuoteSchemaTable(table.Schema, table.Name)}{method} ({cols}){filter};";
    }

    public string DropIndex(SchemaTable table, SchemaIndex ix)
    {
        var schemaPart = string.IsNullOrEmpty(table.Schema) || table.Schema.Equals("public", StringComparison.OrdinalIgnoreCase)
            ? QuoteId(ix.Name)
            : $"{QuoteId(table.Schema)}.{QuoteId(ix.Name)}";
        return $"DROP INDEX {schemaPart};";
    }

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
        if (!string.IsNullOrWhiteSpace(trg.Definition))
        {
            // PG triggers need a function; the action_statement is typically EXECUTE FUNCTION fn().
            var stmt = trg.Definition.Trim().TrimEnd(';');
            return $"CREATE TRIGGER {QuoteId(trg.Name)} {trg.Timing} {trg.Event} ON {QuoteSchemaTable(trg.TableSchema, trg.TableName)} FOR EACH ROW {stmt};";
        }
        return $"-- Trigger {trg.FullName}: definition unavailable";
    }

    public string DropTrigger(SchemaTrigger trg)
        => $"DROP TRIGGER {QuoteId(trg.Name)} ON {QuoteSchemaTable(trg.TableSchema, trg.TableName)};";

    private static string? MapAction(string? action) => action?.ToUpperInvariant() switch
    {
        null or "" or "NO ACTION" or "NO_ACTION" => null,
        "CASCADE" => "CASCADE",
        "SET NULL" or "SET_NULL" => "SET NULL",
        "SET DEFAULT" or "SET_DEFAULT" => "SET DEFAULT",
        "RESTRICT" => "RESTRICT",
        _ => null
    };
}
