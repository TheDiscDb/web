using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscDb.Web.Migrations
{
    /// <inheritdoc />
    public partial class LinkContributionToBoxset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BoxsetId",
                table: "UserContributions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserContributions_BoxsetId",
                table: "UserContributions",
                column: "BoxsetId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserContributions_UserContributionBoxsets_BoxsetId",
                table: "UserContributions",
                column: "BoxsetId",
                principalTable: "UserContributionBoxsets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserContributions_UserContributionBoxsets_BoxsetId",
                table: "UserContributions");

            migrationBuilder.DropIndex(
                name: "IX_UserContributions_BoxsetId",
                table: "UserContributions");

            migrationBuilder.DropColumn(
                name: "BoxsetId",
                table: "UserContributions");
        }
    }
}
