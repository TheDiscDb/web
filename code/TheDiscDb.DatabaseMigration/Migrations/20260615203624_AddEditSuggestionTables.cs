using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscDb.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddEditSuggestionTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EditSuggestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TargetEntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TargetEntityKey = table.Column<string>(type: "nvarchar(410)", maxLength: 410, nullable: true),
                    ReviewedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditSuggestions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EditSuggestionChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SuggestionId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    OriginalSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProposedJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AppliedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    SyncedToFilesAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConflictReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AdminNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditSuggestionChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EditSuggestionChanges_EditSuggestions_SuggestionId",
                        column: x => x.SuggestionId,
                        principalTable: "EditSuggestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EditSuggestionMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SuggestionId = table.Column<int>(type: "int", nullable: false),
                    FromUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ToUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "EditSuggestionHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SuggestionId = table.Column<int>(type: "int", nullable: false),
                    ChangeId = table.Column<int>(type: "int", nullable: true),
                    TimeStamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditSuggestionHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EditSuggestionHistory_EditSuggestionChanges_ChangeId",
                        column: x => x.ChangeId,
                        principalTable: "EditSuggestionChanges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EditSuggestionHistory_EditSuggestions_SuggestionId",
                        column: x => x.SuggestionId,
                        principalTable: "EditSuggestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EditSuggestionChanges_Status_SyncedToFilesAt",
                table: "EditSuggestionChanges",
                columns: new[] { "Status", "SyncedToFilesAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EditSuggestionChanges_SuggestionId_Ordinal",
                table: "EditSuggestionChanges",
                columns: new[] { "SuggestionId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_EditSuggestionHistory_ChangeId",
                table: "EditSuggestionHistory",
                column: "ChangeId");

            migrationBuilder.CreateIndex(
                name: "IX_EditSuggestionHistory_SuggestionId",
                table: "EditSuggestionHistory",
                column: "SuggestionId");

            migrationBuilder.CreateIndex(
                name: "IX_EditSuggestionMessages_SuggestionId",
                table: "EditSuggestionMessages",
                column: "SuggestionId");

            migrationBuilder.CreateIndex(
                name: "IX_EditSuggestionMessages_ToUserId_IsRead",
                table: "EditSuggestionMessages",
                columns: new[] { "ToUserId", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_EditSuggestions_Status_Created",
                table: "EditSuggestions",
                columns: new[] { "Status", "Created" });

            migrationBuilder.CreateIndex(
                name: "IX_EditSuggestions_TargetEntityType_TargetEntityKey",
                table: "EditSuggestions",
                columns: new[] { "TargetEntityType", "TargetEntityKey" });

            migrationBuilder.CreateIndex(
                name: "IX_EditSuggestions_UserId_Created",
                table: "EditSuggestions",
                columns: new[] { "UserId", "Created" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EditSuggestionHistory");

            migrationBuilder.DropTable(
                name: "EditSuggestionMessages");

            migrationBuilder.DropTable(
                name: "EditSuggestionChanges");

            migrationBuilder.DropTable(
                name: "EditSuggestions");
        }
    }
}
