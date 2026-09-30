using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;

/// <summary>
/// DEC-FIN-016 first release (customer cheque / note portfolio): the instruments, their append-only movements, the
/// command record's sixth target and the collection's own money-event kind (3). No existing row is rewritten: every
/// stored command names another target and every stored money event is kind 1 or 2.
/// <see cref="Down"/> refuses with Finance's module refusal (<c>THROW 51051</c>) while any instrument, instrument command
/// or collection money event is stored: a rollback would drop a customer's cheque and its collection. With none it
/// restores exactly the rules this migration replaced.
/// </summary>
[DbContext(typeof(FinanceDbContext))]
[Migration("20260927180000_AddCustomerInstruments")]
public sealed class AddCustomerInstruments : Migration
{
    // Written out, not read from the model: this migration's text never changes with the model.
    private const string TargetCheck =
        "([DraftId] IS NOT NULL AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL AND [ManualBankPaymentId] IS NULL AND [FinanceAllocationId] IS NULL AND [CustomerInstrumentId] IS NULL) OR "
        + "([BankAccountId] IS NOT NULL AND [BankAccountId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [ReversalProposalId] IS NULL AND [ManualBankPaymentId] IS NULL AND [FinanceAllocationId] IS NULL AND [CustomerInstrumentId] IS NULL) OR "
        + "([ReversalProposalId] IS NOT NULL AND [ReversalProposalId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [BankAccountId] IS NULL AND [ManualBankPaymentId] IS NULL AND [FinanceAllocationId] IS NULL AND [CustomerInstrumentId] IS NULL) OR "
        + "([ManualBankPaymentId] IS NOT NULL AND [ManualBankPaymentId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL AND [FinanceAllocationId] IS NULL AND [CustomerInstrumentId] IS NULL) OR "
        + "([FinanceAllocationId] IS NOT NULL AND [FinanceAllocationId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL AND [ManualBankPaymentId] IS NULL AND [CustomerInstrumentId] IS NULL) OR "
        + "([CustomerInstrumentId] IS NOT NULL AND [CustomerInstrumentId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL AND [ManualBankPaymentId] IS NULL AND [FinanceAllocationId] IS NULL) OR "
        + "([DraftId] IS NULL AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL AND [ManualBankPaymentId] IS NULL AND [FinanceAllocationId] IS NULL AND [CustomerInstrumentId] IS NULL AND JSON_VALUE([ResultJson], '$.outcome') = 'ValidationFailed')";

    // AddFinanceAllocations' rule, restored by Down.
    private const string PreviousTargetCheck =
        "([DraftId] IS NOT NULL AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL AND [ManualBankPaymentId] IS NULL AND [FinanceAllocationId] IS NULL) OR "
        + "([BankAccountId] IS NOT NULL AND [BankAccountId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [ReversalProposalId] IS NULL AND [ManualBankPaymentId] IS NULL AND [FinanceAllocationId] IS NULL) OR "
        + "([ReversalProposalId] IS NOT NULL AND [ReversalProposalId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [BankAccountId] IS NULL AND [ManualBankPaymentId] IS NULL AND [FinanceAllocationId] IS NULL) OR "
        + "([ManualBankPaymentId] IS NOT NULL AND [ManualBankPaymentId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL AND [FinanceAllocationId] IS NULL) OR "
        + "([FinanceAllocationId] IS NOT NULL AND [FinanceAllocationId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL AND [ManualBankPaymentId] IS NULL) OR "
        + "([DraftId] IS NULL AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL AND [ManualBankPaymentId] IS NULL AND [FinanceAllocationId] IS NULL AND JSON_VALUE([ResultJson], '$.outcome') = 'ValidationFailed')";

    private const string StateCheck =
        "([Status] IN (1, 6) AND [TreasuryAccountId] IS NULL AND [CollectionSourceId] IS NULL) OR "
        + "([Status] = 2 AND [TreasuryAccountId] IS NOT NULL AND [CollectionSourceId] IS NULL) OR "
        + "([Status] = 3 AND [TreasuryAccountId] IS NOT NULL AND [CollectionSourceId] IS NOT NULL) OR "
        + "([Status] = 4 AND [Kind] = 1 AND [TreasuryAccountId] IS NULL AND [CollectionSourceId] IS NULL) OR "
        + "([Status] = 5 AND [Kind] = 2 AND [TreasuryAccountId] IS NULL AND [CollectionSourceId] IS NULL)";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CustomerInstruments", schema: "finance",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SerialNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Drawer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                BankName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                BranchName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                ReceivedOn = table.Column<DateOnly>(type: "date", nullable: false),
                CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                ProfileId = table.Column<string>(type: "varchar(36)", unicode: false, maxLength: 36, nullable: false),
                ProfileVersion = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                TreasuryAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                StatusChangedOn = table.Column<DateOnly>(type: "date", nullable: false),
                CollectionSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ReceivedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false,
                    collation: "Latin1_General_100_BIN2"),
                ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false,
                    collation: "Latin1_General_100_BIN2"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CustomerInstruments", x => x.Id);
                table.UniqueConstraint("AK_CustomerInstruments_CompanyId_Id", x => new { x.CompanyId, x.Id });
                table.CheckConstraint("CK_CustomerInstruments_Bank",
                    "[Kind] = 2 OR ([BankName] IS NOT NULL AND LEN([BankName]) > 0)");
                table.CheckConstraint("CK_CustomerInstruments_Identity",
                    "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PartyId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_CustomerInstruments_Kind", "[Kind] IN (1, 2)");
                table.CheckConstraint("CK_CustomerInstruments_State", StateCheck);
                table.CheckConstraint("CK_CustomerInstruments_Text",
                    "LEN([SerialNumber]) > 0 AND LEN([Drawer]) > 0 AND LEN([AmountText]) > 0 AND LEN([ProfileId]) > 0 AND LEN([ProfileVersion]) > 0 AND LEN([ReceivedBy]) > 0 AND LEN([UpdatedBy]) > 0");
                table.CheckConstraint("CK_CustomerInstruments_Version", "[Version] >= 1");
                table.ForeignKey("FK_CustomerInstruments_Companies_CompanyId", x => x.CompanyId,
                    principalSchema: "organization", principalTable: "Companies", principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_CustomerInstruments_ReceiptDrafts_CompanyId_CollectionSourceId",
                    x => new { x.CompanyId, x.CollectionSourceId }, principalSchema: "finance",
                    principalTable: "ReceiptDrafts", principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        // Every row: a note's NULL bank counts as one value, so a note is covered too. The filter on the non-nullable
        // CompanyId is true for every row; it is explicit only so the generator does not add its legacy
        // "[BankName] IS NOT NULL" filter (this migration has no target model), which would exempt every note.
        migrationBuilder.CreateIndex("UX_CustomerInstruments_Identity", "CustomerInstruments",
            new[] { "CompanyId", "Kind", "SerialNumber", "Drawer", "BankName" }, schema: "finance", unique: true,
            filter: "[CompanyId] IS NOT NULL");
        migrationBuilder.CreateIndex("UX_CustomerInstruments_CollectionSource", "CustomerInstruments",
            new[] { "CompanyId", "CollectionSourceId" }, schema: "finance", unique: true,
            filter: "[CollectionSourceId] IS NOT NULL");
        migrationBuilder.CreateIndex("IX_CustomerInstruments_CompanyId_ReceivedAtUtc_Id", "CustomerInstruments",
            new[] { "CompanyId", "ReceivedAtUtc", "Id" }, schema: "finance");
        migrationBuilder.CreateIndex("IX_CustomerInstruments_CompanyId_Status_DueOn", "CustomerInstruments",
            new[] { "CompanyId", "Status", "DueOn" }, schema: "finance");

        migrationBuilder.CreateTable(
            name: "CustomerInstrumentMovements", schema: "finance",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                InstrumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                FromStatus = table.Column<int>(type: "int", nullable: true),
                ToStatus = table.Column<int>(type: "int", nullable: false),
                OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                TreasuryAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CollectionSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false,
                    collation: "Latin1_General_100_BIN2"),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CustomerInstrumentMovements", x => x.Id);
                table.CheckConstraint("CK_CustomerInstrumentMovements_Identity",
                    "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [InstrumentId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_CustomerInstrumentMovements_Status",
                    "[ToStatus] IN (1, 2, 3, 4, 5, 6) AND ([FromStatus] IS NULL OR [FromStatus] IN (1, 2, 3, 4, 5, 6))");
                table.CheckConstraint("CK_CustomerInstrumentMovements_Text",
                    "LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
                table.CheckConstraint("CK_CustomerInstrumentMovements_Version", "[Version] >= 1");
                table.ForeignKey("FK_CustomerInstrumentMovements_Companies_CompanyId", x => x.CompanyId,
                    principalSchema: "organization", principalTable: "Companies", principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_CustomerInstrumentMovements_CustomerInstruments_CompanyId_InstrumentId",
                    x => new { x.CompanyId, x.InstrumentId }, principalSchema: "finance",
                    principalTable: "CustomerInstruments", principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_CustomerInstrumentMovements_CompanyId_InstrumentId_Version",
            "CustomerInstrumentMovements", new[] { "CompanyId", "InstrumentId", "Version" }, schema: "finance",
            unique: true);

        migrationBuilder.DropCheckConstraint(name: "CK_MoneyEvents_Kind", schema: "finance", table: "MoneyEvents");
        migrationBuilder.AddCheckConstraint(name: "CK_MoneyEvents_Kind", schema: "finance", table: "MoneyEvents",
            sql: "[Kind] IN (1, 2, 3)");

        migrationBuilder.DropCheckConstraint(name: "CK_Commands_Target", schema: "finance", table: "Commands");
        migrationBuilder.AddColumn<Guid>(
            name: "CustomerInstrumentId", schema: "finance", table: "Commands",
            type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddCheckConstraint(name: "CK_Commands_Target", schema: "finance", table: "Commands",
            sql: TargetCheck);
        migrationBuilder.CreateIndex(
            name: "IX_Commands_CompanyId_CustomerInstrumentId", schema: "finance", table: "Commands",
            columns: new[] { "CompanyId", "CustomerInstrumentId" });
        migrationBuilder.AddForeignKey(
            name: "FK_Commands_CustomerInstruments_CompanyId_CustomerInstrumentId", schema: "finance", table: "Commands",
            columns: new[] { "CompanyId", "CustomerInstrumentId" }, principalSchema: "finance",
            principalTable: "CustomerInstruments", principalColumns: new[] { "CompanyId", "Id" },
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Finance's module refusal (51051): a rollback never drops a customer's cheque, its history or its collection.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [finance].[CustomerInstruments])
               OR EXISTS (SELECT 1 FROM [finance].[CustomerInstrumentMovements])
               OR EXISTS (SELECT 1 FROM [finance].[Commands] WHERE [CustomerInstrumentId] IS NOT NULL)
               OR EXISTS (SELECT 1 FROM [finance].[MoneyEvents] WHERE [Kind] = 3)
                THROW 51051, 'Finance customer instruments exist; AddCustomerInstruments is not rolled back (forward fix).', 1;
            """);

        migrationBuilder.DropForeignKey(
            name: "FK_Commands_CustomerInstruments_CompanyId_CustomerInstrumentId", schema: "finance", table: "Commands");
        migrationBuilder.DropIndex(
            name: "IX_Commands_CompanyId_CustomerInstrumentId", schema: "finance", table: "Commands");
        migrationBuilder.DropCheckConstraint(name: "CK_Commands_Target", schema: "finance", table: "Commands");
        migrationBuilder.DropColumn(name: "CustomerInstrumentId", schema: "finance", table: "Commands");
        migrationBuilder.AddCheckConstraint(name: "CK_Commands_Target", schema: "finance", table: "Commands",
            sql: PreviousTargetCheck);

        migrationBuilder.DropCheckConstraint(name: "CK_MoneyEvents_Kind", schema: "finance", table: "MoneyEvents");
        migrationBuilder.AddCheckConstraint(name: "CK_MoneyEvents_Kind", schema: "finance", table: "MoneyEvents",
            sql: "[Kind] IN (1, 2)");

        migrationBuilder.DropTable(name: "CustomerInstrumentMovements", schema: "finance");
        migrationBuilder.DropTable(name: "CustomerInstruments", schema: "finance");
    }
}
