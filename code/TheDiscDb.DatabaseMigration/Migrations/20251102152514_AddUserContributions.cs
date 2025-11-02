using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscDb.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddUserContributions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserContributions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Created = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MediaType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExternalId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExternalProvider = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReleaseDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Asin = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Upc = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FrontImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BackImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReleaseTitle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReleaseSlug = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Locale = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RegionCode = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserContributions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserContributionDiscs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserContributionId = table.Column<int>(type: "int", nullable: true),
                    Index = table.Column<int>(type: "int", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Format = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Slug = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LogsUploaded = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserContributionDiscs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserContributionDiscs_UserContributions_UserContributionId",
                        column: x => x.UserContributionId,
                        principalTable: "UserContributions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserContributionDiscItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DiscId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Duration = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Size = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChapterCount = table.Column<int>(type: "int", nullable: false),
                    SegmentCount = table.Column<int>(type: "int", nullable: false),
                    SegmentMap = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Season = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Episode = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserContributionDiscItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserContributionDiscItems_UserContributionDiscs_DiscId",
                        column: x => x.DiscId,
                        principalTable: "UserContributionDiscs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserContributionAudioTracks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Index = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserContributionAudioTracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserContributionAudioTracks_UserContributionDiscItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "UserContributionDiscItems",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserContributionChapters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Index = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserContributionChapters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserContributionChapters_UserContributionDiscItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "UserContributionDiscItems",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserContributionAudioTracks_ItemId",
                table: "UserContributionAudioTracks",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_UserContributionChapters_ItemId",
                table: "UserContributionChapters",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_UserContributionDiscItems_DiscId",
                table: "UserContributionDiscItems",
                column: "DiscId");

            migrationBuilder.CreateIndex(
                name: "IX_UserContributionDiscs_UserContributionId",
                table: "UserContributionDiscs",
                column: "UserContributionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserContributionAudioTracks");

            migrationBuilder.DropTable(
                name: "UserContributionChapters");

            migrationBuilder.DropTable(
                name: "UserContributionDiscItems");

            migrationBuilder.DropTable(
                name: "UserContributionDiscs");

            migrationBuilder.DropTable(
                name: "UserContributions");
        }
    }
}
