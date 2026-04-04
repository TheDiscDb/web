using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscDb.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddEngramSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EngramSubmissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
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
                    FrontImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BackImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ScanLogPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EngramVersion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExportVersion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContributionTier = table.Column<int>(type: "int", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngramSubmissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EngramTitles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EngramSubmissionId = table.Column<int>(type: "int", nullable: false),
                    TitleIndex = table.Column<int>(type: "int", nullable: false),
                    SourceFilename = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DurationSeconds = table.Column<int>(type: "int", nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    ChapterCount = table.Column<int>(type: "int", nullable: true),
                    SegmentCount = table.Column<int>(type: "int", nullable: true),
                    SegmentMap = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TitleType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MatchedEpisode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MatchConfidence = table.Column<double>(type: "float", nullable: true),
                    MatchSource = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Edition = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RipLogPath = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngramTitles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EngramTitles_EngramSubmissions_EngramSubmissionId",
                        column: x => x.EngramSubmissionId,
                        principalTable: "EngramSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EngramSubmissions_ContentHash",
                table: "EngramSubmissions",
                column: "ContentHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EngramTitles_EngramSubmissionId_TitleIndex",
                table: "EngramTitles",
                columns: new[] { "EngramSubmissionId", "TitleIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EngramTitles");

            migrationBuilder.DropTable(
                name: "EngramSubmissions");
        }
    }
}
