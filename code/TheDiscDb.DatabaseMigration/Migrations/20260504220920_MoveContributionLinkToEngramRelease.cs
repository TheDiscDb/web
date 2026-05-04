using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscDb.Web.Migrations
{
    /// <inheritdoc />
    public partial class MoveContributionLinkToEngramRelease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EngramDiscs_UserContributions_UserContributionId",
                table: "EngramDiscs");

            migrationBuilder.DropIndex(
                name: "IX_EngramDiscs_UserContributionId",
                table: "EngramDiscs");

            migrationBuilder.DropColumn(
                name: "UserContributionId",
                table: "EngramDiscs");

            migrationBuilder.AddColumn<int>(
                name: "UserContributionId",
                table: "EngramReleases",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EngramReleases_UserContributionId",
                table: "EngramReleases",
                column: "UserContributionId");

            migrationBuilder.AddForeignKey(
                name: "FK_EngramReleases_UserContributions_UserContributionId",
                table: "EngramReleases",
                column: "UserContributionId",
                principalTable: "UserContributions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EngramReleases_UserContributions_UserContributionId",
                table: "EngramReleases");

            migrationBuilder.DropIndex(
                name: "IX_EngramReleases_UserContributionId",
                table: "EngramReleases");

            migrationBuilder.DropColumn(
                name: "UserContributionId",
                table: "EngramReleases");

            migrationBuilder.AddColumn<int>(
                name: "UserContributionId",
                table: "EngramDiscs",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EngramDiscs_UserContributionId",
                table: "EngramDiscs",
                column: "UserContributionId");

            migrationBuilder.AddForeignKey(
                name: "FK_EngramDiscs_UserContributions_UserContributionId",
                table: "EngramDiscs",
                column: "UserContributionId",
                principalTable: "UserContributions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
