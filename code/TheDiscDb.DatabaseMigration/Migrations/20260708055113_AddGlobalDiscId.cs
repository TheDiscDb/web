using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscDb.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalDiscId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GlobalDiscId",
                table: "UserContributionDiscs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GlobalDiscId",
                table: "Discs",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Discs_GlobalDiscId",
                table: "Discs",
                column: "GlobalDiscId",
                filter: "[GlobalDiscId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Discs_GlobalDiscId",
                table: "Discs");

            migrationBuilder.DropColumn(
                name: "GlobalDiscId",
                table: "UserContributionDiscs");

            migrationBuilder.DropColumn(
                name: "GlobalDiscId",
                table: "Discs");
        }
    }
}
