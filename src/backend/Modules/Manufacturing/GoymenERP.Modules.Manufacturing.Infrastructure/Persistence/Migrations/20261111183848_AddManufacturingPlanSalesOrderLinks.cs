using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingPlanSalesOrderLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SalesCustomerAccountId",
                schema: "manufacturing",
                table: "Plans",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesCustomerPartyId",
                schema: "manufacturing",
                table: "Plans",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesOrderId",
                schema: "manufacturing",
                table: "Plans",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesOrderLineId",
                schema: "manufacturing",
                table: "Plans",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SalesOrderLineRevision",
                schema: "manufacturing",
                table: "Plans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SalesOrderLinkRevision",
                schema: "manufacturing",
                table: "Plans",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SalesOrderLinkedAtUtc",
                schema: "manufacturing",
                table: "Plans",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SalesOrderLinkedBy",
                schema: "manufacturing",
                table: "Plans",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesProductId",
                schema: "manufacturing",
                table: "Plans",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Plans_SalesOrder",
                schema: "manufacturing",
                table: "Plans",
                columns: new[] { "CompanyId", "SalesOrderId" },
                filter: "[SalesOrderId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Plans_SalesOrderLink",
                schema: "manufacturing",
                table: "Plans",
                sql: "([SalesOrderId] IS NULL AND [SalesOrderLineId] IS NULL AND [SalesCustomerPartyId] IS NULL AND [SalesCustomerAccountId] IS NULL AND [SalesProductId] IS NULL AND [SalesOrderLineRevision] IS NULL AND [SalesOrderLinkedBy] IS NULL AND [SalesOrderLinkedAtUtc] IS NULL) OR ([SalesOrderId] IS NOT NULL AND [SalesOrderId] <> '00000000-0000-0000-0000-000000000000' AND [SalesOrderLineId] IS NOT NULL AND [SalesOrderLineId] <> '00000000-0000-0000-0000-000000000000' AND [SalesCustomerPartyId] IS NOT NULL AND [SalesCustomerPartyId] <> '00000000-0000-0000-0000-000000000000' AND [SalesCustomerAccountId] IS NOT NULL AND [SalesCustomerAccountId] <> '00000000-0000-0000-0000-000000000000' AND [SalesProductId] IS NOT NULL AND [SalesProductId] <> '00000000-0000-0000-0000-000000000000' AND [SalesOrderLineRevision] IS NOT NULL AND [SalesOrderLineRevision] >= 0 AND [SalesOrderLinkedBy] IS NOT NULL AND LEN([SalesOrderLinkedBy]) > 0 AND [SalesOrderLinkedAtUtc] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Plans_SalesOrderLinkRevision",
                schema: "manufacturing",
                table: "Plans",
                sql: "[SalesOrderLinkRevision] >= 0 AND ([SalesOrderLineId] IS NULL OR [SalesOrderLinkRevision] >= 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Üretim U2-b / API-16 (SAT-D219): a plan that was ever linked to a sales order line carries the link (Sales reads it
            // as the order's open Production supply) and a link revision its audit events count. Dropping either would let Sales
            // cancel an order Manufacturing still supplies, so Down refuses (THROW 51261) while such a plan exists (forward fix).
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [manufacturing].[Plans] WHERE [SalesOrderLinkRevision] > 0 OR [SalesOrderLineId] IS NOT NULL)
                    THROW 51261, 'A Manufacturing plan records a sales order link; AddManufacturingPlanSalesOrderLinks is not rolled back (forward fix).', 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Plans_SalesOrder",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Plans_SalesOrderLink",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Plans_SalesOrderLinkRevision",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "SalesCustomerAccountId",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "SalesCustomerPartyId",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "SalesOrderId",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "SalesOrderLineId",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "SalesOrderLineRevision",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "SalesOrderLinkRevision",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "SalesOrderLinkedAtUtc",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "SalesOrderLinkedBy",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "SalesProductId",
                schema: "manufacturing",
                table: "Plans");
        }
    }
}
