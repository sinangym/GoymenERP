using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPostedManufacturingCostAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryCostProposals_State",
                schema: "accounting",
                table: "InventoryCostProposals");

            migrationBuilder.CreateTable(
                name: "InvoiceCogsEconomicClaims",
                schema: "accounting",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementLineNumber = table.Column<int>(type: "int", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinalizedVersion = table.Column<long>(type: "bigint", nullable: false),
                    Origin = table.Column<int>(type: "int", nullable: false),
                    SourceHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ViewJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceCogsEconomicClaims", x => new { x.CompanyId, x.MovementId, x.MovementLineNumber });
                    table.CheckConstraint("CK_InvoiceCogsEconomicClaims_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [MovementId] <> '00000000-0000-0000-0000-000000000000' AND [MovementLineNumber] > 0 AND [ShipmentId] <> '00000000-0000-0000-0000-000000000000' AND [SalesEventId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceLineId] <> '00000000-0000-0000-0000-000000000000' AND [AdmissionId] <> '00000000-0000-0000-0000-000000000000' AND [FinalizedVersion] > 0 AND [Origin] IN (1,2)");
                    table.CheckConstraint("CK_InvoiceCogsEconomicClaims_Proof", "LEN([SourceHash]) = 64 AND ISJSON([ViewJson]) = 1 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_InvoiceCogsEconomicClaims_InventoryCostSources_CompanyId_SalesEventId",
                        columns: x => new { x.CompanyId, x.SalesEventId },
                        principalSchema: "accounting",
                        principalTable: "InventoryCostSources",
                        principalColumns: new[] { "CompanyId", "EventId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ManufacturingCostAttributions",
                schema: "accounting",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OutputId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GenerationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GenerationVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ReceiptMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MoneyProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MoneyProfileVersion = table.Column<long>(type: "bigint", nullable: false),
                    MoneyScale = table.Column<int>(type: "int", nullable: false),
                    SourceHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    LineageHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ViewJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturingCostAttributions", x => new { x.CompanyId, x.SourceEventId, x.OutputId });
                    table.CheckConstraint("CK_ManufacturingCostAttributions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SourceEventId] <> '00000000-0000-0000-0000-000000000000' AND [OutputId] <> '00000000-0000-0000-0000-000000000000' AND [GenerationId] <> '00000000-0000-0000-0000-000000000000' AND [ReceiptMovementId] <> '00000000-0000-0000-0000-000000000000' AND [JournalId] <> '00000000-0000-0000-0000-000000000000' AND LEN([GenerationVersion]) > 0");
                    table.CheckConstraint("CK_ManufacturingCostAttributions_Profile", "[CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [MoneyProfileId] <> '00000000-0000-0000-0000-000000000000' AND [MoneyProfileVersion] > 0 AND [MoneyScale] BETWEEN 0 AND 28");
                    table.CheckConstraint("CK_ManufacturingCostAttributions_Proof", "LEN([SourceHash]) = 64 AND LEN([LineageHash]) = 64 AND ISJSON([ViewJson]) = 1 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_ManufacturingCostAttributions_Journals_CompanyId_JournalId",
                        columns: x => new { x.CompanyId, x.JournalId },
                        principalSchema: "accounting",
                        principalTable: "Journals",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManufacturingCostAttributions_ManufacturingCostDifferences_CompanyId_SourceEventId",
                        columns: x => new { x.CompanyId, x.SourceEventId },
                        principalSchema: "accounting",
                        principalTable: "ManufacturingCostDifferences",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ManufacturingCostAttributionShares",
                schema: "accounting",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OutputId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementLineNumber = table.Column<int>(type: "int", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ViewJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturingCostAttributionShares", x => new { x.CompanyId, x.SourceEventId, x.OutputId, x.MovementId, x.MovementLineNumber });
                    table.CheckConstraint("CK_ManufacturingCostAttributionShares_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SourceEventId] <> '00000000-0000-0000-0000-000000000000' AND [OutputId] <> '00000000-0000-0000-0000-000000000000' AND [MovementId] <> '00000000-0000-0000-0000-000000000000' AND [MovementLineNumber] > 0 AND [ShipmentId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ManufacturingCostAttributionShares_Proof", "LEN([SourceHash]) = 64 AND ISJSON([ViewJson]) = 1 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_ManufacturingCostAttributionShares_ManufacturingCostAttributions_CompanyId_SourceEventId_OutputId",
                        columns: x => new { x.CompanyId, x.SourceEventId, x.OutputId },
                        principalSchema: "accounting",
                        principalTable: "ManufacturingCostAttributions",
                        principalColumns: new[] { "CompanyId", "SourceEventId", "OutputId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingCostDifferences_CompanyId_ManufacturingOrderId_Status",
                schema: "accounting",
                table: "ManufacturingCostDifferences",
                columns: new[] { "CompanyId", "ManufacturingOrderId", "Status" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryCostProposals_State",
                schema: "accounting",
                table: "InventoryCostProposals",
                sql: "([Status] IN (1,4,5) AND [Version] = 1 AND [PreparedBy] IS NULL AND [ApprovedBy] IS NULL AND [PeriodId] IS NULL AND [JournalId] IS NULL) OR ([Status] = 2 AND [Version] > 1 AND [PreparedBy] IS NOT NULL AND [PeriodId] IS NOT NULL AND [ApprovedBy] IS NULL AND [JournalId] IS NULL) OR ([Status] = 3 AND [Version] > 2 AND [PreparedBy] IS NOT NULL AND [PeriodId] IS NOT NULL AND [ApprovedBy] IS NOT NULL AND [JournalId] IS NOT NULL AND [ApprovedBy] <> [PreparedBy])");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceCogsEconomicClaims_CompanyId_InvoiceId_InvoiceLineId_FinalizedVersion",
                schema: "accounting",
                table: "InvoiceCogsEconomicClaims",
                columns: new[] { "CompanyId", "InvoiceId", "InvoiceLineId", "FinalizedVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceCogsEconomicClaims_CompanyId_SalesEventId",
                schema: "accounting",
                table: "InvoiceCogsEconomicClaims",
                columns: new[] { "CompanyId", "SalesEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceCogsEconomicClaims_CompanyId_ShipmentId",
                schema: "accounting",
                table: "InvoiceCogsEconomicClaims",
                columns: new[] { "CompanyId", "ShipmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingCostAttributions_CompanyId_GenerationId_GenerationVersion_OutputId",
                schema: "accounting",
                table: "ManufacturingCostAttributions",
                columns: new[] { "CompanyId", "GenerationId", "GenerationVersion", "OutputId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingCostAttributions_CompanyId_JournalId",
                schema: "accounting",
                table: "ManufacturingCostAttributions",
                columns: new[] { "CompanyId", "JournalId" });

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingCostAttributionShares_CompanyId_MovementId_MovementLineNumber",
                schema: "accounting",
                table: "ManufacturingCostAttributionShares",
                columns: new[] { "CompanyId", "MovementId", "MovementLineNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingCostAttributionShares_CompanyId_ShipmentId",
                schema: "accounting",
                table: "ManufacturingCostAttributionShares",
                columns: new[] { "CompanyId", "ShipmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingCostAttributionShares_CompanyId_SourceEventId_OutputId_ShipmentId",
                schema: "accounting",
                table: "ManufacturingCostAttributionShares",
                columns: new[] { "CompanyId", "SourceEventId", "OutputId", "ShipmentId" },
                unique: true);

            // Fixed migration-owned identifiers; immutable economic evidence is never updated or deleted.
            foreach (var table in new[] { "ManufacturingCostAttributions", "ManufacturingCostAttributionShares", "InvoiceCogsEconomicClaims" })
            {
                migrationBuilder.Sql($"""
                    CREATE TRIGGER [accounting].[TR_{table}_Immutable]
                    ON [accounting].[{table}] AFTER UPDATE, DELETE AS
                    BEGIN
                        SET NOCOUNT ON;
                        IF EXISTS (SELECT 1 FROM deleted) AND EXISTS (SELECT 1 FROM inserted)
                            THROW 51801, 'Posted manufacturing cost attribution is immutable; append reviewed evidence.', 1;
                        IF EXISTS (SELECT 1 FROM deleted)
                            THROW 51802, 'Posted manufacturing cost attribution cannot be deleted; use forward correction.', 1;
                    END;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [accounting].[ManufacturingCostAttributions])
                   OR EXISTS (SELECT 1 FROM [accounting].[ManufacturingCostAttributionShares])
                   OR EXISTS (SELECT 1 FROM [accounting].[InvoiceCogsEconomicClaims])
                   OR EXISTS (SELECT 1 FROM [accounting].[InventoryCostProposals] WHERE [Status] = 5)
                BEGIN
                    THROW 51800, 'Posted manufacturing attribution or economic claims exist; AddPostedManufacturingCostAttribution is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "InvoiceCogsEconomicClaims",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "ManufacturingCostAttributionShares",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "ManufacturingCostAttributions",
                schema: "accounting");

            migrationBuilder.DropIndex(
                name: "IX_ManufacturingCostDifferences_CompanyId_ManufacturingOrderId_Status",
                schema: "accounting",
                table: "ManufacturingCostDifferences");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryCostProposals_State",
                schema: "accounting",
                table: "InventoryCostProposals");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryCostProposals_State",
                schema: "accounting",
                table: "InventoryCostProposals",
                sql: "([Status] IN (1,4) AND [Version] = 1 AND [PreparedBy] IS NULL AND [ApprovedBy] IS NULL AND [PeriodId] IS NULL AND [JournalId] IS NULL) OR ([Status] = 2 AND [Version] > 1 AND [PreparedBy] IS NOT NULL AND [PeriodId] IS NOT NULL AND [ApprovedBy] IS NULL AND [JournalId] IS NULL) OR ([Status] = 3 AND [Version] > 2 AND [PreparedBy] IS NOT NULL AND [PeriodId] IS NOT NULL AND [ApprovedBy] IS NOT NULL AND [JournalId] IS NOT NULL AND [ApprovedBy] <> [PreparedBy])");
        }
    }
}
