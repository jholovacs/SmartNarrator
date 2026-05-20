using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartNarrator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackgroundJobUpdatedUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedUtc",
                table: "background_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE background_jobs
                SET "UpdatedUtc" = COALESCE("CompletedUtc", "StartedUtc", "CreatedUtc")
                WHERE "UpdatedUtc" IS NULL;
                """);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedUtc",
                table: "background_jobs",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UpdatedUtc",
                table: "background_jobs");
        }
    }
}
