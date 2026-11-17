using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingOrderReopenings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_OrderClosureDecisions",
                schema: "manufacturing",
                table: "OrderClosureDecisions");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OrderClosureDecisions",
                schema: "manufacturing",
                table: "OrderClosureDecisions",
                columns: new[] { "CompanyId", "OrderId", "OrderVersion" });

            migrationBuilder.CreateTable(
                name: "OrderReopenings",
                schema: "manufacturing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReopenedOrderVersion = table.Column<long>(type: "bigint", nullable: false),
                    ClosedOrderVersion = table.Column<long>(type: "bigint", nullable: false),
                    ClosedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReopenedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReopenedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderReopenings", x => new { x.CompanyId, x.OrderId, x.ReopenedOrderVersion });
                    table.CheckConstraint("CK_OrderReopenings_Transition", "[ClosedOrderVersion] >= 2 AND [ReopenedOrderVersion] = [ClosedOrderVersion] + 1 AND [ClosedByUserId] <> '00000000-0000-0000-0000-000000000000' AND [ReopenedByUserId] <> '00000000-0000-0000-0000-000000000000' AND LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN(LTRIM(RTRIM([CorrelationId]))) > 0");
                    table.ForeignKey(
                        name: "FK_OrderReopenings_Orders_CompanyId_OrderId",
                        columns: x => new { x.CompanyId, x.OrderId },
                        principalSchema: "manufacturing",
                        principalTable: "Orders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderReopenings_CompanyId_OrderId_ClosedOrderVersion",
                schema: "manufacturing",
                table: "OrderReopenings",
                columns: new[] { "CompanyId", "OrderId", "ClosedOrderVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [manufacturing].[OrderReopenings])
                    OR EXISTS (SELECT 1 FROM [manufacturing].[OrderClosureDecisions] GROUP BY [CompanyId], [OrderId] HAVING COUNT_BIG(*) > 1)
                BEGIN
                    THROW 51951, 'Recorded reopening or multiple closure generations exist; AddManufacturingOrderReopenings is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "OrderReopenings",
                schema: "manufacturing");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OrderClosureDecisions",
                schema: "manufacturing",
                table: "OrderClosureDecisions");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OrderClosureDecisions",
                schema: "manufacturing",
                table: "OrderClosureDecisions",
                columns: new[] { "CompanyId", "OrderId" });
        }
    }
}
