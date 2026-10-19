using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobAutopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Applications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VacancyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Company = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Position = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Channel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ContactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ContactEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastActionAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SalaryExpectation = table.Column<int>(type: "integer", nullable: true),
                    Documents = table.Column<List<string>>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    NextStep = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    ExternalRef = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DuplicateKey = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Applications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Applications_Vacancies_VacancyId",
                        column: x => x.VacancyId,
                        principalTable: "Vacancies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    From = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    To = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationEvents_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationEvents_ApplicationId_At",
                table: "ApplicationEvents",
                columns: new[] { "ApplicationId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_Applications_DuplicateKey",
                table: "Applications",
                column: "DuplicateKey");

            migrationBuilder.CreateIndex(
                name: "IX_Applications_ExternalRef",
                table: "Applications",
                column: "ExternalRef",
                unique: true,
                filter: "\"ExternalRef\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Applications_Status",
                table: "Applications",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Applications_VacancyId",
                table: "Applications",
                column: "VacancyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationEvents");

            migrationBuilder.DropTable(
                name: "Applications");
        }
    }
}
