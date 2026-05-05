using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscDb.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddUserFileNameTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserFileNameTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ItemType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Template = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserFileNameTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserFileNameTemplates_UserId_ItemType",
                table: "UserFileNameTemplates",
                columns: new[] { "UserId", "ItemType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserFileNameTemplates");
        }
    }
}
