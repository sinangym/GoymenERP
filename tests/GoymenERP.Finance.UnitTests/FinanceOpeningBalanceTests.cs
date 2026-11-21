using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Application.Contracts;
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
/// FIN-S9 (FNS-D23) without a database: the opening balance model (live index, decision and target checks, the command
/// target), the forward-fix Down, the write guard, and the real service in memory: the Settings opening period
/// (unavailable by default, closed refuses and hides the list), the specialist's proposal and the manager's decision
/// (never the proposer), stale versions, replay, authority fences and company isolation; FNS-D84's one movement,
/// FNS-D85's reversal plus new proposal, and FNS-D86's business-day AsOfDate and period-version check. What SQL Server enforces is
/// proven by the relational acceptance tests. SENTETIK / NOT_ENV.
/// </summary>
public sealed class FinanceOpeningBalanceTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("12121212-1212-1212-1212-121212121212");
    private static readonly Guid Currency = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherCurrency = Guid.Parse("23232323-2323-2323-2323-232323232323");
    private static readonly Guid Party = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Account = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Cash = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset At = new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly AsOf = new(2026, 10, 31);
    private const string Specialist = "SENTETIK-specialist";
    private const string Manager = "SENTETIK-manager";
    private const string PeriodVersion = "SENTETIK-period-3";
    private const string Reason = "SENTETIK ret nedeni";
    private const string Guard =
        "Açılış bakiyesi yalnız bekleyen öneriden bir kez, öneren dışındaki kişinin onay veya ret kararıyla ya da onaylı "
        + "açılışın onaylı ters kaydıyla değişir.";
    private const string ReversalGuard =
        "Açılış bakiyesi ters kaydı yalnız bekleyen öneriden bir kez, öneren dışındaki kişinin onay veya ret kararıyla değişir.";

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public async Task Only_active_same_company_carry_blocks_initial_and_reversal_approval_without_consuming_the_attempt(
        bool reversal, bool reversedCarry, bool foreignCarry)
    {
        await using var run = new Run();
        var opening = (await run.ProposeAsync(Specialist, PartyRequest())).Resource!;
        OpeningBalanceView? reversing = null;
        if (reversal)
        {
            opening = (await run.ApproveAsync(Manager, new(opening.Id, opening.Version))).Resource!;
            reversing = (await run.ProposeReversalAsync(Specialist, new(opening.Id, opening.Version, Reason))).Resource!;
        }
        await run.SeedCarryAsync(reversedCarry, foreignCarry ? CompanyB : CompanyA);
        var commandsBefore = (await run.CommandsAsync()).Count;
        var movementsBefore = (await run.MovementsAsync()).Count;
        var result = reversal
            ? await run.ApproveReversalAsync(Manager, new(opening.Id, reversing!.Reversal!.Version))
            : await run.ApproveAsync(Manager, new(opening.Id, opening.Version));
        if (reversedCarry || foreignCarry)
        {
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            Assert.Equal(movementsBefore + 1, (await run.MovementsAsync()).Count);
        }
        else
        {
            AssertIssue(result, CommandOutcome.ValidationFailed, "asOfDate", "opening_balance_period_carried");
            Assert.Equal(movementsBefore, (await run.MovementsAsync()).Count);
            Assert.Equal(commandsBefore, (await run.CommandsAsync()).Count);
        }
    }

    [Fact]
    public void An_opening_balance_is_a_company_row_whose_live_slot_and_two_person_decision_are_held_by_the_database()
    {
        using var context = Context();
        var balances = context.Model.FindEntityType(typeof(OpeningBalanceRow))!;
        Assert.Equal((FinanceDbContext.Schema, "OpeningBalances"), (balances.GetSchema(), balances.GetTableName()));
        Assert.Single(balances.GetDeclaredQueryFilters());
        Assert.True(balances.FindProperty(nameof(OpeningBalanceRow.Version))!.IsConcurrencyToken);
        // The amount is exact text: no column could round it.
        Assert.DoesNotContain(balances.GetProperties(), property => property.ClrType == typeof(decimal)
            || property.ClrType == typeof(decimal?) || property.ClrType == typeof(double));

        var design = context.GetService<IDesignTimeModel>().Model;
        var designBalances = design.FindEntityType(typeof(OpeningBalanceRow))!;
        var decision = Assert.Single(designBalances.GetCheckConstraints(), check => check.Name == "CK_OpeningBalances_Decision");
        Assert.Equal(FinanceDbContext.OpeningBalanceDecisionCheck, decision.Sql);
        Assert.Contains("[DecidedBy] COLLATE Latin1_General_100_BIN2 <> [ProposedBy] COLLATE Latin1_General_100_BIN2",
            decision.Sql, StringComparison.Ordinal);
        var target = Assert.Single(designBalances.GetCheckConstraints(), check => check.Name == "CK_OpeningBalances_Target");
        Assert.Equal(FinanceDbContext.OpeningBalanceTargetCheck, target.Sql);
        var amount = Assert.Single(designBalances.GetCheckConstraints(), check => check.Name == "CK_OpeningBalances_Amount");
        Assert.Contains("[AmountText] LIKE '%[1-9]%'", amount.Sql, StringComparison.Ordinal);
        Assert.Single(designBalances.GetCheckConstraints(), check => check.Name == "CK_OpeningBalances_Identity");
        Assert.Single(designBalances.GetCheckConstraints(), check => check.Name == "CK_OpeningBalances_Version");
        Assert.Equal(FinanceDbContext.IdentityCollation,
            designBalances.FindProperty(nameof(OpeningBalanceRow.ProposedBy))!.GetCollation());
        Assert.Equal("date", designBalances.FindProperty(nameof(OpeningBalanceRow.AsOfDate))!.GetColumnType());

        // One live (proposed or approved) opening per target and currency; a rejected one frees the slot.
        var live = Assert.Single(designBalances.GetIndexes(),
            index => index.GetDatabaseName() == FinanceDbContext.OpeningBalanceLiveIndex);
        Assert.True(live.IsUnique);
        Assert.Equal([nameof(OpeningBalanceRow.CompanyId), nameof(OpeningBalanceRow.TargetKind),
            nameof(OpeningBalanceRow.TargetId), nameof(OpeningBalanceRow.CurrencyId)],
            live.Properties.Select(property => property.Name));
        Assert.Equal("[Status] IN (1, 2)", live.GetFilter());

        var commands = design.FindEntityType(typeof(OpeningBalanceCommandRow))!;
        Assert.Equal((FinanceDbContext.Schema, "OpeningBalanceCommands"), (commands.GetSchema(), commands.GetTableName()));
        var commandTarget = Assert.Single(commands.GetCheckConstraints(),
            check => check.Name == "CK_OpeningBalanceCommands_Target");
        Assert.EndsWith("JSON_VALUE([ResultJson], '$.outcome') = 'ValidationFailed')", commandTarget.Sql,
            StringComparison.Ordinal);
        Assert.Equal([nameof(OpeningBalanceCommandRow.CompanyId), nameof(OpeningBalanceCommandRow.CommandName),
            nameof(OpeningBalanceCommandRow.IdempotencyKeyHash)],
            commands.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Single(context.Model.FindEntityType(typeof(OpeningBalanceCommandRow))!.GetDeclaredQueryFilters());
    }

    [Fact]
    public void The_opening_balance_migration_is_pure_DDL_up_and_refuses_to_drop_stored_openings()
    {
        var migration = new AddOpeningBalances();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Equal(["OpeningBalances", "OpeningBalanceCommands"],
            migration.UpOperations.OfType<CreateTableOperation>().Select(operation => operation.Name));

        // Forward fix: the first Down operation is the one guard; only an empty store rolls back.
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
        Assert.Contains("THROW 51809", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[finance].[OpeningBalances]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[finance].[OpeningBalanceCommands]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("AddOpeningBalances is not rolled back (forward fix).", guard.Sql, StringComparison.Ordinal);
        Assert.Equal(["OpeningBalanceCommands", "OpeningBalances"],
            migration.DownOperations.OfType<DropTableOperation>().Select(operation => operation.Name));
    }

    [Theory]
    [InlineData("decided-by-proposer")]
    [InlineData("two-version-steps")]
    [InlineData("approved-with-reason")]
    [InlineData("rejected-without-reason")]
    [InlineData("decided-twice")]
    [InlineData("amount-changed")]
    public async Task An_opening_balance_only_leaves_its_proposal_once_by_another_persons_decision(string fault)
    {
        await using var run = new Run();
        var seeded = await run.SeedAsync(Proposal(fault == "decided-twice"
            ? FinanceOpeningBalanceService.Approved : FinanceOpeningBalanceService.Proposed));
        await using var context = run.Memory(CompanyA);
        var row = await context.OpeningBalances.SingleAsync(candidate => candidate.Id == seeded.Id);
        switch (fault)
        {
            case "decided-by-proposer":
                Decide(row, FinanceOpeningBalanceService.Approved, Specialist, null);
                break;
            case "two-version-steps":
                Decide(row, FinanceOpeningBalanceService.Approved, Manager, null);
                row.Version += 1;
                break;
            case "approved-with-reason":
                Decide(row, FinanceOpeningBalanceService.Approved, Manager, Reason);
                break;
            case "rejected-without-reason":
                Decide(row, FinanceOpeningBalanceService.Rejected, Manager, null);
                break;
            case "decided-twice":
                Decide(row, FinanceOpeningBalanceService.Rejected, Manager, Reason);
                break;
            default:
                Decide(row, FinanceOpeningBalanceService.Approved, Manager, null);
                row.AmountText = "1.00";
                break;
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        if (fault != "amount-changed")
        {
            Assert.Equal(Guard, exception.Message);
        }
    }

    [Fact]
    public async Task A_decision_by_another_person_passes_the_write_guard()
    {
        await using var run = new Run();
        var seeded = await run.SeedAsync(Proposal(FinanceOpeningBalanceService.Proposed));
        await using (var context = run.Memory(CompanyA))
        {
            Decide(await context.OpeningBalances.SingleAsync(candidate => candidate.Id == seeded.Id),
                FinanceOpeningBalanceService.Approved, Manager, null);
            await context.SaveChangesAsync();
        }

        var stored = await run.RowAsync(seeded.Id);
        Assert.Equal((FinanceOpeningBalanceService.Approved, 2L, Manager), (stored.Status, stored.Version, stored.DecidedBy));
    }

    [Theory]
    [InlineData("default-reader", CommandOutcome.Unavailable, "opening_period_unavailable")]
    [InlineData("unavailable", CommandOutcome.Unavailable, "opening_period_unavailable")]
    [InlineData("open-without-version", CommandOutcome.Unavailable, "opening_period_unavailable")]
    [InlineData("open-padded-version", CommandOutcome.Unavailable, "opening_period_unavailable")]
    [InlineData("closed", CommandOutcome.ValidationFailed, "opening_period_closed")]
    public async Task A_proposal_needs_the_Settings_opening_period_open(string fault, CommandOutcome outcome, string code)
    {
        await using var run = new Run();
        await run.SeedCashAsync();
        IFinanceOpeningPeriodReader period = fault switch
        {
            "default-reader" => new UnavailableFinanceOpeningPeriodReader(),
            "unavailable" => new FixedPeriod(FinanceOpeningPeriodAnswer.NotAvailable),
            "open-without-version" => new FixedPeriod(new(OpeningPeriodState.Open, null)),
            "open-padded-version" => new FixedPeriod(new(OpeningPeriodState.Open, " " + PeriodVersion)),
            _ => new FixedPeriod(new(OpeningPeriodState.Closed, PeriodVersion)),
        };

        AssertIssue(await run.ProposeAsync(Specialist, CashRequest(), period: period), outcome, "openingPeriod", code);
        Assert.Empty(await run.AllRowsAsync());
        Assert.Empty(await run.CommandsAsync());
    }

    [Fact]
    public async Task A_specialist_proposes_and_another_manager_approves_each_audited_with_the_period_version()
    {
        await using var run = new Run();
        await run.SeedCashAsync();
        var permissions = new RecordingPermission(PermissionDecision.Allowed);

        var proposed = await run.ProposeAsync(Specialist, CashRequest(), permissions: permissions);

        Assert.Equal((CommandOutcome.Succeeded, false), (proposed.Outcome, proposed.IsReplay));
        var view = proposed.Resource!;
        Assert.Equal((OpeningBalanceTargetKind.CashAccount, Cash, Currency, "2500.75", AsOf, OpeningBalanceStatus.Proposed,
                PeriodVersion, true, "1"),
            (view.TargetKind, view.TargetId, view.CurrencyId, view.Amount, view.AsOfDate, view.Status,
                view.OpeningPeriodVersion, view.IsOwnProposal, view.Version));
        Assert.Equal(((Guid?)null, (OpeningBalanceDirection?)null), (view.PartyId, view.Direction));
        Assert.All(permissions.Actions, action => Assert.Equal(FinanceAction.ProposeOpeningBalance, action));

        var approved = await run.ApproveAsync(Manager, new OpeningBalanceApproveRequest(view.Id, "1"));
        Assert.Equal((CommandOutcome.Succeeded, OpeningBalanceStatus.Approved, "2", false),
            (approved.Outcome, approved.Resource!.Status, approved.Resource.Version, approved.Resource.IsOwnProposal));
        var stored = await run.RowAsync(view.Id);
        Assert.Equal((Specialist, Manager, (string?)null), (stored.ProposedBy, stored.DecidedBy, stored.RejectionReason));

        var audits = await run.AuditsAsync(view.Id);
        Assert.Equal(["finance.opening-balance.proposed.v1", "finance.opening-balance.approved.v1"],
            audits.Select(audit => audit.EventType));
        Assert.Equal((ErpPermissions.FinanceOpeningBalancesPropose, Specialist, (long?)null, (long?)1),
            (audits[0].PermissionCode, audits[0].ActorId, audits[0].EntityVersionBefore, audits[0].EntityVersionAfter));
        Assert.Equal((ErpPermissions.FinanceOpeningBalancesApprove, Manager, (long?)1, (long?)2),
            (audits[1].PermissionCode, audits[1].ActorId, audits[1].EntityVersionBefore, audits[1].EntityVersionAfter));
        using var approval = JsonDocument.Parse(audits[1].ApprovalContextJson!);
        Assert.Equal(("FNS-D23", Specialist, Manager, PeriodVersion),
            (approval.RootElement.GetProperty("rule").GetString(), approval.RootElement.GetProperty("proposedBy").GetString(),
                approval.RootElement.GetProperty("approvedBy").GetString(),
                approval.RootElement.GetProperty("openingPeriodVersion").GetString()));
        Assert.Equal(2, (await run.CommandsAsync()).Count);
    }

    [Fact]
    public async Task The_proposer_never_decides_their_own_opening_and_nothing_is_stored()
    {
        await using var run = new Run();
        await run.SeedCashAsync();
        var proposed = (await run.ProposeAsync(Specialist, CashRequest())).Resource!;

        AssertIssue(await run.ApproveAsync(Specialist, new OpeningBalanceApproveRequest(proposed.Id, "1")),
            CommandOutcome.Denied, "approval", "finance_approver_is_proposer");
        AssertIssue(await run.RejectAsync(Specialist, new OpeningBalanceRejectRequest(proposed.Id, "1", Reason)),
            CommandOutcome.Denied, "approval", "finance_approver_is_proposer");

        var stored = await run.RowAsync(proposed.Id);
        Assert.Equal((FinanceOpeningBalanceService.Proposed, 1L), (stored.Status, stored.Version));
        Assert.Single(await run.CommandsAsync());
        Assert.Single(await run.AuditsAsync(proposed.Id));
    }

    [Fact]
    public async Task A_rejection_names_its_reason_and_frees_the_slot_for_a_new_proposal()
    {
        await using var run = new Run();
        await run.SeedCashAsync();
        var proposed = (await run.ProposeAsync(Specialist, CashRequest())).Resource!;

        AssertIssue(await run.ProposeAsync(Specialist, CashRequest()), CommandOutcome.ValidationFailed, "targetId",
            "opening_balance_exists");
        AssertIssue(await run.RejectAsync(Manager, new OpeningBalanceRejectRequest(proposed.Id, "1", "  ")),
            CommandOutcome.ValidationFailed, "reason", "reason_invalid");

        var rejected = await run.RejectAsync(Manager, new OpeningBalanceRejectRequest(proposed.Id, "1", Reason));
        Assert.Equal((CommandOutcome.Succeeded, OpeningBalanceStatus.Rejected, Reason),
            (rejected.Outcome, rejected.Resource!.Status, rejected.Resource.RejectionReason));
        var rejectAudit = (await run.AuditsAsync(proposed.Id)).Last();
        using var approval = JsonDocument.Parse(rejectAudit.ApprovalContextJson!);
        Assert.Equal(("finance.opening-balance.rejected.v1", Manager),
            (rejectAudit.EventType, approval.RootElement.GetProperty("rejectedBy").GetString()));
        Assert.False(approval.RootElement.TryGetProperty("approvedBy", out _));

        var again = await run.ProposeAsync(Specialist, CashRequest());
        Assert.Equal(CommandOutcome.Succeeded, again.Outcome);
        Assert.Equal(2, (await run.AllRowsAsync()).Count);
    }

    [Fact]
    public async Task A_decision_names_the_current_version_of_a_pending_proposal()
    {
        await using var run = new Run();
        await run.SeedCashAsync();
        var proposed = (await run.ProposeAsync(Specialist, CashRequest())).Resource!;

        var stale = await run.ApproveAsync(Manager, new OpeningBalanceApproveRequest(proposed.Id, "7"));
        AssertIssue(stale, CommandOutcome.VersionConflict, "expectedVersion", "version_conflict");
        Assert.Contains(await run.CommandsAsync(), command => command.OpeningBalanceId == proposed.Id
            && command.ResultJson.Contains("VersionConflict", StringComparison.Ordinal));

        AssertIssue(await run.ApproveAsync(Manager, new OpeningBalanceApproveRequest(Guid.NewGuid(), "1")),
            CommandOutcome.ValidationFailed, "openingBalanceId", "opening_balance_not_found");

        await run.ApproveAsync(Manager, new OpeningBalanceApproveRequest(proposed.Id, "1"));
        AssertIssue(await run.RejectAsync(Manager, new OpeningBalanceRejectRequest(proposed.Id, "2", Reason)),
            CommandOutcome.ValidationFailed, "openingBalanceId", "opening_balance_not_proposed");
        Assert.Equal(OpeningBalanceStatus.Approved, (await run.ListAsync(Manager)).Resource!.Items.Single().Status);
    }

    [Fact]
    public async Task A_decision_is_refused_once_the_period_is_closed()
    {
        await using var run = new Run();
        await run.SeedCashAsync();
        var proposed = (await run.ProposeAsync(Specialist, CashRequest())).Resource!;

        AssertIssue(await run.ApproveAsync(Manager, new OpeningBalanceApproveRequest(proposed.Id, "1"),
                period: new FixedPeriod(new(OpeningPeriodState.Closed, PeriodVersion))),
            CommandOutcome.ValidationFailed, "openingPeriod", "opening_period_closed");
        Assert.Equal(FinanceOpeningBalanceService.Proposed, (await run.RowAsync(proposed.Id)).Status);
    }

    [Theory]
    [InlineData("cash-proposed", "targetId", "opening_target_not_eligible")]
    [InlineData("cash-missing", "targetId", "opening_target_not_eligible")]
    [InlineData("cash-other-currency", "currencyId", "opening_currency_mismatch")]
    [InlineData("negative", "amount", "opening_amount_invalid")]
    [InlineData("zero", "amount", "opening_amount_invalid")]
    [InlineData("cash-with-party", "targetKind", "opening_target_kind_invalid")]
    [InlineData("party-without-direction", "direction", "opening_direction_required")]
    [InlineData("party-without-party", "partyId", "identity_required")]
    public async Task A_proposal_names_an_eligible_target_its_currency_and_an_exact_positive_amount(string fault,
        string field, string code)
    {
        await using var run = new Run();
        await run.SeedCashAsync(status: fault == "cash-proposed" ? 1 : 2);
        var request = fault switch
        {
            "cash-missing" => CashRequest() with { TargetId = Guid.NewGuid() },
            "cash-other-currency" => CashRequest() with { CurrencyId = OtherCurrency },
            "negative" => CashRequest() with { Amount = "-1" },
            "zero" => CashRequest() with { Amount = "0.00" },
            "cash-with-party" => CashRequest() with { PartyId = Party },
            "party-without-direction" => PartyRequest() with { Direction = null },
            "party-without-party" => PartyRequest() with { PartyId = null },
            _ => CashRequest(),
        };

        AssertIssue(await run.ProposeAsync(Specialist, request), CommandOutcome.ValidationFailed, field, code);
        Assert.Empty(await run.AllRowsAsync());
    }

    [Theory]
    [InlineData("found", CommandOutcome.Succeeded, null)]
    [InlineData("passive-account", CommandOutcome.ValidationFailed, "party_account_ineligible")]
    [InlineData("not-found", CommandOutcome.ValidationFailed, "party_account_ineligible")]
    [InlineData("unavailable", CommandOutcome.Unavailable, "party_account_unavailable")]
    [InlineData("not-a-candidate", CommandOutcome.ValidationFailed, "currency_not_a_candidate")]
    public async Task A_party_opening_needs_Counterpartys_active_pair_and_a_Settings_currency_candidate(string fault,
        CommandOutcome outcome, string? code)
    {
        await using var run = new Run();
        IFinancePartyAccountPort parties = fault switch
        {
            "passive-account" => new FixedParty(accountActive: false),
            "not-found" => new FixedParty(ReadOutcome.NotFound),
            "unavailable" => new FixedParty(ReadOutcome.Unavailable),
            _ => new FixedParty(),
        };
        var request = fault == "not-a-candidate" ? PartyRequest() with { CurrencyId = OtherCurrency } : PartyRequest();

        var result = await run.ProposeAsync(Specialist, request, parties: parties);

        if (code is null)
        {
            Assert.Equal(outcome, result.Outcome);
            Assert.Equal((OpeningBalanceTargetKind.PartyAccount, Party, OpeningBalanceDirection.Credit),
                (result.Resource!.TargetKind, result.Resource.PartyId, result.Resource.Direction));
        }
        else
        {
            AssertIssue(result, outcome, code == "currency_not_a_candidate" ? "currencyId" : "targetId", code);
        }
    }

    [Fact]
    public async Task The_same_key_answers_its_first_result_and_another_payload_or_actor_is_a_conflict()
    {
        await using var run = new Run();
        await run.SeedCashAsync();
        const string key = "SENTETIK-same-key";
        var first = await run.ProposeAsync(Specialist, CashRequest(), key: key, correlation: "SENTETIK-first");
        var again = await run.ProposeAsync(Specialist, CashRequest(), key: key, correlation: "SENTETIK-second");

        Assert.Equal((CommandOutcome.Succeeded, true, "SENTETIK-first", first.Resource!.Id),
            (again.Outcome, again.IsReplay, again.OriginalCorrelationId, again.Resource!.Id));
        Assert.Single(await run.AllRowsAsync());

        var otherPayload = await run.ProposeAsync(Specialist, CashRequest() with { Amount = "1" }, key: key);
        Assert.Equal((CommandOutcome.IdempotencyConflict, (OpeningBalanceView?)null),
            (otherPayload.Outcome, otherPayload.Resource));
        Assert.Equal(CommandOutcome.IdempotencyConflict,
            (await run.ProposeAsync("SENTETIK-other-specialist", CashRequest(), key: key)).Outcome);

        // The authority is measured afresh before a replay.
        var revoked = await run.ProposeAsync(Specialist, CashRequest(), key: key,
            permissions: new RecordingPermission(PermissionDecision.Denied));
        Assert.Equal((CommandOutcome.Denied, false), (revoked.Outcome, revoked.IsReplay));
    }

    [Theory]
    [InlineData(PermissionDecision.Denied, CommandOutcome.Denied)]
    [InlineData(PermissionDecision.Unavailable, CommandOutcome.Unavailable)]
    public async Task Without_the_propose_authority_nothing_is_asked_or_stored(PermissionDecision decision,
        CommandOutcome outcome)
    {
        await using var run = new Run();

        var result = await run.ProposeAsync(Specialist, PartyRequest(), permissions: new RecordingPermission(decision),
            period: new NoPeriodRead(), parties: new NoPartyRead());

        Assert.Equal(outcome, result.Outcome);
        Assert.Empty(await run.CommandsAsync());
    }

    [Fact]
    public async Task Approve_authority_alone_does_not_propose_and_propose_authority_alone_does_not_decide()
    {
        await using var run = new Run();
        await run.SeedCashAsync();
        Assert.Equal(CommandOutcome.Denied, (await run.ProposeAsync(Specialist, CashRequest(),
            permissions: new OnlyPermission(FinanceAction.ApproveOpeningBalance))).Outcome);
        var proposed = (await run.ProposeAsync(Specialist, CashRequest())).Resource!;

        var decided = await run.ApproveAsync(Manager, new OpeningBalanceApproveRequest(proposed.Id, "1"),
            permissions: new OnlyPermission(FinanceAction.ProposeOpeningBalance));
        Assert.Equal(CommandOutcome.Denied, decided.Outcome);
        Assert.Equal(FinanceOpeningBalanceService.Proposed, (await run.RowAsync(proposed.Id)).Status);
    }

    [Fact]
    public async Task Authority_lost_before_the_save_stores_nothing()
    {
        await using var run = new Run();
        await run.SeedCashAsync();

        // Revoke authority when the command stages its audit, after business reads and
        // immediately before saving. This does not depend on the number of earlier fences.
        var permission = new RevocablePermission();
        var result = await run.ProposeAsync(Specialist, CashRequest(),
            permissions: permission, auditObserved: () => permission.IsRevoked = true);

        Assert.Equal(CommandOutcome.Denied, result.Outcome);
        Assert.Empty(await run.AllRowsAsync());
        Assert.Empty(await run.CommandsAsync());
    }

    [Fact]
    public async Task The_list_is_shown_only_while_the_period_is_open_and_the_period_always_answers()
    {
        await using var run = new Run();
        await run.SeedCashAsync();
        await run.ProposeAsync(Specialist, CashRequest());

        var open = await run.ListAsync(Manager);
        Assert.Equal((ReadOutcome.Found, false), (open.Outcome, open.Resource!.Items.Single().IsOwnProposal));
        Assert.True((await run.ListAsync(Specialist)).Resource!.Items.Single().IsOwnProposal);

        var closed = new FixedPeriod(new(OpeningPeriodState.Closed, PeriodVersion));
        Assert.Equal(ReadOutcome.NotFound, (await run.ListAsync(Manager, period: closed)).Outcome);
        Assert.Equal(ReadOutcome.Unavailable,
            (await run.ListAsync(Manager, period: new UnavailableFinanceOpeningPeriodReader())).Outcome);

        var period = await run.PeriodAsync(Manager, closed);
        Assert.Equal((ReadOutcome.Found, OpeningPeriodState.Closed, PeriodVersion),
            (period.Outcome, period.Resource!.State, period.Resource.Version));
        var unavailable = await run.PeriodAsync(Manager, new UnavailableFinanceOpeningPeriodReader());
        Assert.Equal((OpeningPeriodState.Unavailable, (string?)null), (unavailable.Resource!.State, unavailable.Resource.Version));

        Assert.Equal(ReadOutcome.Denied, (await run.ListAsync(Manager,
            permissions: new OnlyPermission(FinanceAction.ApproveOpeningBalance))).Outcome);
        Assert.Equal(ReadOutcome.Denied, (await run.CurrencyCandidatesAsync(Manager,
            new OnlyPermission(FinanceAction.ReadOpeningBalances))).Outcome);
        Assert.Equal(ReadOutcome.Found, (await run.CurrencyCandidatesAsync(Specialist,
            new OnlyPermission(FinanceAction.ProposeOpeningBalance))).Outcome);
    }

    [Fact]
    public async Task Another_company_never_sees_or_decides_the_opening()
    {
        await using var run = new Run();
        await run.SeedCashAsync();
        var proposed = (await run.ProposeAsync(Specialist, CashRequest())).Resource!;

        Assert.Empty((await run.ListAsync(Manager, company: CompanyB)).Resource!.Items);
        AssertIssue(await run.ApproveAsync(Manager, new OpeningBalanceApproveRequest(proposed.Id, "1"), company: CompanyB),
            CommandOutcome.ValidationFailed, "openingBalanceId", "opening_balance_not_found");
        Assert.Equal(FinanceOpeningBalanceService.Proposed, (await run.RowAsync(proposed.Id)).Status);
    }

    [Fact]
    public void A_reversal_and_a_movement_are_company_rows_whose_single_live_slot_is_held_by_the_database()
    {
        using var context = Context();
        var design = context.GetService<IDesignTimeModel>().Model;

        var reversals = design.FindEntityType(typeof(OpeningBalanceReversalRow))!;
        Assert.Equal((FinanceDbContext.Schema, "OpeningBalanceReversals"), (reversals.GetSchema(), reversals.GetTableName()));
        Assert.Single(context.Model.FindEntityType(typeof(OpeningBalanceReversalRow))!.GetDeclaredQueryFilters());
        Assert.True(reversals.FindProperty(nameof(OpeningBalanceReversalRow.Version))!.IsConcurrencyToken);
        var decision = Assert.Single(reversals.GetCheckConstraints(),
            check => check.Name == "CK_OpeningBalanceReversals_Decision");
        Assert.Equal(FinanceDbContext.OpeningBalanceReversalDecisionCheck, decision.Sql);
        Assert.Contains("[DecidedBy] COLLATE Latin1_General_100_BIN2 <> [ProposedBy] COLLATE Latin1_General_100_BIN2",
            decision.Sql, StringComparison.Ordinal);
        Assert.Single(reversals.GetCheckConstraints(), check => check.Name == "CK_OpeningBalanceReversals_Identity");
        Assert.Single(reversals.GetCheckConstraints(), check => check.Name == "CK_OpeningBalanceReversals_Version");
        var live = Assert.Single(reversals.GetIndexes(),
            index => index.GetDatabaseName() == FinanceDbContext.OpeningBalanceReversalLiveIndex);
        Assert.True(live.IsUnique);
        Assert.Equal([nameof(OpeningBalanceReversalRow.CompanyId), nameof(OpeningBalanceReversalRow.OpeningBalanceId)],
            live.Properties.Select(property => property.Name));
        Assert.Equal("[Status] IN (1, 2)", live.GetFilter());

        var movements = design.FindEntityType(typeof(OpeningBalanceMovementRow))!;
        Assert.Equal((FinanceDbContext.Schema, "OpeningBalanceMovements"), (movements.GetSchema(), movements.GetTableName()));
        Assert.Single(context.Model.FindEntityType(typeof(OpeningBalanceMovementRow))!.GetDeclaredQueryFilters());
        // The amount is exact text: no column could round it.
        Assert.DoesNotContain(movements.GetProperties(), property => property.ClrType == typeof(decimal)
            || property.ClrType == typeof(decimal?) || property.ClrType == typeof(double));
        Assert.Equal("[Kind] IN (1, 2)",
            Assert.Single(movements.GetCheckConstraints(), check => check.Name == "CK_OpeningBalanceMovements_Kind").Sql);
        Assert.Equal(FinanceDbContext.OpeningBalanceTargetCheck,
            Assert.Single(movements.GetCheckConstraints(), check => check.Name == "CK_OpeningBalanceMovements_Target").Sql);
        Assert.Equal(FinanceDbContext.OpeningBalanceAmountCheck,
            Assert.Single(movements.GetCheckConstraints(), check => check.Name == "CK_OpeningBalanceMovements_Amount").Sql);
        Assert.Single(movements.GetCheckConstraints(), check => check.Name == "CK_OpeningBalanceMovements_Identity");
        // FNS-D84: one movement per opening and kind, whatever replays or races.
        var once = Assert.Single(movements.GetIndexes(),
            index => index.GetDatabaseName() == FinanceDbContext.OpeningBalanceMovementIndex);
        Assert.True(once.IsUnique);
        Assert.Null(once.GetFilter());
        Assert.Equal([nameof(OpeningBalanceMovementRow.CompanyId), nameof(OpeningBalanceMovementRow.OpeningBalanceId),
            nameof(OpeningBalanceMovementRow.Kind)], once.Properties.Select(property => property.Name));
        Assert.Equal("date", movements.FindProperty(nameof(OpeningBalanceMovementRow.OccurredOn))!.GetColumnType());

        // A reversed opening keeps its decision and frees the opening's live slot.
        var openingDecision = Assert.Single(design.FindEntityType(typeof(OpeningBalanceRow))!.GetCheckConstraints(),
            check => check.Name == "CK_OpeningBalances_Decision");
        Assert.Contains("[Status] IN (2, 4)", openingDecision.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reversal_and_movement_migration_is_pure_DDL_up_and_refuses_to_drop_stored_rows()
    {
        var migration = new AddOpeningBalanceReversalsAndMovements();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Equal(["OpeningBalanceMovements", "OpeningBalanceReversals"],
            migration.UpOperations.OfType<CreateTableOperation>().Select(operation => operation.Name).Order());

        // Forward fix: the first Down operation is the one guard; reversals, movements or a reversed opening stop it.
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
        Assert.Contains("THROW 51841", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[finance].[OpeningBalanceReversals]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[finance].[OpeningBalanceMovements]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[finance].[OpeningBalances]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("AddOpeningBalanceReversalsAndMovements is not rolled back (forward fix).", guard.Sql,
            StringComparison.Ordinal);
        Assert.Equal(["OpeningBalanceMovements", "OpeningBalanceReversals"],
            migration.DownOperations.OfType<DropTableOperation>().Select(operation => operation.Name).Order());
    }

    [Fact]
    public async Task An_opening_dated_after_todays_Istanbul_business_day_is_refused_and_today_is_accepted()
    {
        await using var run = new Run();
        await run.SeedCashAsync();

        // At is 12:00 in Europe/Istanbul on 16 November.
        AssertIssue(await run.ProposeAsync(Specialist, CashRequest() with { AsOfDate = new DateOnly(2026, 11, 17) }),
            CommandOutcome.ValidationFailed, "asOfDate", "opening_as_of_date_in_future");
        Assert.Empty(await run.AllRowsAsync());

        var today = await run.ProposeAsync(Specialist, CashRequest() with { AsOfDate = new DateOnly(2026, 11, 16) });
        Assert.Equal((CommandOutcome.Succeeded, new DateOnly(2026, 11, 16)), (today.Outcome, today.Resource!.AsOfDate));
    }

    [Fact]
    public async Task A_decision_is_refused_once_the_period_setting_changed_since_the_proposal()
    {
        await using var run = new Run();
        await run.SeedCashAsync();
        var proposed = (await run.ProposeAsync(Specialist, CashRequest())).Resource!;
        var changed = new FixedPeriod(new(OpeningPeriodState.Open, "SENTETIK-period-4"));

        AssertIssue(await run.ApproveAsync(Manager, new OpeningBalanceApproveRequest(proposed.Id, "1"), period: changed),
            CommandOutcome.ValidationFailed, "openingPeriod", "opening_period_version_changed");
        var stored = await run.RowAsync(proposed.Id);
        Assert.Equal((FinanceOpeningBalanceService.Proposed, 1L), (stored.Status, stored.Version));
        Assert.Empty(await run.MovementsAsync());
        Assert.Single(await run.AuditsAsync(proposed.Id));
    }

    [Fact]
    public async Task The_approval_writes_exactly_one_opening_movement_and_a_rejection_writes_none()
    {
        await using var run = new Run();
        await run.SeedCashAsync();
        var rejected = (await run.ProposeAsync(Specialist, CashRequest())).Resource!;
        await run.RejectAsync(Manager, new OpeningBalanceRejectRequest(rejected.Id, "1", Reason));
        Assert.Empty(await run.MovementsAsync());

        var proposed = (await run.ProposeAsync(Specialist, PartyRequest())).Resource!;
        const string key = "SENTETIK-approve-key";
        var approved = await run.ApproveAsync(Manager, new OpeningBalanceApproveRequest(proposed.Id, "1"), key: key);
        var replay = await run.ApproveAsync(Manager, new OpeningBalanceApproveRequest(proposed.Id, "1"), key: key);
        Assert.Equal((CommandOutcome.Succeeded, CommandOutcome.Succeeded, true),
            (approved.Outcome, replay.Outcome, replay.IsReplay));

        var movement = Assert.Single(await run.MovementsAsync());
        Assert.Equal((CompanyA, proposed.Id, FinanceOpeningBalanceService.OpeningMovement, Account, (Guid?)Party, Currency,
                "100", AsOf, Manager),
            (movement.CompanyId, movement.OpeningBalanceId, movement.Kind, movement.TargetId, movement.PartyId,
                movement.CurrencyId, movement.AmountText, movement.OccurredOn, movement.RecordedBy));
        Assert.Equal(FinanceOpeningBalanceService.CreditDirection, movement.Direction);
    }

    [Fact]
    public async Task An_approved_opening_is_corrected_by_an_approved_reversal_and_a_new_proposal()
    {
        await using var run = new Run();
        await run.SeedCashAsync();
        var opening = await ApprovedAsync(run);

        var proposed = await run.ProposeReversalAsync(Specialist,
            new OpeningBalanceReversalProposeRequest(opening.Id, "2", Reason));
        Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome);
        var reversal = proposed.Resource!.Reversal!;
        Assert.Equal((OpeningBalanceStatus.Approved, "2", OpeningBalanceReversalStatus.Proposed, Reason, PeriodVersion, true,
                "1"),
            (proposed.Resource.Status, proposed.Resource.Version, reversal.Status, reversal.Reason,
                reversal.OpeningPeriodVersion, reversal.IsOwnProposal, reversal.Version));
        Assert.Single(await run.MovementsAsync());

        var approved = await run.ApproveReversalAsync(Manager, new OpeningBalanceReversalApproveRequest(opening.Id, "1"));
        Assert.Equal((CommandOutcome.Succeeded, OpeningBalanceStatus.Reversed, "3", OpeningBalanceReversalStatus.Approved,
                "2", false),
            (approved.Outcome, approved.Resource!.Status, approved.Resource.Version, approved.Resource.Reversal!.Status,
                approved.Resource.Reversal.Version, approved.Resource.Reversal.IsOwnProposal));

        var stored = await run.RowAsync(opening.Id);
        Assert.Equal((FinanceOpeningBalanceService.Reversed, Manager, (string?)null),
            (stored.Status, stored.DecidedBy, stored.RejectionReason));
        var storedReversal = Assert.Single(await run.ReversalsAsync());
        Assert.Equal((Specialist, Manager), (storedReversal.ProposedBy, storedReversal.DecidedBy));
        var movements = (await run.MovementsAsync()).OrderBy(movement => movement.Kind).ToList();
        Assert.Equal([FinanceOpeningBalanceService.OpeningMovement, FinanceOpeningBalanceService.ReversalMovement],
            movements.Select(movement => movement.Kind));
        Assert.All(movements, movement => Assert.Equal(("2500.75", Cash, AsOf),
            (movement.AmountText, movement.TargetId, movement.OccurredOn)));

        var audits = await run.AuditsAsync(opening.Id);
        Assert.Contains(audits, audit => audit.EventType == "finance.opening-balance.reversal-proposed.v1"
            && audit.PermissionCode == ErpPermissions.FinanceOpeningBalancesPropose && audit.ActorId == Specialist);
        var approval = Assert.Single(audits, audit => audit.EventType == "finance.opening-balance.reversal-approved.v1");
        Assert.Equal((ErpPermissions.FinanceOpeningBalancesApprove, Manager, (long?)2, (long?)3),
            (approval.PermissionCode, approval.ActorId, approval.EntityVersionBefore, approval.EntityVersionAfter));
        using (var context = JsonDocument.Parse(approval.ApprovalContextJson!))
        {
            Assert.Equal(("FNS-D85", Specialist, Manager, PeriodVersion),
                (context.RootElement.GetProperty("rule").GetString(),
                    context.RootElement.GetProperty("proposedBy").GetString(),
                    context.RootElement.GetProperty("approvedBy").GetString(),
                    context.RootElement.GetProperty("openingPeriodVersion").GetString()));
        }

        // FNS-D85: the correction is a new proposal; a reversed opening never takes another reversal.
        Assert.Equal(CommandOutcome.Succeeded, (await run.ProposeAsync(Specialist, CashRequest())).Outcome);
        AssertIssue(await run.ProposeReversalAsync(Specialist,
                new OpeningBalanceReversalProposeRequest(opening.Id, "3", Reason)),
            CommandOutcome.ValidationFailed, "openingBalanceId", "opening_balance_not_approved");
    }

    [Fact]
    public async Task A_rejected_reversal_names_its_reason_writes_no_movement_and_frees_the_slot()
    {
        await using var run = new Run();
        await run.SeedCashAsync();
        var opening = await ApprovedAsync(run);
        await run.ProposeReversalAsync(Specialist, new OpeningBalanceReversalProposeRequest(opening.Id, "2", Reason));

        AssertIssue(await run.RejectReversalAsync(Manager, new OpeningBalanceReversalRejectRequest(opening.Id, "1", " ")),
            CommandOutcome.ValidationFailed, "reason", "reason_invalid");
        var rejected = await run.RejectReversalAsync(Manager,
            new OpeningBalanceReversalRejectRequest(opening.Id, "1", "SENTETIK ters kayıt reddi"));
        Assert.Equal((CommandOutcome.Succeeded, OpeningBalanceStatus.Approved, "2", OpeningBalanceReversalStatus.Rejected,
                "SENTETIK ters kayıt reddi"),
            (rejected.Outcome, rejected.Resource!.Status, rejected.Resource.Version, rejected.Resource.Reversal!.Status,
                rejected.Resource.Reversal.RejectionReason));
        Assert.Single(await run.MovementsAsync());
        var audit = Assert.Single(await run.AuditsAsync(opening.Id),
            candidate => candidate.EventType == "finance.opening-balance.reversal-rejected.v1");
        using (var context = JsonDocument.Parse(audit.ApprovalContextJson!))
        {
            Assert.Equal(Manager, context.RootElement.GetProperty("rejectedBy").GetString());
            Assert.False(context.RootElement.TryGetProperty("approvedBy", out _));
        }

        var again = await run.ProposeReversalAsync(Specialist,
            new OpeningBalanceReversalProposeRequest(opening.Id, "2", Reason));
        Assert.Equal((CommandOutcome.Succeeded, OpeningBalanceReversalStatus.Proposed),
            (again.Outcome, again.Resource!.Reversal!.Status));
        Assert.Equal(2, (await run.ReversalsAsync()).Count);
    }

    [Fact]
    public async Task A_reversal_needs_an_approved_opening_one_live_slot_another_decider_and_current_versions()
    {
        await using var run = new Run();
        await run.SeedCashAsync();
        var pending = (await run.ProposeAsync(Specialist, CashRequest())).Resource!;
        AssertIssue(await run.ProposeReversalAsync(Specialist,
                new OpeningBalanceReversalProposeRequest(pending.Id, "1", Reason)),
            CommandOutcome.ValidationFailed, "openingBalanceId", "opening_balance_not_approved");
        AssertIssue(await run.ApproveReversalAsync(Manager, new OpeningBalanceReversalApproveRequest(pending.Id, "1")),
            CommandOutcome.ValidationFailed, "openingBalanceId", "opening_balance_reversal_not_proposed");
        AssertIssue(await run.ProposeReversalAsync(Specialist,
                new OpeningBalanceReversalProposeRequest(Guid.NewGuid(), "1", Reason)),
            CommandOutcome.ValidationFailed, "openingBalanceId", "opening_balance_not_found");

        await run.ApproveAsync(Manager, new OpeningBalanceApproveRequest(pending.Id, "1"));
        AssertIssue(await run.ProposeReversalAsync(Specialist,
                new OpeningBalanceReversalProposeRequest(pending.Id, "1", Reason)),
            CommandOutcome.VersionConflict, "expectedVersion", "version_conflict");
        AssertIssue(await run.ProposeReversalAsync(Specialist,
                new OpeningBalanceReversalProposeRequest(pending.Id, "2", "  ")),
            CommandOutcome.ValidationFailed, "reason", "reason_invalid");
        await run.ProposeReversalAsync(Specialist, new OpeningBalanceReversalProposeRequest(pending.Id, "2", Reason));
        AssertIssue(await run.ProposeReversalAsync("SENTETIK-other-specialist",
                new OpeningBalanceReversalProposeRequest(pending.Id, "2", Reason)),
            CommandOutcome.ValidationFailed, "openingBalanceId", "opening_balance_reversal_exists");

        AssertIssue(await run.ApproveReversalAsync(Specialist, new OpeningBalanceReversalApproveRequest(pending.Id, "1")),
            CommandOutcome.Denied, "approval", "finance_approver_is_proposer");
        AssertIssue(await run.RejectReversalAsync(Specialist,
                new OpeningBalanceReversalRejectRequest(pending.Id, "1", Reason)),
            CommandOutcome.Denied, "approval", "finance_approver_is_proposer");
        AssertIssue(await run.ApproveReversalAsync(Manager, new OpeningBalanceReversalApproveRequest(pending.Id, "5")),
            CommandOutcome.VersionConflict, "expectedVersion", "version_conflict");
        AssertIssue(await run.ApproveReversalAsync(Manager, new OpeningBalanceReversalApproveRequest(pending.Id, "1"),
                period: new FixedPeriod(new(OpeningPeriodState.Open, "SENTETIK-period-4"))),
            CommandOutcome.ValidationFailed, "openingPeriod", "opening_period_version_changed");
        AssertIssue(await run.ApproveReversalAsync(Manager, new OpeningBalanceReversalApproveRequest(pending.Id, "1"),
                period: new FixedPeriod(new(OpeningPeriodState.Closed, PeriodVersion))),
            CommandOutcome.ValidationFailed, "openingPeriod", "opening_period_closed");
        Assert.Equal(CommandOutcome.Denied, (await run.ApproveReversalAsync(Manager,
            new OpeningBalanceReversalApproveRequest(pending.Id, "1"),
            permissions: new OnlyPermission(FinanceAction.ProposeOpeningBalance))).Outcome);
        Assert.Equal(CommandOutcome.Denied, (await run.ProposeReversalAsync(Specialist,
            new OpeningBalanceReversalProposeRequest(pending.Id, "2", Reason),
            permissions: new OnlyPermission(FinanceAction.ApproveOpeningBalance))).Outcome);
        AssertIssue(await run.ProposeReversalAsync(Specialist,
                new OpeningBalanceReversalProposeRequest(pending.Id, "2", Reason),
                period: new FixedPeriod(FinanceOpeningPeriodAnswer.NotAvailable)),
            CommandOutcome.Unavailable, "openingPeriod", "opening_period_unavailable");

        var stored = await run.RowAsync(pending.Id);
        Assert.Equal((FinanceOpeningBalanceService.Approved, 2L), (stored.Status, stored.Version));
        var reversal = Assert.Single(await run.ReversalsAsync());
        Assert.Equal((FinanceOpeningBalanceService.Proposed, 1L), (reversal.Status, reversal.Version));
        Assert.Single(await run.MovementsAsync());
    }

    [Fact]
    public async Task The_same_reversal_key_answers_its_first_result_and_another_company_never_reverses()
    {
        await using var run = new Run();
        await run.SeedCashAsync();
        var opening = await ApprovedAsync(run);
        const string key = "SENTETIK-reversal-key";
        var request = new OpeningBalanceReversalProposeRequest(opening.Id, "2", Reason);

        var first = await run.ProposeReversalAsync(Specialist, request, key: key);
        var again = await run.ProposeReversalAsync(Specialist, request, key: key);
        Assert.Equal((CommandOutcome.Succeeded, true, first.Resource!.Reversal!.Id),
            (again.Outcome, again.IsReplay, again.Resource!.Reversal!.Id));
        Assert.Equal(CommandOutcome.IdempotencyConflict,
            (await run.ProposeReversalAsync(Specialist, request with { Reason = "SENTETIK başka" }, key: key)).Outcome);
        Assert.Single(await run.ReversalsAsync());

        AssertIssue(await run.ApproveReversalAsync(Manager, new OpeningBalanceReversalApproveRequest(opening.Id, "1"),
                company: CompanyB),
            CommandOutcome.ValidationFailed, "openingBalanceId", "opening_balance_not_found");
        Assert.Equal(FinanceOpeningBalanceService.Approved, (await run.RowAsync(opening.Id)).Status);
    }

    [Theory]
    [InlineData("decided-by-proposer")]
    [InlineData("two-version-steps")]
    [InlineData("approved-with-reason")]
    [InlineData("rejected-without-reason")]
    [InlineData("decided-twice")]
    [InlineData("reason-changed")]
    public async Task A_reversal_only_leaves_its_proposal_once_by_another_persons_decision(string fault)
    {
        await using var run = new Run();
        var opening = await run.SeedAsync(Decided(FinanceOpeningBalanceService.Approved));
        var seeded = await run.SeedReversalAsync(ReversalProposal(opening.Id, fault == "decided-twice"
            ? FinanceOpeningBalanceService.Approved : FinanceOpeningBalanceService.Proposed));
        await using var context = run.Memory(CompanyA);
        var row = await context.OpeningBalanceReversals.SingleAsync(candidate => candidate.Id == seeded.Id);
        switch (fault)
        {
            case "decided-by-proposer":
                DecideReversal(row, FinanceOpeningBalanceService.Approved, Specialist, null);
                break;
            case "two-version-steps":
                DecideReversal(row, FinanceOpeningBalanceService.Approved, Manager, null);
                row.Version += 1;
                break;
            case "approved-with-reason":
                DecideReversal(row, FinanceOpeningBalanceService.Approved, Manager, Reason);
                break;
            case "rejected-without-reason":
                DecideReversal(row, FinanceOpeningBalanceService.Rejected, Manager, null);
                break;
            case "decided-twice":
                DecideReversal(row, FinanceOpeningBalanceService.Rejected, Manager, Reason);
                break;
            default:
                DecideReversal(row, FinanceOpeningBalanceService.Approved, Manager, null);
                row.Reason = "SENTETIK değişti";
                break;
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        if (fault != "reason-changed")
        {
            Assert.Equal(ReversalGuard, exception.Message);
        }
    }

    [Theory]
    [InlineData("reversed-with-new-decider")]
    [InlineData("reversed-with-reason")]
    [InlineData("reversed-again")]
    [InlineData("rejected-to-reversed")]
    public async Task An_opening_only_becomes_reversed_from_approved_with_its_decision_kept(string fault)
    {
        await using var run = new Run();
        var seeded = await run.SeedAsync(Decided(fault switch
        {
            "reversed-again" => FinanceOpeningBalanceService.Reversed,
            "rejected-to-reversed" => FinanceOpeningBalanceService.Rejected,
            _ => FinanceOpeningBalanceService.Approved,
        }));
        await using var context = run.Memory(CompanyA);
        var row = await context.OpeningBalances.SingleAsync(candidate => candidate.Id == seeded.Id);
        row.Status = FinanceOpeningBalanceService.Reversed;
        row.Version += 1;
        row.UpdatedAtUtc = At;
        if (fault == "reversed-with-new-decider")
        {
            row.DecidedBy = "SENTETIK-other-manager";
        }
        else if (fault == "reversed-with-reason")
        {
            row.RejectionReason = Reason;
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Equal(Guard, exception.Message);
    }

    [Fact]
    public async Task An_approved_opening_marked_reversed_with_its_decision_kept_passes_the_write_guard()
    {
        await using var run = new Run();
        var seeded = await run.SeedAsync(Decided(FinanceOpeningBalanceService.Approved));
        await using (var context = run.Memory(CompanyA))
        {
            var row = await context.OpeningBalances.SingleAsync(candidate => candidate.Id == seeded.Id);
            row.Status = FinanceOpeningBalanceService.Reversed;
            row.Version += 1;
            row.UpdatedAtUtc = At;
            await context.SaveChangesAsync();
        }

        var stored = await run.RowAsync(seeded.Id);
        Assert.Equal((FinanceOpeningBalanceService.Reversed, 3L, Manager), (stored.Status, stored.Version, stored.DecidedBy));
    }

    private static async Task<OpeningBalanceView> ApprovedAsync(Run run)
    {
        var proposed = (await run.ProposeAsync(Specialist, CashRequest())).Resource!;
        return (await run.ApproveAsync(Manager, new OpeningBalanceApproveRequest(proposed.Id, "1"))).Resource!;
    }

    private static OpeningBalanceRow Decided(int status)
    {
        var row = Proposal(status);
        row.Version = 2;
        row.RejectionReason = status == FinanceOpeningBalanceService.Rejected ? Reason : null;
        return row;
    }

    private static OpeningBalanceReversalRow ReversalProposal(Guid openingBalanceId, int status) => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, OpeningBalanceId = openingBalanceId, Status = status, Reason = Reason,
        OpeningPeriodVersion = PeriodVersion, ProposedBy = Specialist, ProposedAtUtc = At, Version = 1, UpdatedAtUtc = At,
        DecidedBy = status == FinanceOpeningBalanceService.Proposed ? null : Manager,
        DecidedAtUtc = status == FinanceOpeningBalanceService.Proposed ? null : At,
    };

    private static void DecideReversal(OpeningBalanceReversalRow row, int status, string decidedBy, string? reason)
    {
        row.Status = status;
        row.DecidedBy = decidedBy;
        row.DecidedAtUtc = At;
        row.RejectionReason = reason;
        row.UpdatedAtUtc = At;
        row.Version += 1;
    }

    private static OpeningBalanceProposeRequest CashRequest() =>
        new(OpeningBalanceTargetKind.CashAccount, Cash, null, Currency, null, "2500.75", AsOf);

    private static OpeningBalanceProposeRequest PartyRequest() =>
        new(OpeningBalanceTargetKind.PartyAccount, Account, Party, Currency, OpeningBalanceDirection.Credit, "100", AsOf);

    private static OpeningBalanceRow Proposal(int status) => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, TargetKind = FinanceOpeningBalanceService.CashAccountKind,
        TargetId = Cash, CurrencyId = Currency, AmountText = "10", AsOfDate = AsOf, Status = status,
        OpeningPeriodVersion = PeriodVersion, ProposedBy = Specialist, ProposedAtUtc = At, Version = 1, UpdatedAtUtc = At,
        DecidedBy = status == FinanceOpeningBalanceService.Proposed ? null : Manager,
        DecidedAtUtc = status == FinanceOpeningBalanceService.Proposed ? null : At,
    };

    private static void Decide(OpeningBalanceRow row, int status, string decidedBy, string? reason)
    {
        row.Status = status;
        row.DecidedBy = decidedBy;
        row.DecidedAtUtc = At;
        row.RejectionReason = reason;
        row.UpdatedAtUtc = At;
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

    private static FinanceOpeningBalanceEnvelopes Envelopes() => new(
        new FinanceAuditEnvelope(ErpPermissions.FinanceOpeningBalancesPropose, ["SENTETIK-FINANCE"], SourceChannels.Web),
        new FinanceAuditEnvelope(ErpPermissions.FinanceOpeningBalancesApprove, ["SENTETIK-FINANCE"], SourceChannels.Web));

    private static void AssertIssue(OpeningBalanceCommandResult result, CommandOutcome outcome, string field, string code)
    {
        Assert.Equal(outcome, result.Outcome);
        Assert.Null(result.Resource);
        var issue = Assert.Single(result.Issues);
        Assert.Equal((field, code), (issue.Field, issue.Code));
    }

    private static string Key() => $"SENTETIK-{Guid.NewGuid():N}";

    /// <summary>
    /// The real service over one in-memory store: every call is a fresh scope (context, company, user, audit collector
    /// and interceptor) like a request, on a fixed clock and an open period unless a test names another.
    /// </summary>
    private sealed class Run : IAsyncDisposable
    {
        private readonly string databaseName = $"finance-opening-balances-{Guid.NewGuid():N}";

        public FinanceDbContext Memory(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(databaseName).Options, companyContext);
        }

        public async Task<OpeningBalanceRow> SeedAsync(OpeningBalanceRow row)
        {
            await using var context = Memory(row.CompanyId);
            context.OpeningBalances.Add(row);
            await context.SaveChangesAsync();
            return row;
        }

        public async Task SeedCarryAsync(bool reversed, Guid companyId)
        {
            await using var context = Memory(companyId);
            var actor = Guid.NewGuid();
            var source = new FinancePeriodCarrySourceV1(Guid.NewGuid(), companyId, Guid.NewGuid(), 3,
                new(2026, 10, 1), new(2026, 10, 31), Guid.NewGuid(), new(2026, 11, 1), new(2026, 11, 30), actor, At);
            var json = JsonSerializer.Serialize(source, FinanceJson.CreateOptions());
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json + "|[]")));
            var voucher = new FinancePeriodCarryVoucherRow { Id = Guid.NewGuid(), CompanyId = companyId, ClosureId = source.ClosureId,
                SourcePeriodId = source.SourcePeriodId, ClosedPeriodVersion = 3, SourceEndsOn = source.SourceEndsOn,
                TargetPeriodId = source.TargetPeriodId, TargetStartsOn = source.TargetStartsOn, SourceJson = json,
                SourceHash = hash, LinesJson = "[]", RecordedBy = actor, RecordedAtUtc = At };
            context.PeriodCarryVouchers.Add(voucher);
            if (reversed) context.PeriodCarryReversals.Add(new() { Id = Guid.NewGuid(), CompanyId = companyId,
                ClosureId = source.ClosureId, VoucherId = voucher.Id, RecordedBy = Guid.NewGuid(), RecordedAtUtc = At,
                Reason = Reason, SourceHash = hash });
            await context.SaveChangesAsync();
        }

        public async Task SeedCashAsync(int status = 2)
        {
            await using var context = Memory(CompanyA);
            context.CashAccounts.Add(new CashAccountRow
            {
                Id = Cash, CompanyId = CompanyA, CurrencyId = Currency, DisplayName = "SENTETIK kasa", Status = status,
                Version = status == 1 ? 1 : 2, ProposedBy = "SENTETIK-cash-maker", ProposedAtUtc = At,
                DecidedBy = status == 1 ? null : "SENTETIK-cash-checker", DecidedAtUtc = status == 1 ? null : At,
                UpdatedAtUtc = At,
            });
            await context.SaveChangesAsync();
        }

        public Task<OpeningBalanceCommandResult> ProposeAsync(string actor, OpeningBalanceProposeRequest request,
            string? key = null, IFinancePermissionPort? permissions = null, IFinanceOpeningPeriodReader? period = null,
            IFinancePartyAccountPort? parties = null, string? correlation = null, Action? auditObserved = null) =>
            CallAsync(actor, CompanyA, permissions, period, parties, correlation,
                (service, finance) => service.ProposeAsync(finance, new CommandMetadata(key ?? Key()), request,
                    CancellationToken.None), auditObserved);

        public Task<OpeningBalanceCommandResult> ApproveAsync(string actor, OpeningBalanceApproveRequest request,
            IFinancePermissionPort? permissions = null, IFinanceOpeningPeriodReader? period = null, Guid? company = null,
            string? key = null) =>
            CallAsync(actor, company ?? CompanyA, permissions, period, null, null,
                (service, finance) => service.ApproveAsync(finance, new CommandMetadata(key ?? Key()), request,
                    CancellationToken.None));

        public Task<OpeningBalanceCommandResult> ProposeReversalAsync(string actor,
            OpeningBalanceReversalProposeRequest request, string? key = null, IFinancePermissionPort? permissions = null,
            IFinanceOpeningPeriodReader? period = null) =>
            CallAsync(actor, CompanyA, permissions, period, null, null,
                (service, finance) => service.ProposeReversalAsync(finance, new CommandMetadata(key ?? Key()), request,
                    CancellationToken.None));

        public Task<OpeningBalanceCommandResult> ApproveReversalAsync(string actor,
            OpeningBalanceReversalApproveRequest request, IFinancePermissionPort? permissions = null,
            IFinanceOpeningPeriodReader? period = null, Guid? company = null) =>
            CallAsync(actor, company ?? CompanyA, permissions, period, null, null,
                (service, finance) => service.ApproveReversalAsync(finance, new CommandMetadata(Key()), request,
                    CancellationToken.None));

        public Task<OpeningBalanceCommandResult> RejectReversalAsync(string actor,
            OpeningBalanceReversalRejectRequest request) =>
            CallAsync(actor, CompanyA, null, null, null, null,
                (service, finance) => service.RejectReversalAsync(finance, new CommandMetadata(Key()), request,
                    CancellationToken.None));

        public Task<OpeningBalanceCommandResult> RejectAsync(string actor, OpeningBalanceRejectRequest request) =>
            CallAsync(actor, CompanyA, null, null, null, null,
                (service, finance) => service.RejectAsync(finance, new CommandMetadata(Key()), request,
                    CancellationToken.None));

        public Task<ReadResult<OpeningBalanceListPage>> ListAsync(string actor, IFinancePermissionPort? permissions = null,
            IFinanceOpeningPeriodReader? period = null, Guid? company = null) =>
            CallAsync(actor, company ?? CompanyA, permissions, period, null, null,
                (service, finance) => service.ListAsync(finance, CancellationToken.None));

        public Task<ReadResult<OpeningPeriodView>> PeriodAsync(string actor, IFinanceOpeningPeriodReader period) =>
            CallAsync(actor, CompanyA, null, period, null, null,
                (service, finance) => service.GetPeriodAsync(finance, CancellationToken.None));

        public Task<ReadResult<OpeningBalanceCurrencyCandidates>> CurrencyCandidatesAsync(string actor,
            IFinancePermissionPort permissions) =>
            CallAsync(actor, CompanyA, permissions, null, null, null,
                (service, finance) => service.CurrencyCandidatesAsync(finance, CancellationToken.None));

        public async Task<OpeningBalanceRow> RowAsync(Guid id)
        {
            await using var context = Memory(CompanyA);
            return await context.OpeningBalances.AsNoTracking().SingleAsync(row => row.Id == id);
        }

        public async Task<List<OpeningBalanceRow>> AllRowsAsync()
        {
            await using var context = Memory(CompanyA);
            return await context.OpeningBalances.AsNoTracking().ToListAsync();
        }

        public async Task<OpeningBalanceReversalRow> SeedReversalAsync(OpeningBalanceReversalRow row)
        {
            await using var context = Memory(row.CompanyId);
            context.OpeningBalanceReversals.Add(row);
            await context.SaveChangesAsync();
            return row;
        }

        public async Task<List<OpeningBalanceReversalRow>> ReversalsAsync()
        {
            await using var context = Memory(CompanyA);
            return await context.OpeningBalanceReversals.AsNoTracking().ToListAsync();
        }

        public async Task<List<OpeningBalanceMovementRow>> MovementsAsync()
        {
            await using var context = Memory(CompanyA);
            return await context.OpeningBalanceMovements.AsNoTracking().ToListAsync();
        }

        public async Task<List<OpeningBalanceCommandRow>> CommandsAsync()
        {
            await using var context = Memory(CompanyA);
            return await context.OpeningBalanceCommands.AsNoTracking().ToListAsync();
        }

        public async Task<List<AuditEvent>> AuditsAsync(Guid id)
        {
            var entityId = id.ToString("D");
            await using var context = Memory(CompanyA);
            return await context.Set<AuditEvent>().AsNoTracking().Where(audit => audit.EntityId == entityId)
                .OrderBy(audit => audit.EntityVersionAfter).ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await using var context = Memory(CompanyA);
            await context.Database.EnsureDeletedAsync();
        }

        private async Task<T> CallAsync<T>(string actor, Guid companyId, IFinancePermissionPort? permissions,
            IFinanceOpeningPeriodReader? period, IFinancePartyAccountPort? parties, string? correlation,
            Func<FinanceOpeningBalanceService, FinanceContext, Task<T>> call, Action? auditObserved = null)
        {
            var company = new CompanyContext();
            company.Set(companyId);
            var collector = new AuditEventCollector();
            await using var context = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(databaseName)
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options, company);
            var service = new FinanceOpeningBalanceService(context, company, new FixedUser(actor, companyId),
                permissions ?? new RecordingPermission(PermissionDecision.Allowed),
                new Candidates([new MoneyCurrencyCandidateV1(Currency, "TRY", "SENTETIK Lira")]),
                parties ?? new FixedParty(),
                period ?? new FixedPeriod(new FinanceOpeningPeriodAnswer(OpeningPeriodState.Open, PeriodVersion)),
                new ObservedAuditCollector(collector, auditObserved), Envelopes(), new FixedClock(), new SyntheticSufficientTreasuryBalanceGuard());
            return await call(service, new FinanceContext(companyId, actor, correlation ?? "SENTETIK-correlation", null));
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => At;
    }

    private sealed class FixedPeriod(FinanceOpeningPeriodAnswer answer) : IFinanceOpeningPeriodReader
    {
        public Task<FinanceOpeningPeriodAnswer> ReadAsync(Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult(answer);
    }

    /// <summary>Never reached: the refusal comes before Settings is asked for the period.</summary>
    private sealed class NoPeriodRead : IFinanceOpeningPeriodReader
    {
        public Task<FinanceOpeningPeriodAnswer> ReadAsync(Guid companyId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The opening period must not be asked before the refusal.");
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

    /// <summary>A caller allowed exactly one action.</summary>
    private sealed class OnlyPermission(FinanceAction allowed) : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) =>
            Task.FromResult(action == allowed ? PermissionDecision.Allowed : PermissionDecision.Denied);
    }

    /// <summary>Allows <paramref name="revoked"/> for its first <paramref name="allowed"/> checks, then denies it.</summary>
    private sealed class ObservedAuditCollector(IAuditEventCollector inner, Action? observed) : IAuditEventCollector
    {
        public void Enqueue(AuditEventDraft auditEvent)
        {
            inner.Enqueue(auditEvent);
            observed?.Invoke();
        }
    }

    private sealed class RevocablePermission : IFinancePermissionPort
    {
        public bool IsRevoked { get; set; }

        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) =>
            Task.FromResult(IsRevoked ? PermissionDecision.Denied : PermissionDecision.Allowed);
    }

    /// <summary>Counterparty's answer for the (party, company account) pair the request names.</summary>
    private sealed class FixedParty(ReadOutcome outcome = ReadOutcome.Found, bool accountActive = true)
        : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
            Guid companyAccountId, CancellationToken cancellationToken) =>
            Task.FromResult(outcome == ReadOutcome.Found
                ? new ReadResult<PartyAccountEligibility>(ReadOutcome.Found, new PartyAccountEligibility(partyId,
                    companyAccountId, context.CompanyId, accountActive, Label: null, IsPartyActive: true))
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
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(candidates);
    }

    private sealed class FixedUser(string userId, Guid companyId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { companyId };
        public bool CanAccessCompany(Guid company) => AllowedCompanyIds.Contains(company);
    }
}
