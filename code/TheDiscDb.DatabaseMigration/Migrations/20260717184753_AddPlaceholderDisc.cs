using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscDb.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaceholderDisc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Discs_Format_ContentHash",
                table: "Discs");

            migrationBuilder.AddColumn<bool>(
                name: "IsPlaceholder",
                table: "Discs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Discs_Format_ContentHash",
                table: "Discs",
                columns: new[] { "Format", "ContentHash" },
                unique: true,
                filter: "[ContentHash] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Discs_Format_ContentHash",
                table: "Discs");

            migrationBuilder.DropColumn(
                name: "IsPlaceholder",
                table: "Discs");

            migrationBuilder.CreateIndex(
                name: "IX_Discs_Format_ContentHash",
                table: "Discs",
                columns: new[] { "Format", "ContentHash" },
                unique: true,
                filter: "[Format] IS NOT NULL AND [ContentHash] IS NOT NULL");
        }
    }
}
