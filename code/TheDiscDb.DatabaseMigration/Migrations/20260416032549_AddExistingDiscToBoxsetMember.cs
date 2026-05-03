using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscDb.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddExistingDiscToBoxsetMember : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserContributionBoxsetMembers_DiscId",
                table: "UserContributionBoxsetMembers");

            migrationBuilder.AlterColumn<int>(
                name: "DiscId",
                table: "UserContributionBoxsetMembers",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "ExistingDiscFormat",
                table: "UserContributionBoxsetMembers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExistingDiscName",
                table: "UserContributionBoxsetMembers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExistingDiscPath",
                table: "UserContributionBoxsetMembers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserContributionBoxsetMembers_DiscId",
                table: "UserContributionBoxsetMembers",
                column: "DiscId",
                unique: true,
                filter: "[DiscId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserContributionBoxsetMembers_DiscId",
                table: "UserContributionBoxsetMembers");

            migrationBuilder.DropColumn(
                name: "ExistingDiscFormat",
                table: "UserContributionBoxsetMembers");

            migrationBuilder.DropColumn(
                name: "ExistingDiscName",
                table: "UserContributionBoxsetMembers");

            migrationBuilder.DropColumn(
                name: "ExistingDiscPath",
                table: "UserContributionBoxsetMembers");

            migrationBuilder.AlterColumn<int>(
                name: "DiscId",
                table: "UserContributionBoxsetMembers",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserContributionBoxsetMembers_DiscId",
                table: "UserContributionBoxsetMembers",
                column: "DiscId",
                unique: true);
        }
    }
}
