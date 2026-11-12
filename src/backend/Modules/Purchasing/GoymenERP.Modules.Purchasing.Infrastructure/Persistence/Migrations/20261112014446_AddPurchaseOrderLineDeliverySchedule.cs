using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A3 API-30 (ALS-D33, ALS-D21, master §7.3): the append-only table <c>purchasing.PurchaseOrderLineDeliverySlices</c> — the delivery
    /// slices (termin dilimleri, hubvogal's "sıra") of every schedule revision of a general purchase-order line. One revision is the
    /// rows of one (CompanyId, OrderId, OrderVersionAfter) — the order version the schedule command produced — numbered 1..n
    /// (<c>SliceNo</c>) with a distinct <c>SliceId</c> (unique per revision), a <c>Quantity</c> above zero (decimal(18,3)) and a
    /// <c>DeliveryDate</c> (date). Bound to the order and to the line (the line's alternate key) of the same company; the checks keep
    /// empty identities, a non-positive quantity, a slice number outside 1..500, a version below 2 and empty actor/correlation text
    /// out. The Σ slices = the line's ordered quantity rule is the domain's, measured on write and on every rebuild. Every existing
    /// order has no schedule; nothing is backfilled.
    /// <para>
    /// Down refuses (<c>THROW 51173</c>, used by no other migration) while any slice exists: dropping the table would erase the
    /// recorded schedules and break every rebuilt order version that counts them (forward fix). Otherwise it drops the table.
    /// </para>
    /// </summary>
    public partial class AddPurchaseOrderLineDeliverySchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurchaseOrderLineDeliverySlices",
                schema: "purchasing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderVersionAfter = table.Column<long>(type: "bigint", nullable: false),
                    SliceNo = table.Column<int>(type: "int", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SliceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    DeliveryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderLineDeliverySlices", x => new { x.CompanyId, x.OrderId, x.OrderVersionAfter, x.SliceNo });
                    table.CheckConstraint("CK_PurchaseOrderLineDeliverySlices_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [OrderId] <> '00000000-0000-0000-0000-000000000000' AND [LineId] <> '00000000-0000-0000-0000-000000000000' AND [SliceId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_PurchaseOrderLineDeliverySlices_Quantity", "[Quantity] > 0");
                    table.CheckConstraint("CK_PurchaseOrderLineDeliverySlices_SliceNo", "[SliceNo] >= 1 AND [SliceNo] <= 500");
                    table.CheckConstraint("CK_PurchaseOrderLineDeliverySlices_Text", "LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_PurchaseOrderLineDeliverySlices_Version", "[OrderVersionAfter] >= 2");
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLineDeliverySlices_PurchaseOrderLines_CompanyId_OrderId_LineId",
                        columns: x => new { x.CompanyId, x.OrderId, x.LineId },
                        principalSchema: "purchasing",
                        principalTable: "PurchaseOrderLines",
                        principalColumns: new[] { "CompanyId", "OrderId", "LineId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLineDeliverySlices_PurchaseOrders_CompanyId_OrderId",
                        columns: x => new { x.CompanyId, x.OrderId },
                        principalSchema: "purchasing",
                        principalTable: "PurchaseOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLineDeliverySlices_OrderLineVersion",
                schema: "purchasing",
                table: "PurchaseOrderLineDeliverySlices",
                columns: new[] { "CompanyId", "OrderId", "LineId", "OrderVersionAfter" });

            migrationBuilder.CreateIndex(
                name: "UX_PurchaseOrderLineDeliverySlices_SliceId",
                schema: "purchasing",
                table: "PurchaseOrderLineDeliverySlices",
                columns: new[] { "CompanyId", "OrderId", "OrderVersionAfter", "SliceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseOrderLineDeliverySlices])
                BEGIN
                    THROW 51173, 'Purchase order delivery slices exist; AddPurchaseOrderLineDeliverySchedule is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "PurchaseOrderLineDeliverySlices",
                schema: "purchasing");
        }
    }
}
