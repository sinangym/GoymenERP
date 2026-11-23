using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryFutureSupplyAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FutureSupplyLines",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AllocatableSnapshot = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ActiveAllocated = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FutureSupplyLines", x => new { x.CompanyId, x.PurchaseOrderId, x.PurchaseOrderLineId });
                    table.CheckConstraint("CK_FutureSupplyLines_Ledger", "[AllocatableSnapshot] >= 0 AND [ActiveAllocated] >= 0 AND [ActiveAllocated] <= [AllocatableSnapshot] AND [Version] >= 1 AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PurchaseOrderId] <> '00000000-0000-0000-0000-000000000000' AND [PurchaseOrderLineId] <> '00000000-0000-0000-0000-000000000000' AND [PurchaseUnitId] <> '00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_FutureSupplyLines_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FutureSupplyAllocations",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceKind = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductVersion = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    DemandKind = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    DemandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DemandLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AllocatedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    RemainingQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ReleasedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ManualSequence = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReleaseReasonCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    ReleaseOrigin = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: true),
                    ReleaseReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    ObservedPurchaseOrderVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReleasedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FutureSupplyAllocations", x => x.Id);
                    table.UniqueConstraint("AK_FutureSupplyAllocations_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_FutureSupplyAllocations_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PurchaseOrderId] <> '00000000-0000-0000-0000-000000000000' AND [PurchaseOrderLineId] <> '00000000-0000-0000-0000-000000000000' AND [PurchaseUnitId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' AND [DemandId] <> '00000000-0000-0000-0000-000000000000' AND [DemandLineId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_FutureSupplyAllocations_Kinds", "[SourceKind] = 'purchasing.order-line' AND [DemandKind] = 'sales.order-line' AND LEN([ProductVersion]) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0 AND [Version] >= 1 AND [ObservedPurchaseOrderVersion] >= 0 AND ([ManualSequence] IS NULL OR [ManualSequence] >= 1)");
                    table.CheckConstraint("CK_FutureSupplyAllocations_Quantity", "[AllocatedQuantity] > 0 AND [RemainingQuantity] >= 0 AND [ReleasedQuantity] >= 0 AND [RemainingQuantity] + [ReleasedQuantity] <= [AllocatedQuantity] AND (([Status] = 1 AND [RemainingQuantity] > 0) OR ([Status] = 2 AND [RemainingQuantity] = 0 AND [ReleasedQuantity] < [AllocatedQuantity]) OR ([Status] = 3 AND [RemainingQuantity] = 0 AND [ReleasedQuantity] = [AllocatedQuantity]))");
                    table.CheckConstraint("CK_FutureSupplyAllocations_Release", "([ReleasedQuantity] = 0 AND [ReleaseReasonCode] IS NULL AND [ReleaseOrigin] IS NULL AND [ReleaseReason] IS NULL AND [ReleasedAtUtc] IS NULL) OR ([ReleasedQuantity] > 0 AND [ReleaseReasonCode] IS NOT NULL AND LEN([ReleaseReasonCode]) > 0 AND [ReleaseOrigin] IS NOT NULL AND [ReleaseOrigin] IN ('inventory', 'purchasing', 'sales') AND [ReleaseReason] IS NOT NULL AND LEN(LTRIM(RTRIM([ReleaseReason]))) > 0 AND [ReleasedAtUtc] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_FutureSupplyAllocations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FutureSupplyAllocations_FutureSupplyLines_CompanyId_PurchaseOrderId_PurchaseOrderLineId",
                        columns: x => new { x.CompanyId, x.PurchaseOrderId, x.PurchaseOrderLineId },
                        principalSchema: "inventory",
                        principalTable: "FutureSupplyLines",
                        principalColumns: new[] { "CompanyId", "PurchaseOrderId", "PurchaseOrderLineId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FutureSupplyAllocations_CompanyId_PurchaseOrderId_PurchaseOrderLineId",
                schema: "inventory",
                table: "FutureSupplyAllocations",
                columns: new[] { "CompanyId", "PurchaseOrderId", "PurchaseOrderLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_FutureSupplyAllocations_Demand",
                schema: "inventory",
                table: "FutureSupplyAllocations",
                columns: new[] { "CompanyId", "DemandKind", "DemandId", "DemandLineId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FutureSupplyAllocations_PurchaseLine",
                schema: "inventory",
                table: "FutureSupplyAllocations",
                columns: new[] { "CompanyId", "PurchaseOrderLineId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_FutureSupplyLines_PurchaseLine",
                schema: "inventory",
                table: "FutureSupplyLines",
                columns: new[] { "CompanyId", "PurchaseOrderLineId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a stored allocation or ledger row is a reservation another module relies on; it is never dropped.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [inventory].[FutureSupplyAllocations])
                    OR EXISTS (SELECT 1 FROM [inventory].[FutureSupplyLines])
                BEGIN
                    THROW 51460, 'Future supply allocations exist; AddInventoryFutureSupplyAllocations is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "FutureSupplyAllocations",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "FutureSupplyLines",
                schema: "inventory");
        }
    }
}
