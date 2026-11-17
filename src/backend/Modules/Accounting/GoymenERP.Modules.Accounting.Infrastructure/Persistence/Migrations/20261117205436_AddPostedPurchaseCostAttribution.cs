using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPostedPurchaseCostAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurchaseCostAttributionSources",
                schema: "accounting",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    LineageHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    LineageJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseCostAttributionSources", x => new { x.CompanyId, x.SourceEventId, x.InvoiceLineId });
                    table.CheckConstraint("CK_PurchaseCostAttributionSources_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SourceEventId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceLineId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_PurchaseCostAttributionSources_Proof", "LEN([SourceHash]) = 64 AND LEN([LineageHash]) = 64 AND ISJSON([LineageJson]) = 1 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_PurchaseCostAttributionSources_InventoryCostSources_CompanyId_SourceEventId",
                        columns: x => new { x.CompanyId, x.SourceEventId },
                        principalSchema: "accounting",
                        principalTable: "InventoryCostSources",
                        principalColumns: new[] { "CompanyId", "EventId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseCostAttributionApprovals",
                schema: "accounting",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProposalVersion = table.Column<long>(type: "bigint", nullable: false),
                    JournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SourceHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ViewJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseCostAttributionApprovals", x => new { x.CompanyId, x.SourceEventId, x.InvoiceLineId });
                    table.CheckConstraint("CK_PurchaseCostAttributionApprovals_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SourceEventId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceLineId] <> '00000000-0000-0000-0000-000000000000' AND [ProposalId] <> '00000000-0000-0000-0000-000000000000' AND [ProposalVersion] > 2");
                    table.CheckConstraint("CK_PurchaseCostAttributionApprovals_Proof", "LEN([SourceHash]) = 64 AND ISJSON([ViewJson]) = 1 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
                    table.CheckConstraint("CK_PurchaseCostAttributionApprovals_State", "([Status] = 3 AND [JournalId] IS NOT NULL) OR ([Status] = 6 AND [JournalId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_PurchaseCostAttributionApprovals_InventoryCostProposals_CompanyId_ProposalId_ProposalVersion",
                        columns: x => new { x.CompanyId, x.ProposalId, x.ProposalVersion },
                        principalSchema: "accounting",
                        principalTable: "InventoryCostProposals",
                        principalColumns: new[] { "CompanyId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseCostAttributionApprovals_Journals_CompanyId_JournalId",
                        columns: x => new { x.CompanyId, x.JournalId },
                        principalSchema: "accounting",
                        principalTable: "Journals",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseCostAttributionApprovals_PurchaseCostAttributionSources_CompanyId_SourceEventId_InvoiceLineId",
                        columns: x => new { x.CompanyId, x.SourceEventId, x.InvoiceLineId },
                        principalSchema: "accounting",
                        principalTable: "PurchaseCostAttributionSources",
                        principalColumns: new[] { "CompanyId", "SourceEventId", "InvoiceLineId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseCostAttributionCandidates",
                schema: "accounting",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcceptanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementLineNumber = table.Column<int>(type: "int", nullable: false),
                    ReceiptMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RootLabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ViewJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseCostAttributionCandidates", x => new { x.CompanyId, x.SourceEventId, x.InvoiceLineId, x.AcceptanceId, x.MovementId, x.MovementLineNumber });
                    table.CheckConstraint("CK_PurchaseCostAttributionCandidates_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SourceEventId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceLineId] <> '00000000-0000-0000-0000-000000000000' AND [AcceptanceId] <> '00000000-0000-0000-0000-000000000000' AND [ReceiptMovementId] <> '00000000-0000-0000-0000-000000000000' AND [RootLabelId] <> '00000000-0000-0000-0000-000000000000' AND [MovementId] <> '00000000-0000-0000-0000-000000000000' AND [MovementLineNumber] > 0 AND [ShipmentId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_PurchaseCostAttributionCandidates_Proof", "LEN([SourceHash]) = 64 AND ISJSON([ViewJson]) = 1 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_PurchaseCostAttributionCandidates_PurchaseCostAttributionSources_CompanyId_SourceEventId_InvoiceLineId",
                        columns: x => new { x.CompanyId, x.SourceEventId, x.InvoiceLineId },
                        principalSchema: "accounting",
                        principalTable: "PurchaseCostAttributionSources",
                        principalColumns: new[] { "CompanyId", "SourceEventId", "InvoiceLineId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseCostAttributionApprovals_CompanyId_JournalId",
                schema: "accounting",
                table: "PurchaseCostAttributionApprovals",
                columns: new[] { "CompanyId", "JournalId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseCostAttributionApprovals_CompanyId_ProposalId_ProposalVersion",
                schema: "accounting",
                table: "PurchaseCostAttributionApprovals",
                columns: new[] { "CompanyId", "ProposalId", "ProposalVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseCostAttributionCandidates_CompanyId_MovementId_MovementLineNumber",
                schema: "accounting",
                table: "PurchaseCostAttributionCandidates",
                columns: new[] { "CompanyId", "MovementId", "MovementLineNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseCostAttributionCandidates_CompanyId_ShipmentId",
                schema: "accounting",
                table: "PurchaseCostAttributionCandidates",
                columns: new[] { "CompanyId", "ShipmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseCostAttributionSources_CompanyId_InvoiceId_InvoiceLineId",
                schema: "accounting",
                table: "PurchaseCostAttributionSources",
                columns: new[] { "CompanyId", "InvoiceId", "InvoiceLineId" });

            // Only fixed migration-owned identifiers; source evidence is appended, never rewritten.
            foreach (var table in new[] { "PurchaseCostAttributionSources", "PurchaseCostAttributionCandidates", "PurchaseCostAttributionApprovals" })
            {
                migrationBuilder.Sql($"""
                    CREATE TRIGGER [accounting].[TR_{table}_Immutable]
                    ON [accounting].[{table}] AFTER UPDATE, DELETE AS
                    BEGIN
                        SET NOCOUNT ON;
                        IF EXISTS (SELECT 1 FROM deleted) AND EXISTS (SELECT 1 FROM inserted)
                            THROW 51805, 'Posted purchase cost attribution is immutable; append reviewed evidence.', 1;
                        IF EXISTS (SELECT 1 FROM deleted)
                            THROW 51806, 'Posted purchase cost attribution cannot be deleted; use forward correction.', 1;
                    END;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [accounting].[PurchaseCostAttributionSources])
                   OR EXISTS (SELECT 1 FROM [accounting].[PurchaseCostAttributionCandidates])
                   OR EXISTS (SELECT 1 FROM [accounting].[PurchaseCostAttributionApprovals])
                BEGIN
                    THROW 51804, 'Purchase attribution evidence exists; AddPostedPurchaseCostAttribution is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "PurchaseCostAttributionApprovals",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "PurchaseCostAttributionCandidates",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "PurchaseCostAttributionSources",
                schema: "accounting");
        }
    }
}
