using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integrations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "integrations");

            migrationBuilder.CreateTable(
                name: "broker_connections",
                schema: "integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    protected_credentials = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    last_successful_sync_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    credentials_expire_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broker_connections", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sync_jobs",
                schema: "integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    imported = table.Column<int>(type: "integer", nullable: false),
                    updated = table.Column<int>(type: "integer", nullable: false),
                    ignored = table.Column<int>(type: "integer", nullable: false),
                    errors = table.Column<string>(type: "jsonb", maxLength: 20000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_sync_jobs_broker_connections_connection_id",
                        column: x => x.connection_id,
                        principalSchema: "integrations",
                        principalTable: "broker_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_broker_connections_account_id",
                schema: "integrations",
                table: "broker_connections",
                column: "account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sync_jobs_connection_id_started_at_utc",
                schema: "integrations",
                table: "sync_jobs",
                columns: new[] { "connection_id", "started_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sync_jobs",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "broker_connections",
                schema: "integrations");
        }
    }
}
