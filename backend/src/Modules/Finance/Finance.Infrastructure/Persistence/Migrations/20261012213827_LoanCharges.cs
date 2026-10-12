using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LoanCharges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "monthly_fee",
                schema: "finance",
                table: "loans",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "monthly_insurance",
                schema: "finance",
                table: "loans",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "stamp_duty",
                schema: "finance",
                table: "loans",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "monthly_fee",
                schema: "finance",
                table: "loans");

            migrationBuilder.DropColumn(
                name: "monthly_insurance",
                schema: "finance",
                table: "loans");

            migrationBuilder.DropColumn(
                name: "stamp_duty",
                schema: "finance",
                table: "loans");
        }
    }
}
