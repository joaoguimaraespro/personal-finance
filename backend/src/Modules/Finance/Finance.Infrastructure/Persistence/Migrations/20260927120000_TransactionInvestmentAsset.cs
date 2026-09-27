using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Instrument details on investment entries (what was bought or sold). All columns are nullable, so existing rows
    /// stay valid; the new <c>InvestmentSale</c> type needs no schema change because types are stored as strings.
    /// Hand-written (no designer file): every operation carries its explicit column type.
    /// </summary>
    [DbContext(typeof(FinanceDbContext))]
    [Migration("20260927120000_TransactionInvestmentAsset")]
    public partial class TransactionInvestmentAsset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "asset_kind",
                schema: "finance",
                table: "transactions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "asset_symbol",
                schema: "finance",
                table: "transactions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "asset_name",
                schema: "finance",
                table: "transactions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "asset_isin",
                schema: "finance",
                table: "transactions",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "asset_quantity",
                schema: "finance",
                table: "transactions",
                type: "numeric(28,10)",
                precision: 28,
                scale: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "asset_unit_price",
                schema: "finance",
                table: "transactions",
                type: "numeric(28,10)",
                precision: 28,
                scale: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "asset_price_source",
                schema: "finance",
                table: "transactions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rows of type 'InvestmentSale' are left in place (no data loss); older code cannot read them, so
            // delete or retype them by hand before running a previous release against this database.
            migrationBuilder.DropColumn(name: "asset_kind", schema: "finance", table: "transactions");
            migrationBuilder.DropColumn(name: "asset_symbol", schema: "finance", table: "transactions");
            migrationBuilder.DropColumn(name: "asset_name", schema: "finance", table: "transactions");
            migrationBuilder.DropColumn(name: "asset_isin", schema: "finance", table: "transactions");
            migrationBuilder.DropColumn(name: "asset_quantity", schema: "finance", table: "transactions");
            migrationBuilder.DropColumn(name: "asset_unit_price", schema: "finance", table: "transactions");
            migrationBuilder.DropColumn(name: "asset_price_source", schema: "finance", table: "transactions");
        }
    }
}
