using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A1-3 (plan API-10, TEK-09; No.161 §2): the purchase order's Cancelled (3) state — the cancellation of an approved order's
    /// whole not-yet-accepted remainder — with its facts (CancelledBy, CancelledAtUtc, CancellationReason) and a check that a
    /// Cancelled order has exactly them and any other status none of them; and each line's cancelled share (Cancelled,
    /// decimal(18,3), default 0) inside the shares check (technical + accepted + cancelled ≤ ordered; master INV-006). Every
    /// existing row stays as it was: no order is Cancelled and every line's cancelled share is 0.
    /// <para>
    /// Down refuses (<c>THROW 51168</c>, used by no other migration) while any order is Cancelled or any line carries a cancelled
    /// share: that state cannot be represented before this migration and dropping the column would silently turn the cancelled
    /// remainder back into future supply (forward fix). Otherwise it drops the new check and columns and restores the status and
    /// shares checks exactly as <c>InitialPurchasing</c> created them.
    /// </para>
    /// </summary>
    public partial class AddPurchaseOrderCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrders_Status",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrderLines_Shares",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAtUtc",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledBy",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Cancelled",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrders_Cancellation",
                schema: "purchasing",
                table: "PurchaseOrders",
                sql: "([Status] = 3 AND [CancelledBy] IS NOT NULL AND LEN([CancelledBy]) > 0 AND [CancelledAtUtc] IS NOT NULL AND [CancellationReason] IS NOT NULL AND LEN([CancellationReason]) > 0) OR ([Status] <> 3 AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrders_Status",
                schema: "purchasing",
                table: "PurchaseOrders",
                sql: "[Status] IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrderLines_Shares",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                sql: "[Ordered] > 0 AND [Technical] >= 0 AND [Accepted] >= 0 AND [Cancelled] >= 0 AND [Technical] + [Accepted] + [Cancelled] <= [Ordered]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseOrders] WHERE [Status] = 3)
                   OR EXISTS (SELECT 1 FROM [purchasing].[PurchaseOrderLines] WHERE [Cancelled] <> 0)
                BEGIN
                    THROW 51168, 'Cancelled purchase orders exist; AddPurchaseOrderCancellation is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrders_Cancellation",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrders_Status",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrderLines_Shares",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "Cancelled",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrders_Status",
                schema: "purchasing",
                table: "PurchaseOrders",
                sql: "[Status] IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrderLines_Shares",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                sql: "[Ordered] > 0 AND [Technical] >= 0 AND [Accepted] >= 0 AND [Technical] + [Accepted] <= [Ordered]");
        }
    }
}
