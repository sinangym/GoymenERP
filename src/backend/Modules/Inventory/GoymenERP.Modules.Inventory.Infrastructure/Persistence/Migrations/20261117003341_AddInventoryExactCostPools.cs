using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryExactCostPools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MovementCostRecognitions_State",
                schema: "inventory",
                table: "MovementCostRecognitions");

            migrationBuilder.AlterColumn<decimal>(
                name: "RemainingQuantityKg",
                schema: "inventory",
                table: "MovementCostRecognitions",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,3)",
                oldPrecision: 18,
                oldScale: 3);

            migrationBuilder.AlterColumn<decimal>(
                name: "ConsumedQuantityKg",
                schema: "inventory",
                table: "MovementCostRecognitions",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,3)",
                oldPrecision: 18,
                oldScale: 3);

            migrationBuilder.AddColumn<string>(
                name: "ConsumedQuantityRatioJson",
                schema: "inventory",
                table: "MovementCostRecognitions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemainingQuantityRatioJson",
                schema: "inventory",
                table: "MovementCostRecognitions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExactCostPools",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    UnreadableReason = table.Column<int>(type: "int", nullable: true),
                    QuantityRatioJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AverageCostPerKg = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExactCostPools", x => new { x.CompanyId, x.LocationId, x.ProductId, x.CurrencyId });
                    table.CheckConstraint("CK_ExactCostPools_State", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [LocationId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [Version] >= 1 AND DATEPART(TZOFFSET,[UpdatedAtUtc]) = 0 AND (([Status] = 1 AND [QuantityRatioJson] IS NOT NULL AND ISJSON([QuantityRatioJson], OBJECT) = 1 AND [AverageCostPerKg] >= 0 AND [AverageCostPerKg] IS NOT NULL AND [UnreadableReason] IS NULL) OR ([Status] = 2 AND [QuantityRatioJson] IS NULL AND [AverageCostPerKg] IS NULL AND [UnreadableReason] BETWEEN 1 AND 9 AND [UnreadableReason] IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_ExactCostPools_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExactCostPools_Locations_CompanyId_LocationId",
                        columns: x => new { x.CompanyId, x.LocationId },
                        principalSchema: "organization",
                        principalTable: "Locations",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_MovementCostRecognitions_State",
                schema: "inventory",
                table: "MovementCostRecognitions",
                sql: "[Id] <> '00000000-0000-0000-0000-000000000000' AND [UnitCostPerKg] > 0 AND (([RemainingQuantityKg] >= 0 AND [RemainingQuantityKg] IS NOT NULL) OR ([RemainingQuantityRatioJson] IS NOT NULL AND ISJSON([RemainingQuantityRatioJson], OBJECT) = 1)) AND (([ConsumedQuantityKg] >= 0 AND [ConsumedQuantityKg] IS NOT NULL) OR ([ConsumedQuantityRatioJson] IS NOT NULL AND ISJSON([ConsumedQuantityRatioJson], OBJECT) = 1)) AND ([RemainingQuantityRatioJson] IS NULL OR ISJSON([RemainingQuantityRatioJson], OBJECT) = 1) AND ([ConsumedQuantityRatioJson] IS NULL OR ISJSON([ConsumedQuantityRatioJson], OBJECT) = 1) AND LEN([SourceVersion]) > 0 AND ISJSON([ResultJson], OBJECT) = 1 AND LEN([CorrelationId]) > 0 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [inventory].[ExactCostPools])
                   OR EXISTS (SELECT 1 FROM [inventory].[MovementCostRecognitions]
                              WHERE [RemainingQuantityRatioJson] IS NOT NULL OR [ConsumedQuantityRatioJson] IS NOT NULL
                                 OR [RemainingQuantityKg] IS NULL OR [ConsumedQuantityKg] IS NULL)
                BEGIN
                    THROW 51968, 'Exact cost pool or recognition mass provenance exists; AddInventoryExactCostPools is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "ExactCostPools",
                schema: "inventory");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MovementCostRecognitions_State",
                schema: "inventory",
                table: "MovementCostRecognitions");

            migrationBuilder.DropColumn(
                name: "ConsumedQuantityRatioJson",
                schema: "inventory",
                table: "MovementCostRecognitions");

            migrationBuilder.DropColumn(
                name: "RemainingQuantityRatioJson",
                schema: "inventory",
                table: "MovementCostRecognitions");

            migrationBuilder.AlterColumn<decimal>(
                name: "RemainingQuantityKg",
                schema: "inventory",
                table: "MovementCostRecognitions",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,3)",
                oldPrecision: 18,
                oldScale: 3,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "ConsumedQuantityKg",
                schema: "inventory",
                table: "MovementCostRecognitions",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,3)",
                oldPrecision: 18,
                oldScale: 3,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_MovementCostRecognitions_State",
                schema: "inventory",
                table: "MovementCostRecognitions",
                sql: "[Id] <> '00000000-0000-0000-0000-000000000000' AND [UnitCostPerKg] > 0 AND [RemainingQuantityKg] >= 0 AND [ConsumedQuantityKg] >= 0 AND LEN([SourceVersion]) > 0 AND ISJSON([ResultJson], OBJECT) = 1 AND LEN([CorrelationId]) > 0 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
        }
    }
}
