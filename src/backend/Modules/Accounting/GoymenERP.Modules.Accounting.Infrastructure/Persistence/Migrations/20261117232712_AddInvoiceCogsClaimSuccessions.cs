using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceCogsClaimSuccessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvoiceCogsEconomicClaimSuccessions",
                schema: "accounting",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementLineNumber = table.Column<int>(type: "int", nullable: false),
                    Generation = table.Column<long>(type: "bigint", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinalizedVersion = table.Column<long>(type: "bigint", nullable: false),
                    PreviousClaimSha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CancellationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ViewJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SuccessionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SuccessionSha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceCogsEconomicClaimSuccessions", x => new { x.CompanyId, x.MovementId, x.MovementLineNumber, x.Generation });
                    table.CheckConstraint("CK_InvoiceCogsEconomicClaimSuccessions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [MovementId] <> '00000000-0000-0000-0000-000000000000' AND [MovementLineNumber] > 0 AND [Generation] >= 2 AND [ShipmentId] <> '00000000-0000-0000-0000-000000000000' AND [SalesEventId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceLineId] <> '00000000-0000-0000-0000-000000000000' AND [AdmissionId] <> '00000000-0000-0000-0000-000000000000' AND [FinalizedVersion] > 0 AND [CancellationId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_InvoiceCogsEconomicClaimSuccessions_Proof", "LEN([PreviousClaimSha256]) = 64 AND LEN([SourceHash]) = 64 AND LEN([SuccessionSha256]) = 64 AND ISJSON([ViewJson]) = 1 AND ISJSON([SuccessionJson]) = 1 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_InvoiceCogsEconomicClaimSuccessions_InventoryCostSources_CompanyId_SalesEventId",
                        columns: x => new { x.CompanyId, x.SalesEventId },
                        principalSchema: "accounting",
                        principalTable: "InventoryCostSources",
                        principalColumns: new[] { "CompanyId", "EventId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceCogsEconomicClaimSuccessions_InvoiceCogsEconomicClaims_CompanyId_MovementId_MovementLineNumber",
                        columns: x => new { x.CompanyId, x.MovementId, x.MovementLineNumber },
                        principalSchema: "accounting",
                        principalTable: "InvoiceCogsEconomicClaims",
                        principalColumns: new[] { "CompanyId", "MovementId", "MovementLineNumber" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceCogsEconomicClaimSuccessions_CompanyId_InvoiceId_InvoiceLineId_FinalizedVersion",
                schema: "accounting",
                table: "InvoiceCogsEconomicClaimSuccessions",
                columns: new[] { "CompanyId", "InvoiceId", "InvoiceLineId", "FinalizedVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceCogsEconomicClaimSuccessions_CompanyId_SalesEventId_MovementId_MovementLineNumber",
                schema: "accounting",
                table: "InvoiceCogsEconomicClaimSuccessions",
                columns: new[] { "CompanyId", "SalesEventId", "MovementId", "MovementLineNumber" },
                unique: true);

            // Fixed owner table. A new invoice appends a generation; no prior economic claim is overwritten.
            migrationBuilder.Sql("""
                CREATE TRIGGER [accounting].[TR_InvoiceCogsEconomicClaimSuccessions_Immutable]
                ON [accounting].[InvoiceCogsEconomicClaimSuccessions] AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted) AND EXISTS (SELECT 1 FROM inserted)
                        THROW 51842, 'Invoice cost claim succession is immutable; append a proven generation.', 1;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51843, 'Invoice cost claim succession cannot be deleted; preserve economic history.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [accounting].[InvoiceCogsEconomicClaimSuccessions])
                BEGIN
                    THROW 51840, 'Invoice cost ownership generations exist; AddInvoiceCogsClaimSuccessions is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "InvoiceCogsEconomicClaimSuccessions",
                schema: "accounting");
        }
    }
}
