using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscDb.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddEngramContributionLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UserContributionId",
                table: "EngramSubmissions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EngramSubmissions_UserContributionId",
                table: "EngramSubmissions",
                column: "UserContributionId");

            migrationBuilder.AddForeignKey(
                name: "FK_EngramSubmissions_UserContributions_UserContributionId",
                table: "EngramSubmissions",
                column: "UserContributionId",
                principalTable: "UserContributions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EngramSubmissions_UserContributions_UserContributionId",
                table: "EngramSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_EngramSubmissions_UserContributionId",
                table: "EngramSubmissions");

            migrationBuilder.DropColumn(
                name: "UserContributionId",
                table: "EngramSubmissions");
        }
    }
}
