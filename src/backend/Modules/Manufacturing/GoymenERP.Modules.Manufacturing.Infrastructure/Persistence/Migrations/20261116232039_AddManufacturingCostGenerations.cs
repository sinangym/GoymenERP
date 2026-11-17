using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingCostGenerations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CostGenerations",
                schema: "manufacturing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GenerationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClosedOrderVersion = table.Column<long>(type: "bigint", nullable: false),
                    InventoryVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    InputFingerprint = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    CalculatorVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostGenerations", x => new { x.CompanyId, x.GenerationId, x.Revision });
                    table.CheckConstraint("CK_CostGenerations_Evidence", "[ClosedOrderVersion] >= 2 AND [Revision] >= 1 AND [State] BETWEEN 1 AND 4 AND LEN([InventoryVersion]) > 0 AND LEN([InputFingerprint]) = 64 AND ISJSON([EvidenceJson]) = 1 AND ISJSON([ResultJson]) = 1 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.ForeignKey(
                        name: "FK_CostGenerations_Orders_CompanyId_OrderId",
                        columns: x => new { x.CompanyId, x.OrderId },
                        principalSchema: "manufacturing",
                        principalTable: "Orders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CostGenerations_CompanyId_OrderId_ClosedOrderVersion_Revision",
                schema: "manufacturing",
                table: "CostGenerations",
                columns: new[] { "CompanyId", "OrderId", "ClosedOrderVersion", "Revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [manufacturing].[CostGenerations])
                BEGIN
                    THROW 51960, 'Immutable manufacturing cost generations exist; AddManufacturingCostGenerations is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "CostGenerations",
                schema: "manufacturing");
        }
    }
}
