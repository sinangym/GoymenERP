using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseIncomingInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IncomingInvoiceAttempts",
                schema: "purchasing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RefusalJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomingInvoiceAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_IncomingInvoiceAttempts_Result", "([SnapshotJson] IS NOT NULL AND ISJSON([SnapshotJson]) = 1 AND [RefusalJson] IS NULL) OR ([SnapshotJson] IS NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)");
                    table.ForeignKey(
                        name: "FK_IncomingInvoiceAttempts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IncomingInvoices",
                schema: "purchasing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierPartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ettn = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OfficialNumber = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentVersion = table.Column<long>(type: "bigint", nullable: false),
                    DocumentSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomingInvoices", x => new { x.CompanyId, x.InvoiceId });
                    table.CheckConstraint("CK_IncomingInvoices_Source", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [SupplierPartyId] <> '00000000-0000-0000-0000-000000000000' AND [Ettn] <> '00000000-0000-0000-0000-000000000000' AND [DocumentId] <> '00000000-0000-0000-0000-000000000000' AND [DocumentVersion] > 0 AND LEN([OfficialNumber]) > 0 AND LEN([DocumentSha256]) = 64");
                    table.ForeignKey(
                        name: "FK_IncomingInvoices_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IncomingInvoiceRevisions",
                schema: "purchasing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomingInvoiceRevisions", x => new { x.CompanyId, x.InvoiceId, x.Version });
                    table.CheckConstraint("CK_IncomingInvoiceRevisions_Snapshot", "[Version] > 0 AND [Status] IN (1,2,3,4) AND ISJSON([SnapshotJson]) = 1");
                    table.ForeignKey(
                        name: "FK_IncomingInvoiceRevisions_IncomingInvoices_CompanyId_InvoiceId",
                        columns: x => new { x.CompanyId, x.InvoiceId },
                        principalSchema: "purchasing",
                        principalTable: "IncomingInvoices",
                        principalColumns: new[] { "CompanyId", "InvoiceId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceReceiptBindings",
                schema: "purchasing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcceptanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(24,6)", precision: 24, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceReceiptBindings", x => new { x.CompanyId, x.InvoiceId, x.InvoiceLineId, x.AcceptanceId });
                    table.CheckConstraint("CK_InvoiceReceiptBindings_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_InvoiceReceiptBindings_IncomingInvoices_CompanyId_InvoiceId",
                        columns: x => new { x.CompanyId, x.InvoiceId },
                        principalSchema: "purchasing",
                        principalTable: "IncomingInvoices",
                        principalColumns: new[] { "CompanyId", "InvoiceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceReceiptBindings_PurchaseAcceptanceShares_CompanyId_OrderId_OrderLineId_AcceptanceId",
                        columns: x => new { x.CompanyId, x.OrderId, x.OrderLineId, x.AcceptanceId },
                        principalSchema: "purchasing",
                        principalTable: "PurchaseAcceptanceShares",
                        principalColumns: new[] { "CompanyId", "OrderId", "LineId", "AcceptanceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceReceiptBindings_PurchaseOrderLines_CompanyId_OrderId_OrderLineId",
                        columns: x => new { x.CompanyId, x.OrderId, x.OrderLineId },
                        principalSchema: "purchasing",
                        principalTable: "PurchaseOrderLines",
                        principalColumns: new[] { "CompanyId", "OrderId", "LineId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IncomingInvoices_CompanyId_Ettn",
                schema: "purchasing",
                table: "IncomingInvoices",
                columns: new[] { "CompanyId", "Ettn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncomingInvoices_CompanyId_SupplierPartyId_OfficialNumber",
                schema: "purchasing",
                table: "IncomingInvoices",
                columns: new[] { "CompanyId", "SupplierPartyId", "OfficialNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceReceiptBindings_CompanyId_AcceptanceId",
                schema: "purchasing",
                table: "InvoiceReceiptBindings",
                columns: new[] { "CompanyId", "AcceptanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceReceiptBindings_CompanyId_OrderId_OrderLineId_AcceptanceId",
                schema: "purchasing",
                table: "InvoiceReceiptBindings",
                columns: new[] { "CompanyId", "OrderId", "OrderLineId", "AcceptanceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [purchasing].[IncomingInvoices])
                    OR EXISTS (SELECT 1 FROM [purchasing].[IncomingInvoiceRevisions])
                    OR EXISTS (SELECT 1 FROM [purchasing].[IncomingInvoiceAttempts])
                    OR EXISTS (SELECT 1 FROM [purchasing].[InvoiceReceiptBindings])
                BEGIN
                    THROW 51980, 'Incoming invoice source, history or terminal records exist; AddPurchaseIncomingInvoices is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "IncomingInvoiceAttempts",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "IncomingInvoiceRevisions",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "InvoiceReceiptBindings",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "IncomingInvoices",
                schema: "purchasing");
        }
    }
}
