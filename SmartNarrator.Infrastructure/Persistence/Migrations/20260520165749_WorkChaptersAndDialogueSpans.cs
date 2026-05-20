using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartNarrator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkChaptersAndDialogueSpans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "work_chapters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    StartOffset = table.Column<int>(type: "integer", nullable: false),
                    EndOffset = table.Column<int>(type: "integer", nullable: false),
                    HeadingStartOffset = table.Column<int>(type: "integer", nullable: true),
                    HeadingEndOffset = table.Column<int>(type: "integer", nullable: true),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    IsAiSuggested = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_chapters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_chapters_works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dialogue_spans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChapterId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderIndexInChapter = table.Column<int>(type: "integer", nullable: false),
                    StartOffset = table.Column<int>(type: "integer", nullable: false),
                    EndOffset = table.Column<int>(type: "integer", nullable: false),
                    SpeakerKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    IsAiSuggested = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dialogue_spans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dialogue_spans_work_chapters_ChapterId",
                        column: x => x.ChapterId,
                        principalTable: "work_chapters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_dialogue_spans_works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dialogue_spans_ChapterId_OrderIndexInChapter",
                table: "dialogue_spans",
                columns: new[] { "ChapterId", "OrderIndexInChapter" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dialogue_spans_WorkId_StartOffset",
                table: "dialogue_spans",
                columns: new[] { "WorkId", "StartOffset" });

            migrationBuilder.CreateIndex(
                name: "IX_work_chapters_WorkId_OrderIndex",
                table: "work_chapters",
                columns: new[] { "WorkId", "OrderIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dialogue_spans");

            migrationBuilder.DropTable(
                name: "work_chapters");
        }
    }
}
