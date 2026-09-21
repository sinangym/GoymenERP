using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;

/// <summary>FIN-T06/B1-B2 persistent reversal proposal queue; no existing source is rewritten.</summary>
[DbContext(typeof(FinanceDbContext))]
[Migration("20260921120000_AddReversalProposals")]
public sealed class AddReversalProposals : Migration
{
    private const string DecisionCheck =
        "([Status] = 0 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL AND [ReversalSourceId] IS NULL) OR "
        + "([Status] = 1 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [DecisionReason] IS NULL AND [ReversalSourceId] IS NOT NULL AND [ReversalSourceId] <> [OriginalSourceId] AND [ProposedBy] <> [DecidedBy]) OR "
        + "([Status] = 2 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [DecisionReason] IS NOT NULL AND LEN([DecisionReason]) > 0 AND [ReversalSourceId] IS NULL AND [ProposedBy] <> [DecidedBy])";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_MoneyEvents_Kind", schema: "finance", table: "MoneyEvents");
        migrationBuilder.AddCheckConstraint(
            name: "CK_MoneyEvents_Kind", schema: "finance", table: "MoneyEvents", sql: "[Kind] IN (1, 2)");

        migrationBuilder.CreateTable(
            name: "ReversalProposals", schema: "finance",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OriginalSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                ProposedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false,
                    collation: "Latin1_General_100_BIN2"),
                ProposedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true,
                    collation: "Latin1_General_100_BIN2"),
                DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                DecisionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                ReversalSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReversalProposals", x => x.Id);
                table.UniqueConstraint("AK_ReversalProposals_CompanyId_Id", x => new { x.CompanyId, x.Id });
                table.CheckConstraint("CK_ReversalProposals_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [OriginalSourceId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_ReversalProposals_Text", "LEN([Reason]) > 0 AND LEN([ProposedBy]) > 0");
                table.CheckConstraint("CK_ReversalProposals_Version", "[Version] >= 1");
                table.CheckConstraint("CK_ReversalProposals_Decision", DecisionCheck);
                table.ForeignKey("FK_ReversalProposals_Companies_CompanyId", x => x.CompanyId,
                    principalSchema: "organization", principalTable: "Companies", principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ReversalProposals_ReceiptDrafts_CompanyId_OriginalSourceId",
                    x => new { x.CompanyId, x.OriginalSourceId }, principalSchema: "finance",
                    principalTable: "ReceiptDrafts", principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ReversalProposals_ReceiptDrafts_CompanyId_ReversalSourceId",
                    x => new { x.CompanyId, x.ReversalSourceId }, principalSchema: "finance",
                    principalTable: "ReceiptDrafts", principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("UX_ReversalProposals_PendingOriginal", "ReversalProposals",
            new[] { "CompanyId", "OriginalSourceId" }, schema: "finance", unique: true, filter: "[Status] = 0");
        migrationBuilder.CreateIndex("UX_ReversalProposals_ApprovedOriginal", "ReversalProposals",
            new[] { "CompanyId", "OriginalSourceId" }, schema: "finance", unique: true, filter: "[Status] = 1");
        migrationBuilder.CreateIndex("IX_ReversalProposals_CompanyId_ReversalSourceId", "ReversalProposals",
            new[] { "CompanyId", "ReversalSourceId" }, schema: "finance");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Commands_Target", schema: "finance", table: "Commands");
        migrationBuilder.AddColumn<Guid>(
            name: "ReversalProposalId", schema: "finance", table: "Commands", type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddCheckConstraint(
            name: "CK_Commands_Target", schema: "finance", table: "Commands",
            sql: "([DraftId] IS NOT NULL AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL) OR "
                + "([BankAccountId] IS NOT NULL AND [BankAccountId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [ReversalProposalId] IS NULL) OR "
                + "([ReversalProposalId] IS NOT NULL AND [ReversalProposalId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [BankAccountId] IS NULL) OR "
                + "([DraftId] IS NULL AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL AND JSON_VALUE([ResultJson], '$.outcome') = 'ValidationFailed')");
        migrationBuilder.CreateIndex(
            name: "IX_Commands_CompanyId_ReversalProposalId", schema: "finance", table: "Commands",
            columns: new[] { "CompanyId", "ReversalProposalId" });
        migrationBuilder.AddForeignKey(
            name: "FK_Commands_ReversalProposals_CompanyId_ReversalProposalId", schema: "finance", table: "Commands",
            columns: new[] { "CompanyId", "ReversalProposalId" }, principalSchema: "finance",
            principalTable: "ReversalProposals", principalColumns: new[] { "CompanyId", "Id" },
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Commands_ReversalProposals_CompanyId_ReversalProposalId", schema: "finance", table: "Commands");
        migrationBuilder.DropIndex(
            name: "IX_Commands_CompanyId_ReversalProposalId", schema: "finance", table: "Commands");
        migrationBuilder.DropCheckConstraint(
            name: "CK_Commands_Target", schema: "finance", table: "Commands");
        migrationBuilder.DropColumn(name: "ReversalProposalId", schema: "finance", table: "Commands");
        migrationBuilder.AddCheckConstraint(
            name: "CK_Commands_Target", schema: "finance", table: "Commands",
            sql: "([DraftId] IS NOT NULL AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [BankAccountId] IS NULL) OR "
                + "([BankAccountId] IS NOT NULL AND [BankAccountId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL) OR "
                + "([DraftId] IS NULL AND [BankAccountId] IS NULL AND JSON_VALUE([ResultJson], '$.outcome') = 'ValidationFailed')");
        migrationBuilder.DropTable(name: "ReversalProposals", schema: "finance");
        migrationBuilder.DropCheckConstraint(
            name: "CK_MoneyEvents_Kind", schema: "finance", table: "MoneyEvents");
        migrationBuilder.AddCheckConstraint(
            name: "CK_MoneyEvents_Kind", schema: "finance", table: "MoneyEvents", sql: "[Kind] = 1");
    }
}
