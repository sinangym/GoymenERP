using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowPurchaseIncomingInvoiceFinalizedOutboxEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OutboxMessages_EventType",
                schema: "purchasing",
                table: "OutboxMessages");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutboxMessages_EventType",
                schema: "purchasing",
                table: "OutboxMessages",
                sql: "[EventType] IN ('purchasing.order-line.closed.v1', 'purchasing.order-line.revived.v1', 'purchasing.invoice.finalized.v1')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // EK24 FNS-D105: finalized invoice outbox history must remain representable.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [purchasing].[OutboxMessages]
                    WHERE [EventType] = N'purchasing.invoice.finalized.v1')
                    THROW 51815, 'Finalized purchase invoice outbox history exists; AllowPurchaseIncomingInvoiceFinalizedOutboxEvents is not rolled back (forward fix).', 1;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutboxMessages_EventType",
                schema: "purchasing",
                table: "OutboxMessages");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutboxMessages_EventType",
                schema: "purchasing",
                table: "OutboxMessages",
                sql: "[EventType] IN ('purchasing.order-line.closed.v1', 'purchasing.order-line.revived.v1')");
        }
    }
}
