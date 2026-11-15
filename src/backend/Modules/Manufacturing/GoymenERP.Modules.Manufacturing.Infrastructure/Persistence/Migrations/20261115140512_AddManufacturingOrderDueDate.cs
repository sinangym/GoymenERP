using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingOrderDueDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DueDate",
                schema: "manufacturing",
                table: "Orders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DueDateRevision",
                schema: "manufacturing",
                table: "Orders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_DueDateRevision",
                schema: "manufacturing",
                table: "Orders",
                sql: "[DueDateRevision] >= 0 AND ([DueDateRevision] = 0 OR [DueDate] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // URT-S23 (URT-D66): an order that holds a termin carries the plan's termin copied when it opened, or a termin
            // changed on the order whose audit events count that revision. The earlier schema cannot hold either, so Down
            // refuses (THROW 51466) while such an order exists (forward fix). A store where no order holds a termin rolls
            // back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [manufacturing].[Orders] WHERE [DueDateRevision] > 0 OR [DueDate] IS NOT NULL)
                    THROW 51466, 'A Manufacturing order holds or held a termin; AddManufacturingOrderDueDate is not rolled back (forward fix).', 1;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_DueDateRevision",
                schema: "manufacturing",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DueDate",
                schema: "manufacturing",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DueDateRevision",
                schema: "manufacturing",
                table: "Orders");
        }
    }
}
