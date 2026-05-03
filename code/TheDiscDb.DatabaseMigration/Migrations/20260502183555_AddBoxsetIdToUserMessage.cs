using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscDb.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddBoxsetIdToUserMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "ContributionId",
                table: "UserMessages",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "BoxsetId",
                table: "UserMessages",
                type: "int",
                nullable: true);

            // Null out any orphaned ContributionId values before adding the FK. Historical
            // UserMessages rows had no FK constraint, so a contribution delete (which doesn't
            // cascade to messages) could leave UserMessages.ContributionId pointing at a row
            // that no longer exists. SQL Server would reject the AddForeignKey below if any
            // such orphans remained.
            migrationBuilder.Sql(@"
                UPDATE UserMessages
                SET ContributionId = NULL
                WHERE ContributionId IS NOT NULL
                  AND ContributionId NOT IN (SELECT Id FROM UserContributions);
            ");

            migrationBuilder.CreateIndex(
                name: "IX_UserMessages_BoxsetId",
                table: "UserMessages",
                column: "BoxsetId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserMessages_UserContributionBoxsets_BoxsetId",
                table: "UserMessages",
                column: "BoxsetId",
                principalTable: "UserContributionBoxsets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_UserMessages_UserContributions_ContributionId",
                table: "UserMessages",
                column: "ContributionId",
                principalTable: "UserContributions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserMessages_UserContributionBoxsets_BoxsetId",
                table: "UserMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_UserMessages_UserContributions_ContributionId",
                table: "UserMessages");

            migrationBuilder.DropIndex(
                name: "IX_UserMessages_BoxsetId",
                table: "UserMessages");

            migrationBuilder.DropColumn(
                name: "BoxsetId",
                table: "UserMessages");

            migrationBuilder.AlterColumn<int>(
                name: "ContributionId",
                table: "UserMessages",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
