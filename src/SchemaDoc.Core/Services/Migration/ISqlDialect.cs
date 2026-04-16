using SchemaDoc.Core.Models;

namespace SchemaDoc.Core.Services.Migration;

/// <summary>
/// Generates dialect-specific DDL statements for a single migration action.
/// Each method returns a complete, semicolon-terminated (or GO-terminated for SQL Server) statement.
/// </summary>
public interface ISqlDialect
{
    DatabaseProvider Provider { get; }

    // Identifier quoting
    string QuoteId(string identifier);
    string QuoteSchemaTable(string schema, string table);

    // Statement terminator (batch separator for SQL Server)
    string StatementTerminator { get; }

    // Transaction wrappers (null for MySQL which auto-commits DDL)
    string? BeginTransaction { get; }
    string? CommitTransaction { get; }

    // ── Tables ────────────────────────────────────────────────────────
    string CreateTable(SchemaTable table, IEnumerable<ForeignKeyGroup> foreignKeys);
    string DropTable(SchemaTable table);

    // ── Columns ───────────────────────────────────────────────────────
    string AddColumn(SchemaTable table, SchemaColumn column);
    string DropColumn(SchemaTable table, SchemaColumn column);
    string AlterColumn(SchemaTable table, SchemaColumn baseline, SchemaColumn current);

    // ── Primary Keys ──────────────────────────────────────────────────
    string AddPrimaryKey(SchemaTable table, PrimaryKeyInfo pk);
    string DropPrimaryKey(SchemaTable table, PrimaryKeyInfo pk);

    // ── Unique Constraints ────────────────────────────────────────────
    string AddUniqueConstraint(SchemaTable table, UniqueConstraint uq);
    string DropUniqueConstraint(SchemaTable table, UniqueConstraint uq);

    // ── Check Constraints ─────────────────────────────────────────────
    string AddCheckConstraint(SchemaTable table, CheckConstraint ck);
    string DropCheckConstraint(SchemaTable table, CheckConstraint ck);

    // ── Indexes ───────────────────────────────────────────────────────
    string CreateIndex(SchemaTable table, SchemaIndex ix);
    string DropIndex(SchemaTable table, SchemaIndex ix);

    // ── Foreign Keys ──────────────────────────────────────────────────
    string AddForeignKey(ForeignKeyGroup fk);
    string DropForeignKey(ForeignKeyGroup fk);

    // ── Triggers ──────────────────────────────────────────────────────
    string CreateTrigger(SchemaTrigger trg);
    string DropTrigger(SchemaTrigger trg);

    // ── Types ─────────────────────────────────────────────────────────
    /// <summary>Renders a full column type spec (e.g. "NVARCHAR(100)", "DECIMAL(10,2)", "INT").</summary>
    string RenderColumnType(SchemaColumn col);
}
