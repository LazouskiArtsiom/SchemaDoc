namespace SchemaDoc.Core.Services.Migration;

/// <summary>
/// Categorizes migration actions. Determines ordering in the generated script:
/// drops happen before creates, within the drop/create phase FK constraints are removed first.
/// </summary>
public enum MigrationActionType
{
    // DROPs (run first, in this order)
    DropTrigger = 10,
    DropForeignKey = 20,
    DropCheckConstraint = 30,
    DropUniqueConstraint = 40,
    DropIndex = 50,
    DropPrimaryKey = 60,
    DropColumn = 70,
    DropTable = 80,

    // CREATEs (after all drops, in this order)
    CreateTable = 110,
    AddColumn = 120,
    AlterColumn = 130,
    AddPrimaryKey = 140,
    AddUniqueConstraint = 150,
    AddCheckConstraint = 160,
    CreateIndex = 170,
    AddForeignKey = 180,
    CreateTrigger = 190
}

/// <summary>
/// One generated SQL statement representing a single change the user can toggle.
/// </summary>
public record MigrationAction(
    string Id,                          // deterministic: e.g. "column:add:app.Users:Email"
    MigrationActionType Type,
    string Category,                    // display group (e.g. "Columns", "Indexes", "Triggers")
    string TableFullName,               // schema.table (empty for DB-level actions like DropTrigger)
    string Description,                 // human-readable, e.g. "Add column Email (NVARCHAR(200))"
    string Sql                          // the actual SQL statement
);
