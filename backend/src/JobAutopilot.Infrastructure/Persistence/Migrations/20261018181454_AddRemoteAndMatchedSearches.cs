using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobAutopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRemoteAndMatchedSearches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRemote",
                table: "Vacancies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<List<string>>(
                name: "MatchedSearches",
                table: "Vacancies",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.CreateIndex(
                name: "IX_Vacancies_IsRemote",
                table: "Vacancies",
                column: "IsRemote");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vacancies_IsRemote",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "IsRemote",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "MatchedSearches",
                table: "Vacancies");
        }
    }
}
