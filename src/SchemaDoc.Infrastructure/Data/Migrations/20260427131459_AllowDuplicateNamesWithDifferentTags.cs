using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchemaDoc.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowDuplicateNamesWithDifferentTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Connections_Name",
                table: "Connections");

            migrationBuilder.AlterColumn<string>(
                name: "Tag",
                table: "Connections",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Connections_Name_Tag",
                table: "Connections",
                columns: new[] { "Name", "Tag" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Connections_Name_Tag",
                table: "Connections");

            migrationBuilder.AlterColumn<string>(
                name: "Tag",
                table: "Connections",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.CreateIndex(
                name: "IX_Connections_Name",
                table: "Connections",
                column: "Name",
                unique: true);
        }
    }
}
