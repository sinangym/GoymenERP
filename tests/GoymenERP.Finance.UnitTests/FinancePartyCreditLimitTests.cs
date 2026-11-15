using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// API-85a (F018 plan §L1; FNS-D27/D27a) without a database: the credit limit model (live and revision
/// indexes, payload and decision checks, the command target), the forward-fix Down, the write guard, the payload
/// hash, and the real service in memory: the one-step change by an approver, the proposal decided by another
/// actor, rejection, stale revision and version, one pending proposal per account, replay, company isolation,
/// authority lost before the save and what a read-only caller sees. What SQL Server enforces is proven by the
/// relational acceptance tests. SENTETIK / NOT_ENV.
/// </summary>
public sealed class FinancePartyCreditLimitTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("12121212-1212-1212-1212-121212121212");
    private static readonly Guid Currency = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherCurrency = Guid.Parse("23232323-2323-2323-2323-232323232323");
    private static readonly Guid Party = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OtherParty = Guid.Parse("34343434-3434-3434-3434-343434343434");
    private static readonly Guid Account = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset At = new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
    private const string Proposer = "SENTETIK-proposer";
    private const string Approver = "SENTETIK-approver";
    private const string Reader = "SENTETIK-reader";
    private const string Reason = "SENTETIK neden";
    private const string Guard = "Kredi limiti kaydı yalnız öneri kararıyla veya yerine yenisi geçerek, sürümü bir artarak değişir.";
    private const string Immutable = "Finans kaydının yalnız izinli alanı değişir; komut kayıtları değişmez.";

    [Fact]
    public void A_credit_limit_is_a_company_history_row_whose_live_state_and_decision_are_held_by_the_database()
    {
        using var context = Context();
        var limits = context.Model.FindEntityType(typeof(PartyCreditLimitRow))!;
        Assert.Equal((FinanceDbContext.Schema, "PartyCreditLimits"), (limits.GetSchema(), limits.GetTableName()));
        Assert.Single(limits.GetDeclaredQueryFilters());
        Assert.True(limits.FindProperty(nameof(PartyCreditLimitRow.Version))!.IsConcurrencyToken);
        // The amount is exact text, never a binary or decimal column that could round.
        Assert.DoesNotContain(limits.GetProperties(), property => property.ClrType == typeof(decimal)
            || property.ClrType == typeof(decimal?) || property.ClrType == typeof(double));

        var design = context.GetService<IDesignTimeModel>().Model;
        var designLimits = design.FindEntityType(typeof(PartyCreditLimitRow))!;
        var payload = Assert.Single(designLimits.GetCheckConstraints(), check => check.Name == "CK_PartyCreditLimits_Payload");
        Assert.Equal(FinanceDbContext.PartyCreditLimitPayloadCheck, payload.Sql);
        Assert.Contains("([Kind] = 2 AND [CurrencyId] IS NULL AND [AmountText] IS NULL)", payload.Sql, StringComparison.Ordinal);
        Assert.Contains("[AmountText] LIKE '%[1-9]%'", payload.Sql, StringComparison.Ordinal);

        var decision = Assert.Single(designLimits.GetCheckConstraints(), check => check.Name == "CK_PartyCreditLimits_Decision");
        Assert.Equal(FinanceDbContext.PartyCreditLimitDecisionCheck, decision.Sql);
        // FNS-D27: a direct record is decided by its author; FNS-D27a: a proposal by anyone else (binary collation).
        Assert.Contains("[IsDirect] = 1 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 = [ProposedBy] COLLATE Latin1_General_100_BIN2",
            decision.Sql, StringComparison.Ordinal);
        Assert.Contains("[IsDirect] = 0 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [ProposedBy] COLLATE Latin1_General_100_BIN2",
            decision.Sql, StringComparison.Ordinal);
        Assert.Single(designLimits.GetCheckConstraints(), check => check.Name == "CK_PartyCreditLimits_Identity");
        var revision = Assert.Single(designLimits.GetCheckConstraints(), check => check.Name == "CK_PartyCreditLimits_Revision");
        Assert.Contains("[AccountRevision] > [BaseRevision]", revision.Sql, StringComparison.Ordinal);
        Assert.Equal(FinanceDbContext.IdentityCollation,
            designLimits.FindProperty(nameof(PartyCreditLimitRow.ProposedBy))!.GetCollation());
        Assert.Equal(FinanceDbContext.IdentityCollation,
            designLimits.FindProperty(nameof(PartyCreditLimitRow.DecidedBy))!.GetCollation());

        // One pending proposal and one effective record per account; Status is a key column so EF Core orders a
        // save that moves one row out of a live state before the row that enters it.
        var live = Assert.Single(designLimits.GetIndexes(), index => index.GetDatabaseName() == FinanceDbContext.PartyCreditLimitLiveIndex);
        Assert.True(live.IsUnique);
        Assert.Equal([nameof(PartyCreditLimitRow.CompanyId), nameof(PartyCreditLimitRow.CompanyAccountId),
            nameof(PartyCreditLimitRow.Status)], live.Properties.Select(property => property.Name));
        Assert.Equal(FinanceDbContext.PartyCreditLimitLiveFilter, live.GetFilter());
        Assert.Equal("[Status] IN (1, 2)", live.GetFilter());

        // Two changes from the same revision cannot both win.
        var revisions = Assert.Single(designLimits.GetIndexes(), index => index.GetDatabaseName() == "UX_PartyCreditLimits_Revision");
        Assert.True(revisions.IsUnique);
        Assert.Equal([nameof(PartyCreditLimitRow.CompanyId), nameof(PartyCreditLimitRow.CompanyAccountId),
            nameof(PartyCreditLimitRow.AccountRevision)], revisions.Properties.Select(property => property.Name));
        Assert.Equal("[AccountRevision] IS NOT NULL", revisions.GetFilter());

        var commands = design.FindEntityType(typeof(PartyCreditLimitCommandRow))!;
        Assert.Equal((FinanceDbContext.Schema, "PartyCreditLimitCommands"), (commands.GetSchema(), commands.GetTableName()));
        var target = Assert.Single(commands.GetCheckConstraints(), check => check.Name == "CK_PartyCreditLimitCommands_Target");
        Assert.EndsWith("JSON_VALUE([ResultJson], '$.outcome') IN ('ValidationFailed', 'VersionConflict'))", target.Sql,
            StringComparison.Ordinal);
        Assert.Equal([nameof(PartyCreditLimitCommandRow.CompanyId), nameof(PartyCreditLimitCommandRow.CommandName),
            nameof(PartyCreditLimitCommandRow.IdempotencyKeyHash)], commands.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Single(context.Model.FindEntityType(typeof(PartyCreditLimitCommandRow))!.GetDeclaredQueryFilters());
    }

    [Fact]
    public void The_credit_limit_migration_is_pure_DDL_up_and_refuses_to_drop_stored_limits()
    {
        var migration = new AddPartyCreditLimits();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Equal(2, migration.UpOperations.OfType<CreateTableOperation>().Count());

        // Forward fix: the first Down operation is the one guard; only an empty store rolls back.
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
        Assert.Contains("THROW 51322", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[finance].[PartyCreditLimits]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[finance].[PartyCreditLimitCommands]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("AddPartyCreditLimits is not rolled back (forward fix).", guard.Sql, StringComparison.Ordinal);
        Assert.Equal(["PartyCreditLimitCommands", "PartyCreditLimits"],
            migration.DownOperations.OfType<DropTableOperation>().Select(operation => operation.Name));
    }

    [Theory]
    [InlineData("self-approval")]
    [InlineData("approval-without-revision")]
    [InlineData("rejection-without-reason")]
    [InlineData("two-version-steps")]
    [InlineData("amount-changed")]
    [InlineData("approved-rejected")]
    [InlineData("superseded-decision-changed")]
    [InlineData("direct-approval")]
    public async Task A_credit_limit_record_changes_only_by_one_decision_of_another_actor_or_by_being_superseded(string fault)
    {
        await using var run = new Run();
        var seeded = await run.SeedAsync(fault is "approved-rejected" or "superseded-decision-changed"
            ? ApprovedSet(revision: 1) : Proposal(baseRevision: 0));
        await using var context = run.Memory(CompanyA);
        var row = await context.PartyCreditLimits.SingleAsync(candidate => candidate.Id == seeded.Id);
        switch (fault)
        {
            case "self-approval":
                Approve(row, Proposer, revision: 1);
                break;
            case "approval-without-revision":
                Approve(row, Approver, revision: null);
                break;
            case "rejection-without-reason":
                Reject(row, Approver, reason: " ");
                break;
            case "two-version-steps":
                Approve(row, Approver, revision: 1);
                row.Version += 1;
                break;
            case "amount-changed":
                Approve(row, Approver, revision: 1);
                row.AmountText = "1.00";
                break;
            case "approved-rejected":
                row.Status = 3;
                row.RejectionReason = Reason;
                row.Version += 1;
                break;
            case "superseded-decision-changed":
                row.Status = 4;
                row.SupersededAtUtc = At.AddMinutes(30);
                row.DecidedBy = "SENTETIK-other-approver";
                row.Version += 1;
                break;
            default:
                Approve(row, Approver, revision: 1);
                row.IsDirect = true;
                break;
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        // The limit itself (kind, currency, amount) is outside the fields a decision may touch.
        Assert.Equal(fault == "amount-changed" ? Immutable : Guard, exception.Message);
    }

    [Fact]
    public async Task Every_legal_decision_and_supersession_passes_the_write_guard()
    {
        await using var run = new Run();
        var approved = await run.SeedAsync(Proposal(baseRevision: 0));
        var rejected = await run.SeedAsync(Proposal(baseRevision: 0, account: Guid.NewGuid()));
        var undecided = await run.SeedAsync(Proposal(baseRevision: 0, account: Guid.NewGuid()));
        Action<PartyCreditLimitRow>[] steps =
        [
            row => Approve(row, Approver, revision: 1),
            row =>
            {
                row.Status = 4;
                row.SupersededAtUtc = At.AddMinutes(30);
                row.UpdatedAtUtc = At.AddMinutes(30);
                row.Version += 1;
            },
        ];
        foreach (var step in steps)
        {
            await using var context = run.Memory(CompanyA);
            step(await context.PartyCreditLimits.SingleAsync(candidate => candidate.Id == approved.Id));
            await context.SaveChangesAsync();
        }

        await using (var context = run.Memory(CompanyA))
        {
            Reject(await context.PartyCreditLimits.SingleAsync(candidate => candidate.Id == rejected.Id), Approver, Reason);
            var pending = await context.PartyCreditLimits.SingleAsync(candidate => candidate.Id == undecided.Id);
            pending.Status = 4;
            pending.SupersededAtUtc = At.AddMinutes(30);
            pending.UpdatedAtUtc = At.AddMinutes(30);
            pending.Version += 1;
            await context.SaveChangesAsync();
        }

        Assert.Equal((4, 3L, (long?)1), ((await run.RowAsync(approved.Id)).Status, (await run.RowAsync(approved.Id)).Version,
            (await run.RowAsync(approved.Id)).AccountRevision));
        Assert.Equal((3, Reason), ((await run.RowAsync(rejected.Id)).Status, (await run.RowAsync(rejected.Id)).RejectionReason));
        Assert.Equal(4, (await run.RowAsync(undecided.Id)).Status);
    }

    [Fact]
    public void The_payload_hash_names_the_command_and_every_field()
    {
        var request = new PartyCreditLimitSetRequest(Party, Account, Currency, "100.00", "0");
        var hash = FinancePartyCreditLimitService.PayloadHash(FinancePartyCreditLimitService.SetCommand, request);
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, FinancePartyCreditLimitService.PayloadHash(FinancePartyCreditLimitService.SetCommand,
            new PartyCreditLimitSetRequest(Party, Account, Currency, "100.00", "0")));
        string[] others =
        [
            FinancePartyCreditLimitService.PayloadHash(FinancePartyCreditLimitService.ProposeCommand, request),
            FinancePartyCreditLimitService.PayloadHash(FinancePartyCreditLimitService.SetCommand, request with { PartyId = OtherParty }),
            FinancePartyCreditLimitService.PayloadHash(FinancePartyCreditLimitService.SetCommand, request with { CompanyAccountId = Guid.NewGuid() }),
            FinancePartyCreditLimitService.PayloadHash(FinancePartyCreditLimitService.SetCommand, request with { CurrencyId = OtherCurrency }),
            // The exact text is the value: "100.0" is another payload than "100.00".
            FinancePartyCreditLimitService.PayloadHash(FinancePartyCreditLimitService.SetCommand, request with { Amount = "100.0" }),
            FinancePartyCreditLimitService.PayloadHash(FinancePartyCreditLimitService.SetCommand, request with { ExpectedRevision = "1" }),
        ];
        Assert.All(others, other => Assert.NotEqual(hash, other));
        Assert.Equal(others.Length, others.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("100", true)]
    [InlineData("100.50", true)]
    [InlineData("0.01", true)]
    [InlineData("007.10", true)]
    [InlineData("0", false)]
    [InlineData("0.00", false)]
    [InlineData("-1", false)]
    [InlineData("+1", false)]
    [InlineData("1,00", false)]
    [InlineData("1e3", false)]
    [InlineData(" 1", false)]
    [InlineData("1 ", false)]
    [InlineData(".5", false)]
    [InlineData("5.", false)]
    [InlineData("1.2.3", false)]
    [InlineData("", false)]
    [InlineData("79228162514264337593543950336", false)]
    public void An_amount_is_exact_positive_decimal_text_or_nothing(string amount, bool exact) =>
        Assert.Equal(exact, FinancePartyCreditLimitService.IsExactAmount(amount));

    [Theory]
    [InlineData("key", "idempotencyKey", "idempotency_key_invalid")]
    [InlineData("party", "partyId", "identity_required")]
    [InlineData("account", "companyAccountId", "identity_required")]
    [InlineData("revision", "expectedRevision", "identity_required")]
    [InlineData("currency", "currencyId", "identity_required")]
    [InlineData("zero", "amount", "credit_limit_amount_invalid")]
    [InlineData("negative", "amount", "credit_limit_amount_invalid")]
    [InlineData("comma", "amount", "credit_limit_amount_invalid")]
    [InlineData("null-amount", "amount", "credit_limit_amount_invalid")]
    public async Task A_direct_set_is_refused_before_Settings_or_Counterparty_is_asked(string fault, string field, string code)
    {
        await using var run = new Run();
        var request = fault switch
        {
            "party" => new PartyCreditLimitSetRequest(Guid.Empty, Account, Currency, "100", "0"),
            "account" => new PartyCreditLimitSetRequest(Party, Guid.Empty, Currency, "100", "0"),
            "revision" => new PartyCreditLimitSetRequest(Party, Account, Currency, "100", " 0"),
            "currency" => new PartyCreditLimitSetRequest(Party, Account, Guid.Empty, "100", "0"),
            "zero" => new PartyCreditLimitSetRequest(Party, Account, Currency, "0.00", "0"),
            "negative" => new PartyCreditLimitSetRequest(Party, Account, Currency, "-100", "0"),
            "comma" => new PartyCreditLimitSetRequest(Party, Account, Currency, "100,00", "0"),
            "null-amount" => new PartyCreditLimitSetRequest(Party, Account, Currency, null!, "0"),
            _ => new PartyCreditLimitSetRequest(Party, Account, Currency, "100", "0"),
        };

        var result = await run.SetAsync(Approver, request, key: fault == "key" ? " SENTETIK-key" : null,
            currencies: new NoCandidates(), parties: new NoPartyRead());

        AssertIssue(result, CommandOutcome.ValidationFailed, field, code);
        Assert.Empty(await run.CommandsAsync());
        Assert.Empty(await run.AllRowsAsync());
    }

    [Theory]
    [InlineData("set-without-currency", "currencyId", "identity_required")]
    [InlineData("set-without-amount", "amount", "credit_limit_amount_invalid")]
    [InlineData("remove-with-amount", "amount", "credit_limit_remove_carries_no_amount")]
    [InlineData("remove-with-currency", "currencyId", "credit_limit_remove_carries_no_amount")]
    [InlineData("unknown-kind", "kind", "credit_limit_kind_invalid")]
    public async Task A_proposal_payload_is_refused_before_Settings_or_Counterparty_is_asked(string fault, string field, string code)
    {
        await using var run = new Run();
        var request = fault switch
        {
            "set-without-currency" => new PartyCreditLimitProposeRequest(Party, Account, PartyCreditLimitKind.Set, null, "100", "0"),
            "set-without-amount" => new PartyCreditLimitProposeRequest(Party, Account, PartyCreditLimitKind.Set, Currency, null, "0"),
            "remove-with-amount" => new PartyCreditLimitProposeRequest(Party, Account, PartyCreditLimitKind.Remove, null, "100", "0"),
            "remove-with-currency" => new PartyCreditLimitProposeRequest(Party, Account, PartyCreditLimitKind.Remove, Currency, null, "0"),
            _ => new PartyCreditLimitProposeRequest(Party, Account, (PartyCreditLimitKind)7, Currency, "100", "0"),
        };

        var result = await run.ProposeAsync(Proposer, request, currencies: new NoCandidates(), parties: new NoPartyRead());

        var issue = Assert.Single(result.Issues);
        Assert.Equal((CommandOutcome.ValidationFailed, field, code), (result.Outcome, issue.Field, issue.Code));
        Assert.Empty(await run.CommandsAsync());
    }

    [Fact]
    public async Task An_approver_sets_changes_and_removes_a_limit_in_one_step_each_audited_with_old_and_new_value()
    {
        await using var run = new Run();
        var permissions = new RecordingPermission(PermissionDecision.Allowed);

        var set = await run.SetAsync(Approver, new PartyCreditLimitSetRequest(Party, Account, Currency, "250000.50", "0"),
            permissions: permissions);

        Assert.Equal((CommandOutcome.Succeeded, false), (set.Outcome, set.IsReplay));
        var first = set.Resource!;
        Assert.Equal((PartyCreditLimitKind.Set, PartyCreditLimitStatus.Approved, true, "1", "1", "250000.50", (Guid?)Currency),
            (first.Kind, first.Status, first.IsDirect, first.Revision, first.Version, first.Amount, first.CurrencyId));
        Assert.Equal((CompanyA, Party, Account), (first.CompanyId, first.PartyId, first.CompanyAccountId));
        // FNS-D27: the one-step change is measured on the approve authority only.
        Assert.All(permissions.Actions, action => Assert.Equal(FinanceAction.ApproveCreditLimit, action));

        var changed = await run.SetAsync(Approver, new PartyCreditLimitSetRequest(Party, Account, Currency, "300000", "1"));
        Assert.Equal((CommandOutcome.Succeeded, "2"), (changed.Outcome, changed.Resource!.Revision));
        var superseded = await run.RowAsync(first.Id);
        Assert.Equal((FinancePartyCreditLimitService.Superseded, 2L, (long?)1, Approver),
            (superseded.Status, superseded.Version, superseded.AccountRevision, superseded.DecidedBy));

        var removed = await run.RemoveAsync(Approver, new PartyCreditLimitRemoveRequest(Party, Account, "2"));
        Assert.Equal((CommandOutcome.Succeeded, PartyCreditLimitKind.Remove, "3", (string?)null, (Guid?)null),
            (removed.Outcome, removed.Resource!.Kind, removed.Resource.Revision, removed.Resource.Amount, removed.Resource.CurrencyId));

        var account = await run.GetAccountAsync(Reader, Account, new ReadOnlyPermission());
        Assert.Equal(ReadOutcome.Found, account.Outcome);
        Assert.Equal((removed.Resource.Id, "3", (PartyCreditLimitRecordView?)null),
            (account.Resource!.Effective!.Id, account.Resource.Revision, account.Resource.Pending));

        var audit = Assert.Single(await run.AuditsAsync(changed.Resource.Id));
        Assert.Equal(("finance.credit-limit.set.v1", "Set", ErpPermissions.FinanceCreditLimitsApprove, Approver, (long?)null, (long?)1),
            (audit.EventType, audit.Operation, audit.PermissionCode, audit.ActorId, audit.EntityVersionBefore, audit.EntityVersionAfter));
        // The old value beside the new one.
        Assert.Contains("250000.50", audit.BeforeJson, StringComparison.Ordinal);
        Assert.Contains("300000", audit.AfterJson, StringComparison.Ordinal);
        using var approval = JsonDocument.Parse(audit.ApprovalContextJson!);
        Assert.Equal(("FNS-D27", Approver, true), (approval.RootElement.GetProperty("rule").GetString(),
            approval.RootElement.GetProperty("approvedBy").GetString(), approval.RootElement.GetProperty("direct").GetBoolean()));
        Assert.Equal("finance.credit-limit.removed.v1", Assert.Single(await run.AuditsAsync(removed.Resource.Id)).EventType);
        Assert.Equal(3, (await run.CommandsAsync()).Count);
    }

    [Fact]
    public async Task A_proposal_changes_nothing_until_another_approver_approves_it_and_the_proposer_can_never_decide_it()
    {
        await using var run = new Run();
        var proposed = await run.ProposeAsync(Proposer,
            new PartyCreditLimitProposeRequest(Party, Account, PartyCreditLimitKind.Set, Currency, "150000.00", "0"),
            permissions: new ProposerPermission());
        Assert.Equal((CommandOutcome.Succeeded, PartyCreditLimitStatus.Proposed, false, (string?)null, "1"),
            (proposed.Outcome, proposed.Resource!.Status, proposed.Resource.IsDirect, proposed.Resource.Revision, proposed.Resource.Version));
        var proposal = proposed.Resource.Id;
        Assert.Equal("finance.credit-limit.proposed.v1", Assert.Single(await run.AuditsAsync(proposal)).EventType);

        // Nothing effective yet; the proposer sees their own pending proposal, a reader sees nothing.
        var own = await run.GetAccountAsync(Proposer, Account, new ProposerPermission());
        Assert.Equal((ReadOutcome.Found, (PartyCreditLimitRecordView?)null, proposal, true, "0"),
            (own.Outcome, own.Resource!.Effective, own.Resource.Pending!.Id, own.Resource.IsOwnPending, own.Resource.Revision));
        Assert.Equal(ReadOutcome.NotFound, (await run.GetAccountAsync(Reader, Account, new ReadOnlyPermission())).Outcome);

        // Maker≠checker: even holding the approve authority, the proposer is refused and nothing is stored.
        var before = await run.SnapshotAsync(proposal);
        var self = await run.ApproveAsync(Proposer, new PartyCreditLimitApproveRequest(proposal, "1"));
        AssertIssue(self, CommandOutcome.Denied, "approval", "finance_approver_is_proposer");
        Assert.Equal(before, await run.SnapshotAsync(proposal));

        var approved = await run.ApproveAsync(Approver, new PartyCreditLimitApproveRequest(proposal, "1"));
        Assert.Equal((CommandOutcome.Succeeded, PartyCreditLimitStatus.Approved, false, "1", "2"),
            (approved.Outcome, approved.Resource!.Status, approved.Resource.IsDirect, approved.Resource.Revision, approved.Resource.Version));
        var stored = await run.RowAsync(proposal);
        Assert.Equal((Proposer, Approver), (stored.ProposedBy, stored.DecidedBy));

        var audit = (await run.AuditsAsync(proposal))[^1];
        Assert.Equal(("finance.credit-limit.approved.v1", "Approve", ErpPermissions.FinanceCreditLimitsApprove, (long?)1, (long?)2),
            (audit.EventType, audit.Operation, audit.PermissionCode, audit.EntityVersionBefore, audit.EntityVersionAfter));
        using var decision = JsonDocument.Parse(audit.ApprovalContextJson!);
        Assert.Equal(("FNS-D27a", Proposer, Approver), (decision.RootElement.GetProperty("rule").GetString(),
            decision.RootElement.GetProperty("proposedBy").GetString(), decision.RootElement.GetProperty("approvedBy").GetString()));

        var effective = await run.GetAccountAsync(Reader, Account, new ReadOnlyPermission());
        Assert.Equal((proposal, "150000.00", "1"), (effective.Resource!.Effective!.Id, effective.Resource.Effective.Amount,
            effective.Resource.Revision));
    }

    [Fact]
    public async Task An_approved_proposal_supersedes_the_effective_limit_and_a_remove_proposal_needs_one()
    {
        await using var run = new Run();
        var noLimit = await run.ProposeAsync(Proposer,
            new PartyCreditLimitProposeRequest(Party, Account, PartyCreditLimitKind.Remove, null, null, "0"),
            permissions: new ProposerPermission());
        AssertIssue(noLimit, CommandOutcome.ValidationFailed, "kind", "credit_limit_not_set");

        var set = await run.SetAsync(Approver, new PartyCreditLimitSetRequest(Party, Account, Currency, "500", "0"));
        var remove = await run.ProposeAsync(Proposer,
            new PartyCreditLimitProposeRequest(Party, Account, PartyCreditLimitKind.Remove, null, null, "1"),
            permissions: new ProposerPermission());
        Assert.Equal(CommandOutcome.Succeeded, remove.Outcome);

        var approved = await run.ApproveAsync(Approver, new PartyCreditLimitApproveRequest(remove.Resource!.Id, "1"));
        Assert.Equal((CommandOutcome.Succeeded, PartyCreditLimitKind.Remove, "2"),
            (approved.Outcome, approved.Resource!.Kind, approved.Resource.Revision));
        Assert.Equal(FinancePartyCreditLimitService.Superseded, (await run.RowAsync(set.Resource!.Id)).Status);
    }

    [Fact]
    public async Task A_rejection_needs_a_reason_and_another_actor_and_keeps_the_effective_limit()
    {
        await using var run = new Run();
        var set = await run.SetAsync(Approver, new PartyCreditLimitSetRequest(Party, Account, Currency, "1000", "0"));
        var proposed = await run.ProposeAsync(Proposer,
            new PartyCreditLimitProposeRequest(Party, Account, PartyCreditLimitKind.Set, Currency, "2000", "1"),
            permissions: new ProposerPermission());
        var proposal = proposed.Resource!.Id;

        AssertIssue(await run.RejectAsync(Approver, new PartyCreditLimitRejectRequest(proposal, "1", " ")),
            CommandOutcome.ValidationFailed, "reason", "reason_invalid");
        AssertIssue(await run.RejectAsync(Approver, new PartyCreditLimitRejectRequest(proposal, "1", new string('x', 501))),
            CommandOutcome.ValidationFailed, "reason", "finance_value_does_not_fit");
        AssertIssue(await run.RejectAsync(Proposer, new PartyCreditLimitRejectRequest(proposal, "1", Reason)),
            CommandOutcome.Denied, "approval", "finance_approver_is_proposer");

        var rejected = await run.RejectAsync(Approver, new PartyCreditLimitRejectRequest(proposal, "1", Reason));
        Assert.Equal((CommandOutcome.Succeeded, PartyCreditLimitStatus.Rejected, Reason, (string?)null),
            (rejected.Outcome, rejected.Resource!.Status, rejected.Resource.RejectionReason, rejected.Resource.Revision));
        var audit = (await run.AuditsAsync(proposal))[^1];
        Assert.Equal(("finance.credit-limit.rejected.v1", "Reject"), (audit.EventType, audit.Operation));

        var account = await run.GetAccountAsync(Approver, Account);
        Assert.Equal((set.Resource!.Id, (PartyCreditLimitRecordView?)null, "1"),
            (account.Resource!.Effective!.Id, account.Resource.Pending, account.Resource.Revision));
        // A decided proposal is decided once.
        AssertIssue(await run.ApproveAsync(Approver, new PartyCreditLimitApproveRequest(proposal, "2")),
            CommandOutcome.ValidationFailed, "proposalId", "credit_limit_not_proposed");
    }

    [Fact]
    public async Task One_proposal_is_pending_per_account_and_a_direct_change_supersedes_it()
    {
        await using var run = new Run();
        var first = await run.ProposeAsync(Proposer,
            new PartyCreditLimitProposeRequest(Party, Account, PartyCreditLimitKind.Set, Currency, "100", "0"),
            permissions: new ProposerPermission());
        var second = await run.ProposeAsync(Proposer,
            new PartyCreditLimitProposeRequest(Party, Account, PartyCreditLimitKind.Set, Currency, "200", "0"),
            permissions: new ProposerPermission());
        AssertIssue(second, CommandOutcome.ValidationFailed, "companyAccountId", "credit_limit_proposal_pending");

        var direct = await run.SetAsync(Approver, new PartyCreditLimitSetRequest(Party, Account, Currency, "300", "0"));
        Assert.Equal(CommandOutcome.Succeeded, direct.Outcome);
        var pending = await run.RowAsync(first.Resource!.Id);
        Assert.Equal((FinancePartyCreditLimitService.Superseded, (string?)null, 2L),
            (pending.Status, pending.DecidedBy, pending.Version));

        // The superseded proposal can no longer be decided: its old version is stale, its new one is not a proposal.
        AssertIssue(await run.ApproveAsync(Approver, new PartyCreditLimitApproveRequest(first.Resource.Id, "1")),
            CommandOutcome.VersionConflict, "expectedVersion", "version_conflict");
        AssertIssue(await run.ApproveAsync(Approver, new PartyCreditLimitApproveRequest(first.Resource.Id, "2")),
            CommandOutcome.ValidationFailed, "proposalId", "credit_limit_not_proposed");
    }

    [Fact]
    public async Task A_stale_revision_is_a_recorded_conflict_that_replays_and_a_stale_proposal_cannot_be_approved()
    {
        await using var run = new Run();
        var key = Key();
        var stale = await run.SetAsync(Approver, new PartyCreditLimitSetRequest(Party, Account, Currency, "100", "5"), key: key);
        AssertIssue(stale, CommandOutcome.VersionConflict, "expectedRevision", "version_conflict");
        var recorded = Assert.Single(await run.CommandsAsync());
        Assert.Null(recorded.PartyCreditLimitId);

        var replay = await run.SetAsync(Approver, new PartyCreditLimitSetRequest(Party, Account, Currency, "100", "5"), key: key,
            correlation: "SENTETIK-other-correlation");
        Assert.Equal((CommandOutcome.VersionConflict, true, "SENTETIK-correlation"),
            (replay.Outcome, replay.IsReplay, replay.OriginalCorrelationId));

        // A proposal made on revision 0 while the account has moved to revision 1 (a race the database settles).
        await run.SeedAsync(ApprovedSet(revision: 1));
        var proposal = await run.SeedAsync(Proposal(baseRevision: 0));
        AssertIssue(await run.ApproveAsync(Approver, new PartyCreditLimitApproveRequest(proposal.Id, "1")),
            CommandOutcome.VersionConflict, "expectedVersion", "credit_limit_revision_stale");
        Assert.Equal(FinancePartyCreditLimitService.Proposed, (await run.RowAsync(proposal.Id)).Status);
        // It can still be rejected.
        Assert.Equal(CommandOutcome.Succeeded,
            (await run.RejectAsync(Approver, new PartyCreditLimitRejectRequest(proposal.Id, "1", Reason))).Outcome);
    }

    [Fact]
    public async Task A_command_replays_by_key_and_another_payload_or_actor_under_the_same_key_is_a_conflict()
    {
        await using var run = new Run();
        var key = Key();
        var request = new PartyCreditLimitSetRequest(Party, Account, Currency, "100", "0");
        var first = await run.SetAsync(Approver, request, key: key);
        var replay = await run.SetAsync(Approver, request, key: key, correlation: "SENTETIK-retry");
        Assert.Equal((CommandOutcome.Succeeded, true, "SENTETIK-correlation", first.Resource!.Id),
            (replay.Outcome, replay.IsReplay, replay.OriginalCorrelationId, replay.Resource!.Id));

        Assert.Equal(CommandOutcome.IdempotencyConflict,
            (await run.SetAsync(Approver, request with { Amount = "101" }, key: key)).Outcome);
        Assert.Equal(CommandOutcome.IdempotencyConflict,
            (await run.SetAsync("SENTETIK-approver-2", request, key: key)).Outcome);
        Assert.Single(await run.AllRowsAsync());
        Assert.Single(await run.CommandsAsync());
    }

    [Fact]
    public async Task Another_companys_limit_is_never_seen_decided_or_counted()
    {
        await using var run = new Run();
        var foreign = await run.SeedAsync(Proposal(baseRevision: 0, company: CompanyB));
        await run.SeedAsync(ApprovedSet(revision: 1, company: CompanyB, account: Guid.NewGuid()));

        AssertIssue(await run.ApproveAsync(Approver, new PartyCreditLimitApproveRequest(foreign.Id, "1")),
            CommandOutcome.ValidationFailed, "proposalId", "credit_limit_proposal_not_found");
        Assert.Equal(FinancePartyCreditLimitService.Proposed, (await run.RowAsync(foreign.Id, CompanyB)).Status);

        var list = await run.ListAsync(Approver);
        Assert.Equal((ReadOutcome.Found, 0), (list.Outcome, list.Resource!.Items.Count));
        Assert.Equal(ReadOutcome.NotFound, (await run.GetAccountAsync(Approver, Account)).Outcome);
        // The company's own first change starts at revision 0: another company's revisions are not this account's.
        Assert.Equal(CommandOutcome.Succeeded,
            (await run.SetAsync(Approver, new PartyCreditLimitSetRequest(Party, Account, Currency, "100", "0"))).Outcome);
    }

    [Fact]
    public async Task Authority_revoked_before_the_save_refuses_the_change_and_stores_nothing()
    {
        await using var run = new Run();
        // Start, after Settings, after Counterparty, after the account read: allowed; immediately before the save: denied.
        var result = await run.SetAsync(Approver, new PartyCreditLimitSetRequest(Party, Account, Currency, "100", "0"),
            permissions: new RevokingPermission(FinanceAction.ApproveCreditLimit, allowed: 4));

        Assert.Equal((CommandOutcome.Denied, (PartyCreditLimitRecordView?)null), (result.Outcome, result.Resource));
        Assert.Empty(await run.AllRowsAsync());
        Assert.Empty(await run.CommandsAsync());
    }

    [Theory]
    [InlineData("denied", CommandOutcome.Denied)]
    [InlineData("unmapped", CommandOutcome.Unavailable)]
    [InlineData("proposer-only", CommandOutcome.Denied)]
    public async Task A_direct_change_needs_the_approve_authority_now(string fault, CommandOutcome outcome)
    {
        await using var run = new Run();
        IFinancePermissionPort permissions = fault switch
        {
            "denied" => new RecordingPermission(PermissionDecision.Denied),
            "unmapped" => new RecordingPermission(PermissionDecision.Unavailable),
            _ => new ProposerPermission(),
        };

        var result = await run.SetAsync(Proposer, new PartyCreditLimitSetRequest(Party, Account, Currency, "100", "0"),
            permissions: permissions, currencies: new NoCandidates(), parties: new NoPartyRead());

        Assert.Equal(outcome, result.Outcome);
        Assert.Empty(await run.CommandsAsync());
    }

    [Theory]
    [InlineData("inactive-account", "party_account_ineligible", CommandOutcome.ValidationFailed)]
    [InlineData("inactive-party", "party_account_ineligible", CommandOutcome.ValidationFailed)]
    [InlineData("another-party", "party_account_ineligible", CommandOutcome.ValidationFailed)]
    [InlineData("not-found", "party_account_ineligible", CommandOutcome.ValidationFailed)]
    [InlineData("unavailable", "party_account_unavailable", CommandOutcome.Unavailable)]
    public async Task A_set_needs_Counterpartys_active_party_account_pair(string fault, string code, CommandOutcome outcome)
    {
        await using var run = new Run();
        IFinancePartyAccountPort parties = fault switch
        {
            "inactive-account" => new FixedParty(accountActive: false),
            "inactive-party" => new FixedParty(partyActive: false),
            "another-party" => new FixedParty(party: OtherParty),
            "not-found" => new FixedParty(outcome: ReadOutcome.NotFound),
            _ => new FixedParty(outcome: ReadOutcome.Unavailable),
        };

        var result = await run.SetAsync(Approver, new PartyCreditLimitSetRequest(Party, Account, Currency, "100", "0"),
            parties: parties);

        AssertIssue(result, outcome, "companyAccountId", code);
        Assert.Empty(await run.AllRowsAsync());
    }

    [Fact]
    public async Task A_remove_is_allowed_for_a_passive_account_but_never_for_an_account_another_party_holds()
    {
        await using var run = new Run();
        await run.SetAsync(Approver, new PartyCreditLimitSetRequest(Party, Account, Currency, "100", "0"));
        var removed = await run.RemoveAsync(Approver, new PartyCreditLimitRemoveRequest(Party, Account, "1"),
            parties: new FixedParty(accountActive: false, partyActive: false));
        Assert.Equal(CommandOutcome.Succeeded, removed.Outcome);

        // Counterparty answers the account for another party now: refused rather than guessed.
        var moved = await run.SetAsync(Approver, new PartyCreditLimitSetRequest(OtherParty, Account, Currency, "100", "2"),
            parties: new FixedParty(party: OtherParty));
        AssertIssue(moved, CommandOutcome.ValidationFailed, "companyAccountId", "party_account_ineligible");
    }

    [Theory]
    [InlineData("not-a-candidate", CommandOutcome.ValidationFailed, "currency_not_a_candidate")]
    [InlineData("settings-unavailable", CommandOutcome.Unavailable, "settings_unavailable")]
    public async Task The_currency_must_be_one_of_Settings_candidates_now(string fault, CommandOutcome outcome, string code)
    {
        await using var run = new Run();
        ISettingsMoneyCandidateReader currencies = fault == "not-a-candidate"
            ? new Candidates([new MoneyCurrencyCandidateV1(OtherCurrency, "USD", "SENTETIK Dolar")])
            : new FailingCandidates("settings_unavailable");

        var result = await run.SetAsync(Approver, new PartyCreditLimitSetRequest(Party, Account, Currency, "100", "0"),
            currencies: currencies, parties: new NoPartyRead());

        AssertIssue(result, outcome, "currencyId", code);
    }

    [Fact]
    public async Task A_reader_sees_approved_records_only_and_a_proposer_or_approver_sees_proposals_too()
    {
        await using var run = new Run();
        var set = await run.SetAsync(Approver, new PartyCreditLimitSetRequest(Party, Account, Currency, "100", "0"));
        var proposed = await run.ProposeAsync(Proposer,
            new PartyCreditLimitProposeRequest(Party, Account, PartyCreditLimitKind.Set, Currency, "200", "1"),
            permissions: new ProposerPermission());
        await run.RejectAsync(Approver, new PartyCreditLimitRejectRequest(proposed.Resource!.Id, "1", Reason));
        var pending = await run.ProposeAsync(Proposer,
            new PartyCreditLimitProposeRequest(Party, Account, PartyCreditLimitKind.Set, Currency, "300", "1"),
            permissions: new ProposerPermission());

        var readerList = await run.ListAsync(Reader, new ReadOnlyPermission());
        var readerAccount = Assert.Single(readerList.Resource!.Items).Account;
        Assert.Equal((set.Resource!.Id, (PartyCreditLimitRecordView?)null, false),
            (readerAccount.Effective!.Id, readerAccount.Pending, readerAccount.IsOwnPending));
        var readerHistory = await run.HistoryAsync(Reader, Account, new ReadOnlyPermission());
        Assert.Equal([set.Resource.Id], readerHistory.Resource!.Items.Select(item => item.Id));

        var approverAccount = Assert.Single((await run.ListAsync(Approver)).Resource!.Items).Account;
        Assert.Equal((pending.Resource!.Id, false), (approverAccount.Pending!.Id, approverAccount.IsOwnPending));
        var history = await run.HistoryAsync(Approver, Account);
        Assert.Equal([set.Resource.Id, proposed.Resource.Id, pending.Resource.Id], history.Resource!.Items.Select(item => item.Id));

        Assert.Equal(ReadOutcome.Denied, (await run.ListAsync(Reader, new RecordingPermission(PermissionDecision.Denied))).Outcome);
        Assert.Equal(ReadOutcome.Unavailable,
            (await run.ListAsync(Reader, new RecordingPermission(PermissionDecision.Unavailable))).Outcome);
        Assert.Equal(ReadOutcome.NotFound, (await run.HistoryAsync(Approver, Guid.NewGuid())).Outcome);
        // Currency candidates belong to a proposer or approver, not to a reader.
        Assert.Equal(ReadOutcome.Denied, (await run.CandidatesAsync(Reader, new ReadOnlyPermission())).Outcome);
        var candidates = await run.CandidatesAsync(Proposer, new ProposerPermission());
        Assert.Equal((ReadOutcome.Found, Currency), (candidates.Outcome, Assert.Single(candidates.Resource!.Items).CurrencyId));
    }

    private static FinancePartyCreditLimitEnvelopes Envelopes() => new FinancePartyCreditLimitEnvelopes(
        Envelope(ErpPermissions.FinanceCreditLimitsPropose), Envelope(ErpPermissions.FinanceCreditLimitsApprove));

    private static FinanceAuditEnvelope Envelope(string permission) => new FinanceAuditEnvelope(permission, ["SENTETIK-FINANCE"],
        SourceChannels.Web);

    private static void AssertIssue(PartyCreditLimitCommandResult result, CommandOutcome outcome, string field, string code)
    {
        Assert.Equal(outcome, result.Outcome);
        Assert.Null(result.Resource);
        var issue = Assert.Single(result.Issues);
        Assert.Equal((field, code), (issue.Field, issue.Code));
    }

    private static string Key() => $"SENTETIK-{Guid.NewGuid():N}";

    private static PartyCreditLimitRow Proposal(long baseRevision, Guid? company = null, Guid? account = null) => new PartyCreditLimitRow
    {
        Id = Guid.NewGuid(), CompanyId = company ?? CompanyA, PartyId = Party, CompanyAccountId = account ?? Account,
        Kind = FinancePartyCreditLimitService.SetKind, CurrencyId = Currency, AmountText = "1000", Status = FinancePartyCreditLimitService.Proposed,
        IsDirect = false, BaseRevision = baseRevision, Version = 1, ProposedBy = Proposer, ProposedAtUtc = At, UpdatedAtUtc = At,
    };

    private static PartyCreditLimitRow ApprovedSet(long revision, Guid? company = null, Guid? account = null) => new PartyCreditLimitRow
    {
        Id = Guid.NewGuid(), CompanyId = company ?? CompanyA, PartyId = Party, CompanyAccountId = account ?? Account,
        Kind = FinancePartyCreditLimitService.SetKind, CurrencyId = Currency, AmountText = "500", Status = FinancePartyCreditLimitService.Approved,
        IsDirect = true, BaseRevision = revision - 1, AccountRevision = revision, Version = 1, ProposedBy = Approver,
        ProposedAtUtc = At, DecidedBy = Approver, DecidedAtUtc = At, UpdatedAtUtc = At,
    };

    private static void Approve(PartyCreditLimitRow row, string decidedBy, long? revision)
    {
        row.Status = FinancePartyCreditLimitService.Approved;
        row.DecidedBy = decidedBy;
        row.DecidedAtUtc = At.AddMinutes(5);
        row.AccountRevision = revision;
        row.UpdatedAtUtc = At.AddMinutes(5);
        row.Version += 1;
    }

    private static void Reject(PartyCreditLimitRow row, string decidedBy, string reason)
    {
        row.Status = FinancePartyCreditLimitService.Rejected;
        row.DecidedBy = decidedBy;
        row.DecidedAtUtc = At.AddMinutes(5);
        row.RejectionReason = reason;
        row.UpdatedAtUtc = At.AddMinutes(5);
        row.Version += 1;
    }

    private static FinanceDbContext Context()
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .Options;
        return new FinanceDbContext(options, company);
    }

    /// <summary>
    /// The real service over one in-memory store: every call is a fresh scope (context, company, user, audit
    /// collector and interceptor) like a request; seeds and checks read the store through the write guard.
    /// </summary>
    private sealed class Run : IAsyncDisposable
    {
        private readonly string databaseName = $"finance-credit-limits-{Guid.NewGuid():N}";

        public FinanceDbContext Memory(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(databaseName).Options, companyContext);
        }

        public async Task<PartyCreditLimitRow> SeedAsync(PartyCreditLimitRow row)
        {
            await using var context = Memory(row.CompanyId);
            context.PartyCreditLimits.Add(row);
            await context.SaveChangesAsync();
            return row;
        }

        public Task<PartyCreditLimitCommandResult> SetAsync(string actor, PartyCreditLimitSetRequest request, string? key = null,
            IFinancePermissionPort? permissions = null, ISettingsMoneyCandidateReader? currencies = null,
            IFinancePartyAccountPort? parties = null, string? correlation = null) =>
            CommandAsync(actor, permissions, currencies, parties, correlation,
                (service, finance) => service.SetAsync(finance, new CommandMetadata(key ?? Key()), request, CancellationToken.None));

        public Task<PartyCreditLimitCommandResult> RemoveAsync(string actor, PartyCreditLimitRemoveRequest request,
            IFinancePartyAccountPort? parties = null) =>
            CommandAsync(actor, null, null, parties, null,
                (service, finance) => service.RemoveAsync(finance, new CommandMetadata(Key()), request, CancellationToken.None));

        public Task<PartyCreditLimitCommandResult> ProposeAsync(string actor, PartyCreditLimitProposeRequest request,
            IFinancePermissionPort? permissions = null, ISettingsMoneyCandidateReader? currencies = null,
            IFinancePartyAccountPort? parties = null) =>
            CommandAsync(actor, permissions, currencies, parties, null,
                (service, finance) => service.ProposeAsync(finance, new CommandMetadata(Key()), request, CancellationToken.None));

        public Task<PartyCreditLimitCommandResult> ApproveAsync(string actor, PartyCreditLimitApproveRequest request) =>
            CommandAsync(actor, null, null, null, null,
                (service, finance) => service.ApproveAsync(finance, new CommandMetadata(Key()), request, CancellationToken.None));

        public Task<PartyCreditLimitCommandResult> RejectAsync(string actor, PartyCreditLimitRejectRequest request) =>
            CommandAsync(actor, null, null, null, null,
                (service, finance) => service.RejectAsync(finance, new CommandMetadata(Key()), request, CancellationToken.None));

        public Task<ReadResult<PartyCreditLimitListPage>> ListAsync(string actor, IFinancePermissionPort? permissions = null) =>
            CommandAsync(actor, permissions, null, null, null, (service, finance) => service.ListAsync(finance, CancellationToken.None));

        public Task<ReadResult<PartyCreditLimitAccountView>> GetAccountAsync(string actor, Guid account,
            IFinancePermissionPort? permissions = null) =>
            CommandAsync(actor, permissions, null, null, null,
                (service, finance) => service.GetAccountAsync(finance, account, CancellationToken.None));

        public Task<ReadResult<PartyCreditLimitHistoryPage>> HistoryAsync(string actor, Guid account,
            IFinancePermissionPort? permissions = null) =>
            CommandAsync(actor, permissions, null, null, null,
                (service, finance) => service.HistoryAsync(finance, account, CancellationToken.None));

        public Task<ReadResult<PartyCreditLimitCurrencyCandidates>> CandidatesAsync(string actor, IFinancePermissionPort permissions) =>
            CommandAsync(actor, permissions, null, null, null,
                (service, finance) => service.CurrencyCandidatesAsync(finance, CancellationToken.None));

        public async Task<PartyCreditLimitRow> RowAsync(Guid id, Guid? company = null)
        {
            await using var context = Memory(company ?? CompanyA);
            return await context.PartyCreditLimits.AsNoTracking().SingleAsync(row => row.Id == id);
        }

        public async Task<List<PartyCreditLimitRow>> AllRowsAsync(Guid? company = null)
        {
            await using var context = Memory(company ?? CompanyA);
            return await context.PartyCreditLimits.AsNoTracking().ToListAsync();
        }

        public async Task<List<PartyCreditLimitCommandRow>> CommandsAsync(Guid? company = null)
        {
            await using var context = Memory(company ?? CompanyA);
            return await context.PartyCreditLimitCommands.AsNoTracking().ToListAsync();
        }

        public async Task<List<AuditEvent>> AuditsAsync(Guid id)
        {
            var entityId = id.ToString("D");
            await using var context = Memory(CompanyA);
            return await context.Set<AuditEvent>().AsNoTracking().Where(audit => audit.EntityId == entityId)
                .OrderBy(audit => audit.EntityVersionAfter).ToListAsync();
        }

        /// <summary>The record's version, the company's stored commands and the record's audit events.</summary>
        public async Task<(long Version, int Commands, int Audits)> SnapshotAsync(Guid id) =>
            ((await RowAsync(id)).Version, (await CommandsAsync()).Count, (await AuditsAsync(id)).Count);

        public async ValueTask DisposeAsync()
        {
            await using var context = Memory(CompanyA);
            await context.Database.EnsureDeletedAsync();
        }

        private async Task<T> CommandAsync<T>(string actor, IFinancePermissionPort? permissions,
            ISettingsMoneyCandidateReader? currencies, IFinancePartyAccountPort? parties, string? correlation,
            Func<FinancePartyCreditLimitService, FinanceContext, Task<T>> call)
        {
            var company = new CompanyContext();
            company.Set(CompanyA);
            var collector = new AuditEventCollector();
            await using var context = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(databaseName)
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options, company);
            var service = new FinancePartyCreditLimitService(context, company, new FixedUser(actor),
                permissions ?? new RecordingPermission(PermissionDecision.Allowed),
                currencies ?? new Candidates([new MoneyCurrencyCandidateV1(Currency, "TRY", "SENTETIK Lira")]),
                parties ?? new FixedParty(), collector, Envelopes(), TimeProvider.System);
            return await call(service, new FinanceContext(CompanyA, actor, correlation ?? "SENTETIK-correlation", null));
        }
    }

    private sealed class RecordingPermission(PermissionDecision decision) : IFinancePermissionPort
    {
        public List<FinanceAction> Actions { get; } = [];

        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken)
        {
            Actions.Add(action);
            return Task.FromResult(decision);
        }
    }

    /// <summary>A caller allowed to read credit limits and nothing else.</summary>
    private sealed class ReadOnlyPermission : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) =>
            Task.FromResult(action == FinanceAction.ReadCreditLimits ? PermissionDecision.Allowed : PermissionDecision.Denied);
    }

    /// <summary>A caller allowed to read and propose credit limits, never to approve.</summary>
    private sealed class ProposerPermission : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) =>
            Task.FromResult(action is FinanceAction.ReadCreditLimits or FinanceAction.ProposeCreditLimit
                ? PermissionDecision.Allowed
                : PermissionDecision.Denied);
    }

    /// <summary>Allows <paramref name="revoked"/> for its first <paramref name="allowed"/> checks, then denies it.</summary>
    private sealed class RevokingPermission(FinanceAction revoked, int allowed) : IFinancePermissionPort
    {
        private int checks;

        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) =>
            Task.FromResult(action != revoked || Interlocked.Increment(ref checks) <= allowed
                ? PermissionDecision.Allowed
                : PermissionDecision.Denied);
    }

    /// <summary>Counterparty's answer for the (party, company account) pair the request names.</summary>
    private sealed class FixedParty(ReadOutcome outcome = ReadOutcome.Found, Guid? party = null, bool accountActive = true,
        bool partyActive = true) : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
            Guid companyAccountId, CancellationToken cancellationToken) =>
            Task.FromResult(outcome == ReadOutcome.Found
                ? new ReadResult<PartyAccountEligibility>(ReadOutcome.Found, new PartyAccountEligibility(party ?? partyId,
                    companyAccountId, context.CompanyId, accountActive, Label: null, IsPartyActive: partyActive))
                : new ReadResult<PartyAccountEligibility>(outcome, null));
    }

    /// <summary>Never reached: the refusal comes before Counterparty is asked.</summary>
    private sealed class NoPartyRead : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
            Guid companyAccountId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Counterparty must not be asked before the refusal.");
    }

    private sealed class Candidates(IReadOnlyList<MoneyCurrencyCandidateV1> candidates) : ISettingsMoneyCandidateReader
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(candidates);
    }

    private sealed class FailingCandidates(string code) : ISettingsMoneyCandidateReader
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken cancellationToken = default) =>
            throw new SettingsPrerequisiteException(code);
    }

    /// <summary>Never reached by these tests: every refusal comes before Settings is asked.</summary>
    private sealed class NoCandidates : ISettingsMoneyCandidateReader
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Settings must not be asked before the refusal.");
    }

    private sealed class FixedUser(string userId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
