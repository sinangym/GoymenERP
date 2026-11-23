using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseAdditionalCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdditionalCostAttempts",
                schema: "purchasing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RefusalJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdditionalCostAttempts", x => new { x.CompanyId, x.KeyHash });
                    table.CheckConstraint("CK_AdditionalCostAttempts_Result", "([SnapshotJson] IS NOT NULL AND ISJSON([SnapshotJson])=1 AND [RefusalJson] IS NULL) OR ([SnapshotJson] IS NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson])=1)");
                    table.ForeignKey(
                        name: "FK_AdditionalCostAttempts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdditionalCosts",
                schema: "purchasing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceKind = table.Column<int>(type: "int", nullable: false),
                    NormalOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NormalLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NormalAcceptanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ServiceOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ServiceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ServiceAcceptanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdditionalCosts", x => new { x.CompanyId, x.CostId });
                    table.CheckConstraint("CK_AdditionalCosts_Json", "ISJSON([SnapshotJson])=1");
                    table.CheckConstraint("CK_AdditionalCosts_Source", "([SourceKind]=1 AND [NormalOrderId] IS NOT NULL AND [NormalOrderId]=[OrderId] AND [ServiceOrderId] IS NULL AND [ServiceLineId] IS NULL AND [ServiceAcceptanceId] IS NULL AND (([NormalLineId] IS NULL AND [NormalAcceptanceId] IS NULL) OR ([NormalLineId] IS NOT NULL AND [NormalAcceptanceId] IS NOT NULL))) OR ([SourceKind]=2 AND [ServiceOrderId] IS NOT NULL AND [ServiceOrderId]=[OrderId] AND [NormalOrderId] IS NULL AND [NormalLineId] IS NULL AND [NormalAcceptanceId] IS NULL AND (([ServiceLineId] IS NULL AND [ServiceAcceptanceId] IS NULL) OR ([ServiceLineId] IS NOT NULL AND [ServiceAcceptanceId] IS NOT NULL)))");
                    table.ForeignKey(
                        name: "FK_AdditionalCosts_PurchaseAcceptanceShares_CompanyId_NormalOrderId_NormalLineId_NormalAcceptanceId",
                        columns: x => new { x.CompanyId, x.NormalOrderId, x.NormalLineId, x.NormalAcceptanceId },
                        principalSchema: "purchasing",
                        principalTable: "PurchaseAcceptanceShares",
                        principalColumns: new[] { "CompanyId", "OrderId", "LineId", "AcceptanceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdditionalCosts_PurchaseOrders_CompanyId_NormalOrderId",
                        columns: x => new { x.CompanyId, x.NormalOrderId },
                        principalSchema: "purchasing",
                        principalTable: "PurchaseOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdditionalCosts_ServiceAcceptances_CompanyId_ServiceOrderId_ServiceLineId_ServiceAcceptanceId",
                        columns: x => new { x.CompanyId, x.ServiceOrderId, x.ServiceLineId, x.ServiceAcceptanceId },
                        principalSchema: "purchasing",
                        principalTable: "ServiceAcceptances",
                        principalColumns: new[] { "CompanyId", "OrderId", "LineId", "AcceptanceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdditionalCosts_ServiceOrders_CompanyId_ServiceOrderId",
                        columns: x => new { x.CompanyId, x.ServiceOrderId },
                        principalSchema: "purchasing",
                        principalTable: "ServiceOrders",
                        principalColumns: new[] { "CompanyId", "OrderId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdditionalCosts_CompanyId_NormalOrderId_NormalLineId_NormalAcceptanceId",
                schema: "purchasing",
                table: "AdditionalCosts",
                columns: new[] { "CompanyId", "NormalOrderId", "NormalLineId", "NormalAcceptanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_AdditionalCosts_CompanyId_OrderId_RecordedAtUtc_CostId",
                schema: "purchasing",
                table: "AdditionalCosts",
                columns: new[] { "CompanyId", "OrderId", "RecordedAtUtc", "CostId" });

            migrationBuilder.CreateIndex(
                name: "IX_AdditionalCosts_CompanyId_ServiceOrderId_ServiceLineId_ServiceAcceptanceId",
                schema: "purchasing",
                table: "AdditionalCosts",
                columns: new[] { "CompanyId", "ServiceOrderId", "ServiceLineId", "ServiceAcceptanceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [purchasing].[AdditionalCosts])
                    OR EXISTS (SELECT 1 FROM [purchasing].[AdditionalCostAttempts])
                BEGIN
                    THROW 51998, 'Additional purchase expense history exists; AddPurchaseAdditionalCosts is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "AdditionalCostAttempts",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "AdditionalCosts",
                schema: "purchasing");
        }
    }
}
