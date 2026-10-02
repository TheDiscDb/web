using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscDb.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscManifestComparisons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ManifestUploaded",
                table: "UserContributionDiscs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ManifestUserAgent",
                table: "UserContributionDiscs",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserContributionDiscComparisons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DiscId = table.Column<int>(type: "int", nullable: false),
                    ComparedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Format = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ProducerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProducerVersion = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MakeMkvVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LogTitleCount = table.Column<int>(type: "int", nullable: false),
                    ManifestTitleCount = table.Column<int>(type: "int", nullable: false),
                    MatchedTitleCount = table.Column<int>(type: "int", nullable: false),
                    OrderMatches = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DifferencesJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserContributionDiscComparisons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserContributionDiscComparisons_UserContributionDiscs_DiscId",
                        column: x => x.DiscId,
                        principalTable: "UserContributionDiscs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserContributionDiscComparisons_DiscId_ComparedAt",
                table: "UserContributionDiscComparisons",
                columns: new[] { "DiscId", "ComparedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserContributionDiscComparisons");

            migrationBuilder.DropColumn(
                name: "ManifestUploaded",
                table: "UserContributionDiscs");

            migrationBuilder.DropColumn(
                name: "ManifestUserAgent",
                table: "UserContributionDiscs");
        }
    }
}
