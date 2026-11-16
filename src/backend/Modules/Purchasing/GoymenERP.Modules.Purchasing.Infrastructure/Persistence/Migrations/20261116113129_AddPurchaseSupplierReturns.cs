using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseSupplierReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurchaseSupplierReturns",
                schema: "purchasing",
                columns: table => new
                {
                    ReturnId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReturnLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GoodsAcceptanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    SourceBlock = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    QuantityKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    CostBasis = table.Column<int>(type: "int", nullable: false),
                    UnitCostTryPerKg = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ReplacementExpected = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseSupplierReturns", x => x.ReturnId);
                    table.UniqueConstraint("AK_PurchaseSupplierReturns_CompanyId_ReturnId", x => new { x.CompanyId, x.ReturnId });
                    table.CheckConstraint("CK_PurchaseSupplierReturns_Decision", "([CostBasis] = 1 AND [Status] = 2 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL) OR ([CostBasis] = 2 AND [Status] = 1 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL) OR ([CostBasis] = 2 AND [Status] = 2 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [DecisionReason] IS NULL) OR ([CostBasis] = 2 AND [Status] = 3 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [DecisionReason] IS NOT NULL AND LEN([DecisionReason]) > 0)");
                    table.CheckConstraint("CK_PurchaseSupplierReturns_Identity", "[ReturnId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ReturnLineId] <> '00000000-0000-0000-0000-000000000000' AND [OrderId] <> '00000000-0000-0000-0000-000000000000' AND [LineId] <> '00000000-0000-0000-0000-000000000000' AND [GoodsAcceptanceId] <> '00000000-0000-0000-0000-000000000000' AND [MovementId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_PurchaseSupplierReturns_Status", "[Status] IN (1, 2, 3) AND [CostBasis] IN (1, 2)");
                    table.CheckConstraint("CK_PurchaseSupplierReturns_Text", "LEN([Reason]) > 0 AND LEN([RequestedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_PurchaseSupplierReturns_Values", "[QuantityKg] > 0 AND [UnitCostTryPerKg] > 0 AND [SourceBlock] IN (N'Free', N'Blocked') AND [ReplacementExpected] = 0");
                    table.CheckConstraint("CK_PurchaseSupplierReturns_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_PurchaseSupplierReturns_PurchaseOrderLines_CompanyId_OrderId_LineId",
                        columns: x => new { x.CompanyId, x.OrderId, x.LineId },
                        principalSchema: "purchasing",
                        principalTable: "PurchaseOrderLines",
                        principalColumns: new[] { "CompanyId", "OrderId", "LineId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseSupplierReturns_PurchaseOrders_CompanyId_OrderId",
                        columns: x => new { x.CompanyId, x.OrderId },
                        principalSchema: "purchasing",
                        principalTable: "PurchaseOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseSupplierReturnAttempts",
                schema: "purchasing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    ReturnId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefusalJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseSupplierReturnAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_PurchaseSupplierReturnAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                    table.CheckConstraint("CK_PurchaseSupplierReturnAttempts_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
                    table.CheckConstraint("CK_PurchaseSupplierReturnAttempts_Outcome", "([Outcome] = 1 AND [RefusalJson] IS NULL AND [ReturnId] IS NOT NULL AND [ReturnId] <> '00000000-0000-0000-0000-000000000000') OR ([Outcome] = 2 AND [ReturnId] IS NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)");
                    table.ForeignKey(
                        name: "FK_PurchaseSupplierReturnAttempts_PurchaseSupplierReturns_CompanyId_ReturnId",
                        columns: x => new { x.CompanyId, x.ReturnId },
                        principalSchema: "purchasing",
                        principalTable: "PurchaseSupplierReturns",
                        principalColumns: new[] { "CompanyId", "ReturnId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseSupplierReturnAttempts_ReturnId",
                schema: "purchasing",
                table: "PurchaseSupplierReturnAttempts",
                columns: new[] { "CompanyId", "ReturnId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseSupplierReturns_CompanyId_OrderId_LineId",
                schema: "purchasing",
                table: "PurchaseSupplierReturns",
                columns: new[] { "CompanyId", "OrderId", "LineId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseSupplierReturns_GoodsAcceptanceId",
                schema: "purchasing",
                table: "PurchaseSupplierReturns",
                columns: new[] { "CompanyId", "GoodsAcceptanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseSupplierReturns_Order",
                schema: "purchasing",
                table: "PurchaseSupplierReturns",
                columns: new[] { "CompanyId", "OrderId", "RequestedAtUtc", "ReturnId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseSupplierReturns_Status",
                schema: "purchasing",
                table: "PurchaseSupplierReturns",
                columns: new[] { "CompanyId", "Status", "RequestedAtUtc", "ReturnId" });

            migrationBuilder.CreateIndex(
                name: "UX_PurchaseSupplierReturns_MovementId",
                schema: "purchasing",
                table: "PurchaseSupplierReturns",
                columns: new[] { "CompanyId", "MovementId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseSupplierReturns])
                    OR EXISTS (SELECT 1 FROM [purchasing].[PurchaseSupplierReturnAttempts])
                BEGIN
                    THROW 51893, 'Purchase supplier returns exist; AddPurchaseSupplierReturns is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "PurchaseSupplierReturnAttempts",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "PurchaseSupplierReturns",
                schema: "purchasing");
        }
    }
}
