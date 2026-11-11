using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A2a-5a (ALS-D3, Fable F-015): the purchase order records who approved it and when — ApprovedBy (nvarchar(128), nullable)
    /// and ApprovedAtUtc (datetimeoffset, nullable) — checked by <c>CK_PurchaseOrders_Approval</c>: both or neither, only on an
    /// Approved (2) or Cancelled (3) order, and the approver never equals CreatedBy (binary collation, the domain's ordinal
    /// hazırlayan ≠ onaylayan rule). Every existing row stays as it was (null): an order approved before A2a-5a keeps no
    /// approver; none is invented and no backfill runs.
    /// <para>
    /// Down refuses (<c>THROW 51172</c>, used by no other migration) while any order carries an approver: dropping the columns
    /// would erase the recorded maker/checker evidence (forward fix). Otherwise it drops the check and both columns.
    /// </para>
    /// </summary>
    public partial class AddPurchaseOrderApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAtUtc",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrders_Approval",
                schema: "purchasing",
                table: "PurchaseOrders",
                sql: "([ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL) OR ([ApprovedBy] IS NOT NULL AND LEN([ApprovedBy]) > 0 AND [ApprovedAtUtc] IS NOT NULL AND [Status] IN (2, 3) AND [ApprovedBy] COLLATE Latin1_General_100_BIN2 <> [CreatedBy] COLLATE Latin1_General_100_BIN2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseOrders] WHERE [ApprovedBy] IS NOT NULL)
                BEGIN
                    THROW 51172, 'Purchase orders with a recorded approver exist; AddPurchaseOrderApproval is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrders_Approval",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                schema: "purchasing",
                table: "PurchaseOrders");
        }
    }
}
