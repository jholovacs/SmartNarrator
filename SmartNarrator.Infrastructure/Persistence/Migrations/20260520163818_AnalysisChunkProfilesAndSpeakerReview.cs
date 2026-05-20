using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartNarrator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnalysisChunkProfilesAndSpeakerReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SpeakerNeedsReview",
                table: "utterances",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PersonalitySummary",
                table: "characters",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpeechStyleSummary",
                table: "characters",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProgressPhase",
                table: "background_jobs",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpeakerNeedsReview",
                table: "utterances");

            migrationBuilder.DropColumn(
                name: "PersonalitySummary",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "SpeechStyleSummary",
                table: "characters");

            migrationBuilder.AlterColumn<string>(
                name: "ProgressPhase",
                table: "background_jobs",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024,
                oldNullable: true);
        }
    }
}
