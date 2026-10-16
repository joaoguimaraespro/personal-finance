using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SharedExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "shared_from_id",
                schema: "finance",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_transactions_shared_from_id",
                schema: "finance",
                table: "transactions",
                column: "shared_from_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_transactions_shared_from_id",
                schema: "finance",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "shared_from_id",
                schema: "finance",
                table: "transactions");
        }
    }
}
