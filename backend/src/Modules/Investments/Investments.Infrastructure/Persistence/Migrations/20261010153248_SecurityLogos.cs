using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Investments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SecurityLogos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "security_logos",
                schema: "investments",
                columns: table => new
                {
                    security_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: true),
                    content_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    fetched_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_security_logos", x => x.security_id);
                    table.ForeignKey(
                        name: "fk_security_logos_securities_security_id",
                        column: x => x.security_id,
                        principalSchema: "investments",
                        principalTable: "securities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "security_logos",
                schema: "investments");
        }
    }
}
