using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseAdditionalCostInvoiceBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdditionalCostInvoiceBindings",
                schema: "purchasing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(24,6)", precision: 24, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdditionalCostInvoiceBindings", x => new { x.CompanyId, x.InvoiceId, x.InvoiceLineId, x.CostId });
                    table.CheckConstraint("CK_AdditionalCostInvoiceBindings_Net", "[NetAmount] > 0 AND [InvoiceLineId] <> '00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_AdditionalCostInvoiceBindings_AdditionalCosts_CompanyId_CostId",
                        columns: x => new { x.CompanyId, x.CostId },
                        principalSchema: "purchasing",
                        principalTable: "AdditionalCosts",
                        principalColumns: new[] { "CompanyId", "CostId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdditionalCostInvoiceBindings_IncomingInvoices_CompanyId_InvoiceId",
                        columns: x => new { x.CompanyId, x.InvoiceId },
                        principalSchema: "purchasing",
                        principalTable: "IncomingInvoices",
                        principalColumns: new[] { "CompanyId", "InvoiceId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdditionalCostInvoiceBindings_CompanyId_CostId",
                schema: "purchasing",
                table: "AdditionalCostInvoiceBindings",
                columns: new[] { "CompanyId", "CostId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [purchasing].[AdditionalCostInvoiceBindings])
                    THROW 51939, 'Purchase expense invoice matching history requires a forward fix.', 1;
                """);

            migrationBuilder.DropTable(
                name: "AdditionalCostInvoiceBindings",
                schema: "purchasing");
        }
    }
}
