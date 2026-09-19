using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscDb.Web.Migrations
{
    /// <inheritdoc />
    public partial class UnifyEditSuggestionMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EditSuggestionId",
                table: "UserMessages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "UserMessages",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserMessages_EditSuggestionId",
                table: "UserMessages",
                column: "EditSuggestionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMessages_EditSuggestionId_Purpose",
                table: "UserMessages",
                columns: new[] { "EditSuggestionId", "Purpose" },
                unique: true,
                filter: "[EditSuggestionId] IS NOT NULL AND [Purpose] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_UserMessages_EditSuggestions_EditSuggestionId",
                table: "UserMessages",
                column: "EditSuggestionId",
                principalTable: "EditSuggestions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.Sql(
                """
                INSERT INTO [UserMessages]
                    ([ContributionId], [BoxsetId], [EditSuggestionId], [FromUserId], [ToUserId], [Message], [IsRead], [CreatedAt], [Type], [Purpose])
                SELECT
                    NULL,
                    NULL,
                    message.[SuggestionId],
                    message.[FromUserId],
                    message.[ToUserId],
                    message.[Message],
                    message.[IsRead],
                    message.[CreatedAt],
                    CASE
                        WHEN message.[FromUserId] = suggestion.[UserId] THEN N'UserMessage'
                        ELSE N'AdminMessage'
                    END,
                    NULL
                FROM [EditSuggestionMessages] AS message
                INNER JOIN [EditSuggestions] AS suggestion
                    ON suggestion.[Id] = message.[SuggestionId];
                """);

            migrationBuilder.DropTable(
                name: "EditSuggestionMessages");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE [EditSuggestions]
                SET [Status] = N'InReview'
                WHERE [Status] = N'ChangesRequested';
                """);

            migrationBuilder.CreateTable(
                name: "EditSuggestionMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SuggestionId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FromUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ToUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditSuggestionMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EditSuggestionMessages_EditSuggestions_SuggestionId",
                        column: x => x.SuggestionId,
                        principalTable: "EditSuggestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EditSuggestionMessages_SuggestionId",
                table: "EditSuggestionMessages",
                column: "SuggestionId");

            migrationBuilder.CreateIndex(
                name: "IX_EditSuggestionMessages_ToUserId_IsRead",
                table: "EditSuggestionMessages",
                columns: new[] { "ToUserId", "IsRead" });

            migrationBuilder.Sql(
                """
                INSERT INTO [EditSuggestionMessages]
                    ([SuggestionId], [FromUserId], [ToUserId], [Message], [IsRead], [CreatedAt])
                SELECT
                    [EditSuggestionId],
                    [FromUserId],
                    [ToUserId],
                    [Message],
                    [IsRead],
                    [CreatedAt]
                FROM [UserMessages]
                WHERE [EditSuggestionId] IS NOT NULL;

                DELETE FROM [UserMessages]
                WHERE [EditSuggestionId] IS NOT NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_UserMessages_EditSuggestions_EditSuggestionId",
                table: "UserMessages");

            migrationBuilder.DropIndex(
                name: "IX_UserMessages_EditSuggestionId",
                table: "UserMessages");

            migrationBuilder.DropIndex(
                name: "IX_UserMessages_EditSuggestionId_Purpose",
                table: "UserMessages");

            migrationBuilder.DropColumn(
                name: "EditSuggestionId",
                table: "UserMessages");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "UserMessages");
        }
    }
}
