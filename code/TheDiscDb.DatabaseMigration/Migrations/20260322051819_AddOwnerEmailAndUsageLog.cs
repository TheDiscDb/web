using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscDb.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerEmailAndUsageLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerEmail",
                table: "ApiKeys",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ApiKeyUsageLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApiKeyId = table.Column<int>(type: "int", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OperationName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    FieldCost = table.Column<double>(type: "float", nullable: false),
                    TypeCost = table.Column<double>(type: "float", nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeyUsageLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiKeyUsageLogs_ApiKeys_ApiKeyId",
                        column: x => x.ApiKeyId,
                        principalTable: "ApiKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeyUsageLogs_ApiKeyId_Timestamp",
                table: "ApiKeyUsageLogs",
                columns: new[] { "ApiKeyId", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiKeyUsageLogs");

            migrationBuilder.DropColumn(
                name: "OwnerEmail",
                table: "ApiKeys");
        }
    }
}
