using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LoanInstalmentBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "last_handled_instalment",
                schema: "finance",
                table: "loans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "payment_account_id",
                schema: "finance",
                table: "loans",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_handled_instalment",
                schema: "finance",
                table: "loans");

            migrationBuilder.DropColumn(
                name: "payment_account_id",
                schema: "finance",
                table: "loans");
        }
    }
}
