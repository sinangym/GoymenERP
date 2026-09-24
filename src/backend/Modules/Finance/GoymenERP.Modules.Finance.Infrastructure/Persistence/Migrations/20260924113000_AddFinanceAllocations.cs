using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;

/// <summary>F-03 allocation persistence and the real reversal gate source; no command/grant is opened.</summary>
[DbContext(typeof(FinanceDbContext))]
[Migration("20260924113000_AddFinanceAllocations")]
public sealed class AddFinanceAllocations : Migration
{
    private const string StatusCheck =
        "([Status] = 1 AND [ReleasedBy] IS NULL AND [ReleasedAtUtc] IS NULL AND [ReleaseReason] IS NULL) OR "
        + "([Status] = 2 AND [ReleasedBy] IS NOT NULL AND LEN([ReleasedBy]) > 0 AND [ReleasedAtUtc] IS NOT NULL "
        + "AND [ReleaseReason] IS NOT NULL AND LEN([ReleaseReason]) > 0)";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "OpenItemSourceBatches", schema: "finance",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceKind = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                SourceVersion = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                SourceChecksum = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                IdempotencyKeyHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                PayloadHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false,
                    collation: "Latin1_General_100_BIN2"),
                ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false,
                    collation: "Latin1_General_100_BIN2"),
                CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true,
                    collation: "Latin1_General_100_BIN2"),
                AdmittedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OpenItemSourceBatches", x => x.Id);
                table.UniqueConstraint("AK_OpenItemSourceBatches_CompanyId_Id", x => new { x.CompanyId, x.Id });
                table.CheckConstraint("CK_OpenItemSourceBatches_Identity",
                    "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_OpenItemSourceBatches_Text",
                    "LEN([SourceKind]) > 0 AND LEN([SourceVersion]) > 0 AND LEN([SourceChecksum]) = 64 AND LEN([IdempotencyKeyHash]) = 64 AND LEN([PayloadHash]) = 64 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0 AND ISJSON([ResultJson]) = 1");
                table.ForeignKey("FK_OpenItemSourceBatches_Companies_CompanyId", x => x.CompanyId,
                    principalSchema: "organization", principalTable: "Companies", principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("UX_OpenItemSourceBatches_CompanyId_IdempotencyKeyHash",
            "OpenItemSourceBatches", new[] { "CompanyId", "IdempotencyKeyHash" },
            schema: "finance", unique: true);

        migrationBuilder.CreateTable(
            name: "OpenItems", schema: "finance",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceKind = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceVersion = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                SourceBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceBatchVersion = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                SourceBatchChecksum = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OriginalAmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                OutstandingAmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                Status = table.Column<int>(type: "int", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false,
                    collation: "Latin1_General_100_BIN2"),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false,
                    collation: "Latin1_General_100_BIN2"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OpenItems", x => x.Id);
                table.UniqueConstraint("AK_OpenItems_CompanyId_Id", x => new { x.CompanyId, x.Id });
                table.CheckConstraint("CK_OpenItems_Identity",
                    "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SourceId] <> '00000000-0000-0000-0000-000000000000' AND [SourceLineId] <> '00000000-0000-0000-0000-000000000000' AND [SourceBatchId] <> '00000000-0000-0000-0000-000000000000' AND [PartyId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_OpenItems_Text",
                    "LEN([SourceKind]) > 0 AND LEN([SourceVersion]) > 0 AND LEN([SourceBatchVersion]) > 0 AND LEN([SourceBatchChecksum]) = 64 AND LEN([OriginalAmountText]) > 0 AND LEN([OutstandingAmountText]) > 0 AND LEN([CreatedBy]) > 0 AND LEN([UpdatedBy]) > 0");
                table.CheckConstraint("CK_OpenItems_Status", "[Status] IN (1, 2)");
                table.CheckConstraint("CK_OpenItems_Version", "[Version] >= 1");
                table.ForeignKey("FK_OpenItems_Companies_CompanyId", x => x.CompanyId,
                    principalSchema: "organization", principalTable: "Companies", principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_OpenItems_OpenItemSourceBatches_CompanyId_SourceBatchId",
                    x => new { x.CompanyId, x.SourceBatchId }, principalSchema: "finance",
                    principalTable: "OpenItemSourceBatches", principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("UX_OpenItems_CompanyId_Source", "OpenItems",
            new[] { "CompanyId", "SourceKind", "SourceId", "SourceLineId" },
            schema: "finance", unique: true);
        migrationBuilder.CreateIndex("IX_OpenItems_CompanyId_SourceBatchId", "OpenItems",
            new[] { "CompanyId", "SourceBatchId" }, schema: "finance");
        migrationBuilder.CreateIndex("IX_OpenItems_CompanyId_Account_Currency_Status_DueOn", "OpenItems",
            new[] { "CompanyId", "CompanyAccountId", "CurrencyId", "Status", "DueOn" }, schema: "finance");

        migrationBuilder.CreateTable(
            name: "SourceAllocationStates", schema: "finance",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OriginalAmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                UnallocatedAmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false,
                    collation: "Latin1_General_100_BIN2"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SourceAllocationStates", x => new { x.CompanyId, x.SourceId });
                table.CheckConstraint("CK_SourceAllocationStates_Identity",
                    "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SourceId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_SourceAllocationStates_Text",
                    "LEN([OriginalAmountText]) > 0 AND LEN([UnallocatedAmountText]) > 0 AND LEN([UpdatedBy]) > 0");
                table.CheckConstraint("CK_SourceAllocationStates_Version", "[Version] >= 1");
                table.ForeignKey("FK_SourceAllocationStates_Companies_CompanyId", x => x.CompanyId,
                    principalSchema: "organization", principalTable: "Companies", principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_SourceAllocationStates_ReceiptDrafts_CompanyId_SourceId",
                    x => new { x.CompanyId, x.SourceId }, principalSchema: "finance",
                    principalTable: "ReceiptDrafts", principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Allocations", schema: "finance",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OpenItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false,
                    collation: "Latin1_General_100_BIN2"),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ReleasedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true,
                    collation: "Latin1_General_100_BIN2"),
                ReleasedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ReleaseReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Allocations", x => x.Id);
                table.UniqueConstraint("AK_Allocations_CompanyId_Id", x => new { x.CompanyId, x.Id });
                table.CheckConstraint("CK_Allocations_Identity",
                    "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SourceId] <> '00000000-0000-0000-0000-000000000000' AND [OpenItemId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Allocations_Text",
                    "LEN([AmountText]) > 0 AND LEN([Reason]) > 0 AND LEN([CreatedBy]) > 0");
                table.CheckConstraint("CK_Allocations_Version", "[Version] >= 1");
                table.CheckConstraint("CK_Allocations_Status", StatusCheck);
                table.ForeignKey("FK_Allocations_Companies_CompanyId", x => x.CompanyId,
                    principalSchema: "organization", principalTable: "Companies", principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Allocations_ReceiptDrafts_CompanyId_SourceId",
                    x => new { x.CompanyId, x.SourceId }, principalSchema: "finance",
                    principalTable: "ReceiptDrafts", principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Allocations_OpenItems_CompanyId_OpenItemId",
                    x => new { x.CompanyId, x.OpenItemId }, principalSchema: "finance",
                    principalTable: "OpenItems", principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_Allocations_CompanyId_SourceId_Status", "Allocations",
            new[] { "CompanyId", "SourceId", "Status" }, schema: "finance");
        migrationBuilder.CreateIndex("IX_Allocations_CompanyId_OpenItemId_Status", "Allocations",
            new[] { "CompanyId", "OpenItemId", "Status" }, schema: "finance");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Commands_Target", schema: "finance", table: "Commands");
        migrationBuilder.AddColumn<Guid>(
            name: "FinanceAllocationId", schema: "finance", table: "Commands",
            type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddCheckConstraint(
            name: "CK_Commands_Target", schema: "finance", table: "Commands",
            sql: "([DraftId] IS NOT NULL AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL AND [ManualBankPaymentId] IS NULL AND [FinanceAllocationId] IS NULL) OR "
                + "([BankAccountId] IS NOT NULL AND [BankAccountId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [ReversalProposalId] IS NULL AND [ManualBankPaymentId] IS NULL AND [FinanceAllocationId] IS NULL) OR "
                + "([ReversalProposalId] IS NOT NULL AND [ReversalProposalId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [BankAccountId] IS NULL AND [ManualBankPaymentId] IS NULL AND [FinanceAllocationId] IS NULL) OR "
                + "([ManualBankPaymentId] IS NOT NULL AND [ManualBankPaymentId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL AND [FinanceAllocationId] IS NULL) OR "
                + "([FinanceAllocationId] IS NOT NULL AND [FinanceAllocationId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL AND [ManualBankPaymentId] IS NULL) OR "
                + "([DraftId] IS NULL AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL AND [ManualBankPaymentId] IS NULL AND [FinanceAllocationId] IS NULL AND JSON_VALUE([ResultJson], '$.outcome') = 'ValidationFailed')");
        migrationBuilder.CreateIndex(
            name: "IX_Commands_CompanyId_FinanceAllocationId", schema: "finance", table: "Commands",
            columns: new[] { "CompanyId", "FinanceAllocationId" });
        migrationBuilder.AddForeignKey(
            name: "FK_Commands_Allocations_CompanyId_FinanceAllocationId", schema: "finance", table: "Commands",
            columns: new[] { "CompanyId", "FinanceAllocationId" }, principalSchema: "finance",
            principalTable: "Allocations", principalColumns: new[] { "CompanyId", "Id" },
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "IF EXISTS (SELECT 1 FROM [finance].[Allocations]) OR EXISTS (SELECT 1 FROM [finance].[OpenItems]) "
            + "OR EXISTS (SELECT 1 FROM [finance].[SourceAllocationStates]) OR EXISTS (SELECT 1 FROM [finance].[OpenItemSourceBatches]) "
            + "THROW 51052, 'Finance open items or allocations exist; use a forward fix.', 1;");
        migrationBuilder.DropForeignKey(
            name: "FK_Commands_Allocations_CompanyId_FinanceAllocationId", schema: "finance", table: "Commands");
        migrationBuilder.DropIndex(
            name: "IX_Commands_CompanyId_FinanceAllocationId", schema: "finance", table: "Commands");
        migrationBuilder.DropCheckConstraint(
            name: "CK_Commands_Target", schema: "finance", table: "Commands");
        migrationBuilder.DropColumn(name: "FinanceAllocationId", schema: "finance", table: "Commands");
        migrationBuilder.AddCheckConstraint(
            name: "CK_Commands_Target", schema: "finance", table: "Commands",
            sql: "([DraftId] IS NOT NULL AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL AND [ManualBankPaymentId] IS NULL) OR "
                + "([BankAccountId] IS NOT NULL AND [BankAccountId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [ReversalProposalId] IS NULL AND [ManualBankPaymentId] IS NULL) OR "
                + "([ReversalProposalId] IS NOT NULL AND [ReversalProposalId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [BankAccountId] IS NULL AND [ManualBankPaymentId] IS NULL) OR "
                + "([ManualBankPaymentId] IS NOT NULL AND [ManualBankPaymentId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL) OR "
                + "([DraftId] IS NULL AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL AND [ManualBankPaymentId] IS NULL AND JSON_VALUE([ResultJson], '$.outcome') = 'ValidationFailed')");
        migrationBuilder.DropTable(name: "Allocations", schema: "finance");
        migrationBuilder.DropTable(name: "SourceAllocationStates", schema: "finance");
        migrationBuilder.DropTable(name: "OpenItems", schema: "finance");
        migrationBuilder.DropTable(name: "OpenItemSourceBatches", schema: "finance");
    }
}
