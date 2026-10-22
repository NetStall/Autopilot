using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobAutopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHardFilters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FilterNote",
                table: "Vacancies",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FilterReason",
                table: "Vacancies",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vacancies_FilterReason",
                table: "Vacancies",
                column: "FilterReason");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vacancies_FilterReason",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "FilterNote",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "FilterReason",
                table: "Vacancies");
        }
    }
}
