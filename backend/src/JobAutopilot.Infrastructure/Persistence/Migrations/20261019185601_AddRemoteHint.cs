using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobAutopilot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Step 05 part 0: the sticky source/search signal is stored separately, so <c>RemoteLevel</c> can be recomputed
    /// (and lowered) on re-scoring. Data: Himalayas jobs are remote by source; every vacancy that was hybrid or remote so
    /// far keeps at least hybrid (found by a remote search or with a home-office flag); the next import restores
    /// „100 %" values from the Arbeitsagentur data.
    /// </summary>
    public partial class AddRemoteHint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RemoteHint",
                table: "Vacancies",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.Sql("""
                UPDATE "Vacancies" SET "RemoteHint" = CASE
                    WHEN "Source" = 'Himalayas' THEN 'FullRemote'
                    WHEN "RemoteLevel" IN ('Hybrid', 'FullRemote') THEN 'Hybrid'
                    ELSE 'Unknown' END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemoteHint",
                table: "Vacancies");
        }
    }
}
