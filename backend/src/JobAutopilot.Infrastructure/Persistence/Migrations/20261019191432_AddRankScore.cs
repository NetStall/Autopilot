using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobAutopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRankScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RankScore",
                table: "Vacancies",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vacancies_RankScore",
                table: "Vacancies",
                column: "RankScore");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vacancies_RankScore",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "RankScore",
                table: "Vacancies");
        }
    }
}
