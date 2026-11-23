using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryCostProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryCostSources",
                schema: "accounting",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceVersion = table.Column<long>(type: "bigint", nullable: false),
                    AdmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayloadHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryCostSources", x => new { x.CompanyId, x.EventId });
                    table.CheckConstraint("CK_InventoryCostSources_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [EventId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [AdmissionId] <> '00000000-0000-0000-0000-000000000000' AND [Kind] IN (1,2) AND [InvoiceVersion] >= 1");
                    table.CheckConstraint("CK_InventoryCostSources_Proof", "ISJSON([SourceJson]) = 1 AND LEN([PayloadHash]) = 64 AND DATEPART(TZOFFSET,[OccurredAtUtc]) = 0 AND DATEPART(TZOFFSET,[ReceivedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_InventoryCostSources_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryCostProposals",
                schema: "accounting",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComponentKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PreparedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    JournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ViewJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryCostProposals", x => new { x.CompanyId, x.Id, x.Version });
                    table.CheckConstraint("CK_InventoryCostProposals_Actors", "([PreparedBy] IS NULL OR [PreparedBy] <> '00000000-0000-0000-0000-000000000000') AND ([ApprovedBy] IS NULL OR [ApprovedBy] <> '00000000-0000-0000-0000-000000000000') AND ([PeriodId] IS NULL OR [PeriodId] <> '00000000-0000-0000-0000-000000000000') AND ([JournalId] IS NULL OR [JournalId] <> '00000000-0000-0000-0000-000000000000')");
                    table.CheckConstraint("CK_InventoryCostProposals_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [EventId] <> '00000000-0000-0000-0000-000000000000' AND [Version] >= 1 AND LEN([ComponentKey]) > 0");
                    table.CheckConstraint("CK_InventoryCostProposals_Proof", "ISJSON([ViewJson]) = 1 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
                    table.CheckConstraint("CK_InventoryCostProposals_State", "([Status] IN (1,4) AND [Version] = 1 AND [PreparedBy] IS NULL AND [ApprovedBy] IS NULL AND [PeriodId] IS NULL AND [JournalId] IS NULL) OR ([Status] = 2 AND [Version] > 1 AND [PreparedBy] IS NOT NULL AND [PeriodId] IS NOT NULL AND [ApprovedBy] IS NULL AND [JournalId] IS NULL) OR ([Status] = 3 AND [Version] > 2 AND [PreparedBy] IS NOT NULL AND [PeriodId] IS NOT NULL AND [ApprovedBy] IS NOT NULL AND [JournalId] IS NOT NULL AND [ApprovedBy] <> [PreparedBy])");
                    table.ForeignKey(
                        name: "FK_InventoryCostProposals_InventoryCostSources_CompanyId_EventId",
                        columns: x => new { x.CompanyId, x.EventId },
                        principalSchema: "accounting",
                        principalTable: "InventoryCostSources",
                        principalColumns: new[] { "CompanyId", "EventId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCostProposals_Journals_CompanyId_JournalId",
                        columns: x => new { x.CompanyId, x.JournalId },
                        principalSchema: "accounting",
                        principalTable: "Journals",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCostProposals_Periods_CompanyId_PeriodId",
                        columns: x => new { x.CompanyId, x.PeriodId },
                        principalSchema: "accounting",
                        principalTable: "Periods",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostProposals_CompanyId_EventId_ComponentKey_Version",
                schema: "accounting",
                table: "InventoryCostProposals",
                columns: new[] { "CompanyId", "EventId", "ComponentKey", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostProposals_CompanyId_JournalId",
                schema: "accounting",
                table: "InventoryCostProposals",
                columns: new[] { "CompanyId", "JournalId" },
                unique: true,
                filter: "[JournalId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostProposals_CompanyId_PeriodId",
                schema: "accounting",
                table: "InventoryCostProposals",
                columns: new[] { "CompanyId", "PeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostSources_CompanyId_Kind_InvoiceId_InvoiceVersion",
                schema: "accounting",
                table: "InventoryCostSources",
                columns: new[] { "CompanyId", "Kind", "InvoiceId", "InvoiceVersion" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE TRIGGER [accounting].[TR_InventoryCostSources_Immutable]
                ON [accounting].[InventoryCostSources] AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51931, 'Inventory cost sources are immutable; use a reviewed forward correction.', 1;
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER [accounting].[TR_InventoryCostProposals_Immutable]
                ON [accounting].[InventoryCostProposals] AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51932, 'Inventory cost proposal history is immutable; append a new version.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [accounting].[InventoryCostSources])
                   OR EXISTS (SELECT 1 FROM [accounting].[InventoryCostProposals])
                BEGIN
                    THROW 51930, 'Inventory cost source or proposal history exists; AddInventoryCostProposals is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "InventoryCostProposals",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "InventoryCostSources",
                schema: "accounting");
        }
    }
}
