using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchemaDoc.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDatabaseTagsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DatabaseTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConnectionId = table.Column<int>(type: "INTEGER", nullable: false),
                    DatabaseName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Tag = table.Column<string>(type: "TEXT", nullable: true),
                    TagColor = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseTags", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseTags_ConnectionId_DatabaseName",
                table: "DatabaseTags",
                columns: new[] { "ConnectionId", "DatabaseName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DatabaseTags");
        }
    }
}
