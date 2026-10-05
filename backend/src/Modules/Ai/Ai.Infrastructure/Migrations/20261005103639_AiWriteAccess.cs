using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Ai.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AiWriteAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "writes_per_hour",
                schema: "ai",
                table: "clients",
                type: "integer",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<string>(
                name: "changes",
                schema: "ai",
                table: "audit_events",
                type: "jsonb",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "record_id",
                schema: "ai",
                table: "audit_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "write",
                schema: "ai",
                table: "audit_events",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "recycle_bin",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    summary = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    purge_after_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    restored_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    purged_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recycle_bin", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "write_receipts",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tool = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    arguments_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    result = table.Column<string>(type: "jsonb", maxLength: 200, nullable: false),
                    at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_write_receipts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recycle_bin_purge_after_utc",
                schema: "ai",
                table: "recycle_bin",
                column: "purge_after_utc");

            migrationBuilder.CreateIndex(
                name: "ix_recycle_bin_record_id",
                schema: "ai",
                table: "recycle_bin",
                column: "record_id");

            migrationBuilder.CreateIndex(
                name: "ix_write_receipts_at_utc",
                schema: "ai",
                table: "write_receipts",
                column: "at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_write_receipts_client_id_key",
                schema: "ai",
                table: "write_receipts",
                columns: new[] { "client_id", "key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recycle_bin",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "write_receipts",
                schema: "ai");

            migrationBuilder.DropColumn(
                name: "writes_per_hour",
                schema: "ai",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "changes",
                schema: "ai",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "record_id",
                schema: "ai",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "write",
                schema: "ai",
                table: "audit_events");
        }
    }
}
