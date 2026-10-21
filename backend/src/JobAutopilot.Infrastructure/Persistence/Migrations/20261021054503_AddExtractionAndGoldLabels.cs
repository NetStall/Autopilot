using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobAutopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExtractionAndGoldLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtractionJson",
                table: "Vacancies",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractionKey",
                table: "Vacancies",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GoldLabels",
                columns: table => new
                {
                    VacancyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fits = table.Column<bool>(type: "boolean", nullable: false),
                    LabeledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoldLabels", x => x.VacancyId);
                    table.ForeignKey(
                        name: "FK_GoldLabels_Vacancies_VacancyId",
                        column: x => x.VacancyId,
                        principalTable: "Vacancies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VacancyExtractions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DescriptionHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PromptVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: true),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VacancyExtractions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VacancyExtractions_DescriptionHash_Model_PromptVersion",
                table: "VacancyExtractions",
                columns: new[] { "DescriptionHash", "Model", "PromptVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GoldLabels");

            migrationBuilder.DropTable(
                name: "VacancyExtractions");

            migrationBuilder.DropColumn(
                name: "ExtractionJson",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "ExtractionKey",
                table: "Vacancies");
        }
    }
}
