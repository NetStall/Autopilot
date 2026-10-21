using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobAutopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRuleRankScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RuleRankScore",
                table: "Vacancies",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vacancies_AiScore",
                table: "Vacancies",
                column: "AiScore");

            migrationBuilder.CreateIndex(
                name: "IX_Vacancies_RuleRankScore",
                table: "Vacancies",
                column: "RuleRankScore");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vacancies_AiScore",
                table: "Vacancies");

            migrationBuilder.DropIndex(
                name: "IX_Vacancies_RuleRankScore",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "RuleRankScore",
                table: "Vacancies");
        }
    }
}
