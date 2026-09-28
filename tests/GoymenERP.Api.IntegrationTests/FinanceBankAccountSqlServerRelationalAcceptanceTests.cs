using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// FS0 against a REAL SQL Server: the Finance bank account master and its authorized reader, on
/// Finance's own migrations over the host's migrated and seeded database, with Audit's real save
/// interceptor.
/// <para>
/// Permission and the Settings currency candidates are ISOLATED SYNTHETIC ports: neither a Finance
/// grant nor a Settings currency source exists on main, so these tests prove the store and its
/// rules, not a user outcome. The real unavailable Settings adapter refuses every proposal
/// (<see cref="A_currency_outside_the_settings_candidates_or_an_unavailable_source_stores_nothing"/>).
/// Every identity and currency below is SENTETIK / NOT_ENV test data.
/// </para>
/// </summary>
public sealed class FinanceBankAccountSqlServerRelationalAcceptanceTests
{
    private const string Operation = "SENTETIK-operation";
    private const string Proposer = "SENTETIK-proposer";
    private const string Approver = "SENTETIK-approver";
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Currency = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a91");
    private static readonly Guid ProfileId = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a92");

    [SqlServerFact]
    public async Task A_proposal_is_not_usable_until_another_user_approves_it_once_and_moves_no_money()
    {
        await using var run = await Run.StartAsync();

        BankAccountView proposed;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            var result = await a.Accounts.ProposeAsync(a.Context, Key("p-1"), new BankAccountProposeRequest("SENTETIK hesap", Currency, "SENTETIK Banka", "SENTETIK tür",
                    // B3: this case is about APPROVAL making a card usable, so the card must be one that
                    // approval CAN make usable — it declares itself for money movement and therefore
                    // carries an IBAN (K2). A card declaring false stays unusable however it is approved,
                    // and that is the separate case below.
                    MoneyMovementEligible: true, Iban: "TR33 0006 1005 1978 6457 8413 26"),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            proposed = result.Resource!;
            Assert.Equal((false, false, "1"), (proposed.IsActive, proposed.IsUsableForReceipt, proposed.Version));

            // Listed for the approver to see, but a draft against a proposal is refused by the real reader.
            Assert.Equal(proposed.Id, Assert.Single((await a.Accounts.ListAuthorizedAsync(a.Context, CancellationToken.None)).Resource!.Items).Id);
            var draft = await a.Drafts.DraftSaveAsync(a.Context, Key("d-1"), Draft(proposed.Id), CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, draft.Outcome);
            Assert.Equal("bank_account_ineligible", Assert.Single(draft.Issues).Code);

            // Ö4: the proposer cannot approve their own proposal.
            var self = await a.Accounts.ApproveAsync(a.Context, Key("a-self"), new BankAccountApproveRequest(proposed.Id, "1"),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Denied, self.Outcome);
            Assert.Equal("finance_approver_is_proposer", Assert.Single(self.Issues).Code);
        }

        await using (var b = run.Open(CompanyA, Approver))
        {
            var approved = await b.Accounts.ApproveAsync(b.Context, Key("a-1"), new BankAccountApproveRequest(proposed.Id, "1"),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
            Assert.Equal((true, true, "2"), (approved.Resource!.IsActive, approved.Resource.IsUsableForReceipt, approved.Resource.Version));

            // One approval only: a second is refused, whatever version it names.
            Assert.Equal(CommandOutcome.ValidationFailed, (await b.Accounts.ApproveAsync(b.Context, Key("a-2"),
                new BankAccountApproveRequest(proposed.Id, "2"), CancellationToken.None)).Outcome);
            Assert.Equal(CommandOutcome.VersionConflict, (await b.Accounts.ApproveAsync(b.Context, Key("a-3"),
                new BankAccountApproveRequest(proposed.Id, "1"), CancellationToken.None)).Outcome);
        }

        await using (var a = run.Open(CompanyA, Proposer))
        {
            // The same account is now usable: the real reader answers it and the draft saves against it.
            var read = await a.Accounts.GetAuthorizedAsync(a.Context, proposed.Id, CancellationToken.None);
            Assert.Equal((ReadOutcome.Found, true), (read.Outcome, read.Resource!.IsUsableForReceipt));
            Assert.Equal(CommandOutcome.Succeeded,
                (await a.Drafts.DraftSaveAsync(a.Context, Key("d-2"), Draft(proposed.Id), CancellationToken.None)).Outcome);
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT Status FROM finance.BankAccounts WHERE Id = @id", ("@id", proposed.Id)));
        Assert.Equal(Approver, await run.ScalarAsync<string>("SELECT ApprovedBy FROM finance.BankAccounts WHERE Id = @id", ("@id", proposed.Id)));
        // Proposal, approval, the stale approval (a terminal refusal, ADR-011 §7) and the second draft: four
        // command records; the other refusals (Ö4, not proposed, the ineligible draft) store none.
        Assert.Equal(4, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands"));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands WHERE BankAccountId IS NOT NULL"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.BankAccount' AND CorrelationId = @c", ("@c", run.Correlation)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.bank-account.approved.v1' AND ApprovalContextJson IS NOT NULL"));
        // Money effect 0. FIN-T05a gave the schema a MoneyEvents table, so listing the tables no longer says
        // "no money was moved" by itself — the table now exists whether or not anything is in it. The inventory
        // is kept (a new Finance table must be a deliberate edit here) and the claim it used to carry is stated
        // directly underneath: approving a bank account writes NO money event.
        Assert.Equal("Allocations,BankAccounts,Commands,CustomerInstrumentMovements,CustomerInstruments,ManualBankPayments,MoneyEvents,OpenItemSourceBatches,OpenItems,OutgoingMoneyEvents,ReceiptDrafts,ReversalProposals,SourceAllocationStates,__EFMigrationsHistory", await run.ScalarAsync<string>(
            "SELECT STRING_AGG(name, ',') WITHIN GROUP (ORDER BY name COLLATE Latin1_General_BIN2) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'finance')"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
    }

    /// <summary>
    /// K2 (Sinan #66 5748258299) against real SQL: what the CARD actually holds after a proposal.
    /// <para>
    /// The IBAN is typed the way a person types it — in groups of four — and the claim is that the
    /// database holds the NORMALISED form. That claim is only meaningful here: a unit test could not
    /// tell normalisation-on-write from normalisation-on-read, and the spaced input is the witness that
    /// separates them, because a read-path normalisation would leave the spacing in the column.
    /// </para>
    /// <para>
    /// The second account is K2's other half: "IBAN'sız özel kredi/vadeli hesapların tanımı
    /// desteklenir". It is stored with a NULL IBAN rather than an empty string, so "no IBAN was given"
    /// and "an IBAN was given as blank" stay different facts in the column.
    /// </para>
    /// </summary>
    [SqlServerFact]
    public async Task A_card_keeps_what_was_typed_with_its_IBAN_normalised_and_an_IBAN_less_card_is_storable()
    {
        await using var run = await Run.StartAsync();

        BankAccountView withIban;
        BankAccountView withoutIban;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            var typed = await a.Accounts.ProposeAsync(a.Context, Key("p-iban"),
                new BankAccountProposeRequest("SENTETIK hesap", Currency, "SENTETIK Banka", "SENTETIK tür",
                    MoneyMovementEligible: true,
                    BranchName: "SENTETIK şube", BranchCode: "0001",
                    Iban: "TR33 0006 1005 1978 6457 8413 26", AccountNumber: "123456"),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, typed.Outcome);
            withIban = typed.Resource!;

            // K2: a special credit or term account with no IBAN is definable. It must not be refused.
            var special = await a.Accounts.ProposeAsync(a.Context, Key("p-no-iban"),
                new BankAccountProposeRequest("SENTETIK vadeli", Currency, "SENTETIK Banka", "SENTETIK özel tür", false),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, special.Outcome);
            withoutIban = special.Resource!;
        }

        // Stored normalised: no spacing, upper case — the value validated is the value kept.
        Assert.Equal("TR330006100519786457841326", await run.ScalarAsync<string>(
            "SELECT Iban FROM finance.BankAccounts WHERE Id = @id", ("@id", withIban.Id)));
        Assert.Equal(("SENTETIK Banka", "SENTETIK tür"), (
            await run.ScalarAsync<string>("SELECT BankName FROM finance.BankAccounts WHERE Id = @id", ("@id", withIban.Id)),
            await run.ScalarAsync<string>("SELECT AccountType FROM finance.BankAccounts WHERE Id = @id", ("@id", withIban.Id))));
        Assert.Equal(("SENTETIK şube", "0001", "123456"), (
            await run.ScalarAsync<string>("SELECT BranchName FROM finance.BankAccounts WHERE Id = @id", ("@id", withIban.Id)),
            await run.ScalarAsync<string>("SELECT BranchCode FROM finance.BankAccounts WHERE Id = @id", ("@id", withIban.Id)),
            await run.ScalarAsync<string>("SELECT AccountNumber FROM finance.BankAccounts WHERE Id = @id", ("@id", withIban.Id))));

        // An absent IBAN is NULL, not "": the two are different facts and the column keeps them apart.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.BankAccounts WHERE Id = @id AND Iban IS NULL AND BranchName IS NULL",
            ("@id", withoutIban.Id)));

        // The card moves no money and this slice touches no usability: both accounts are still PROPOSED.
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.BankAccounts WHERE Status = 1"));

        // And the card reaches its reader on the MASTER, which is the only place it travels.
        await using (var a = run.Open(CompanyA, Proposer))
        {
            // The MASTER read is the one that carries the card; ListAuthorizedAsync answers plain views,
            // which is exactly the separation this slice relies on.
            var page = await a.Accounts.ListMasterAsync(a.Context, CancellationToken.None);
            var card = Assert.Single(page.Resource!.Items, item => item.Account.Id == withIban.Id).Card;
            Assert.Equal(("SENTETIK Banka", "SENTETIK tür", "TR330006100519786457841326"),
                (card.BankName, card.AccountType, card.Iban));
        }
    }

    /// <summary>
    /// K2's leak clause, as its own case rather than an aside: "yetkisiz kişi/Sevkiyatçıya IBAN/para
    /// verisi sızmaması". An actor the permission port refuses receives no account at all — so no IBAN
    /// — and another company's actor cannot see the card either, which the tenant filter decides in the
    /// database rather than in the projection.
    /// <para>
    /// The IBAN really is stored, read straight from the column here, so the case cannot pass merely
    /// because nothing was written: the value exists and still does not reach either caller.
    /// </para>
    /// </summary>
    [SqlServerFact]
    public async Task Without_the_bank_account_permission_and_from_another_company_no_card_and_no_IBAN_is_answered()
    {
        await using var run = await Run.StartAsync();

        BankAccountView account;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            account = (await a.Accounts.ProposeAsync(a.Context, Key("p-1"),
                new BankAccountProposeRequest("SENTETIK hesap", Currency, "SENTETIK Banka", "SENTETIK tür",
                    MoneyMovementEligible: true, Iban: "TR330006100519786457841326"),
                CancellationToken.None)).Resource!;
        }

        // Written: the value the two callers below must NOT receive is really in the database.
        Assert.Equal("TR330006100519786457841326", await run.ScalarAsync<string>(
            "SELECT Iban FROM finance.BankAccounts WHERE Id = @id", ("@id", account.Id)));

        // (1) The same company, but the action is refused — a Sevkiyatçı holds no Finance read name.
        await using (var denied = run.OpenRefused(CompanyA, "SENTETIK-sevkiyatci"))
        {
            var master = await denied.Accounts.ListMasterAsync(denied.Context, CancellationToken.None);
            Assert.Equal(ReadOutcome.Denied, master.Outcome);
            Assert.Null(master.Resource);

            var page = await denied.Accounts.ListAuthorizedAsync(denied.Context, CancellationToken.None);
            Assert.Equal(ReadOutcome.Denied, page.Outcome);
            Assert.Null(page.Resource);

            var byId = await denied.Accounts.GetAuthorizedAsync(denied.Context, account.Id, CancellationToken.None);
            Assert.Equal(ReadOutcome.Denied, byId.Outcome);
            Assert.Null(byId.Resource);
        }

        // (2) Another company, permitted for ITS own company — the row is not theirs to see.
        await using (var b = run.Open(CompanyB, Approver))
        {
            var master = await b.Accounts.ListMasterAsync(b.Context, CancellationToken.None);
            Assert.Equal(ReadOutcome.Found, master.Outcome);
            Assert.Empty(master.Resource!.Items);

            var page = await b.Accounts.ListAuthorizedAsync(b.Context, CancellationToken.None);
            Assert.Equal(ReadOutcome.Found, page.Outcome);
            Assert.Empty(page.Resource!.Items);
            Assert.Equal(ReadOutcome.NotFound,
                (await b.Accounts.GetAuthorizedAsync(b.Context, account.Id, CancellationToken.None)).Outcome);
        }
    }

    [SqlServerFact]
    public async Task A_legacy_NULL_money_declaration_is_visible_as_undeclared_and_is_refused_on_the_money_path()
    {
        await using var run = await Run.StartAsync();
        BankAccountView account;
        await using (var proposer = run.Open(CompanyA, Proposer))
        {
            account = (await proposer.Accounts.ProposeAsync(proposer.Context, Key("legacy-null-propose"),
                new BankAccountProposeRequest("SENTETIK legacy", Currency, "SENTETIK Banka", "SENTETIK tur",
                    MoneyMovementEligible: true, Iban: "TR330006100519786457841326"), CancellationToken.None)).Resource!;
        }

        await using (var approver = run.Open(CompanyA, Approver))
        {
            account = (await approver.Accounts.ApproveAsync(approver.Context, Key("legacy-null-approve"),
                new BankAccountApproveRequest(account.Id, "1"), CancellationToken.None)).Resource!;
        }

        // Represents a row written before B3. No invented true/false backfill is allowed.
        await run.ExecuteAsync("UPDATE finance.BankAccounts SET MoneyMovementEligible = NULL WHERE Id = @id", ("@id", account.Id));

        await using (var reader = run.Open(CompanyA, Proposer))
        {
            var item = Assert.Single((await reader.Accounts.ListMasterAsync(reader.Context, CancellationToken.None)).Resource!.Items);
            Assert.Equal(BankAccountMoneyEligibility.Undeclared, item.Card.MoneyEligibility);
            Assert.False(item.Account.IsUsableForReceipt);

            var draft = await reader.Drafts.DraftSaveAsync(reader.Context, Key("legacy-null-draft"), Draft(account.Id),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, draft.Outcome);
            Assert.Equal("bank_account_ineligible", Assert.Single(draft.Issues).Code);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReceiptDrafts"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.BankAccounts WHERE Id = @id AND MoneyMovementEligible IS NULL", ("@id", account.Id)));
    }

    [SqlServerFact]
    public async Task A_pending_IBAN_never_replaces_the_approved_value_and_only_another_actor_can_approve_it()
    {
        await using var run = await Run.StartAsync();
        const string oldIban = "TR330006100519786457841326";
        const string newIban = "TR060006100519786457841327";
        BankAccountView account;

        await using (var proposer = run.Open(CompanyA, Proposer))
        {
            account = (await proposer.Accounts.ProposeAsync(proposer.Context, Key("iban-card-propose"),
                new BankAccountProposeRequest("SENTETIK IBAN", Currency, "SENTETIK Banka", "SENTETIK tur",
                    MoneyMovementEligible: true, Iban: oldIban), CancellationToken.None)).Resource!;
        }
        await using (var approver = run.Open(CompanyA, Approver))
        {
            account = (await approver.Accounts.ApproveAsync(approver.Context, Key("iban-card-approve"),
                new BankAccountApproveRequest(account.Id, "1"), CancellationToken.None)).Resource!;
        }

        Guid historicalDraftId;
        await using (var proposer = run.Open(CompanyA, Proposer))
        {
            var draft = await proposer.Drafts.DraftSaveAsync(proposer.Context, Key("iban-historical-draft"), Draft(account.Id),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, draft.Outcome);
            historicalDraftId = draft.Resource!.Source.Id;

            var pending = await proposer.Accounts.ProposeIbanChangeAsync(proposer.Context, Key("iban-change-propose"),
                new BankAccountIbanChangeProposeRequest(account.Id, "2", newIban, "SENTETIK banka degisikligi"),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, pending.Outcome);
            Assert.Equal("3", pending.Resource!.Version);

            // D4's single waiting slot keeps the newest proposal. Replacing its reason exercises the
            // complete-pending -> complete-pending persistence transition without moving the approved
            // IBAN that the money path still uses.
            var replaced = await proposer.Accounts.ProposeIbanChangeAsync(proposer.Context, Key("iban-change-replace"),
                new BankAccountIbanChangeProposeRequest(account.Id, "3", newIban, "SENTETIK guncel gerekce"),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, replaced.Outcome);
            Assert.Equal("4", replaced.Resource!.Version);

            var self = await proposer.Accounts.ApproveIbanChangeAsync(proposer.Context, Key("iban-change-self"),
                new BankAccountIbanChangeApproveRequest(account.Id, "4"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Denied, self.Outcome);
            Assert.Equal("finance_approver_is_proposer", Assert.Single(self.Issues).Code);
        }

        Assert.Equal(oldIban, await run.ScalarAsync<string>(
            "SELECT Iban FROM finance.BankAccounts WHERE Id = @id", ("@id", account.Id)));
        Assert.Equal(newIban, await run.ScalarAsync<string>(
            "SELECT PendingIban FROM finance.BankAccounts WHERE Id = @id", ("@id", account.Id)));
        Assert.Equal("SENTETIK guncel gerekce", await run.ScalarAsync<string>(
            "SELECT PendingIbanReason FROM finance.BankAccounts WHERE Id = @id", ("@id", account.Id)));
        Assert.Equal(account.Id, await run.ScalarAsync<Guid>(
            "SELECT TreasuryAccountId FROM finance.ReceiptDrafts WHERE Id = @id", ("@id", historicalDraftId)));

        await using (var approver = run.Open(CompanyA, Approver))
        {
            var approved = await approver.Accounts.ApproveIbanChangeAsync(approver.Context, Key("iban-change-approve"),
                new BankAccountIbanChangeApproveRequest(account.Id, "4"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
            Assert.Equal("5", approved.Resource!.Version);
        }

        Assert.Equal(newIban, await run.ScalarAsync<string>(
            "SELECT Iban FROM finance.BankAccounts WHERE Id = @id", ("@id", account.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.BankAccounts WHERE Id = @id AND PendingIban IS NULL AND PendingIbanProposedBy IS NULL AND PendingIbanProposedAtUtc IS NULL AND PendingIbanReason IS NULL",
            ("@id", account.Id)));
        Assert.Equal(account.Id, await run.ScalarAsync<Guid>(
            "SELECT TreasuryAccountId FROM finance.ReceiptDrafts WHERE Id = @id", ("@id", historicalDraftId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = CONVERT(nvarchar(36), @id) AND EventType = N'finance.bank-account.iban-change-approved.v1' AND JSON_VALUE(ApprovalContextJson, '$.previousIban') = @old AND JSON_VALUE(ApprovalContextJson, '$.newIban') = @new AND JSON_VALUE(ApprovalContextJson, '$.proposedBy') = @proposer AND JSON_VALUE(ApprovalContextJson, '$.approvedBy') = @approver",
            ("@id", account.Id), ("@old", oldIban), ("@new", newIban), ("@proposer", Proposer), ("@approver", Approver)));
    }

    [SqlServerFact]
    public async Task Permission_revoked_during_IBAN_reads_refuses_propose_and_approve_before_save()
    {
        await using var run = await Run.StartAsync();
        const string oldIban = "TR330006100519786457841326";
        const string newIban = "TR060006100519786457841327";
        BankAccountView account;

        await using (var proposer = run.Open(CompanyA, Proposer))
        {
            account = (await proposer.Accounts.ProposeAsync(proposer.Context, Key("iban-fence-card-propose"),
                new BankAccountProposeRequest("SENTETIK IBAN FENCE", Currency, "SENTETIK Banka", "SENTETIK tur",
                    MoneyMovementEligible: true, Iban: oldIban), CancellationToken.None)).Resource!;
        }
        await using (var approver = run.Open(CompanyA, Approver))
        {
            account = (await approver.Accounts.ApproveAsync(approver.Context, Key("iban-fence-card-approve"),
                new BankAccountApproveRequest(account.Id, "1"), CancellationToken.None)).Resource!;
        }

        var commandsBeforePropose = await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands");
        var auditsBeforePropose = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        await using (var proposer = run.OpenWithPermissions(CompanyA, Proposer, new AllowInitialDecisionOnlyPermissions()))
        {
            var refused = await proposer.Accounts.ProposeIbanChangeAsync(proposer.Context, Key("iban-fence-propose"),
                new BankAccountIbanChangeProposeRequest(account.Id, "2", newIban, "SENTETIK revoke"),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Denied, refused.Outcome);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.BankAccounts WHERE Id = @id AND Version = 2 AND Iban = @old AND PendingIban IS NULL",
            ("@id", account.Id), ("@old", oldIban)));
        Assert.Equal(commandsBeforePropose, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands"));
        Assert.Equal(auditsBeforePropose, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));

        await using (var proposer = run.Open(CompanyA, Proposer))
        {
            var pending = await proposer.Accounts.ProposeIbanChangeAsync(proposer.Context, Key("iban-fence-pending"),
                new BankAccountIbanChangeProposeRequest(account.Id, "2", newIban, "SENTETIK pending"),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, pending.Outcome);
        }

        var commandsBeforeApprove = await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands");
        var auditsBeforeApprove = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        await using (var approver = run.OpenWithPermissions(CompanyA, Approver, new AllowInitialDecisionOnlyPermissions()))
        {
            var refused = await approver.Accounts.ApproveIbanChangeAsync(approver.Context, Key("iban-fence-approve"),
                new BankAccountIbanChangeApproveRequest(account.Id, "3"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Denied, refused.Outcome);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.BankAccounts WHERE Id = @id AND Version = 3 AND Iban = @old AND PendingIban = @new",
            ("@id", account.Id), ("@old", oldIban), ("@new", newIban)));
        Assert.Equal(commandsBeforeApprove, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands"));
        Assert.Equal(auditsBeforeApprove, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
    }

    [SqlServerFact]
    public async Task Company_B_can_neither_see_nor_approve_company_A_account()
    {
        await using var run = await Run.StartAsync();
        BankAccountView accountA;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            accountA = (await a.Accounts.ProposeAsync(a.Context, Key("p-1"), new BankAccountProposeRequest("SENTETIK A", Currency, "SENTETIK Banka", "SENTETIK tür", false),
                CancellationToken.None)).Resource!;
        }

        await using (var b = run.Open(CompanyB, Approver))
        {
            Assert.Equal(ReadOutcome.NotFound, (await b.Accounts.GetAuthorizedAsync(b.Context, accountA.Id, CancellationToken.None)).Outcome);
            Assert.Empty((await b.Accounts.ListAuthorizedAsync(b.Context, CancellationToken.None)).Resource!.Items);
            var approve = await b.Accounts.ApproveAsync(b.Context, Key("a-1"), new BankAccountApproveRequest(accountA.Id, "1"),
                CancellationToken.None);
            Assert.Equal("bank_account_not_found", Assert.Single(approve.Issues).Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT Status FROM finance.BankAccounts WHERE Id = @id", ("@id", accountA.Id)));
        // A's proposal, and B's not-found as B's key's terminal answer (§7): company B's, with no target.
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE CompanyId = @b AND BankAccountId IS NULL AND DraftId IS NULL", ("@b", CompanyB)));
    }

    [SqlServerFact]
    public async Task The_same_key_replays_and_another_payload_or_actor_conflicts()
    {
        await using var run = await Run.StartAsync();
        BankAccountView first;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            var request = new BankAccountProposeRequest("SENTETIK hesap", Currency, "SENTETIK Banka", "SENTETIK tür", false);
            first = (await a.Accounts.ProposeAsync(a.Context, Key("k-1"), request, CancellationToken.None)).Resource!;
            var replay = await a.Accounts.ProposeAsync(a.Context, Key("k-1"), request, CancellationToken.None);
            Assert.True(replay.IsReplay);
            Assert.Equal(first.Id, replay.Resource!.Id);
            Assert.Equal(CommandOutcome.IdempotencyConflict, (await a.Accounts.ProposeAsync(a.Context, Key("k-1"),
                request with { DisplayName = "SENTETIK başka" }, CancellationToken.None)).Outcome);
            // ADR-011 §2: the key belongs to its command. The same key on another command is that
            // command's own attempt: here the draft is refused on its merits (a proposed account is not
            // usable), not as a replay or a conflict of the proposal.
            var draft = await a.Drafts.DraftSaveAsync(a.Context, Key("k-1"), Draft(first.Id), CancellationToken.None);
            Assert.Equal("bank_account_ineligible", Assert.Single(draft.Issues).Code);
        }

        await using (var other = run.Open(CompanyA, Approver))
        {
            // ADR-011 §3: the same key and payload by ANOTHER actor is a conflict that discloses nothing.
            var conflict = await other.Accounts.ProposeAsync(other.Context, Key("k-1"),
                new BankAccountProposeRequest("SENTETIK hesap", Currency, "SENTETIK Banka", "SENTETIK tür", false), CancellationToken.None);
            Assert.Equal(CommandOutcome.IdempotencyConflict, conflict.Outcome);
            Assert.Null(conflict.Resource);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.BankAccounts"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReceiptDrafts"));
    }

    [SqlServerFact]
    public async Task A_currency_outside_the_settings_candidates_or_an_unavailable_source_stores_nothing()
    {
        await using var run = await Run.StartAsync();
        await using (var a = run.Open(CompanyA, Proposer))
        {
            var outside = await a.Accounts.ProposeAsync(a.Context, Key("k-1"), new BankAccountProposeRequest("SENTETIK", Guid.NewGuid(), "SENTETIK Banka", "SENTETIK tür", false),
                CancellationToken.None);
            Assert.Equal("currency_not_a_candidate", Assert.Single(outside.Issues).Code);
        }

        // The real adapter on main: no Settings currency source exists, so every proposal is refused.
        await using (var a = run.Open(CompanyA, Proposer, realSettings: true))
        {
            var unavailable = await a.Accounts.ProposeAsync(a.Context, Key("k-2"), new BankAccountProposeRequest("SENTETIK", Currency, "SENTETIK Banka", "SENTETIK tür", false),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Unavailable, unavailable.Outcome);
            Assert.Equal("settings_provider_unavailable", Assert.Single(unavailable.Issues).Code);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.BankAccounts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.BankAccount'"));
    }

    [SqlServerFact]
    public async Task Two_racing_approvals_store_one_and_the_database_refuses_a_self_approval()
    {
        await using var run = await Run.StartAsync();
        BankAccountView proposed;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            proposed = (await a.Accounts.ProposeAsync(a.Context, Key("p-1"), new BankAccountProposeRequest("SENTETIK", Currency, "SENTETIK Banka", "SENTETIK tür", false),
                CancellationToken.None)).Resource!;
        }

        // Two approvers read the same proposal, then wait for each other at the save.
        var barrier = new Barrier(2);
        async Task<CommandOutcome> ApproveAsync(string approver)
        {
            await using var scope = run.Open(CompanyA, approver, new BarrierInterceptor(barrier));
            return (await scope.Accounts.ApproveAsync(scope.Context, Key("a-" + approver),
                new BankAccountApproveRequest(proposed.Id, "1"), CancellationToken.None)).Outcome;
        }

        var results = await Task.WhenAll(Task.Run(() => ApproveAsync(Approver)), Task.Run(() => ApproveAsync("SENTETIK-approver-2")));
        Assert.Single(results, outcome => outcome == CommandOutcome.Succeeded);
        Assert.Single(results, outcome => outcome == CommandOutcome.VersionConflict);
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM finance.BankAccounts WHERE Id = @id", ("@id", proposed.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.bank-account.approved.v1'"));

        // Without the service: the database itself refuses an approval by the proposer (547).
        var error = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO finance.BankAccounts (Id, CompanyId, CurrencyId, DisplayName, Status, Version, ProposedBy, ProposedAtUtc, "
            + "ApprovedBy, ApprovedAtUtc, UpdatedAtUtc) VALUES (NEWID(), @c, @cur, N'SENTETIK', 2, 2, N'x', SYSDATETIMEOFFSET(), "
            + "N'x', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())", ("@c", CompanyA), ("@cur", Currency)));
        Assert.Equal(547, error.Number);
        Assert.Contains("CK_BankAccounts_Approval", error.Message, StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task A_save_that_fails_in_the_database_leaves_nothing_behind()
    {
        await using var run = await Run.StartAsync();
        // A company Organization does not know: the foreign key refuses the save (547), and the
        // account, the command record and the audit event roll back together.
        var unknownCompany = Guid.NewGuid();
        await using (var scope = run.Open(unknownCompany, Proposer))
        {
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => scope.Accounts.ProposeAsync(scope.Context, Key("k-1"),
                new BankAccountProposeRequest("SENTETIK", Currency, "SENTETIK Banka", "SENTETIK tür", false), CancellationToken.None));
            Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.BankAccounts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.BankAccount'"));
    }

    private static CommandMetadata Key(string key) => new CommandMetadata(key);

    private static DraftSaveRequest Draft(Guid bankAccount) =>
        new DraftSaveRequest(null, null, new ReceiptPreparation(
            Parties.Party, Parties.Account, bankAccount, new MoneyText("1000", Currency),
            new ProfileReference(ProfileId.ToString("D"), "1"), new DateOnly(2026, 9, 13), null, null, null));

    private static class Parties
    {
        public static readonly Guid Party = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a93");
        public static readonly Guid Account = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a94");
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public string Correlation { get; } = $"fs0-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            // The host migrates and seeds Organization, Audit and the rest; Finance's own migrations
            // run after it, as their foreign keys need Organization's tables.
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            await using var context = new FinanceDbContext(Options(factory), companyContext);
            await context.Database.MigrateAsync();
            return run;
        }

        public Scope Open(Guid company, string actor, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] extra) =>
            Open(company, actor, realSettings: false, extra);

        /// <summary>
        /// The same scope with an action the permission port REFUSES — a caller holding no Finance
        /// bank-account name, which is what a Sevkiyatçı is. The port is the only synthetic part; the
        /// database, the tenant filter and the reader are the real ones.
        /// </summary>
        public Scope OpenRefused(Guid company, string actor) =>
            Open(company, actor, realSettings: false, new RefusingPermissions(), []);

        public Scope Open(Guid company, string actor, bool realSettings,
            params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] extra) =>
            Open(company, actor, realSettings, new SyntheticPermissions(), extra);

        public Scope OpenWithPermissions(Guid company, string actor, IFinancePermissionPort permissions) =>
            Open(company, actor, realSettings: false, permissions, []);

        private Scope Open(Guid company, string actor, bool realSettings, IFinancePermissionPort permissions,
            Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] extra)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var currentUser = new FixedCurrentUser(actor, company);
            var collector = new AuditEventCollector();
            var interceptors = new List<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor>
            {
                new AuditSaveChangesInterceptor(collector, companyContext),
            };
            interceptors.AddRange(extra);
            var options = new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .AddInterceptors(interceptors)
                .Options;
            var dbContext = new FinanceDbContext(options, companyContext);
            ISettingsMoneyCandidateReader currencies = realSettings
                ? new UnavailableSettingsPrerequisiteProvider(companyContext, currentUser)
                : new SyntheticCandidates();
            var accounts = new FinanceBankAccountService(dbContext, companyContext, currentUser, permissions, currencies, collector,
                new FinanceBankAccountEnvelopes(
                    new FinanceAuditEnvelope(FinanceBankAccountService.ProposeCommand, ["SENTETIK-FINANCE"], "Web"),
                    new FinanceAuditEnvelope(FinanceBankAccountService.ApproveCommand, ["SENTETIK-FINANCE"], "Web")),
                TimeProvider.System);
            // The draft service reads bank accounts through the REAL persisted reader above.
            var drafts = new ReceiptDraftService(dbContext, companyContext, currentUser, permissions, accounts,
                new SyntheticParty(company), new SyntheticSettings(company), collector, new FinanceReceiptDraftBinding(Operation),
                new FinanceAuditEnvelope(ReceiptDraftService.SaveDraftCommand, ["SENTETIK-FINANCE"], "Web"), TimeProvider.System);
            return new Scope(dbContext, accounts, drafts, new FinanceContext(company, actor, Correlation, null));
        }

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        private static DbContextOptions<FinanceDbContext> Options(SqlServerRelationalApiFactory factory) =>
            new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .Options;
    }

    private sealed class Scope(FinanceDbContext dbContext, FinanceBankAccountService accounts, ReceiptDraftService drafts,
        FinanceContext context) : IAsyncDisposable
    {
        public FinanceBankAccountService Accounts => accounts;
        public IManualReceiptService Drafts => drafts;
        public FinanceContext Context => context;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    /// <summary>SENTETIK: allows every Finance action. Not a grant; there is no Finance permission on main.</summary>
    private sealed class SyntheticPermissions : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }

    /// <summary>
    /// SENTETIK: the command's initial authorization succeeds, then the permission is revoked while
    /// owner rows are being read. The production service must measure the action again before save.
    /// This is not a real grant-revocation acceptance test.
    /// </summary>
    private sealed class AllowInitialDecisionOnlyPermissions : IFinancePermissionPort
    {
        private int decisions;

        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(
                Interlocked.Increment(ref decisions) == 1 ? PermissionDecision.Allowed : PermissionDecision.Denied);
    }

    /// <summary>
    /// SENTETIK: a caller the authority refuses for every Finance action — someone holding no
    /// `finance.bank-account.read` name at all. K2 asks that such a person receive no IBAN; the case
    /// using this port proves they receive no ACCOUNT, which is the stronger statement.
    /// </summary>
    private sealed class RefusingPermissions : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Denied);
    }

    /// <summary>SENTETIK: one currency candidate. Not a Settings source; none exists on main.</summary>
    private sealed class SyntheticCandidates : ISettingsMoneyCandidateReader
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MoneyCurrencyCandidateV1>>([new MoneyCurrencyCandidateV1(Currency, "SENTETIK", "SENTETIK para")]);
    }

    /// <summary>SENTETIK: one active party account of the company.</summary>
    private sealed class SyntheticParty(Guid company) : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
            Guid companyAccountId, CancellationToken cancellationToken) =>
            Task.FromResult(partyId == Parties.Party && companyAccountId == Parties.Account
                ? new ReadResult<PartyAccountEligibility>(ReadOutcome.Found,
                    new PartyAccountEligibility(Parties.Party, Parties.Account, company, true))
                : new ReadResult<PartyAccountEligibility>(ReadOutcome.NotFound, null));
    }

    /// <summary>SENTETIK: a money profile of scale 2 for the test currency — a test input, not a production scale.</summary>
    private sealed class SyntheticSettings(Guid company) : ISettingsPrerequisiteProvider
    {
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MoneyProfileV1(company, currencyId, "SENTETIK", operation,
                new ProfileVersionV1(ProfileId, 1), 2, true, new DateOnly(2026, 1, 1), null));

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedCurrentUser(string userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    /// <summary>Holds each save until both writers have reached it, so both read the same state first.</summary>
    private sealed class BarrierInterceptor(Barrier barrier) : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return ValueTask.FromResult(result);
        }
    }
}
