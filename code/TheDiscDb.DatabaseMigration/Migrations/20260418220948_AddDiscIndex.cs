using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscDb.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Index",
                table: "UserContributionDiscs",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Index",
                table: "UserContributionDiscs");
        }
    }
}
