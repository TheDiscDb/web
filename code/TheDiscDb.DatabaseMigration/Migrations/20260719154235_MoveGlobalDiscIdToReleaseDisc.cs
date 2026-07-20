using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscDb.Web.Migrations
{
    /// <inheritdoc />
    public partial class MoveGlobalDiscIdToReleaseDisc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GlobalDiscId",
                table: "ReleaseDiscs",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            // Preserve existing ids: the id used to live on the shared canonical disc; move each
            // disc's value onto ONE of its release-discs. A given id value is now globally unique
            // per release-disc, so if the legacy (non-unique) column recorded the same value under
            // multiple discs/releases, keep only the lowest release-disc for that value via
            // ROW_NUMBER — otherwise the new unique index would fail to build.
            migrationBuilder.Sql(
                @"WITH ranked AS (
                      SELECT rd.[Id] AS ReleaseDiscId,
                             d.[GlobalDiscId] AS Val,
                             ROW_NUMBER() OVER (PARTITION BY d.[GlobalDiscId] ORDER BY rd.[Id]) AS rn
                      FROM [ReleaseDiscs] rd
                      INNER JOIN [Discs] d ON d.[Id] = rd.[DiscId]
                      WHERE d.[GlobalDiscId] IS NOT NULL AND d.[GlobalDiscId] <> ''
                  )
                  UPDATE rd
                  SET rd.[GlobalDiscId] = ranked.Val
                  FROM [ReleaseDiscs] rd
                  INNER JOIN ranked ON ranked.ReleaseDiscId = rd.[Id]
                  WHERE ranked.rn = 1;");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseDiscs_GlobalDiscId",
                table: "ReleaseDiscs",
                column: "GlobalDiscId",
                unique: true,
                filter: "[GlobalDiscId] IS NOT NULL");

            migrationBuilder.DropIndex(
                name: "IX_Discs_GlobalDiscId",
                table: "Discs");

            migrationBuilder.DropColumn(
                name: "GlobalDiscId",
                table: "Discs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GlobalDiscId",
                table: "Discs",
                type: "nvarchar(450)",
                nullable: true);

            // Best-effort restore of the scalar column: a canonical disc may map to several
            // release-discs, so keep the lowest id per disc (the scalar model holds only one).
            migrationBuilder.Sql(
                @"UPDATE d
                  SET d.[GlobalDiscId] = x.[GlobalDiscId]
                  FROM [Discs] d
                  INNER JOIN (
                      SELECT [DiscId], MIN([GlobalDiscId]) AS [GlobalDiscId]
                      FROM [ReleaseDiscs]
                      WHERE [GlobalDiscId] IS NOT NULL
                      GROUP BY [DiscId]
                  ) x ON x.[DiscId] = d.[Id];");

            migrationBuilder.DropIndex(
                name: "IX_ReleaseDiscs_GlobalDiscId",
                table: "ReleaseDiscs");

            migrationBuilder.DropColumn(
                name: "GlobalDiscId",
                table: "ReleaseDiscs");

            migrationBuilder.CreateIndex(
                name: "IX_Discs_GlobalDiscId",
                table: "Discs",
                column: "GlobalDiscId",
                filter: "[GlobalDiscId] IS NOT NULL");
        }
    }
}
