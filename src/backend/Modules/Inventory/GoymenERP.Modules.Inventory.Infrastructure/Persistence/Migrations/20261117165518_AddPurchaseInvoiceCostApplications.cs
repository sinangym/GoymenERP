using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseInvoiceCostApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurchaseInvoiceCostApplications",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceVersion = table.Column<long>(type: "bigint", nullable: false),
                    AdmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    SourceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoiceCostApplications", x => new { x.CompanyId, x.SourceEventId });
                    table.CheckConstraint("CK_PurchaseInvoiceCostApplications_Envelope", "DATEPART(TZOFFSET,[OccurredAtUtc]) = 0 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_PurchaseInvoiceCostApplications_Source", "[SourceEventId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [AdmissionId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceVersion] >= 1 AND LEN([SourceSha256]) = 64 AND ISJSON([SourceJson], OBJECT) = 1 AND ISJSON([ResultJson], OBJECT) = 1");
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceCostApplications_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseInvoiceReceiptCostEffects",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcceptanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementLineNumber = table.Column<int>(type: "int", nullable: false),
                    MatchedQuantity = table.Column<string>(type: "varchar(64)", nullable: false),
                    AcceptedQuantity = table.Column<string>(type: "varchar(64)", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoiceReceiptCostEffects", x => new { x.CompanyId, x.SourceEventId, x.AcceptanceId });
                    table.CheckConstraint("CK_PurchaseInvoiceReceiptCostEffects_Quantity", "([MatchedQuantity] IS NULL OR (LEN([MatchedQuantity]) BETWEEN 1 AND 50 AND [MatchedQuantity] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9.]%' AND [MatchedQuantity] NOT LIKE '%.%.%' AND [MatchedQuantity] NOT LIKE '.%' AND [MatchedQuantity] NOT LIKE '%.' AND [MatchedQuantity] NOT LIKE '0[0-9]%' AND (CHARINDEX('.',[MatchedQuantity]) = 0 OR [MatchedQuantity] NOT LIKE '%0') AND LEN([MatchedQuantity]) = DATALENGTH([MatchedQuantity]) AND LEN(REPLACE([MatchedQuantity],'.','')) <= 29 AND (CHARINDEX('.',[MatchedQuantity]) = 0 OR LEN([MatchedQuantity]) - CHARINDEX('.',[MatchedQuantity]) <= 28) AND (LEN(REPLACE([MatchedQuantity],'.','')) < 29 OR REPLACE([MatchedQuantity],'.','') COLLATE Latin1_General_100_BIN2 <= '79228162514264337593543950335') AND TRY_CONVERT(decimal(38,18),[MatchedQuantity]) IS NOT NULL)) AND ([AcceptedQuantity] IS NULL OR (LEN([AcceptedQuantity]) BETWEEN 1 AND 50 AND [AcceptedQuantity] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9.]%' AND [AcceptedQuantity] NOT LIKE '%.%.%' AND [AcceptedQuantity] NOT LIKE '.%' AND [AcceptedQuantity] NOT LIKE '%.' AND [AcceptedQuantity] NOT LIKE '0[0-9]%' AND (CHARINDEX('.',[AcceptedQuantity]) = 0 OR [AcceptedQuantity] NOT LIKE '%0') AND LEN([AcceptedQuantity]) = DATALENGTH([AcceptedQuantity]) AND LEN(REPLACE([AcceptedQuantity],'.','')) <= 29 AND (CHARINDEX('.',[AcceptedQuantity]) = 0 OR LEN([AcceptedQuantity]) - CHARINDEX('.',[AcceptedQuantity]) <= 28) AND (LEN(REPLACE([AcceptedQuantity],'.','')) < 29 OR REPLACE([AcceptedQuantity],'.','') COLLATE Latin1_General_100_BIN2 <= '79228162514264337593543950335') AND TRY_CONVERT(decimal(38,18),[AcceptedQuantity]) IS NOT NULL)) AND TRY_CONVERT(decimal(38,18),[MatchedQuantity]) > 0 AND TRY_CONVERT(decimal(38,18),[AcceptedQuantity]) >= TRY_CONVERT(decimal(38,18),[MatchedQuantity])");
                    table.CheckConstraint("CK_PurchaseInvoiceReceiptCostEffects_Source", "[AcceptanceId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceLineId] <> '00000000-0000-0000-0000-000000000000' AND [MovementLineNumber] >= 1 AND ISJSON([ResultJson], OBJECT) = 1");
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceReceiptCostEffects_MovementLines_CompanyId_MovementId_MovementLineNumber",
                        columns: x => new { x.CompanyId, x.MovementId, x.MovementLineNumber },
                        principalSchema: "inventory",
                        principalTable: "MovementLines",
                        principalColumns: new[] { "CompanyId", "MovementId", "LineNumber" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceReceiptCostEffects_PurchaseInvoiceCostApplications_CompanyId_SourceEventId",
                        columns: x => new { x.CompanyId, x.SourceEventId },
                        principalSchema: "inventory",
                        principalTable: "PurchaseInvoiceCostApplications",
                        principalColumns: new[] { "CompanyId", "SourceEventId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceCostApplications_CompanyId_AdmissionId",
                schema: "inventory",
                table: "PurchaseInvoiceCostApplications",
                columns: new[] { "CompanyId", "AdmissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceCostApplications_CompanyId_InvoiceId",
                schema: "inventory",
                table: "PurchaseInvoiceCostApplications",
                columns: new[] { "CompanyId", "InvoiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceReceiptCostEffects_CompanyId_AcceptanceId_SourceEventId",
                schema: "inventory",
                table: "PurchaseInvoiceReceiptCostEffects",
                columns: new[] { "CompanyId", "AcceptanceId", "SourceEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceReceiptCostEffects_CompanyId_MovementId_MovementLineNumber",
                schema: "inventory",
                table: "PurchaseInvoiceReceiptCostEffects",
                columns: new[] { "CompanyId", "MovementId", "MovementLineNumber" });

            migrationBuilder.Sql("""
                CREATE TRIGGER [inventory].[TR_PurchaseInvoiceCostApplications_Immutable]
                ON [inventory].[PurchaseInvoiceCostApplications] AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51927, 'Purchase invoice cost applications are immutable; use a reviewed forward correction.', 1;
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER [inventory].[TR_PurchaseInvoiceReceiptCostEffects_Immutable]
                ON [inventory].[PurchaseInvoiceReceiptCostEffects] AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51928, 'Purchase invoice receipt cost effects are immutable; use a reviewed forward correction.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [inventory].[PurchaseInvoiceCostApplications])
                   OR EXISTS (SELECT 1 FROM [inventory].[PurchaseInvoiceReceiptCostEffects])
                BEGIN
                    THROW 51926, 'Purchase invoice cost provenance exists; AddPurchaseInvoiceCostApplications is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "PurchaseInvoiceReceiptCostEffects",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "PurchaseInvoiceCostApplications",
                schema: "inventory");
        }
    }
}
