using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscDb.Web.Migrations
{
    /// <inheritdoc />
    public partial class ContributionCascadeDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserContributionAudioTracks_UserContributionDiscItems_ItemId",
                table: "UserContributionAudioTracks");

            migrationBuilder.DropForeignKey(
                name: "FK_UserContributionChapters_UserContributionDiscItems_ItemId",
                table: "UserContributionChapters");

            migrationBuilder.DropForeignKey(
                name: "FK_UserContributionDiscHashItems_UserContributions_UserContributionId",
                table: "UserContributionDiscHashItems");

            migrationBuilder.DropForeignKey(
                name: "FK_UserContributionDiscItems_UserContributionDiscs_DiscId",
                table: "UserContributionDiscItems");

            migrationBuilder.DropForeignKey(
                name: "FK_UserContributionDiscs_UserContributions_UserContributionId",
                table: "UserContributionDiscs");

            migrationBuilder.AddForeignKey(
                name: "FK_UserContributionAudioTracks_UserContributionDiscItems_ItemId",
                table: "UserContributionAudioTracks",
                column: "ItemId",
                principalTable: "UserContributionDiscItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserContributionChapters_UserContributionDiscItems_ItemId",
                table: "UserContributionChapters",
                column: "ItemId",
                principalTable: "UserContributionDiscItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserContributionDiscHashItems_UserContributions_UserContributionId",
                table: "UserContributionDiscHashItems",
                column: "UserContributionId",
                principalTable: "UserContributions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserContributionDiscItems_UserContributionDiscs_DiscId",
                table: "UserContributionDiscItems",
                column: "DiscId",
                principalTable: "UserContributionDiscs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserContributionDiscs_UserContributions_UserContributionId",
                table: "UserContributionDiscs",
                column: "UserContributionId",
                principalTable: "UserContributions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserContributionAudioTracks_UserContributionDiscItems_ItemId",
                table: "UserContributionAudioTracks");

            migrationBuilder.DropForeignKey(
                name: "FK_UserContributionChapters_UserContributionDiscItems_ItemId",
                table: "UserContributionChapters");

            migrationBuilder.DropForeignKey(
                name: "FK_UserContributionDiscHashItems_UserContributions_UserContributionId",
                table: "UserContributionDiscHashItems");

            migrationBuilder.DropForeignKey(
                name: "FK_UserContributionDiscItems_UserContributionDiscs_DiscId",
                table: "UserContributionDiscItems");

            migrationBuilder.DropForeignKey(
                name: "FK_UserContributionDiscs_UserContributions_UserContributionId",
                table: "UserContributionDiscs");

            migrationBuilder.AddForeignKey(
                name: "FK_UserContributionAudioTracks_UserContributionDiscItems_ItemId",
                table: "UserContributionAudioTracks",
                column: "ItemId",
                principalTable: "UserContributionDiscItems",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_UserContributionChapters_UserContributionDiscItems_ItemId",
                table: "UserContributionChapters",
                column: "ItemId",
                principalTable: "UserContributionDiscItems",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_UserContributionDiscHashItems_UserContributions_UserContributionId",
                table: "UserContributionDiscHashItems",
                column: "UserContributionId",
                principalTable: "UserContributions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_UserContributionDiscItems_UserContributionDiscs_DiscId",
                table: "UserContributionDiscItems",
                column: "DiscId",
                principalTable: "UserContributionDiscs",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_UserContributionDiscs_UserContributions_UserContributionId",
                table: "UserContributionDiscs",
                column: "UserContributionId",
                principalTable: "UserContributions",
                principalColumn: "Id");
        }
    }
}
