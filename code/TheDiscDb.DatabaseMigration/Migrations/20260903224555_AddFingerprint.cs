using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscDb.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddFingerprint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Fingerprint",
                table: "UserContributionDiscs",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Fingerprint",
                table: "ReleaseDiscs",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseDiscs_Fingerprint",
                table: "ReleaseDiscs",
                column: "Fingerprint",
                unique: true,
                filter: "[Fingerprint] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReleaseDiscs_Fingerprint",
                table: "ReleaseDiscs");

            migrationBuilder.DropColumn(
                name: "Fingerprint",
                table: "UserContributionDiscs");

            migrationBuilder.DropColumn(
                name: "Fingerprint",
                table: "ReleaseDiscs");
        }
    }
}
