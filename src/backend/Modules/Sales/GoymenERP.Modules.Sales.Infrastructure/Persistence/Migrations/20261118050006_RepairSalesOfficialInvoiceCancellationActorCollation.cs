using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RepairSalesOfficialInvoiceCancellationActorCollation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Reapply the owner trigger without changing rows or restoring the collation-dependent comparison.
            migrationBuilder.Sql(SalesOfficialInvoiceCancellationGuards.InvoiceGuardSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reapply the owner trigger without changing rows or restoring the collation-dependent comparison.
            migrationBuilder.Sql(SalesOfficialInvoiceCancellationGuards.InvoiceGuardSql);
        }
    }
}
