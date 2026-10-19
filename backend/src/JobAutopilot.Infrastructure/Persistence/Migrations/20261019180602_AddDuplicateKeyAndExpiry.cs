using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobAutopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDuplicateKeyAndExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DuplicateKey",
                table: "Vacancies",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "Vacancies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vacancies_DuplicateKey",
                table: "Vacancies",
                column: "DuplicateKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vacancies_DuplicateKey",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "DuplicateKey",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "Vacancies");
        }
    }
}
