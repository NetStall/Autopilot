using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobAutopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiFitScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AiScore",
                table: "Vacancies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiScoreJson",
                table: "Vacancies",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiScoreKey",
                table: "Vacancies",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VacancyFitScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DescriptionHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ProfileHash = table.Column<string>(type: "character(16)", fixedLength: true, maxLength: 16, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PromptVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: true),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VacancyFitScores", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VacancyFitScores_DescriptionHash_ProfileHash_Model_PromptVe~",
                table: "VacancyFitScores",
                columns: new[] { "DescriptionHash", "ProfileHash", "Model", "PromptVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VacancyFitScores");

            migrationBuilder.DropColumn(
                name: "AiScore",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "AiScoreJson",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "AiScoreKey",
                table: "Vacancies");
        }
    }
}
