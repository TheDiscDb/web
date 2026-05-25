using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscDb.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddReleaseAffiliateLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReleaseAffiliateLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MediaItemSlug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BoxsetSlug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReleaseSlug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ProviderHandle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ProviderUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    MatchedUpc = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    MatchSource = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    MatchedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseAffiliateLinks", x => x.Id);
                    table.CheckConstraint("CK_ReleaseAffiliateLinks_OneParentSlug", "([MediaItemSlug] IS NOT NULL AND [MediaItemSlug] <> '' AND [BoxsetSlug] IS NULL) OR ([MediaItemSlug] IS NULL AND [BoxsetSlug] IS NOT NULL AND [BoxsetSlug] <> '')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseAffiliateLinks_BoxsetSlug_ReleaseSlug_Provider",
                table: "ReleaseAffiliateLinks",
                columns: new[] { "BoxsetSlug", "ReleaseSlug", "Provider" },
                unique: true,
                filter: "[BoxsetSlug] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseAffiliateLinks_MediaItemSlug_ReleaseSlug_Provider",
                table: "ReleaseAffiliateLinks",
                columns: new[] { "MediaItemSlug", "ReleaseSlug", "Provider" },
                unique: true,
                filter: "[MediaItemSlug] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReleaseAffiliateLinks");
        }
    }
}
