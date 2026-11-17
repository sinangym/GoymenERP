using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseServiceOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServiceOrderAttempts",
                schema: "purchasing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Command = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                    KeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RefusalJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceOrderAttempts", x => new { x.CompanyId, x.Command, x.KeyHash });
                    table.CheckConstraint("CK_ServiceOrderAttempts_Result", "([SnapshotJson] IS NOT NULL AND ISJSON([SnapshotJson]) = 1 AND [RefusalJson] IS NULL) OR ([SnapshotJson] IS NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)");
                    table.ForeignKey(
                        name: "FK_ServiceOrderAttempts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServiceOrders",
                schema: "purchasing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceOrders", x => new { x.CompanyId, x.OrderId });
                    table.CheckConstraint("CK_ServiceOrders_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [OrderId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0");
                    table.ForeignKey(
                        name: "FK_ServiceOrders_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServiceAcceptances",
                schema: "purchasing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcceptanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(24,6)", precision: 24, scale: 6, nullable: false),
                    OrderVersionAfter = table.Column<long>(type: "bigint", nullable: false),
                    SupplierPartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceAcceptances", x => new { x.CompanyId, x.OrderId, x.LineId, x.AcceptanceId });
                    table.CheckConstraint("CK_ServiceAcceptances_Fact", "[Quantity] > 0 AND [OrderVersionAfter] >= 3");
                    table.ForeignKey(
                        name: "FK_ServiceAcceptances_ServiceOrders_CompanyId_OrderId",
                        columns: x => new { x.CompanyId, x.OrderId },
                        principalSchema: "purchasing",
                        principalTable: "ServiceOrders",
                        principalColumns: new[] { "CompanyId", "OrderId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServiceOrderRevisions",
                schema: "purchasing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceOrderRevisions", x => new { x.CompanyId, x.OrderId, x.Version });
                    table.CheckConstraint("CK_ServiceOrderRevisions_Snapshot", "[Version] > 0 AND ISJSON([SnapshotJson]) = 1");
                    table.ForeignKey(
                        name: "FK_ServiceOrderRevisions_ServiceOrders_CompanyId_OrderId",
                        columns: x => new { x.CompanyId, x.OrderId },
                        principalSchema: "purchasing",
                        principalTable: "ServiceOrders",
                        principalColumns: new[] { "CompanyId", "OrderId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServiceInvoiceBindings",
                schema: "purchasing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcceptanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(24,6)", precision: 24, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceInvoiceBindings", x => new { x.CompanyId, x.InvoiceId, x.InvoiceLineId, x.AcceptanceId });
                    table.CheckConstraint("CK_ServiceInvoiceBindings_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_ServiceInvoiceBindings_IncomingInvoices_CompanyId_InvoiceId",
                        columns: x => new { x.CompanyId, x.InvoiceId },
                        principalSchema: "purchasing",
                        principalTable: "IncomingInvoices",
                        principalColumns: new[] { "CompanyId", "InvoiceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceInvoiceBindings_ServiceAcceptances_CompanyId_OrderId_LineId_AcceptanceId",
                        columns: x => new { x.CompanyId, x.OrderId, x.LineId, x.AcceptanceId },
                        principalSchema: "purchasing",
                        principalTable: "ServiceAcceptances",
                        principalColumns: new[] { "CompanyId", "OrderId", "LineId", "AcceptanceId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceAcceptances_CompanyId_AcceptanceId",
                schema: "purchasing",
                table: "ServiceAcceptances",
                columns: new[] { "CompanyId", "AcceptanceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceAcceptances_CompanyId_SupplierPartyId_CompanyAccountId_CurrencyId_ServiceTypeId_UnitId",
                schema: "purchasing",
                table: "ServiceAcceptances",
                columns: new[] { "CompanyId", "SupplierPartyId", "CompanyAccountId", "CurrencyId", "ServiceTypeId", "UnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceInvoiceBindings_CompanyId_AcceptanceId",
                schema: "purchasing",
                table: "ServiceInvoiceBindings",
                columns: new[] { "CompanyId", "AcceptanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceInvoiceBindings_CompanyId_OrderId_LineId_AcceptanceId",
                schema: "purchasing",
                table: "ServiceInvoiceBindings",
                columns: new[] { "CompanyId", "OrderId", "LineId", "AcceptanceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [purchasing].[ServiceOrders])
                    OR EXISTS (SELECT 1 FROM [purchasing].[ServiceOrderRevisions])
                    OR EXISTS (SELECT 1 FROM [purchasing].[ServiceOrderAttempts])
                    OR EXISTS (SELECT 1 FROM [purchasing].[ServiceAcceptances])
                    OR EXISTS (SELECT 1 FROM [purchasing].[ServiceInvoiceBindings])
                BEGIN
                    THROW 51992, 'Service order, acceptance, invoice binding or terminal history exists; AddPurchaseServiceOrders is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "ServiceInvoiceBindings",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "ServiceOrderAttempts",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "ServiceOrderRevisions",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "ServiceAcceptances",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "ServiceOrders",
                schema: "purchasing");
        }
    }
}
