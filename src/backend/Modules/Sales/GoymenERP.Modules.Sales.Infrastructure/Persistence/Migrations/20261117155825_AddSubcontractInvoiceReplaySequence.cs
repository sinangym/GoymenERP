using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubcontractInvoiceReplaySequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LifecycleSequence",
                schema: "sales",
                table: "SubcontractInvoiceCommandAttempts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubcontractInvoiceCommandAttempts_Sequence",
                schema: "sales",
                table: "SubcontractInvoiceCommandAttempts",
                sql: "[LifecycleSequence] IS NULL OR ([LifecycleSequence] >= 1 AND [RefusalStatus] IS NULL AND [InvoiceId] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[SubcontractInvoiceCommandAttempts] WHERE [LifecycleSequence] IS NOT NULL)
                BEGIN
                    THROW 51937, 'Exact subcontract invoice command response history exists; AddSubcontractInvoiceReplaySequence is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_SubcontractInvoiceCommandAttempts_Sequence",
                schema: "sales",
                table: "SubcontractInvoiceCommandAttempts");

            migrationBuilder.DropColumn(
                name: "LifecycleSequence",
                schema: "sales",
                table: "SubcontractInvoiceCommandAttempts");
        }
    }
}
