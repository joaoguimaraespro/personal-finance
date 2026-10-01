using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Persistence.Migrations
{
    /// <summary>Broker accounts created before the provider was renamed still say "Interactive Brokers (Flex)".</summary>
    public partial class RenameIbkrInstitution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE finance.accounts SET institution = 'Interactive Brokers' WHERE institution = 'Interactive Brokers (Flex)';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the old label is not restored.
        }
    }
}
