using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryCostComponentCancellations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryCostCancellationVersions",
                schema: "accounting",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InventoryCostProposalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    InitialProposalVersion = table.Column<long>(type: "bigint", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CancellationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreparedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ViewJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Sha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryCostCancellationVersions", x => new { x.CompanyId, x.InventoryCostProposalId, x.Version });
                    table.CheckConstraint("CK_InventoryCostCancellationVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [InventoryCostProposalId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [CancellationId] <> '00000000-0000-0000-0000-000000000000' AND [Version] >= 1 AND [InitialProposalVersion] >= 1 AND [PreparedBy] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_InventoryCostCancellationVersions_Proof", "ISJSON([ViewJson]) = 1 AND LEN([Sha256]) = 64 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
                    table.CheckConstraint("CK_InventoryCostCancellationVersions_State", "([Status] = 1 AND [ApprovedBy] IS NULL) OR ([Status] = 2 AND [Version] >= 2 AND [ApprovedBy] IS NOT NULL AND [ApprovedBy] <> [PreparedBy] AND [ApprovedBy] <> '00000000-0000-0000-0000-000000000000')");
                    table.ForeignKey(
                        name: "FK_InventoryCostCancellationVersions_InventoryCostProposals_CompanyId_InventoryCostProposalId_InitialProposalVersion",
                        columns: x => new { x.CompanyId, x.InventoryCostProposalId, x.InitialProposalVersion },
                        principalSchema: "accounting",
                        principalTable: "InventoryCostProposals",
                        principalColumns: new[] { "CompanyId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCostCancellationVersions_Periods_CompanyId_PeriodId",
                        columns: x => new { x.CompanyId, x.PeriodId },
                        principalSchema: "accounting",
                        principalTable: "Periods",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryCostCancellationClaims",
                schema: "accounting",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinalizedVersion = table.Column<long>(type: "bigint", nullable: false),
                    EconomicKey = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    InventoryCostProposalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CancellationVersion = table.Column<long>(type: "bigint", nullable: false),
                    SourceJournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReversingJournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ViewJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Sha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryCostCancellationClaims", x => new { x.CompanyId, x.InvoiceId, x.InvoiceLineId, x.FinalizedVersion, x.EconomicKey });
                    table.CheckConstraint("CK_InventoryCostCancellationClaims_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceLineId] <> '00000000-0000-0000-0000-000000000000' AND [FinalizedVersion] >= 1 AND [CancellationVersion] >= 2 AND LEN([EconomicKey]) = 64");
                    table.CheckConstraint("CK_InventoryCostCancellationClaims_Proof", "ISJSON([ViewJson]) = 1 AND LEN([Sha256]) = 64 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0 AND ([ReversingJournalId] IS NULL OR [SourceJournalId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_InventoryCostCancellationClaims_InventoryCostCancellationVersions_CompanyId_InventoryCostProposalId_CancellationVersion",
                        columns: x => new { x.CompanyId, x.InventoryCostProposalId, x.CancellationVersion },
                        principalSchema: "accounting",
                        principalTable: "InventoryCostCancellationVersions",
                        principalColumns: new[] { "CompanyId", "InventoryCostProposalId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCostCancellationClaims_Journals_CompanyId_ReversingJournalId",
                        columns: x => new { x.CompanyId, x.ReversingJournalId },
                        principalSchema: "accounting",
                        principalTable: "Journals",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCostCancellationClaims_Journals_CompanyId_SourceJournalId",
                        columns: x => new { x.CompanyId, x.SourceJournalId },
                        principalSchema: "accounting",
                        principalTable: "Journals",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostCancellationClaims_CompanyId_InventoryCostProposalId_CancellationVersion",
                schema: "accounting",
                table: "InventoryCostCancellationClaims",
                columns: new[] { "CompanyId", "InventoryCostProposalId", "CancellationVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostCancellationClaims_CompanyId_ReversingJournalId",
                schema: "accounting",
                table: "InventoryCostCancellationClaims",
                columns: new[] { "CompanyId", "ReversingJournalId" },
                unique: true,
                filter: "[ReversingJournalId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostCancellationClaims_CompanyId_SourceJournalId",
                schema: "accounting",
                table: "InventoryCostCancellationClaims",
                columns: new[] { "CompanyId", "SourceJournalId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostCancellationVersions_CompanyId_Id_Version",
                schema: "accounting",
                table: "InventoryCostCancellationVersions",
                columns: new[] { "CompanyId", "Id", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostCancellationVersions_CompanyId_InventoryCostProposalId_InitialProposalVersion",
                schema: "accounting",
                table: "InventoryCostCancellationVersions",
                columns: new[] { "CompanyId", "InventoryCostProposalId", "InitialProposalVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostCancellationVersions_CompanyId_PeriodId",
                schema: "accounting",
                table: "InventoryCostCancellationVersions",
                columns: new[] { "CompanyId", "PeriodId" });

            // Fixed migration-owned tables; approved source shares and their answers are append-only.
            foreach (var table in new[] { "InventoryCostCancellationVersions", "InventoryCostCancellationClaims" })
            {
                migrationBuilder.Sql($"""
                    CREATE TRIGGER [accounting].[TR_{table}_Immutable]
                    ON [accounting].[{table}] AFTER UPDATE, DELETE AS
                    BEGIN
                        SET NOCOUNT ON;
                        IF EXISTS (SELECT 1 FROM deleted) AND EXISTS (SELECT 1 FROM inserted)
                            THROW 51813, 'Inventory cost cancellation evidence is immutable; append a reviewed version.', 1;
                        IF EXISTS (SELECT 1 FROM deleted)
                            THROW 51814, 'Inventory cost cancellation evidence cannot be deleted; use forward correction.', 1;
                    END;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [accounting].[InventoryCostCancellationVersions])
                   OR EXISTS (SELECT 1 FROM [accounting].[InventoryCostCancellationClaims])
                BEGIN
                    THROW 51812, 'Component cancellation evidence exists; AddInventoryCostComponentCancellations is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "InventoryCostCancellationClaims",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "InventoryCostCancellationVersions",
                schema: "accounting");
        }
    }
}
