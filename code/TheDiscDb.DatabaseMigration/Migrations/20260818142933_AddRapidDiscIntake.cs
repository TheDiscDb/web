using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscDb.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddRapidDiscIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IntakeDiscs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Format = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeDiscs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntakeReleases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceReleaseId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    MediaItemSlug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BoxsetSlug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReleaseSlug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExternalProvider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Upc = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Asin = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ReleaseDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleaseTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Locale = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    RegionCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    FrontImageLocation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeReleases", x => x.Id);
                    table.CheckConstraint("CK_IntakeReleases_OneParentSlug", "[MediaItemSlug] IS NULL OR [BoxsetSlug] IS NULL");
                });

            migrationBuilder.CreateTable(
                name: "IntakeDiscEvidence",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IntakeDiscId = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceEvidenceId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    EvidenceSetId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    GlobalDiscId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeDiscEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntakeDiscEvidence_IntakeDiscs_IntakeDiscId",
                        column: x => x.IntakeDiscId,
                        principalTable: "IntakeDiscs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IntakePromotions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Target = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IntakeReleaseId = table.Column<int>(type: "int", nullable: true),
                    IntakeDiscId = table.Column<int>(type: "int", nullable: true),
                    UserContributionId = table.Column<int>(type: "int", nullable: true),
                    MediaItemSlug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BoxsetSlug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReleaseSlug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DiscFormat = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DiscContentHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DiscGlobalId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    EvidenceSetId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakePromotions", x => x.Id);
                    table.CheckConstraint("CK_IntakePromotions_OneOwner", "([Target] = 'Release' AND [IntakeReleaseId] IS NOT NULL AND [IntakeDiscId] IS NULL) OR ([Target] = 'Disc' AND [IntakeReleaseId] IS NULL AND [IntakeDiscId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_IntakePromotions_IntakeDiscs_IntakeDiscId",
                        column: x => x.IntakeDiscId,
                        principalTable: "IntakeDiscs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IntakePromotions_IntakeReleases_IntakeReleaseId",
                        column: x => x.IntakeReleaseId,
                        principalTable: "IntakeReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IntakePromotions_UserContributions_UserContributionId",
                        column: x => x.UserContributionId,
                        principalTable: "UserContributions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "IntakeReleaseDiscs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IntakeReleaseId = table.Column<int>(type: "int", nullable: false),
                    IntakeDiscId = table.Column<int>(type: "int", nullable: false),
                    SourceDiscId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    GlobalDiscId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Index = table.Column<int>(type: "int", nullable: true),
                    Slug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    AddedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeReleaseDiscs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntakeReleaseDiscs_IntakeDiscs_IntakeDiscId",
                        column: x => x.IntakeDiscId,
                        principalTable: "IntakeDiscs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IntakeReleaseDiscs_IntakeReleases_IntakeReleaseId",
                        column: x => x.IntakeReleaseId,
                        principalTable: "IntakeReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IntakeDiscEvidence_IntakeDiscId_EvidenceSetId_RecordedAt",
                table: "IntakeDiscEvidence",
                columns: new[] { "IntakeDiscId", "EvidenceSetId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IntakeDiscEvidence_IntakeDiscId_GlobalDiscId_EvidenceSetId",
                table: "IntakeDiscEvidence",
                columns: new[] { "IntakeDiscId", "GlobalDiscId", "EvidenceSetId" },
                filter: "[GlobalDiscId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IntakeDiscEvidence_Source_SourceEvidenceId_Type",
                table: "IntakeDiscEvidence",
                columns: new[] { "Source", "SourceEvidenceId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntakeDiscs_Format_ContentHash",
                table: "IntakeDiscs",
                columns: new[] { "Format", "ContentHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntakeDiscs_Status_ReceivedAt",
                table: "IntakeDiscs",
                columns: new[] { "Status", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IntakePromotions_IntakeDiscId",
                table: "IntakePromotions",
                column: "IntakeDiscId");

            migrationBuilder.CreateIndex(
                name: "IX_IntakePromotions_IntakeReleaseId",
                table: "IntakePromotions",
                column: "IntakeReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_IntakePromotions_Status_CreatedAt",
                table: "IntakePromotions",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IntakePromotions_UserContributionId",
                table: "IntakePromotions",
                column: "UserContributionId");

            migrationBuilder.CreateIndex(
                name: "IX_IntakePromotions_UserContributionId_IntakeDiscId_Target_Status",
                table: "IntakePromotions",
                columns: new[] { "UserContributionId", "IntakeDiscId", "Target", "Status" },
                unique: true,
                filter: "[Target] = 'Disc' AND [Status] = 'Completed' AND [UserContributionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IntakePromotions_UserContributionId_IntakeReleaseId_Target_Status",
                table: "IntakePromotions",
                columns: new[] { "UserContributionId", "IntakeReleaseId", "Target", "Status" },
                unique: true,
                filter: "[Target] = 'Release' AND [Status] = 'Completed' AND [UserContributionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IntakeReleaseDiscs_GlobalDiscId",
                table: "IntakeReleaseDiscs",
                column: "GlobalDiscId",
                filter: "[GlobalDiscId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IntakeReleaseDiscs_IntakeDiscId",
                table: "IntakeReleaseDiscs",
                column: "IntakeDiscId");

            migrationBuilder.CreateIndex(
                name: "IX_IntakeReleaseDiscs_IntakeReleaseId_Index",
                table: "IntakeReleaseDiscs",
                columns: new[] { "IntakeReleaseId", "Index" },
                unique: true,
                filter: "[Index] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IntakeReleaseDiscs_IntakeReleaseId_IntakeDiscId",
                table: "IntakeReleaseDiscs",
                columns: new[] { "IntakeReleaseId", "IntakeDiscId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntakeReleaseDiscs_IntakeReleaseId_SourceDiscId",
                table: "IntakeReleaseDiscs",
                columns: new[] { "IntakeReleaseId", "SourceDiscId" },
                unique: true,
                filter: "[SourceDiscId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IntakeReleases_BoxsetSlug_ReleaseSlug",
                table: "IntakeReleases",
                columns: new[] { "BoxsetSlug", "ReleaseSlug" });

            migrationBuilder.CreateIndex(
                name: "IX_IntakeReleases_ExternalProvider_ExternalId_Upc",
                table: "IntakeReleases",
                columns: new[] { "ExternalProvider", "ExternalId", "Upc" });

            migrationBuilder.CreateIndex(
                name: "IX_IntakeReleases_MediaItemSlug_ReleaseSlug",
                table: "IntakeReleases",
                columns: new[] { "MediaItemSlug", "ReleaseSlug" });

            migrationBuilder.CreateIndex(
                name: "IX_IntakeReleases_Source_SourceReleaseId",
                table: "IntakeReleases",
                columns: new[] { "Source", "SourceReleaseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntakeReleases_Status_ReceivedAt",
                table: "IntakeReleases",
                columns: new[] { "Status", "ReceivedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntakeDiscEvidence");

            migrationBuilder.DropTable(
                name: "IntakePromotions");

            migrationBuilder.DropTable(
                name: "IntakeReleaseDiscs");

            migrationBuilder.DropTable(
                name: "IntakeDiscs");

            migrationBuilder.DropTable(
                name: "IntakeReleases");
        }
    }
}
