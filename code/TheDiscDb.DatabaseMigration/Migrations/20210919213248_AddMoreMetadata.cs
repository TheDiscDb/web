using Microsoft.EntityFrameworkCore.Migrations;

namespace TheDiscDb.Web.Migrations
{
    public partial class AddMoreMetadata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentRating",
                table: "MediaItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Directors",
                table: "MediaItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Genres",
                table: "MediaItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Plot",
                table: "MediaItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Runtime",
                table: "MediaItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RuntimeMinutes",
                table: "MediaItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Stars",
                table: "MediaItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tagline",
                table: "MediaItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Writers",
                table: "MediaItems",
                type: "nvarchar(max)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentRating",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "Directors",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "Genres",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "Plot",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "Runtime",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "RuntimeMinutes",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "Stars",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "Tagline",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "Writers",
                table: "MediaItems");
        }
    }
}
