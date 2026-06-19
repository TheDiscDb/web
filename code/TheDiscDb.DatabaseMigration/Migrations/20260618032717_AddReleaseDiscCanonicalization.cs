using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscDb.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddReleaseDiscCanonicalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReleaseDiscs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReleaseId = table.Column<int>(type: "int", nullable: false),
                    DiscId = table.Column<int>(type: "int", nullable: false),
                    Index = table.Column<int>(type: "int", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseDiscs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseDiscs_Discs_DiscId",
                        column: x => x.DiscId,
                        principalTable: "Discs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReleaseDiscs_Releases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "Releases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO [ReleaseDiscs] ([ReleaseId], [DiscId], [Index], [Slug], [Name])
                SELECT [ReleaseId], [Id], [Index], [Slug], [Name]
                FROM [Discs]
                WHERE [ReleaseId] IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                WITH [CanonicalMap] AS (
                    SELECT
                        [Id],
                        MIN([Id]) OVER (PARTITION BY COALESCE([Format], ''), COALESCE([ContentHash], '')) AS [CanonicalId]
                    FROM [Discs]
                )
                UPDATE rd
                SET [DiscId] = cm.[CanonicalId]
                FROM [ReleaseDiscs] rd
                INNER JOIN [CanonicalMap] cm ON rd.[DiscId] = cm.[Id];
                """);

            migrationBuilder.Sql("""
                WITH [CanonicalMap] AS (
                    SELECT
                        [Id],
                        MIN([Id]) OVER (PARTITION BY COALESCE([Format], ''), COALESCE([ContentHash], '')) AS [CanonicalId]
                    FROM [Discs]
                )
                UPDATE t
                SET [DiscId] = cm.[CanonicalId]
                FROM [Titles] t
                INNER JOIN [CanonicalMap] cm ON t.[DiscId] = cm.[Id];
                """);

            migrationBuilder.Sql("""
                WITH [CanonicalMap] AS (
                    SELECT
                        [Id],
                        MIN([Id]) OVER (PARTITION BY COALESCE([Format], ''), COALESCE([ContentHash], '')) AS [CanonicalId]
                    FROM [Discs]
                )
                DELETE d
                FROM [Discs] d
                INNER JOIN [CanonicalMap] cm ON d.[Id] = cm.[Id]
                WHERE d.[Id] <> cm.[CanonicalId];
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Discs_Releases_ReleaseId",
                table: "Discs");

            migrationBuilder.DropIndex(
                name: "IX_Discs_ReleaseId",
                table: "Discs");

            migrationBuilder.DropColumn(
                name: "Index",
                table: "Discs");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "Discs");

            migrationBuilder.DropColumn(
                name: "ReleaseId",
                table: "Discs");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "Discs");

            migrationBuilder.AlterColumn<string>(
                name: "Format",
                table: "Discs",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ContentHash",
                table: "Discs",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            // Normalize legacy release-disc ordering and slug collisions before applying unique indexes.
            migrationBuilder.Sql("""
                WITH [Ordered] AS (
                    SELECT
                        [Id],
                        ROW_NUMBER() OVER (PARTITION BY [ReleaseId] ORDER BY [Index], [Id]) AS [NewIndex]
                    FROM [ReleaseDiscs]
                )
                UPDATE rd
                SET [Index] = o.[NewIndex]
                FROM [ReleaseDiscs] rd
                INNER JOIN [Ordered] o ON rd.[Id] = o.[Id];
                """);

            migrationBuilder.Sql("""
                WITH [SlugConflicts] AS (
                    SELECT
                        [Id],
                        ROW_NUMBER() OVER (PARTITION BY [ReleaseId], [Slug] ORDER BY [Id]) AS [RowNum]
                    FROM [ReleaseDiscs]
                    WHERE [Slug] IS NOT NULL
                )
                UPDATE rd
                SET [Slug] = NULL
                FROM [ReleaseDiscs] rd
                INNER JOIN [SlugConflicts] sc ON rd.[Id] = sc.[Id]
                WHERE sc.[RowNum] > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Discs_Format_ContentHash",
                table: "Discs",
                columns: new[] { "Format", "ContentHash" },
                unique: true,
                filter: "[Format] IS NOT NULL AND [ContentHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseDiscs_DiscId",
                table: "ReleaseDiscs",
                column: "DiscId");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseDiscs_ReleaseId_Index",
                table: "ReleaseDiscs",
                columns: new[] { "ReleaseId", "Index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseDiscs_ReleaseId_Slug",
                table: "ReleaseDiscs",
                columns: new[] { "ReleaseId", "Slug" },
                unique: true,
                filter: "[Slug] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReleaseDiscs");

            migrationBuilder.DropIndex(
                name: "IX_Discs_Format_ContentHash",
                table: "Discs");

            migrationBuilder.AlterColumn<string>(
                name: "Format",
                table: "Discs",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ContentHash",
                table: "Discs",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Index",
                table: "Discs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Discs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReleaseId",
                table: "Discs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Discs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Discs_ReleaseId",
                table: "Discs",
                column: "ReleaseId");

            migrationBuilder.AddForeignKey(
                name: "FK_Discs_Releases_ReleaseId",
                table: "Discs",
                column: "ReleaseId",
                principalTable: "Releases",
                principalColumn: "Id");
        }
    }
}
