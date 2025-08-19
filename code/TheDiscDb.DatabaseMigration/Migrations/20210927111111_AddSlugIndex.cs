namespace TheDiscDb.Web.Migrations
{
    using Microsoft.EntityFrameworkCore.Migrations;

    public partial class AddSlugIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_MediaItem_Slug",
                table: "MediaItems",
                column: "Slug",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "idx_Releases_Slug",
                table: "Releases",
                column: "Slug",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "idx_Boxset_Slug",
                table: "Boxsets",
                column: "Slug",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "idx_Discs_Index",
                table: "Discs",
                column: "Index"
            );

            migrationBuilder.CreateIndex(
                name: "idx_Titles_SourceFile",
                table: "Titles",
                column: "SourceFile"
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "idx_MediaItem_Slug");
            migrationBuilder.DropIndex(name: "idx_Releases_Slug");
            migrationBuilder.DropIndex(name: "idx_Boxset_Slug");
            migrationBuilder.DropIndex(name: "idx_Discs_Index");
            migrationBuilder.DropIndex(name: "idx_Titles_SourceFile");
        }
    }
}
