using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryGoodsReceiptReversals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Movements_Kind",
                schema: "inventory",
                table: "Movements");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_FutureSupplyConversions_CompanyId_Id",
                schema: "inventory",
                table: "FutureSupplyConversions",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.CreateTable(
                name: "GoodsReceiptReversals",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcceptanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    QuantityKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReceiptReversals", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_GoodsReceiptReversals_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [AcceptanceId] <> '00000000-0000-0000-0000-000000000000' AND [MovementId] <> [OriginalMovementId] AND [Kind] IN (1, 2)");
                    table.CheckConstraint("CK_GoodsReceiptReversals_Quantity", "[QuantityKg] > 0");
                    table.CheckConstraint("CK_GoodsReceiptReversals_Text", "LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_GoodsReceiptReversals_Labels_CompanyId_LabelId",
                        columns: x => new { x.CompanyId, x.LabelId },
                        principalSchema: "inventory",
                        principalTable: "Labels",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsReceiptReversals_Movements_CompanyId_MovementId",
                        columns: x => new { x.CompanyId, x.MovementId },
                        principalSchema: "inventory",
                        principalTable: "Movements",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsReceiptReversals_Movements_CompanyId_OriginalMovementId",
                        columns: x => new { x.CompanyId, x.OriginalMovementId },
                        principalSchema: "inventory",
                        principalTable: "Movements",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FutureSupplyConversionReversals",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReversalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConversionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UndoneQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    UndoneKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ReopenedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    OverflowReleasedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    UnheldQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FutureSupplyConversionReversals", x => new { x.CompanyId, x.ReversalId, x.ConversionId });
                    table.CheckConstraint("CK_FutureSupplyConversionReversals_Quantity", "[UndoneQuantity] > 0 AND [UndoneKg] > 0 AND [ReopenedQuantity] >= 0 AND [OverflowReleasedQuantity] >= 0 AND [UnheldQuantity] >= 0 AND [ReopenedQuantity] + [OverflowReleasedQuantity] + [UnheldQuantity] = [UndoneQuantity]");
                    table.ForeignKey(
                        name: "FK_FutureSupplyConversionReversals_FutureSupplyConversions_CompanyId_ConversionId",
                        columns: x => new { x.CompanyId, x.ConversionId },
                        principalSchema: "inventory",
                        principalTable: "FutureSupplyConversions",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FutureSupplyConversionReversals_GoodsReceiptReversals_CompanyId_ReversalId",
                        columns: x => new { x.CompanyId, x.ReversalId },
                        principalSchema: "inventory",
                        principalTable: "GoodsReceiptReversals",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Movements_Kind",
                schema: "inventory",
                table: "Movements",
                sql: "[Kind] BETWEEN 1 AND 16");

            migrationBuilder.CreateIndex(
                name: "IX_FutureSupplyConversionReversals_CompanyId_ConversionId",
                schema: "inventory",
                table: "FutureSupplyConversionReversals",
                columns: new[] { "CompanyId", "ConversionId" });

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptReversals_CompanyId_AcceptanceId",
                schema: "inventory",
                table: "GoodsReceiptReversals",
                columns: new[] { "CompanyId", "AcceptanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptReversals_CompanyId_LabelId",
                schema: "inventory",
                table: "GoodsReceiptReversals",
                columns: new[] { "CompanyId", "LabelId" });

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptReversals_CompanyId_MovementId",
                schema: "inventory",
                table: "GoodsReceiptReversals",
                columns: new[] { "CompanyId", "MovementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptReversals_CompanyId_OriginalMovementId",
                schema: "inventory",
                table: "GoodsReceiptReversals",
                columns: new[] { "CompanyId", "OriginalMovementId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [inventory].[GoodsReceiptReversals])
                   OR EXISTS (SELECT 1 FROM [inventory].[FutureSupplyConversionReversals])
                   OR EXISTS (SELECT 1 FROM [inventory].[Movements] WHERE [Kind] = 16)
                BEGIN
                    THROW 51950, 'Receipt reversal history or movements exist; AddInventoryGoodsReceiptReversals is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "FutureSupplyConversionReversals",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "GoodsReceiptReversals",
                schema: "inventory");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Movements_Kind",
                schema: "inventory",
                table: "Movements");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_FutureSupplyConversions_CompanyId_Id",
                schema: "inventory",
                table: "FutureSupplyConversions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Movements_Kind",
                schema: "inventory",
                table: "Movements",
                sql: "[Kind] BETWEEN 1 AND 15");
        }
    }
}
