using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartNarrator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "works",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CanonicalText = table.Column<string>(type: "text", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_works", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "audio_artifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    UtteranceId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartOffset = table.Column<int>(type: "integer", nullable: true),
                    EndOffset = table.Column<int>(type: "integer", nullable: true),
                    RelativePath = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    MimeType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audio_artifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audio_artifacts_works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "background_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProgressPercent = table.Column<int>(type: "integer", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayloadJson = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_background_jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_background_jobs_works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "characters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    AiExternalKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AliasesJson = table.Column<string>(type: "text", nullable: true),
                    GenderPresentation = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Tone = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Accent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Breathiness = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SpeakingPace = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsAiSuggested = table.Column<bool>(type: "boolean", nullable: false),
                    UserApproved = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_characters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_characters_works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "source_documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Format = table.Column<int>(type: "integer", nullable: false),
                    StoredRelativePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    OriginalFileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_source_documents_works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "text_segments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    StartOffset = table.Column<int>(type: "integer", nullable: false),
                    EndOffset = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_text_segments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_text_segments_works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "narrative_passages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartOffset = table.Column<int>(type: "integer", nullable: false),
                    EndOffset = table.Column<int>(type: "integer", nullable: false),
                    NarratorCharacterId = table.Column<Guid>(type: "uuid", nullable: true),
                    PerspectiveNotes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    GenderPresentation = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Tone = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Accent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Breathiness = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SpeakingPace = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsAiSuggested = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_narrative_passages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_narrative_passages_characters_NarratorCharacterId",
                        column: x => x.NarratorCharacterId,
                        principalTable: "characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_narrative_passages_works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "utterances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartOffset = table.Column<int>(type: "integer", nullable: false),
                    EndOffset = table.Column<int>(type: "integer", nullable: false),
                    SpeakerKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CharacterId = table.Column<Guid>(type: "uuid", nullable: true),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    IsAiSuggested = table.Column<bool>(type: "boolean", nullable: false),
                    UserApproved = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_utterances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_utterances_characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_utterances_works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audio_artifacts_WorkId",
                table: "audio_artifacts",
                column: "WorkId");

            migrationBuilder.CreateIndex(
                name: "IX_background_jobs_Status_CreatedUtc",
                table: "background_jobs",
                columns: new[] { "Status", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_background_jobs_WorkId",
                table: "background_jobs",
                column: "WorkId");

            migrationBuilder.CreateIndex(
                name: "IX_characters_WorkId_AiExternalKey",
                table: "characters",
                columns: new[] { "WorkId", "AiExternalKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_narrative_passages_NarratorCharacterId",
                table: "narrative_passages",
                column: "NarratorCharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_narrative_passages_WorkId_StartOffset",
                table: "narrative_passages",
                columns: new[] { "WorkId", "StartOffset" });

            migrationBuilder.CreateIndex(
                name: "IX_source_documents_WorkId",
                table: "source_documents",
                column: "WorkId");

            migrationBuilder.CreateIndex(
                name: "IX_text_segments_WorkId_OrderIndex",
                table: "text_segments",
                columns: new[] { "WorkId", "OrderIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_utterances_CharacterId",
                table: "utterances",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_utterances_WorkId_StartOffset",
                table: "utterances",
                columns: new[] { "WorkId", "StartOffset" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audio_artifacts");

            migrationBuilder.DropTable(
                name: "background_jobs");

            migrationBuilder.DropTable(
                name: "narrative_passages");

            migrationBuilder.DropTable(
                name: "source_documents");

            migrationBuilder.DropTable(
                name: "text_segments");

            migrationBuilder.DropTable(
                name: "utterances");

            migrationBuilder.DropTable(
                name: "characters");

            migrationBuilder.DropTable(
                name: "works");
        }
    }
}
