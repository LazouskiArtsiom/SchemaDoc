using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchemaDoc.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSnapshotNameAndNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "SchemaSnapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "SchemaSnapshots",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Name",
                table: "SchemaSnapshots");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "SchemaSnapshots");
        }
    }
}
