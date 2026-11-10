using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// EK56 SAT-D286: a pending single-shipment risk exception can be withdrawn by its requester (State 4, no decision facts).
    /// Pure DDL in Up: the state, decision and attempt-command checks are widened. TR_ShipmentRiskExceptions_Guard (51252) is
    /// unchanged: it already lets only a Requested row move once, whatever the target state.
    /// </summary>
    public partial class AddShipmentRiskExceptionWithdrawal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ShipmentRiskExceptions_Decision",
                schema: "sales",
                table: "ShipmentRiskExceptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ShipmentRiskExceptions_State",
                schema: "sales",
                table: "ShipmentRiskExceptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ShipmentRiskExceptionAttempts_Command",
                schema: "sales",
                table: "ShipmentRiskExceptionAttempts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ShipmentRiskExceptions_Decision",
                schema: "sales",
                table: "ShipmentRiskExceptions",
                sql: "([State] = 1 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL AND [ValidOn] IS NULL AND [Version] >= 1) OR ([State] = 2 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [RejectionReason] IS NULL AND [ValidOn] IS NOT NULL AND [Version] >= 2) OR ([State] = 3 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [RejectionReason] IS NOT NULL AND LEN([RejectionReason]) > 0 AND [ValidOn] IS NULL AND [Version] >= 2) OR ([State] = 4 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL AND [ValidOn] IS NULL AND [Version] >= 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ShipmentRiskExceptions_State",
                schema: "sales",
                table: "ShipmentRiskExceptions",
                sql: "[State] IN (1, 2, 3, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ShipmentRiskExceptionAttempts_Command",
                schema: "sales",
                table: "ShipmentRiskExceptionAttempts",
                sql: "[CommandName] IN (N'sales.order.risk-exception.request.v1', N'sales.order.risk-exception.approve.v1', N'sales.order.risk-exception.reject.v1', N'sales.order.risk-exception.withdraw.v1')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a withdrawn exception (or a recorded withdrawal attempt) is approval evidence that the previous checks
            // cannot hold. The guard refuses (51254) before any check is narrowed; an empty or pre-withdrawal store rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[ShipmentRiskExceptions] WHERE [State] = 4)
                   OR EXISTS (SELECT 1 FROM [sales].[ShipmentRiskExceptionAttempts] WHERE [CommandName] = N'sales.order.risk-exception.withdraw.v1')
                BEGIN
                    THROW 51254, 'Sales shipment risk exceptions were withdrawn; AddShipmentRiskExceptionWithdrawal is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_ShipmentRiskExceptions_Decision",
                schema: "sales",
                table: "ShipmentRiskExceptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ShipmentRiskExceptions_State",
                schema: "sales",
                table: "ShipmentRiskExceptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ShipmentRiskExceptionAttempts_Command",
                schema: "sales",
                table: "ShipmentRiskExceptionAttempts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ShipmentRiskExceptions_Decision",
                schema: "sales",
                table: "ShipmentRiskExceptions",
                sql: "([State] = 1 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL AND [ValidOn] IS NULL AND [Version] >= 1) OR ([State] = 2 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [RejectionReason] IS NULL AND [ValidOn] IS NOT NULL AND [Version] >= 2) OR ([State] = 3 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [RejectionReason] IS NOT NULL AND LEN([RejectionReason]) > 0 AND [ValidOn] IS NULL AND [Version] >= 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ShipmentRiskExceptions_State",
                schema: "sales",
                table: "ShipmentRiskExceptions",
                sql: "[State] IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ShipmentRiskExceptionAttempts_Command",
                schema: "sales",
                table: "ShipmentRiskExceptionAttempts",
                sql: "[CommandName] IN (N'sales.order.risk-exception.request.v1', N'sales.order.risk-exception.approve.v1', N'sales.order.risk-exception.reject.v1')");
        }
    }
}
