using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseSupplierRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrders_Approval",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.AddColumn<string>(
                name: "CurrentPreparedBy",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierRevisionHistoryJson",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PurchaseInvoiceOrderBindings",
                schema: "purchasing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderVersion = table.Column<long>(type: "bigint", nullable: false),
                    SupplierPartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoiceOrderBindings", x => new { x.CompanyId, x.InvoiceId, x.InvoiceLineId, x.OrderLineId });
                    table.CheckConstraint("CK_PurchaseInvoiceOrderBindings_Scope", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceLineId] <> '00000000-0000-0000-0000-000000000000' AND [OrderId] <> '00000000-0000-0000-0000-000000000000' AND [OrderLineId] <> '00000000-0000-0000-0000-000000000000' AND [SupplierPartyId] <> '00000000-0000-0000-0000-000000000000' AND [OrderVersion] > 0");
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceOrderBindings_PurchaseOrderLines_CompanyId_OrderId_OrderLineId",
                        columns: x => new { x.CompanyId, x.OrderId, x.OrderLineId },
                        principalSchema: "purchasing",
                        principalTable: "PurchaseOrderLines",
                        principalColumns: new[] { "CompanyId", "OrderId", "LineId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrders_Approval",
                schema: "purchasing",
                table: "PurchaseOrders",
                sql: "([ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL) OR ([ApprovedBy] IS NOT NULL AND LEN([ApprovedBy]) > 0 AND [ApprovedAtUtc] IS NOT NULL AND [Status] IN (2, 3) AND [ApprovedBy] COLLATE Latin1_General_100_BIN2 <> COALESCE([CurrentPreparedBy], [CreatedBy]) COLLATE Latin1_General_100_BIN2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrders_SupplierRevisionHistory",
                schema: "purchasing",
                table: "PurchaseOrders",
                sql: "([SupplierRevisionHistoryJson] IS NULL AND [CurrentPreparedBy] IS NULL) OR ([SupplierRevisionHistoryJson] IS NOT NULL AND ISJSON([SupplierRevisionHistoryJson]) = 1 AND [CurrentPreparedBy] IS NOT NULL AND LEN([CurrentPreparedBy]) > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceOrderBindings_CompanyId_OrderId",
                schema: "purchasing",
                table: "PurchaseInvoiceOrderBindings",
                columns: new[] { "CompanyId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceOrderBindings_CompanyId_OrderId_OrderLineId",
                schema: "purchasing",
                table: "PurchaseInvoiceOrderBindings",
                columns: new[] { "CompanyId", "OrderId", "OrderLineId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseOrders] WHERE [SupplierRevisionHistoryJson] IS NOT NULL OR [CurrentPreparedBy] IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM [purchasing].[PurchaseInvoiceOrderBindings])
                BEGIN
                    THROW 51977, 'Supplier revision or invoice binding history exists; AddPurchaseSupplierRevisions is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "PurchaseInvoiceOrderBindings",
                schema: "purchasing");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrders_Approval",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrders_SupplierRevisionHistory",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "CurrentPreparedBy",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "SupplierRevisionHistoryJson",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrders_Approval",
                schema: "purchasing",
                table: "PurchaseOrders",
                sql: "([ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL) OR ([ApprovedBy] IS NOT NULL AND LEN([ApprovedBy]) > 0 AND [ApprovedAtUtc] IS NOT NULL AND [Status] IN (2, 3) AND [ApprovedBy] COLLATE Latin1_General_100_BIN2 <> [CreatedBy] COLLATE Latin1_General_100_BIN2)");
        }
    }
}
