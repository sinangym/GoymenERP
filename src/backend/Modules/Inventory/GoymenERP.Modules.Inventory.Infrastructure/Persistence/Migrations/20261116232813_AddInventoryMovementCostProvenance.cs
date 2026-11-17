using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryMovementCostProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CurrencyAverageCostLedger",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    UnreadableReason = table.Column<int>(type: "int", nullable: true),
                    QuantityKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    AverageCostPerKg = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurrencyAverageCostLedger", x => x.Id);
                    table.CheckConstraint("CK_CurrencyAverageCostLedger_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [LocationId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_CurrencyAverageCostLedger_State", "[Version] >= 1 AND (([Status] = 1 AND [UnreadableReason] IS NULL AND [QuantityKg] IS NOT NULL AND [QuantityKg] >= 0 AND [AverageCostPerKg] IS NOT NULL AND [AverageCostPerKg] >= 0) OR ([Status] = 2 AND [UnreadableReason] IS NOT NULL AND [UnreadableReason] BETWEEN 1 AND 9 AND [QuantityKg] IS NULL AND [AverageCostPerKg] IS NULL))");
                    table.ForeignKey(
                        name: "FK_CurrencyAverageCostLedger_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CurrencyAverageCostLedger_Locations_CompanyId_LocationId",
                        columns: x => new { x.CompanyId, x.LocationId },
                        principalSchema: "organization",
                        principalTable: "Locations",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ManufacturingCostGenerations",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManufacturingOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClosedOrderVersion = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    OutputFactsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturingCostGenerations", x => x.Id);
                    table.UniqueConstraint("AK_ManufacturingCostGenerations_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_ManufacturingCostGenerations_State", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [ManufacturingOrderId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Version] >= 1 AND [State] IN (1,2,3,4) AND LEN([ClosedOrderVersion]) > 0 AND ISJSON([OutputFactsJson], ARRAY) = 1 AND ISJSON([LastResultJson], OBJECT) = 1 AND [UpdatedAtUtc] >= [CreatedAtUtc] AND DATEPART(TZOFFSET,[CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET,[UpdatedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_ManufacturingCostGenerations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MovementCostStamps",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    UnitCostPerKg = table.Column<string>(type: "varchar(64)", nullable: true),
                    Amount = table.Column<string>(type: "varchar(64)", nullable: true),
                    LedgerVersion = table.Column<long>(type: "bigint", nullable: true),
                    StampVersion = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: true),
                    SourceIssueMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EntryCurrencyStatus = table.Column<int>(type: "int", nullable: false),
                    OriginalCurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ForeignUnitCostPerKg = table.Column<string>(type: "varchar(64)", nullable: true),
                    EntryExchangeRate = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: true),
                    CurrencySourceVersion = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: false),
                    CurrencyReason = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: true),
                    EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovementCostStamps", x => new { x.CompanyId, x.MovementId, x.LineNumber, x.CurrencyId });
                    table.CheckConstraint("CK_MovementCostStamps_Currency", "LEN([CurrencySourceVersion]) > 0 AND (([EntryCurrencyStatus] = 1 AND [OriginalCurrencyId] IS NULL AND [ForeignUnitCostPerKg] IS NULL AND [EntryExchangeRate] IS NULL AND [CurrencyReason] IS NULL) OR ([EntryCurrencyStatus] = 2 AND [OriginalCurrencyId] IS NOT NULL AND [OriginalCurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [ForeignUnitCostPerKg] <> '0' AND [ForeignUnitCostPerKg] IS NOT NULL AND [EntryExchangeRate] > 0 AND [EntryExchangeRate] IS NOT NULL AND [CurrencyReason] IS NULL) OR ([EntryCurrencyStatus] = 3 AND [OriginalCurrencyId] IS NOT NULL AND [OriginalCurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [EntryExchangeRate] IS NULL AND ([ForeignUnitCostPerKg] IS NULL OR [ForeignUnitCostPerKg] <> '0') AND LEN([CurrencyReason]) > 0 AND [CurrencyReason] IS NOT NULL) OR ([EntryCurrencyStatus] = 4 AND [OriginalCurrencyId] IS NULL AND [ForeignUnitCostPerKg] IS NULL AND [EntryExchangeRate] IS NULL AND LEN([CurrencyReason]) > 0 AND [CurrencyReason] IS NOT NULL))");
                    table.CheckConstraint("CK_MovementCostStamps_DecimalText", "([UnitCostPerKg] IS NULL OR (LEN([UnitCostPerKg]) BETWEEN 1 AND 50 AND [UnitCostPerKg] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9.]%' AND [UnitCostPerKg] NOT LIKE '%.%.%' AND [UnitCostPerKg] NOT LIKE '.%' AND [UnitCostPerKg] NOT LIKE '%.' AND [UnitCostPerKg] NOT LIKE '0[0-9]%' AND (CHARINDEX('.',[UnitCostPerKg]) = 0 OR [UnitCostPerKg] NOT LIKE '%0') AND LEN([UnitCostPerKg]) = DATALENGTH([UnitCostPerKg]) AND LEN(REPLACE([UnitCostPerKg],'.','')) <= 29 AND (CHARINDEX('.',[UnitCostPerKg]) = 0 OR LEN([UnitCostPerKg]) - CHARINDEX('.',[UnitCostPerKg]) <= 28) AND (LEN(REPLACE([UnitCostPerKg],'.','')) < 29 OR REPLACE([UnitCostPerKg],'.','') COLLATE Latin1_General_100_BIN2 <= '79228162514264337593543950335') AND TRY_CONVERT(decimal(38,18),[UnitCostPerKg]) IS NOT NULL)) AND ([Amount] IS NULL OR (LEN([Amount]) BETWEEN 1 AND 50 AND [Amount] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9.]%' AND [Amount] NOT LIKE '%.%.%' AND [Amount] NOT LIKE '.%' AND [Amount] NOT LIKE '%.' AND [Amount] NOT LIKE '0[0-9]%' AND (CHARINDEX('.',[Amount]) = 0 OR [Amount] NOT LIKE '%0') AND LEN([Amount]) = DATALENGTH([Amount]) AND LEN(REPLACE([Amount],'.','')) <= 29 AND (CHARINDEX('.',[Amount]) = 0 OR LEN([Amount]) - CHARINDEX('.',[Amount]) <= 28) AND (LEN(REPLACE([Amount],'.','')) < 29 OR REPLACE([Amount],'.','') COLLATE Latin1_General_100_BIN2 <= '79228162514264337593543950335') AND TRY_CONVERT(decimal(38,18),[Amount]) IS NOT NULL)) AND ([ForeignUnitCostPerKg] IS NULL OR (LEN([ForeignUnitCostPerKg]) BETWEEN 1 AND 50 AND [ForeignUnitCostPerKg] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9.]%' AND [ForeignUnitCostPerKg] NOT LIKE '%.%.%' AND [ForeignUnitCostPerKg] NOT LIKE '.%' AND [ForeignUnitCostPerKg] NOT LIKE '%.' AND [ForeignUnitCostPerKg] NOT LIKE '0[0-9]%' AND (CHARINDEX('.',[ForeignUnitCostPerKg]) = 0 OR [ForeignUnitCostPerKg] NOT LIKE '%0') AND LEN([ForeignUnitCostPerKg]) = DATALENGTH([ForeignUnitCostPerKg]) AND LEN(REPLACE([ForeignUnitCostPerKg],'.','')) <= 29 AND (CHARINDEX('.',[ForeignUnitCostPerKg]) = 0 OR LEN([ForeignUnitCostPerKg]) - CHARINDEX('.',[ForeignUnitCostPerKg]) <= 28) AND (LEN(REPLACE([ForeignUnitCostPerKg],'.','')) < 29 OR REPLACE([ForeignUnitCostPerKg],'.','') COLLATE Latin1_General_100_BIN2 <= '79228162514264337593543950335') AND TRY_CONVERT(decimal(38,18),[ForeignUnitCostPerKg]) IS NOT NULL))");
                    table.CheckConstraint("CK_MovementCostStamps_Envelope", "LEN([CorrelationId]) > 0 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0 AND ISJSON([EvidenceJson], OBJECT) = 1");
                    table.CheckConstraint("CK_MovementCostStamps_Identity", "[LineNumber] >= 1 AND [StampVersion] >= 1 AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND ([SourceIssueMovementId] IS NULL OR [SourceIssueMovementId] <> '00000000-0000-0000-0000-000000000000')");
                    table.CheckConstraint("CK_MovementCostStamps_Value", "([Status] = 1 AND [UnitCostPerKg] IS NOT NULL AND [Amount] IS NOT NULL AND [LedgerVersion] >= 1 AND [LedgerVersion] IS NOT NULL AND [Reason] IS NULL) OR ([Status] = 2 AND [UnitCostPerKg] IS NULL AND [Amount] IS NULL AND LEN([Reason]) > 0 AND [Reason] IS NOT NULL AND ([LedgerVersion] IS NULL OR [LedgerVersion] >= 1))");
                    table.ForeignKey(
                        name: "FK_MovementCostStamps_MovementLines_CompanyId_MovementId_LineNumber",
                        columns: x => new { x.CompanyId, x.MovementId, x.LineNumber },
                        principalSchema: "inventory",
                        principalTable: "MovementLines",
                        principalColumns: new[] { "CompanyId", "MovementId", "LineNumber" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovementCostStamps_Movements_CompanyId_SourceIssueMovementId",
                        columns: x => new { x.CompanyId, x.SourceIssueMovementId },
                        principalSchema: "inventory",
                        principalTable: "Movements",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PendingMovementCosts",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Reason = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingMovementCosts", x => new { x.CompanyId, x.MovementId, x.LineNumber, x.CurrencyId });
                    table.CheckConstraint("CK_PendingMovementCosts_State", "[LineNumber] >= 1 AND [QuantityKg] > 0 AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([Reason]) > 0 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_PendingMovementCosts_Labels_CompanyId_LabelId",
                        columns: x => new { x.CompanyId, x.LabelId },
                        principalSchema: "inventory",
                        principalTable: "Labels",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PendingMovementCosts_Locations_CompanyId_LocationId",
                        columns: x => new { x.CompanyId, x.LocationId },
                        principalSchema: "organization",
                        principalTable: "Locations",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PendingMovementCosts_MovementLines_CompanyId_MovementId_LineNumber",
                        columns: x => new { x.CompanyId, x.MovementId, x.LineNumber },
                        principalSchema: "inventory",
                        principalTable: "MovementLines",
                        principalColumns: new[] { "CompanyId", "MovementId", "LineNumber" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ManufacturingCostGenerationHistory",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GenerationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    InputFingerprint = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: false),
                    InputEvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturingCostGenerationHistory", x => new { x.CompanyId, x.GenerationId, x.Version });
                    table.CheckConstraint("CK_ManufacturingCostGenerationHistory_State", "[Version] >= 1 AND LEN([InputFingerprint]) > 0 AND ISJSON([InputEvidenceJson], OBJECT) = 1 AND ISJSON([ResultJson], OBJECT) = 1 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_ManufacturingCostGenerationHistory_ManufacturingCostGenerations_CompanyId_GenerationId",
                        columns: x => new { x.CompanyId, x.GenerationId },
                        principalSchema: "inventory",
                        principalTable: "ManufacturingCostGenerations",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MovementCostRecognitions",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GenerationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UnitCostPerKg = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    RemainingQuantityKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ConsumedQuantityKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    SourceVersion = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovementCostRecognitions", x => x.Id);
                    table.CheckConstraint("CK_MovementCostRecognitions_State", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [UnitCostPerKg] > 0 AND [RemainingQuantityKg] >= 0 AND [ConsumedQuantityKg] >= 0 AND LEN([SourceVersion]) > 0 AND ISJSON([ResultJson], OBJECT) = 1 AND LEN([CorrelationId]) > 0 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_MovementCostRecognitions_PendingMovementCosts_CompanyId_MovementId_LineNumber_CurrencyId",
                        columns: x => new { x.CompanyId, x.MovementId, x.LineNumber, x.CurrencyId },
                        principalSchema: "inventory",
                        principalTable: "PendingMovementCosts",
                        principalColumns: new[] { "CompanyId", "MovementId", "LineNumber", "CurrencyId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CurrencyAverageCostLedger_CompanyId_LocationId_ProductId_CurrencyId",
                schema: "inventory",
                table: "CurrencyAverageCostLedger",
                columns: new[] { "CompanyId", "LocationId", "ProductId", "CurrencyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingCostGenerations_CompanyId_ManufacturingOrderId",
                schema: "inventory",
                table: "ManufacturingCostGenerations",
                columns: new[] { "CompanyId", "ManufacturingOrderId" },
                unique: true,
                filter: "[State] <> 4");

            migrationBuilder.CreateIndex(
                name: "IX_MovementCostRecognitions_CompanyId_MovementId_LineNumber_CurrencyId",
                schema: "inventory",
                table: "MovementCostRecognitions",
                columns: new[] { "CompanyId", "MovementId", "LineNumber", "CurrencyId" },
                unique: true,
                filter: "[GenerationId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MovementCostRecognitions_CompanyId_MovementId_LineNumber_CurrencyId_GenerationId",
                schema: "inventory",
                table: "MovementCostRecognitions",
                columns: new[] { "CompanyId", "MovementId", "LineNumber", "CurrencyId", "GenerationId" },
                unique: true,
                filter: "[GenerationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MovementCostStamps_CompanyId_SourceIssueMovementId",
                schema: "inventory",
                table: "MovementCostStamps",
                columns: new[] { "CompanyId", "SourceIssueMovementId" });

            migrationBuilder.CreateIndex(
                name: "IX_PendingMovementCosts_CompanyId_LabelId",
                schema: "inventory",
                table: "PendingMovementCosts",
                columns: new[] { "CompanyId", "LabelId" });

            migrationBuilder.CreateIndex(
                name: "IX_PendingMovementCosts_CompanyId_LocationId",
                schema: "inventory",
                table: "PendingMovementCosts",
                columns: new[] { "CompanyId", "LocationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [inventory].[MovementCostStamps])
                    OR EXISTS (SELECT 1 FROM [inventory].[CurrencyAverageCostLedger])
                    OR EXISTS (SELECT 1 FROM [inventory].[PendingMovementCosts])
                    OR EXISTS (SELECT 1 FROM [inventory].[MovementCostRecognitions])
                    OR EXISTS (SELECT 1 FROM [inventory].[ManufacturingCostGenerations])
                    OR EXISTS (SELECT 1 FROM [inventory].[ManufacturingCostGenerationHistory])
                BEGIN
                    THROW 51959, 'Inventory movement cost provenance exists; AddInventoryMovementCostProvenance is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "CurrencyAverageCostLedger",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "ManufacturingCostGenerationHistory",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "MovementCostRecognitions",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "MovementCostStamps",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "ManufacturingCostGenerations",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "PendingMovementCosts",
                schema: "inventory");
        }
    }
}
