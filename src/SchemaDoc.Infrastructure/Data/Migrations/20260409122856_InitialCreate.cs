using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchemaDoc.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Connections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    EncryptedConnectionString = table.Column<string>(type: "TEXT", nullable: false),
                    LastConnectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastDatabaseName = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Connections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TableAnnotations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConnectionName = table.Column<string>(type: "TEXT", nullable: false),
                    DatabaseName = table.Column<string>(type: "TEXT", nullable: false),
                    SchemaName = table.Column<string>(type: "TEXT", nullable: false),
                    TableName = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Tags = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TableAnnotations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SchemaSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConnectionId = table.Column<int>(type: "INTEGER", nullable: false),
                    DatabaseName = table.Column<string>(type: "TEXT", nullable: false),
                    ExtractedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SchemaJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchemaSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchemaSnapshots_Connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "Connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ColumnAnnotations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TableAnnotationId = table.Column<int>(type: "INTEGER", nullable: false),
                    ColumnName = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColumnAnnotations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ColumnAnnotations_TableAnnotations_TableAnnotationId",
                        column: x => x.TableAnnotationId,
                        principalTable: "TableAnnotations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ColumnAnnotations_TableAnnotationId_ColumnName",
                table: "ColumnAnnotations",
                columns: new[] { "TableAnnotationId", "ColumnName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Connections_Name",
                table: "Connections",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchemaSnapshots_ConnectionId_DatabaseName",
                table: "SchemaSnapshots",
                columns: new[] { "ConnectionId", "DatabaseName" });

            migrationBuilder.CreateIndex(
                name: "IX_TableAnnotations_ConnectionName_DatabaseName_SchemaName_TableName",
                table: "TableAnnotations",
                columns: new[] { "ConnectionName", "DatabaseName", "SchemaName", "TableName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ColumnAnnotations");

            migrationBuilder.DropTable(
                name: "SchemaSnapshots");

            migrationBuilder.DropTable(
                name: "TableAnnotations");

            migrationBuilder.DropTable(
                name: "Connections");
        }
    }
}
