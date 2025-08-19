using Microsoft.EntityFrameworkCore.Migrations;

namespace TheDiscDb.Web.Migrations
{
    public partial class AddBoxsetImageUrl : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "BoxSets",
                type: "nvarchar(max)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "BoxSets");
        }
    }
}
