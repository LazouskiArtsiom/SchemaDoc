using System.Text;
using SchemaDoc.Core.Models;

namespace SchemaDoc.Core.Services.Migration;

public class MySqlDialect : ISqlDialect
{
    public DatabaseProvider Provider => DatabaseProvider.MySql;
    public string StatementTerminator => "";
    public string? BeginTransaction => null;   // MySQL auto-commits DDL — no transaction wrapper
    public string? CommitTransaction => null;

    public string QuoteId(string identifier) => $"`{identifier.Replace("`", "``")}`";
    public string QuoteSchemaTable(string schema, string table) => QuoteId(table); // MySQL: schema = database, use current DB

    public string RenderColumnType(SchemaColumn col)
    {
        var t = col.DataType.ToLowerInvariant();
        if (t is "varchar" or "char" or "varbinary" or "binary" && !string.IsNullOrEmpty(col.MaxLength))
            return $"{col.DataType.ToUpperInvariant()}({col.MaxLength})";
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
        sb.Append(col.IsNullable ? " NULL" : " NOT NULL");
        if (col.IsIdentity) sb.Append(" AUTO_INCREMENT");
        if (!string.IsNullOrEmpty(col.DefaultValue))
            sb.Append(" DEFAULT ").Append(col.DefaultValue);
        if (!string.IsNullOrEmpty(col.DbNativeComment))
            sb.Append(" COMMENT '").Append(col.DbNativeComment.Replace("'", "''")).Append('\'');
        return sb.ToString();
    }

    public string CreateTable(SchemaTable table, IEnumerable<ForeignKeyGroup> foreignKeys)
    {
        var sb = new StringBuilder();
        sb.Append("CREATE TABLE ").Append(QuoteId(table.Name)).AppendLine(" (");
        var lines = new List<string>();
        foreach (var c in table.Columns.OrderBy(c => c.OrdinalPosition))
            lines.Add("    " + RenderColumnDefinition(c));
        if (table.PrimaryKey is not null)
            lines.Add($"    PRIMARY KEY ({string.Join(", ", table.PrimaryKey.Columns.Select(QuoteId))})");
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
        => $"DROP TABLE {QuoteId(table.Name)};";

    public string AddColumn(SchemaTable table, SchemaColumn column)
        => $"ALTER TABLE {QuoteId(table.Name)} ADD COLUMN {RenderColumnDefinition(column)};";

    public string DropColumn(SchemaTable table, SchemaColumn column)
        => $"ALTER TABLE {QuoteId(table.Name)} DROP COLUMN {QuoteId(column.Name)};";

    public string AlterColumn(SchemaTable table, SchemaColumn baseline, SchemaColumn current)
        => $"ALTER TABLE {QuoteId(table.Name)} MODIFY COLUMN {RenderColumnDefinition(current)};";

    public string AddPrimaryKey(SchemaTable table, PrimaryKeyInfo pk)
    {
        var cols = string.Join(", ", pk.Columns.Select(QuoteId));
        return $"ALTER TABLE {QuoteId(table.Name)} ADD PRIMARY KEY ({cols});";
    }

    public string DropPrimaryKey(SchemaTable table, PrimaryKeyInfo pk)
        => $"ALTER TABLE {QuoteId(table.Name)} DROP PRIMARY KEY;";

    public string AddUniqueConstraint(SchemaTable table, UniqueConstraint uq)
    {
        var cols = string.Join(", ", uq.Columns.Select(QuoteId));
        return $"ALTER TABLE {QuoteId(table.Name)} ADD CONSTRAINT {QuoteId(uq.Name)} UNIQUE ({cols});";
    }

    public string DropUniqueConstraint(SchemaTable table, UniqueConstraint uq)
        => $"ALTER TABLE {QuoteId(table.Name)} DROP INDEX {QuoteId(uq.Name)};";

    public string AddCheckConstraint(SchemaTable table, CheckConstraint ck)
        => $"ALTER TABLE {QuoteId(table.Name)} ADD CONSTRAINT {QuoteId(ck.Name)} CHECK ({ck.Expression});";

    public string DropCheckConstraint(SchemaTable table, CheckConstraint ck)
        => $"ALTER TABLE {QuoteId(table.Name)} DROP CHECK {QuoteId(ck.Name)};";

    public string CreateIndex(SchemaTable table, SchemaIndex ix)
    {
        var unique = ix.IsUnique ? "UNIQUE " : "";
        var cols = string.Join(", ", ix.Columns.Select(c => c.IsDescending ? $"{QuoteId(c.Name)} DESC" : QuoteId(c.Name)));
        var usingClause = !string.IsNullOrEmpty(ix.IndexType) && !ix.IndexType.Equals("btree", StringComparison.OrdinalIgnoreCase)
            ? $" USING {ix.IndexType.ToUpperInvariant()}" : "";
        return $"CREATE {unique}INDEX {QuoteId(ix.Name)} ON {QuoteId(table.Name)} ({cols}){usingClause};";
    }

    public string DropIndex(SchemaTable table, SchemaIndex ix)
        => $"DROP INDEX {QuoteId(ix.Name)} ON {QuoteId(table.Name)};";

    public string AddForeignKey(ForeignKeyGroup fk)
    {
        var parentCols = string.Join(", ", fk.ParentColumns.Select(QuoteId));
        var refCols = string.Join(", ", fk.ReferencedColumns.Select(QuoteId));
        var onDelete = MapAction(fk.OnDelete);
        var onUpdate = MapAction(fk.OnUpdate);
        var clauses = "";
        if (onDelete is not null) clauses += $" ON DELETE {onDelete}";
        if (onUpdate is not null) clauses += $" ON UPDATE {onUpdate}";
        return $"ALTER TABLE {QuoteId(fk.ParentTable)} " +
               $"ADD CONSTRAINT {QuoteId(fk.ConstraintName)} FOREIGN KEY ({parentCols}) " +
               $"REFERENCES {QuoteId(fk.ReferencedTable)} ({refCols}){clauses};";
    }

    public string DropForeignKey(ForeignKeyGroup fk)
        => $"ALTER TABLE {QuoteId(fk.ParentTable)} DROP FOREIGN KEY {QuoteId(fk.ConstraintName)};";

    public string CreateTrigger(SchemaTrigger trg)
    {
        // MySQL triggers: CREATE TRIGGER name timing event ON table FOR EACH ROW body
        var body = trg.Definition?.Trim().TrimEnd(';') ?? "BEGIN END";
        return $"CREATE TRIGGER {QuoteId(trg.Name)} {trg.Timing} {trg.Event} ON {QuoteId(trg.TableName)} FOR EACH ROW {body};";
    }

    public string DropTrigger(SchemaTrigger trg)
        => $"DROP TRIGGER {QuoteId(trg.Name)};";

    private static string? MapAction(string? action) => action?.ToUpperInvariant() switch
    {
        null or "" or "NO ACTION" or "NO_ACTION" or "RESTRICT" => null,
        "CASCADE" => "CASCADE",
        "SET NULL" or "SET_NULL" => "SET NULL",
        _ => null
    };
}
