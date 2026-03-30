using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscDb.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddContributionBoxsets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserContributionBoxsets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortTitle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Slug = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FrontImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BackImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Asin = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Upc = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReleaseDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Locale = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RegionCode = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserContributionBoxsets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserContributionBoxsetMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BoxsetId = table.Column<int>(type: "int", nullable: false),
                    DiscId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserContributionBoxsetMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserContributionBoxsetMembers_UserContributionBoxsets_BoxsetId",
                        column: x => x.BoxsetId,
                        principalTable: "UserContributionBoxsets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserContributionBoxsetMembers_UserContributionDiscs_DiscId",
                        column: x => x.DiscId,
                        principalTable: "UserContributionDiscs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserContributionBoxsetMembers_BoxsetId",
                table: "UserContributionBoxsetMembers",
                column: "BoxsetId");

            migrationBuilder.CreateIndex(
                name: "IX_UserContributionBoxsetMembers_DiscId",
                table: "UserContributionBoxsetMembers",
                column: "DiscId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserContributionBoxsetMembers");

            migrationBuilder.DropTable(
                name: "UserContributionBoxsets");
        }
    }
}
