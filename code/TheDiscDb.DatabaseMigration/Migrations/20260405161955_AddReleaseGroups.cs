using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscDb.DatabaseMigration.Migrations
{
    /// <inheritdoc />
    public partial class AddReleaseGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReleaseGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReleaseId = table.Column<int>(type: "int", nullable: false),
                    GroupId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseGroups_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReleaseGroups_Releases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "Releases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseGroups_GroupId",
                table: "ReleaseGroups",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseGroups_ReleaseId_GroupId",
                table: "ReleaseGroups",
                columns: new[] { "ReleaseId", "GroupId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReleaseGroups");
        }
    }
}
