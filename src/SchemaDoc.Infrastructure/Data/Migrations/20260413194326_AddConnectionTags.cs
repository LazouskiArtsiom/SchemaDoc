using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchemaDoc.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectionTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Tag",
                table: "Connections",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TagColor",
                table: "Connections",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Tag",
                table: "Connections");

            migrationBuilder.DropColumn(
                name: "TagColor",
                table: "Connections");
        }
    }
}
