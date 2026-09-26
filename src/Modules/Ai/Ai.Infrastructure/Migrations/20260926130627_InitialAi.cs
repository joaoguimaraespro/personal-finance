using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Ai.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialAi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ai");

            migrationBuilder.CreateTable(
                name: "audit_events",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    tool = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scope = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    arguments = table.Column<string>(type: "jsonb", maxLength: 200, nullable: false),
                    record_count = table.Column<int>(type: "integer", nullable: false),
                    response_bytes = table.Column<int>(type: "integer", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "clients",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    token_prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scopes = table.Column<List<string>>(type: "text[]", nullable: false),
                    rate_limit_per_minute = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clients", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_at_utc",
                schema: "ai",
                table: "audit_events",
                column: "at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_client_id",
                schema: "ai",
                table: "audit_events",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_clients_token_prefix",
                schema: "ai",
                table: "clients",
                column: "token_prefix",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "clients",
                schema: "ai");
        }
    }
}
