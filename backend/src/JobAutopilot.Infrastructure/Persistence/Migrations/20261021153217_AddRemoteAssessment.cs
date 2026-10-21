using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobAutopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRemoteAssessment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RemoteNote",
                table: "Vacancies",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RemotePartial",
                table: "Vacancies",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemoteNote",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "RemotePartial",
                table: "Vacancies");
        }
    }
}
