using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscDb.Web.Migrations
{
    /// <inheritdoc />
    public partial class RenameSubmissionToDiscAndAddEngramRelease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Clear EngramTitles first — its rows reference EngramSubmissions.Id, which
            // no longer exist after the table is dropped, and would block re-creation of
            // the FK at the bottom of this migration.
            migrationBuilder.Sql("DELETE FROM EngramTitles;");

            migrationBuilder.DropForeignKey(
                name: "FK_EngramTitles_EngramSubmissions_EngramSubmissionId",
                table: "EngramTitles");

            migrationBuilder.DropTable(
                name: "EngramSubmissions");

            migrationBuilder.RenameColumn(
                name: "EngramSubmissionId",
                table: "EngramTitles",
                newName: "EngramDiscId");

            migrationBuilder.RenameIndex(
                name: "IX_EngramTitles_EngramSubmissionId_TitleIndex",
                table: "EngramTitles",
                newName: "IX_EngramTitles_EngramDiscId_TitleIndex");

            migrationBuilder.CreateTable(
                name: "EngramReleases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReleaseId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FrontImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BackImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngramReleases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EngramDiscs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EngramReleaseId = table.Column<int>(type: "int", nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    VolumeLabel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DiscNumber = table.Column<int>(type: "int", nullable: true),
                    TmdbId = table.Column<int>(type: "int", nullable: true),
                    DetectedTitle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DetectedSeason = table.Column<int>(type: "int", nullable: true),
                    ClassificationSource = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClassificationConfidence = table.Column<double>(type: "float", nullable: true),
                    Upc = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ScanLogPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EngramVersion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExportVersion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContributionTier = table.Column<int>(type: "int", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UserContributionId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngramDiscs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EngramDiscs_EngramReleases_EngramReleaseId",
                        column: x => x.EngramReleaseId,
                        principalTable: "EngramReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EngramDiscs_UserContributions_UserContributionId",
                        column: x => x.UserContributionId,
                        principalTable: "UserContributions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EngramDiscs_ContentHash",
                table: "EngramDiscs",
                column: "ContentHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EngramDiscs_EngramReleaseId",
                table: "EngramDiscs",
                column: "EngramReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_EngramDiscs_UserContributionId",
                table: "EngramDiscs",
                column: "UserContributionId");

            migrationBuilder.CreateIndex(
                name: "IX_EngramReleases_ReleaseId",
                table: "EngramReleases",
                column: "ReleaseId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_EngramTitles_EngramDiscs_EngramDiscId",
                table: "EngramTitles",
                column: "EngramDiscId",
                principalTable: "EngramDiscs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Symmetric to Up: clear EngramTitles before swapping the FK target back,
            // because the new EngramSubmissions table starts empty.
            migrationBuilder.Sql("DELETE FROM EngramTitles;");

            migrationBuilder.DropForeignKey(
                name: "FK_EngramTitles_EngramDiscs_EngramDiscId",
                table: "EngramTitles");

            migrationBuilder.DropTable(
                name: "EngramDiscs");

            migrationBuilder.DropTable(
                name: "EngramReleases");

            migrationBuilder.RenameColumn(
                name: "EngramDiscId",
                table: "EngramTitles",
                newName: "EngramSubmissionId");

            migrationBuilder.RenameIndex(
                name: "IX_EngramTitles_EngramDiscId_TitleIndex",
                table: "EngramTitles",
                newName: "IX_EngramTitles_EngramSubmissionId_TitleIndex");

            migrationBuilder.CreateTable(
                name: "EngramSubmissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserContributionId = table.Column<int>(type: "int", nullable: true),
                    BackImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClassificationConfidence = table.Column<double>(type: "float", nullable: true),
                    ClassificationSource = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContributionTier = table.Column<int>(type: "int", nullable: false),
                    DetectedSeason = table.Column<int>(type: "int", nullable: true),
                    DetectedTitle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DiscNumber = table.Column<int>(type: "int", nullable: true),
                    EngramVersion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExportVersion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FrontImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReleaseId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ScanLogPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TmdbId = table.Column<int>(type: "int", nullable: true),
                    Upc = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VolumeLabel = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngramSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EngramSubmissions_UserContributions_UserContributionId",
                        column: x => x.UserContributionId,
                        principalTable: "UserContributions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EngramSubmissions_ContentHash",
                table: "EngramSubmissions",
                column: "ContentHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EngramSubmissions_UserContributionId",
                table: "EngramSubmissions",
                column: "UserContributionId");

            migrationBuilder.AddForeignKey(
                name: "FK_EngramTitles_EngramSubmissions_EngramSubmissionId",
                table: "EngramTitles",
                column: "EngramSubmissionId",
                principalTable: "EngramSubmissions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
