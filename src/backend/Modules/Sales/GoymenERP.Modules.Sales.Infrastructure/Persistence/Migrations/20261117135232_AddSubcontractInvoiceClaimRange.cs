using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubcontractInvoiceClaimRange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SubcontractServiceInvoices_ClaimRange",
                schema: "sales",
                table: "SubcontractServiceInvoices",
                columns: new[] { "CompanyId", "LoadingInstructionId", "WeighingId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubcontractServiceInvoices_ClaimRange",
                schema: "sales",
                table: "SubcontractServiceInvoices");
        }
    }
}
