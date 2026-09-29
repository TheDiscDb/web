using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscDb.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddReleaseExternalIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BlurayComId",
                table: "UserContributions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DvdCompareId",
                table: "UserContributions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DvdTalkId",
                table: "UserContributions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExternalIdsId",
                table: "Releases",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlurayCom",
                table: "ExternalIds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DvdCompare",
                table: "ExternalIds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DvdTalk",
                table: "ExternalIds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Releases_ExternalIdsId",
                table: "Releases",
                column: "ExternalIdsId",
                unique: true,
                filter: "[ExternalIdsId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Releases_ExternalIds_ExternalIdsId",
                table: "Releases",
                column: "ExternalIdsId",
                principalTable: "ExternalIds",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Releases_ExternalIds_ExternalIdsId",
                table: "Releases");

            migrationBuilder.DropIndex(
                name: "IX_Releases_ExternalIdsId",
                table: "Releases");

            migrationBuilder.DropColumn(
                name: "BlurayComId",
                table: "UserContributions");

            migrationBuilder.DropColumn(
                name: "DvdCompareId",
                table: "UserContributions");

            migrationBuilder.DropColumn(
                name: "DvdTalkId",
                table: "UserContributions");

            migrationBuilder.DropColumn(
                name: "ExternalIdsId",
                table: "Releases");

            migrationBuilder.DropColumn(
                name: "BlurayCom",
                table: "ExternalIds");

            migrationBuilder.DropColumn(
                name: "DvdCompare",
                table: "ExternalIds");

            migrationBuilder.DropColumn(
                name: "DvdTalk",
                table: "ExternalIds");
        }
    }
}
