using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobAutopilot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Step 04: description details, relevance score, and <c>RemoteLevel</c> instead of the <c>IsRemote</c> flag.
    /// Data: a vacancy found by a remote-only search (<c>IsRemote</c>) is at least <c>Hybrid</c>; the next import and
    /// the re-scoring raise it to <c>FullRemote</c> where the source or the description says so.
    /// </summary>
    public partial class AddRelevanceAndRemoteLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RemoteLevel",
                table: "Vacancies",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unknown");

            // Before IsRemote is dropped: keep the information.
            migrationBuilder.Sql("""UPDATE "Vacancies" SET "RemoteLevel" = 'Hybrid' WHERE "IsRemote";""");

            migrationBuilder.DropIndex(
                name: "IX_Vacancies_IsRemote",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "IsRemote",
                table: "Vacancies");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DetailsFetchedAt",
                table: "Vacancies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelevanceLevel",
                table: "Vacancies",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "RelevanceReasons",
                table: "Vacancies",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<int>(
                name: "RelevanceScore",
                table: "Vacancies",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vacancies_RelevanceLevel",
                table: "Vacancies",
                column: "RelevanceLevel");

            migrationBuilder.CreateIndex(
                name: "IX_Vacancies_RemoteLevel",
                table: "Vacancies",
                column: "RemoteLevel");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRemote",
                table: "Vacancies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""UPDATE "Vacancies" SET "IsRemote" = TRUE WHERE "RemoteLevel" IN ('Hybrid', 'FullRemote');""");

            migrationBuilder.CreateIndex(
                name: "IX_Vacancies_IsRemote",
                table: "Vacancies",
                column: "IsRemote");

            migrationBuilder.DropIndex(
                name: "IX_Vacancies_RelevanceLevel",
                table: "Vacancies");

            migrationBuilder.DropIndex(
                name: "IX_Vacancies_RemoteLevel",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "DetailsFetchedAt",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "RelevanceLevel",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "RelevanceReasons",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "RelevanceScore",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "RemoteLevel",
                table: "Vacancies");
        }
    }
}
