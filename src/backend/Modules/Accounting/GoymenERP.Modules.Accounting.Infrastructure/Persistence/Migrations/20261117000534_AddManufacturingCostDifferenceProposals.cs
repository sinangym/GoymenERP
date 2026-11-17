using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingCostDifferenceProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ManufacturingCostDifferences",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManufacturingOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GenerationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GenerationVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceOccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SourceCorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SignedTlDifference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AccountingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DebitAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreditAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AccountPlanVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AccountPlanSha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    AccountPlanProofJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PreparedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    JournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturingCostDifferences", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_ManufacturingCostDifferences_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ManufacturingOrderId] <> '00000000-0000-0000-0000-000000000000' AND [GenerationId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ManufacturingCostDifferences_Source", "ISJSON([SourceJson]) = 1 AND LEN([SourceHash]) = 64");
                    table.CheckConstraint("CK_ManufacturingCostDifferences_State", "([Status] IN (1,4) AND [PreparedBy] IS NULL AND [PreparedAtUtc] IS NULL AND [PeriodId] IS NULL AND [AccountingDate] IS NULL AND [TransactionDate] IS NULL AND [CurrencyId] IS NULL AND [DebitAccountId] IS NULL AND [CreditAccountId] IS NULL AND [AccountPlanVersion] IS NULL AND [AccountPlanSha256] IS NULL AND [AccountPlanProofJson] IS NULL AND [Reason] IS NULL AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [JournalId] IS NULL) OR ([Status] IN (2,3) AND [PreparedBy] IS NOT NULL AND [PreparedBy] <> '00000000-0000-0000-0000-000000000000' AND [PreparedAtUtc] IS NOT NULL AND [PeriodId] IS NOT NULL AND [AccountingDate] IS NOT NULL AND [TransactionDate] IS NOT NULL AND [CurrencyId] IS NOT NULL AND [DebitAccountId] IS NOT NULL AND [CreditAccountId] IS NOT NULL AND [DebitAccountId] <> [CreditAccountId] AND [PeriodId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [DebitAccountId] <> '00000000-0000-0000-0000-000000000000' AND [CreditAccountId] <> '00000000-0000-0000-0000-000000000000' AND [AccountPlanVersion] IS NOT NULL AND LEN([AccountPlanVersion]) > 0 AND [AccountPlanSha256] IS NOT NULL AND LEN([AccountPlanSha256]) = 64 AND [AccountPlanProofJson] IS NOT NULL AND ISJSON([AccountPlanProofJson]) = 1 AND [Reason] IS NOT NULL AND LEN([Reason]) > 0 AND (([Status] = 2 AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [JournalId] IS NULL) OR ([Status] = 3 AND [ApprovedBy] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL AND [JournalId] IS NOT NULL AND [ApprovedBy] <> [PreparedBy] AND [ApprovedBy] <> '00000000-0000-0000-0000-000000000000' AND [JournalId] <> '00000000-0000-0000-0000-000000000000')))");
                    table.CheckConstraint("CK_ManufacturingCostDifferences_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_ManufacturingCostDifferences_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManufacturingCostDifferences_Journals_CompanyId_JournalId",
                        columns: x => new { x.CompanyId, x.JournalId },
                        principalSchema: "accounting",
                        principalTable: "Journals",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManufacturingCostDifferences_Periods_CompanyId_PeriodId",
                        columns: x => new { x.CompanyId, x.PeriodId },
                        principalSchema: "accounting",
                        principalTable: "Periods",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingCostDifferences_CompanyId_GenerationId_GenerationVersion",
                schema: "accounting",
                table: "ManufacturingCostDifferences",
                columns: new[] { "CompanyId", "GenerationId", "GenerationVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingCostDifferences_CompanyId_JournalId",
                schema: "accounting",
                table: "ManufacturingCostDifferences",
                columns: new[] { "CompanyId", "JournalId" });

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingCostDifferences_CompanyId_PeriodId",
                schema: "accounting",
                table: "ManufacturingCostDifferences",
                columns: new[] { "CompanyId", "PeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingCostDifferences_CompanyId_Status_SourceOccurredAtUtc",
                schema: "accounting",
                table: "ManufacturingCostDifferences",
                columns: new[] { "CompanyId", "Status", "SourceOccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [accounting].[ManufacturingCostDifferences])
                    THROW 51962, 'Manufacturing cost-difference source or human decision exists; use a forward fix.', 1;
                """);
            migrationBuilder.DropTable(
                name: "ManufacturingCostDifferences",
                schema: "accounting");
        }
    }
}
