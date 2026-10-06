using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// FIN-T05a against a REAL SQL Server: the Record of a realized manual bank receipt (card #66 5743678389).
/// <para>
/// Q1 was answered by Sinan's K1 (#66 5748258299): occurredOn decides. The module's fallback rule
/// decides nothing and every Record is refused (<see cref="Undecided_period_rule_refuses_every_record_and_stores_nothing"/>).
/// The other cases inject <see cref="SyntheticOpenPeriodRule"/>, a TEST INPUT that answers "open" for any date: it
/// proves the one-event write, the ADR-011 answers and the race, NOT a period policy. Owner ports (bank account, party
/// account, money profile, permission) are isolated synthetic ones, as in the draft tests. Every identity, scale and
/// currency is SENTETIK / NOT_ENV test data. This is not a finance delivery and claims no user outcome.
/// </para>
/// </summary>
public sealed class FinanceRecordSqlServerRelationalAcceptanceTests
{
    private const string Operation = "SENTETIK-operation";
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Currency = Guid.Parse("6e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a91");
    private static readonly Guid ProfileId = Guid.Parse("6e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a92");
    private static readonly DateOnly OccurredOn = new DateOnly(2026, 9, 21);
    private static readonly DateOnly ValueOn = new DateOnly(2026, 9, 22);

    [SqlServerFact]
    public async Task Undecided_period_rule_refuses_every_record_and_stores_nothing()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var draft = await run.SaveDraftAsync(owners, "1250.50");

        await using (var a = run.Service(CompanyA, owners, productRule: true))
        {
            var refused = await a.Service.RecordAsync(a.Context, Key("r-1"), new RecordRequest(draft.Id, draft.Version), CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
            Assert.Equal(new ValidationIssue("period", RecordPeriodAnswer.RuleUnavailableCode), Assert.Single(refused.Issues));
            Assert.Null(refused.Resource);
        }

        // Nothing moved: no money event, the source is still a draft at its version, and the refusal is not the
        // key's answer (a corrected retry is measured again).
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal((0, 1L), (await run.ScalarAsync<int>("SELECT Stage FROM finance.ReceiptDrafts"),
            await run.ScalarAsync<long>("SELECT Version FROM finance.ReceiptDrafts")));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands WHERE CommandName = @c",
            ("@c", ReceiptDraftService.RecordCommand)));
    }

    [SqlServerFact]
    public async Task A_record_makes_one_money_event_and_every_answer_of_its_key_holds()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var draft = await run.SaveDraftAsync(owners, "1250.50");

        CommandResult recorded;
        await using (var a = run.Service(CompanyA, owners))
        {
            recorded = await a.Service.RecordAsync(a.Context, Key("r-1"), new RecordRequest(draft.Id, draft.Version), CancellationToken.None);
        }

        Assert.Equal(CommandOutcome.Succeeded, recorded.Outcome);
        var source = recorded.Resource!;
        Assert.Equal((SourceStage.Recorded, "2"), (source.Source.Stage, source.Source.Version));
        Assert.NotNull(source.RecordedAtUtc);
        // No allocation relation exists yet: unallocated = the source amount − 0 active allocations, known and exact.
        Assert.Equal((AllocationKnowledge.Known, "1250.50"), (source.Unallocated.Knowledge, source.Unallocated.Amount.Money!.Amount));

        // ONE money event, its facts kept apart as Ö5 keeps them; the source records who and when.
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents WHERE SourceId = @s", ("@s", draft.Id)));
        Assert.Equal("1250.50", await run.ScalarAsync<string>("SELECT AmountText FROM finance.MoneyEvents"));
        Assert.Equal(OccurredOn.ToDateTime(TimeOnly.MinValue), await run.ScalarAsync<DateTime>("SELECT OccurredOn FROM finance.MoneyEvents"));
        Assert.Equal(ValueOn.ToDateTime(TimeOnly.MinValue), await run.ScalarAsync<DateTime>("SELECT ValueOn FROM finance.MoneyEvents"));
        Assert.Equal(run.Actor, await run.ScalarAsync<string>("SELECT RecordedBy FROM finance.MoneyEvents"));
        Assert.Equal((1, run.Actor), (await run.ScalarAsync<int>("SELECT Stage FROM finance.ReceiptDrafts"),
            await run.ScalarAsync<string>("SELECT RecordedBy FROM finance.ReceiptDrafts")));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.manual-receipt.recorded.v1' AND EntityId = @s",
            ("@s", draft.Id.ToString("D"))));

        await using (var a = run.Service(CompanyA, owners, correlation: "SENTETIK-retry"))
        {
            // Same key, same request: the stored answer as a replay with the FIRST correlation, and no second event.
            var replay = await a.Service.RecordAsync(a.Context, Key("r-1"), new RecordRequest(draft.Id, draft.Version), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, true, run.Correlation), (replay.Outcome, replay.IsReplay, replay.OriginalCorrelationId));
            // Same key, another request: a conflict that discloses nothing.
            Assert.Equal(CommandOutcome.IdempotencyConflict,
                (await a.Service.RecordAsync(a.Context, Key("r-1"), new RecordRequest(draft.Id, "2"), CancellationToken.None)).Outcome);
            // A new key at the stale version: the key's terminal answer (§7).
            Assert.Equal(CommandOutcome.VersionConflict,
                (await a.Service.RecordAsync(a.Context, Key("r-2"), new RecordRequest(draft.Id, draft.Version), CancellationToken.None)).Outcome);
            // A new key at the current version: recorded already, not a draft — refused, and not the key's answer.
            var again = await a.Service.RecordAsync(a.Context, Key("r-3"), new RecordRequest(draft.Id, "2"), CancellationToken.None);
            Assert.Equal((CommandOutcome.ValidationFailed, "source_not_draft"), (again.Outcome, again.Issues.Single().Code));
            // A recorded source is never edited again.
            var edit = await a.Service.DraftSaveAsync(a.Context, Key("s-edit"), Save(owners, "1", draft.Id, "2"), CancellationToken.None);
            Assert.Equal((CommandOutcome.ValidationFailed, "source_not_draft"), (edit.Outcome, edit.Issues.Single().Code));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM finance.ReceiptDrafts"));
        // Record commands kept: the success and the stale version's terminal answer.
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands WHERE CommandName = @c",
            ("@c", ReceiptDraftService.RecordCommand)));

        // Company B never records company A's source.
        await using (var b = run.Service(CompanyB, owners))
        {
            var foreign = await b.Service.RecordAsync(b.Context, Key("r-b"), new RecordRequest(draft.Id, "2"), CancellationToken.None);
            Assert.Equal((CommandOutcome.ValidationFailed, "draft_not_found"), (foreign.Outcome, foreign.Issues.Single().Code));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
    }

    [SqlServerFact]
    public async Task Two_records_of_one_source_at_once_make_one_money_event()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var draft = await run.SaveDraftAsync(owners, "700");

        // Two operators' Records of the same source under two keys, at once: whatever the interleaving (a lost
        // version, a lost unique index, or the second reading a recorded source), exactly one money event.
        async Task<CommandResult> RecordAsync(string key, string actor)
        {
            await using var scope = run.Service(CompanyA, owners, actor: actor);
            return await scope.Service.RecordAsync(scope.Context, Key(key), new RecordRequest(draft.Id, draft.Version), CancellationToken.None);
        }

        var results = await Task.WhenAll(
            Task.Run(() => RecordAsync("race-a", "SENTETIK-operator-a")),
            Task.Run(() => RecordAsync("race-b", "SENTETIK-operator-b")));

        Assert.Single(results, result => result.Outcome == CommandOutcome.Succeeded && !result.IsReplay);
        Assert.Single(results, result => result.Outcome is CommandOutcome.VersionConflict or CommandOutcome.ValidationFailed);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents WHERE SourceId = @s", ("@s", draft.Id)));
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM finance.ReceiptDrafts"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.manual-receipt.recorded.v1'"));
    }

    [SqlServerFact]
    public async Task The_money_event_migration_rolls_back_only_while_no_money_fact_is_stored()
    {
        await using var run = await Run.StartAsync();
        const string beforeMoneyEvents = "20260919110000_RecordTerminalCommandResults";

        await using (var context = run.Context(CompanyA))
        {
            await context.GetService<IMigrator>().MigrateAsync(beforeMoneyEvents);
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE [name] = N'MoneyEvents' AND [schema_id] = SCHEMA_ID(N'finance')"));
            await context.Database.MigrateAsync();
        }

        var owners = Owners.For(CompanyA);
        var draft = await run.SaveDraftAsync(owners, "10");
        await using (var a = run.Service(CompanyA, owners))
        {
            Assert.Equal(CommandOutcome.Succeeded, (await a.Service.RecordAsync(a.Context, Key("r-1"),
                new RecordRequest(draft.Id, draft.Version), CancellationToken.None)).Outcome);
        }

        await using (var context = run.Context(CompanyA))
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(beforeMoneyEvents));
            Assert.Equal(51051, error.Number);
            Assert.EndsWith("_AddMoneyEvents", (await context.Database.GetAppliedMigrationsAsync()).Last(), StringComparison.Ordinal);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
    }

    /// <summary>
    /// DEC-FIN-014 (customer POS collection) on a real SQL Server: the POS facts are stored beside the GROSS amount,
    /// read back after the scope is closed, carried by the ONE money event, replayed once, refused at a stale version,
    /// invisible to company B, all-or-none in the database itself, and the same source is then distributed (F-03) at
    /// its gross to the customer's open item, which closes. Owner ports, the open period and the permissions are
    /// SENTETIK test inputs; no journal is made (chart of accounts, NEEDS-BUSINESS 5740501573).
    /// </summary>
    [SqlServerFact]
    public async Task A_customer_POS_collection_closes_the_debt_at_gross_and_keeps_net_and_commission_in_one_money_event()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var pos = new PosCollectionInput("25.50", PosCommissionBearer.Company);

        CommandResult saved;
        await using (var a = run.Service(CompanyA, owners))
        {
            saved = await a.Service.DraftSaveAsync(a.Context, Key("pos-save"), Save(owners, "1000.00", pos: pos),
                CancellationToken.None);
        }

        Assert.Equal(CommandOutcome.Succeeded, saved.Outcome);
        var draftId = saved.Resource!.Source.Id;
        Assert.Equal(("25.50", "974.50", 1), (
            await run.ScalarAsync<string>("SELECT PosCommissionAmountText FROM finance.ReceiptDrafts WHERE Id = @s", ("@s", draftId)),
            await run.ScalarAsync<string>("SELECT PosBankNetAmountText FROM finance.ReceiptDrafts WHERE Id = @s", ("@s", draftId)),
            await run.ScalarAsync<int>("SELECT PosCommissionBearer FROM finance.ReceiptDrafts WHERE Id = @s", ("@s", draftId))));

        // Close and reopen: a new scope reads the stored facts; company B reads nothing.
        await using (var reader = run.Service(CompanyA, owners, correlation: "SENTETIK-pos-reopen"))
        {
            var reopened = await reader.Service.DetailAuthorizedAsync(reader.Context, new SourceDetailRequest(draftId),
                CancellationToken.None);
            Assert.Equal((ReadOutcome.Found, "1000.00"), (reopened.Outcome, reopened.Resource!.Source.Amount.Money!.Amount));
            Assert.Equal((PosCommissionBearer.Company, "25.50", "974.50"), (reopened.Resource.Pos!.CommissionBearer,
                reopened.Resource.Pos.Commission.Money!.Amount, reopened.Resource.Pos.BankNet.Money!.Amount));
        }

        await using (var b = run.Service(CompanyB, owners))
        {
            Assert.Equal(ReadOutcome.NotFound, (await b.Service.DetailAuthorizedAsync(b.Context,
                new SourceDetailRequest(draftId), CancellationToken.None)).Outcome);
        }

        var record = new RecordRequest(draftId, saved.Resource.Source.Version);
        CommandResult recorded;
        await using (var a = run.Service(CompanyA, owners))
        {
            recorded = await a.Service.RecordAsync(a.Context, Key("pos-record"), record, CancellationToken.None);
        }

        Assert.Equal((CommandOutcome.Succeeded, "1000.00"), (recorded.Outcome, recorded.Resource!.Unallocated.Amount.Money!.Amount));
        await using (var a = run.Service(CompanyA, owners, correlation: "SENTETIK-pos-retry"))
        {
            var replay = await a.Service.RecordAsync(a.Context, Key("pos-record"), record, CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, true, run.Correlation), (replay.Outcome, replay.IsReplay, replay.OriginalCorrelationId));
            Assert.Equal(CommandOutcome.VersionConflict,
                (await a.Service.RecordAsync(a.Context, Key("pos-record-stale"), record, CancellationToken.None)).Outcome);
        }

        // ONE money event: the customer's credit is the gross; the net and the commission are its own facts.
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents WHERE SourceId = @s", ("@s", draftId)));
        Assert.Equal(("1000.00", "25.50", "974.50", 1), (
            await run.ScalarAsync<string>("SELECT AmountText FROM finance.MoneyEvents"),
            await run.ScalarAsync<string>("SELECT PosCommissionAmountText FROM finance.MoneyEvents"),
            await run.ScalarAsync<string>("SELECT PosBankNetAmountText FROM finance.MoneyEvents"),
            await run.ScalarAsync<int>("SELECT PosCommissionBearer FROM finance.MoneyEvents")));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.manual-receipt.recorded.v1' AND EntityId = @s "
            + "AND SourceChannel = N'Web' AND AfterJson LIKE N'%\"channel\":\"Pos\"%'",
            ("@s", draftId.ToString("D"))));

        // The database keeps the three facts all-or-none, whatever writes it.
        var halfWritten = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE finance.MoneyEvents SET PosBankNetAmountText = NULL WHERE SourceId = @s; SELECT 1", ("@s", draftId)));
        Assert.Contains("CK_MoneyEvents_Pos", halfWritten.Message, StringComparison.Ordinal);

        // The next step: the same source is distributed at its gross to the customer's open item, which closes.
        var openItem = await run.SeedOpenItemAsync(CompanyA, owners.Account, Currency, "1000.00");
        FinanceAllocationCommandResult allocated;
        await using (var allocation = run.AllocationService(CompanyA, run.Actor, new SyntheticPermissions()))
        {
            allocated = await allocation.Service.CreateAsync(allocation.Context, Key("pos-allocate"),
                new CreateFinanceAllocationRequest(Guid.NewGuid(), draftId, recorded.Resource.Source.Version,
                    openItem.Id, openItem.Version, "1000.00", "SENTETIK POS brüt kapama"), CancellationToken.None);
        }

        Assert.Equal((CommandOutcome.Succeeded, "1000.00"), (allocated.Outcome, allocated.Resource!.Amount));
        Assert.Equal(("0.00", 2), (
            await run.ScalarAsync<string>("SELECT OutstandingAmountText FROM finance.OpenItems WHERE Id = @i", ("@i", openItem.Id)),
            await run.ScalarAsync<int>("SELECT Status FROM finance.OpenItems WHERE Id = @i", ("@i", openItem.Id))));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        await using (var reader = run.Service(CompanyA, owners, correlation: "SENTETIK-pos-after-allocation"))
        {
            var after = (await reader.Service.DetailAuthorizedAsync(reader.Context, new SourceDetailRequest(draftId),
                CancellationToken.None)).Resource!;
            Assert.Equal("0.00", after.Unallocated.Amount.Money!.Amount);
            Assert.Equal("974.50", after.Pos!.BankNet.Money!.Amount);
        }
    }

    /// <summary>DEC-FIN-014: a rollback never drops a stored POS commission or bank net (Finance's 51051).</summary>
    [SqlServerFact]
    public async Task The_POS_migration_rolls_back_only_while_no_POS_fact_is_stored()
    {
        await using var run = await Run.StartAsync();
        const string beforePos = "20260924113000_AddFinanceAllocations";

        await using (var context = run.Context(CompanyA))
        {
            await context.GetService<IMigrator>().MigrateAsync(beforePos);
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'finance.MoneyEvents') AND name LIKE N'Pos%'"));
            await context.Database.MigrateAsync();
        }

        var owners = Owners.For(CompanyA);
        await using (var a = run.Service(CompanyA, owners))
        {
            var saved = await a.Service.DraftSaveAsync(a.Context, Key("pos-save"),
                Save(owners, "10", pos: new PosCollectionInput("0.10", PosCommissionBearer.Customer)), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, saved.Outcome);
        }

        await using (var context = run.Context(CompanyA))
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(beforePos));
            Assert.Equal(51051, error.Number);
            Assert.EndsWith("_AddPosCollectionFacts", (await context.Database.GetAppliedMigrationsAsync()).Last(), StringComparison.Ordinal);
        }

        Assert.Equal("9.90", await run.ScalarAsync<string>("SELECT PosBankNetAmountText FROM finance.ReceiptDrafts"));
    }

    /// <summary>
    /// FIN-T05, Sinan's K1 (#66 5748258299, closes 5745134466): <c>occurredOn</c> decides the open-period
    /// check; <c>valueOn</c> does not move the period and <c>recordedAtUtc</c> is only the audit moment. The
    /// rule is the PRODUCT's (OccurredOnFinanceRecordPeriodRule over the real AccountingPeriodStatusPort) and
    /// the periods are real Accounting rows in the same database — no synthetic period rule here.
    /// The permission the port measures is a synthetic TEST grant, not a Sinan or production grant.
    /// </summary>
    [SqlServerFact]
    public async Task The_period_follows_occurredOn_and_a_closed_period_records_nothing()
    {
        await using var run = await Run.StartAsync();
        await run.MigrateAccountingAsync();
        var owners = Owners.For(CompanyA);

        // A CLOSED period over the transaction date, and an OPEN one over "today" — the shape the decision
        // names: a back-dated event recorded today must NOT move into today's period.
        var closed = await run.PeriodAsync(CompanyA, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), close: true);
        var open = await run.PeriodAsync(CompanyA, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), close: false);
        Assert.NotEqual(closed, open);

        // (1) occurredOn in the CLOSED period, recorded today: refused, and it is the period's refusal —
        // never the rule-could-not-decide one — and nothing is stored.
        var backDated = await run.SaveDraftAsync(owners, "1000", occurredOn: new DateOnly(2026, 8, 20));
        await using (var a = run.Service(CompanyA, owners, rule: run.RealRule(CompanyA)))
        {
            var refused = await a.Service.RecordAsync(a.Context, Key("record-closed"),
                new RecordRequest(backDated.Id, backDated.Version), CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
            // The FIELD differs from the unavailable refusal on purpose, and the product is what decided it:
            // a period that is not open is reported on `occurredOn`, the date that DECIDED it (K1), so the
            // recorder is pointed at the thing to correct; a rule that could not decide is reported on
            // `period`, because no date is at fault. This case had it as "period" and the server said
            // otherwise — the server is right, and the difference is now asserted rather than assumed.
            Assert.Equal(new ValidationIssue("occurredOn", RecordPeriodAnswer.PeriodNotOpenCode), Assert.Single(refused.Issues));
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));

        // (2) occurredOn in the OPEN period with a DIFFERENT value date — the value date lies in the CLOSED
        // one, so a rule that consulted valueOn would refuse. It records: the period follows occurredOn.
        var current = await run.SaveDraftAsync(owners, "1250.5",
            occurredOn: new DateOnly(2026, 9, 15), valueOn: new DateOnly(2026, 8, 25));
        await using (var a = run.Service(CompanyA, owners, rule: run.RealRule(CompanyA)))
        {
            var recorded = await a.Service.RecordAsync(a.Context, Key("record-open"),
                new RecordRequest(current.Id, current.Version), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, recorded.Outcome);
            Assert.Equal(SourceStage.Recorded, recorded.Resource!.Source.Stage);

            // (3) the same key again: the stored answer, never a second money or cari effect.
            var again = await a.Service.RecordAsync(a.Context, Key("record-open"),
                new RecordRequest(current.Id, current.Version), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, again.Outcome);
            Assert.True(again.IsReplay);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.MoneyEvents WHERE SourceId = @id", ("@id", current.Id)));

        // (4) The port could not answer — the actor lost the period read — so the rule DECIDED NOTHING and the
        // refusal says so. This must never look like "the period is not open" (the LOG-4b lesson).
        var third = await run.SaveDraftAsync(owners, "10", occurredOn: new DateOnly(2026, 9, 16));
        await using (var a = run.Service(CompanyA, owners, rule: run.RealRule(CompanyA, granted: false)))
        {
            var undecided = await a.Service.RecordAsync(a.Context, Key("record-ungranted"),
                new RecordRequest(third.Id, third.Version), CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, undecided.Outcome);
            Assert.Equal(new ValidationIssue("period", RecordPeriodAnswer.RuleUnavailableCode), Assert.Single(undecided.Issues));
        }

        // (5) The actor is swapped WHILE the owner answers: the port releases nothing, so the rule is
        // undecided again — the #366 fence, measured in the round that introduces the port.
        await using (var a = run.Service(CompanyA, owners, rule: run.RealRule(CompanyA, swapDuringRead: true)))
        {
            var swapped = await a.Service.RecordAsync(a.Context, Key("record-swapped"),
                new RecordRequest(third.Id, third.Version), CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, swapped.Outcome);
            Assert.Equal(new ValidationIssue("period", RecordPeriodAnswer.RuleUnavailableCode), Assert.Single(swapped.Issues));
        }

        // Nothing of (1), (4) or (5) reached the ledger: one money event in the whole database.
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
    }

    /// <summary>
    /// FIN-T06/B1-B2 against the authorized CI SQL fixture. A maker can only propose; a different checker
    /// creates one separate reversing source/event. The recorded original remains immutable and both detail
    /// projections retain their navigable historical relationship. All identities and authority answers are
    /// synthetic test inputs, never environment grants.
    /// </summary>
    [SqlServerFact]
    public async Task Reversal_requires_another_checker_and_keeps_the_original_with_two_way_history()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var draft = await run.SaveDraftAsync(owners, "1250.50");

        CommandResult recorded;
        await using (var recording = run.Service(CompanyA, owners))
        {
            recorded = await recording.Service.RecordAsync(recording.Context, Key("reverse-record"),
                new RecordRequest(draft.Id, draft.Version), CancellationToken.None);
        }

        Assert.Equal(CommandOutcome.Succeeded, recorded.Outcome);
        var recordedVersion = recorded.Resource!.Source.Version;
        var reversalOccurredOn = OccurredOn.AddDays(2);

        ReversalProposalCommandResult proposed;
        await using (var maker = run.ReversalService(CompanyA, run.Actor))
        {
            proposed = await maker.Service.ProposeAsync(maker.Context, Key("reverse-propose"),
                new ProposeReversalRequest(draft.Id, recordedVersion, reversalOccurredOn, "SENTETIK-yanlis-tahsilat"),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome);
            Assert.Equal(ReversalProposalStatus.Pending, proposed.Resource!.Status);

            var selfApproval = await maker.Service.ApproveAsync(maker.Context, Key("reverse-self-approve"),
                new ApproveReversalRequest(proposed.Resource.Id, proposed.Resource.Version), CancellationToken.None);
            Assert.NotEqual(CommandOutcome.Succeeded, selfApproval.Outcome);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReceiptDrafts"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT Status FROM finance.ReversalProposals WHERE Id = @p", ("@p", proposed.Resource!.Id)));

        var checker = Guid.NewGuid().ToString("D");
        ReversalProposalCommandResult approved;
        await using (var approval = run.ReversalService(CompanyA, checker))
        {
            var staleVersion = await approval.Service.ApproveAsync(approval.Context,
                Key("reverse-stale-proposal-version"),
                new ApproveReversalRequest(proposed.Resource.Id, "999999"), CancellationToken.None);
            Assert.Equal(CommandOutcome.VersionConflict, staleVersion.Outcome);
            Assert.Equal(new ValidationIssue("expectedVersion", "version_conflict"),
                Assert.Single(staleVersion.Issues));

            approved = await approval.Service.ApproveAsync(approval.Context, Key("reverse-approve"),
                new ApproveReversalRequest(proposed.Resource.Id, proposed.Resource.Version), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
            Assert.Equal(ReversalProposalStatus.Approved, approved.Resource!.Status);
            Assert.Equal((run.Actor, checker), (approved.Resource.ProposedBy, approved.Resource.DecidedBy));
            Assert.NotEqual(approved.Resource.ProposedBy, approved.Resource.DecidedBy);

            var duplicate = await approval.Service.ApproveAsync(approval.Context, Key("reverse-approve-again"),
                new ApproveReversalRequest(approved.Resource.Id, approved.Resource.Version), CancellationToken.None);
            Assert.NotEqual(CommandOutcome.Succeeded, duplicate.Outcome);
        }

        var reversalSourceId = Assert.IsType<Guid>(approved.Resource!.ReversalSourceId);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReceiptDrafts"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal((1, 2L), (
            await run.ScalarAsync<int>("SELECT Stage FROM finance.ReceiptDrafts WHERE Id = @s", ("@s", draft.Id)),
            await run.ScalarAsync<long>("SELECT Version FROM finance.ReceiptDrafts WHERE Id = @s", ("@s", draft.Id))));
        Assert.Equal((1, "1250.50", OccurredOn.ToDateTime(TimeOnly.MinValue), run.Actor), (
            await run.ScalarAsync<int>("SELECT Kind FROM finance.MoneyEvents WHERE SourceId = @s", ("@s", draft.Id)),
            await run.ScalarAsync<string>("SELECT AmountText FROM finance.MoneyEvents WHERE SourceId = @s", ("@s", draft.Id)),
            await run.ScalarAsync<DateTime>("SELECT OccurredOn FROM finance.MoneyEvents WHERE SourceId = @s", ("@s", draft.Id)),
            await run.ScalarAsync<string>("SELECT RecordedBy FROM finance.MoneyEvents WHERE SourceId = @s", ("@s", draft.Id))));
        Assert.Equal((2, "1250.50", reversalOccurredOn.ToDateTime(TimeOnly.MinValue), checker), (
            await run.ScalarAsync<int>("SELECT Kind FROM finance.MoneyEvents WHERE SourceId = @s", ("@s", reversalSourceId)),
            await run.ScalarAsync<string>("SELECT AmountText FROM finance.MoneyEvents WHERE SourceId = @s", ("@s", reversalSourceId)),
            await run.ScalarAsync<DateTime>("SELECT OccurredOn FROM finance.MoneyEvents WHERE SourceId = @s", ("@s", reversalSourceId)),
            await run.ScalarAsync<string>("SELECT RecordedBy FROM finance.MoneyEvents WHERE SourceId = @s", ("@s", reversalSourceId))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.ReversalProposals WHERE OriginalSourceId = @o AND ReversalSourceId = @r AND Status = 1 AND ProposedBy <> DecidedBy",
            ("@o", draft.Id), ("@r", reversalSourceId)));

        await using var history = run.Service(CompanyA, owners, actor: checker, correlation: "SENTETIK-reversal-history");
        var originalDetail = await history.Service.DetailAuthorizedAsync(history.Context,
            new SourceDetailRequest(draft.Id), CancellationToken.None);
        var reversalDetail = await history.Service.DetailAuthorizedAsync(history.Context,
            new SourceDetailRequest(reversalSourceId), CancellationToken.None);
        var historyList = await history.Service.ListAuthorizedAsync(history.Context,
            new SourceListRequest(null), CancellationToken.None);
        Assert.Equal(ReadOutcome.Found, historyList.Outcome);
        var originalSummary = Assert.Single(historyList.Resource!.Items, item => item.Id == draft.Id);
        var reversalSummary = Assert.Single(historyList.Resource.Items, item => item.Id == reversalSourceId);
        Assert.Equal((ReadOutcome.Found, reversalSourceId, (Guid?)null),
            (originalDetail.Outcome, originalDetail.Resource!.ReversalSourceId, originalDetail.Resource.ReversesSourceId));
        Assert.Equal((ReadOutcome.Found, (Guid?)null, draft.Id),
            (reversalDetail.Outcome, reversalDetail.Resource!.ReversalSourceId, reversalDetail.Resource.ReversesSourceId));
        Assert.Equal(originalDetail.Resource.Source, originalSummary);
        Assert.Equal(reversalDetail.Resource.Source, reversalSummary);

        // B1 corrects a normal receipt once; the generated reversal source is not a new reversal root.
        // Refusal is not terminal and produces no proposal, event, command or audit side effect.
        var beforeReverseOfReverse = (
            Proposals: await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReversalProposals"),
            Events: await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"),
            Commands: await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM finance.Commands WHERE CommandName = @c",
                ("@c", ReversalProposalService.ProposeCommand)),
            Audits: await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.receipt-reversal.proposed.v1'"));
        await using (var reverseSourceMaker = run.ReversalService(CompanyA, Guid.NewGuid().ToString("D")))
        {
            var refused = await reverseSourceMaker.Service.ProposeAsync(reverseSourceMaker.Context,
                Key("reverse-of-reversal"),
                new ProposeReversalRequest(reversalSourceId, reversalDetail.Resource.Source.Version,
                    reversalOccurredOn.AddDays(1), "SENTETIK-ters-kaynagi-yeniden-tersleme"),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
            Assert.Equal(new ValidationIssue("sourceId", "finance_reversal_source_not_original_receipt"),
                Assert.Single(refused.Issues));
            Assert.Null(refused.Resource);
        }
        Assert.Equal(beforeReverseOfReverse, (
            Proposals: await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReversalProposals"),
            Events: await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"),
            Commands: await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM finance.Commands WHERE CommandName = @c",
                ("@c", ReversalProposalService.ProposeCommand)),
            Audits: await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.receipt-reversal.proposed.v1'")));

        // The first definitive reversal does not prevent an auditor from seeing a later proposal attempt,
        // but the database and service together prevent that proposal from becoming a second money fact.
        var secondMaker = Guid.NewGuid().ToString("D");
        ReversalProposalCommandResult secondProposal;
        await using (var maker = run.ReversalService(CompanyA, secondMaker))
        {
            secondProposal = await maker.Service.ProposeAsync(maker.Context, Key("second-reversal-propose"),
                new ProposeReversalRequest(draft.Id, recordedVersion, reversalOccurredOn.AddDays(1),
                    "SENTETIK-ikinci-kesin-ters-denemesi"), CancellationToken.None);
        }

        Assert.Equal(CommandOutcome.Succeeded, secondProposal.Outcome);
        await using (var checkerAgain = run.ReversalService(CompanyA, Guid.NewGuid().ToString("D")))
        {
            var refused = await checkerAgain.Service.ApproveAsync(checkerAgain.Context,
                Key("second-reversal-approve"),
                new ApproveReversalRequest(secondProposal.Resource!.Id, secondProposal.Resource.Version),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
            Assert.Equal(new ValidationIssue("sourceId", "finance_allocation_state_invalid"),
                Assert.Single(refused.Issues));
            Assert.Null(refused.Resource);
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReceiptDrafts"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal((1, 1, 0), (
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM finance.ReversalProposals WHERE OriginalSourceId = @o AND Status = 1",
                ("@o", draft.Id)),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM finance.ReversalProposals WHERE Id = @p AND Status = 0 AND ReversalSourceId IS NULL",
                ("@p", secondProposal.Resource!.Id)),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM finance.Commands WHERE CommandName = @c AND ReversalProposalId = @p",
                ("@c", ReversalProposalService.ApproveCommand), ("@p", secondProposal.Resource.Id))));
    }

    [SqlServerFact]
    public async Task Permission_revoked_during_reads_refuses_propose_and_reject_before_save()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var draft = await run.SaveDraftAsync(owners, "1250.50");

        CommandResult recorded;
        await using (var recording = run.Service(CompanyA, owners))
        {
            recorded = await recording.Service.RecordAsync(recording.Context, Key("revoke-record"),
                new RecordRequest(draft.Id, draft.Version), CancellationToken.None);
        }
        Assert.Equal(CommandOutcome.Succeeded, recorded.Outcome);

        var proposePermission = new RevokeAfterFirstPermissionCheck();
        await using (var maker = run.ReversalService(CompanyA, run.Actor, permissions: proposePermission))
        {
            var refused = await maker.Service.ProposeAsync(maker.Context, Key("revoke-propose"),
                new ProposeReversalRequest(draft.Id, recorded.Resource!.Source.Version, OccurredOn.AddDays(2),
                    "SENTETIK-yetki-geri-alindi"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Denied, refused.Outcome);
            Assert.Null(refused.Resource);
        }
        Assert.Equal(2, proposePermission.CheckCount);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReversalProposals"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE CommandName = @c",
            ("@c", ReversalProposalService.ProposeCommand)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.receipt-reversal.proposed.v1'"));

        ReversalProposalCommandResult proposed;
        await using (var maker = run.ReversalService(CompanyA, run.Actor))
        {
            proposed = await maker.Service.ProposeAsync(maker.Context, Key("allowed-propose"),
                new ProposeReversalRequest(draft.Id, recorded.Resource!.Source.Version, OccurredOn.AddDays(2),
                    "SENTETIK-reject-hazirligi"), CancellationToken.None);
        }
        Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome);

        var rejectPermission = new RevokeAfterFirstPermissionCheck();
        await using (var checker = run.ReversalService(CompanyA, Guid.NewGuid().ToString("D"),
                         permissions: rejectPermission))
        {
            var refused = await checker.Service.RejectAsync(checker.Context, Key("revoke-reject"),
                new RejectReversalRequest(proposed.Resource!.Id, proposed.Resource.Version,
                    "SENTETIK-yetki-geri-alindi"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Denied, refused.Outcome);
            Assert.Null(refused.Resource);
        }
        Assert.Equal(2, rejectPermission.CheckCount);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.ReversalProposals WHERE Id = @p AND Status = 0 AND Version = 1 AND DecidedBy IS NULL AND DecisionReason IS NULL",
            ("@p", proposed.Resource!.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE CommandName = @c",
            ("@c", ReversalProposalService.RejectCommand)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.receipt-reversal.rejected.v1'"));
    }

    /// <summary>
    /// FIN-T06 keeps the period decision at approval time and on the requested reversal occurrence date.
    /// A closed real Accounting period leaves the pending proposal, original source and original event intact.
    /// </summary>
    [SqlServerFact]
    public async Task A_closed_reversal_period_refuses_approval_and_writes_no_reversal()
    {
        await using var run = await Run.StartAsync();
        await run.MigrateAccountingAsync();
        var reversalOccurredOn = OccurredOn.AddDays(2);
        await run.PeriodAsync(CompanyA, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), close: true);
        var owners = Owners.For(CompanyA);
        var draft = await run.SaveDraftAsync(owners, "1250.50");

        CommandResult recorded;
        await using (var recording = run.Service(CompanyA, owners))
        {
            recorded = await recording.Service.RecordAsync(recording.Context, Key("closed-reverse-record"),
                new RecordRequest(draft.Id, draft.Version), CancellationToken.None);
        }

        Assert.Equal(CommandOutcome.Succeeded, recorded.Outcome);
        ReversalProposalCommandResult proposed;
        await using (var maker = run.ReversalService(CompanyA, run.Actor))
        {
            proposed = await maker.Service.ProposeAsync(maker.Context, Key("closed-reverse-propose"),
                new ProposeReversalRequest(draft.Id, recorded.Resource!.Source.Version, reversalOccurredOn,
                    "SENTETIK-kapali-donem"), CancellationToken.None);
        }

        Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome);
        var checker = Guid.NewGuid().ToString("D");
        await using (var approval = run.ReversalService(CompanyA, checker,
                         rule: run.RealRule(CompanyA, actor: checker)))
        {
            var refused = await approval.Service.ApproveAsync(approval.Context, Key("closed-reverse-approve"),
                new ApproveReversalRequest(proposed.Resource!.Id, proposed.Resource.Version), CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
            Assert.Equal(new ValidationIssue("occurredOn", RecordPeriodAnswer.PeriodNotOpenCode), Assert.Single(refused.Issues));
            Assert.Null(refused.Resource);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReceiptDrafts"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal((0, 0), (
            await run.ScalarAsync<int>(
                "SELECT Status FROM finance.ReversalProposals WHERE Id = @p", ("@p", proposed.Resource!.Id)),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM finance.ReversalProposals WHERE Id = @p AND ReversalSourceId IS NOT NULL",
                ("@p", proposed.Resource.Id))));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE CommandName = @c",
            ("@c", ReversalProposalService.ApproveCommand)));
    }

    /// <summary>
    /// The allocation owner is authoritative. A positive allocation answer refuses approval without
    /// interpreting balances locally and without changing the pending proposal or immutable original.
    /// </summary>
    [SqlServerFact]
    public async Task An_allocated_source_refuses_reversal_approval_and_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var draft = await run.SaveDraftAsync(owners, "1250.50");

        CommandResult recorded;
        await using (var recording = run.Service(CompanyA, owners))
        {
            recorded = await recording.Service.RecordAsync(recording.Context, Key("allocated-reverse-record"),
                new RecordRequest(draft.Id, draft.Version), CancellationToken.None);
        }

        ReversalProposalCommandResult proposed;
        await using (var maker = run.ReversalService(CompanyA, run.Actor))
        {
            proposed = await maker.Service.ProposeAsync(maker.Context, Key("allocated-reverse-propose"),
                new ProposeReversalRequest(draft.Id, recorded.Resource!.Source.Version, OccurredOn.AddDays(2),
                    "SENTETIK-tahsisli-kaynak"), CancellationToken.None);
        }

        Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome);
        var allocationId = await run.SeedActiveAllocationAsync(
            CompanyA, draft.Id, owners.Account, Currency, "1250.50", "250.10");
        await using (var checker = run.ReversalService(CompanyA, Guid.NewGuid().ToString("D")))
        {
            var refused = await checker.Service.ApproveAsync(checker.Context, Key("allocated-reverse-approve"),
                new ApproveReversalRequest(proposed.Resource!.Id, proposed.Resource.Version), CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
            Assert.Equal(new ValidationIssue("sourceId", "finance_reversal_has_allocations"), Assert.Single(refused.Issues));
            Assert.Null(refused.Resource);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReceiptDrafts"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal((0, 0), (
            await run.ScalarAsync<int>(
                "SELECT Status FROM finance.ReversalProposals WHERE Id = @p", ("@p", proposed.Resource!.Id)),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM finance.ReversalProposals WHERE Id = @p AND ReversalSourceId IS NOT NULL",
                ("@p", proposed.Resource.Id))));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE CommandName = @c",
            ("@c", ReversalProposalService.ApproveCommand)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Allocations WHERE Id = @id AND CompanyId = @company AND SourceId = @source AND Status = 1",
            ("@id", allocationId), ("@company", CompanyA), ("@source", draft.Id)));
    }

    [SqlServerFact]
    public async Task Allocation_terminal_refusal_is_stable_after_state_mutation_and_a_new_key_can_succeed()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var draft = await run.SaveDraftAsync(owners, "1250.50");
        CommandResult recorded;
        await using (var recording = run.Service(CompanyA, owners))
        {
            recorded = await recording.Service.RecordAsync(recording.Context, Key("allocation-replay-record"),
                new RecordRequest(draft.Id, draft.Version), CancellationToken.None);
        }
        Assert.Equal(CommandOutcome.Succeeded, recorded.Outcome);
        var openItem = await run.SeedOpenItemAsync(CompanyA, owners.Account, Currency, "500.00");
        await using (var context = run.Context(CompanyA))
        {
            Assert.Equal(1, await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE finance.OpenItems SET OutstandingAmountText = {"100.00"}
                WHERE CompanyId = {CompanyA} AND Id = {openItem.Id}
                """));
        }
        var allocationId = Guid.NewGuid();
        var actor = Guid.NewGuid().ToString("D");
        var request = new CreateFinanceAllocationRequest(
            allocationId, draft.Id, recorded.Resource!.Source.Version,
            openItem.Id, openItem.Version, "0250.10", " SENTETIK terminal refusal replay ");

        FinanceAllocationCommandResult refused;
        await using (var allocation = run.AllocationService(CompanyA, actor, new SyntheticPermissions()))
        {
            refused = await allocation.Service.CreateAsync(allocation.Context, Key("allocation-terminal-refusal"),
                request, CancellationToken.None);
        }
        Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
        Assert.False(refused.IsReplay);
        Assert.Equal(new ValidationIssue("amount", "finance_allocation_exceeds_open_item"), Assert.Single(refused.Issues));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE CommandName = @command AND DraftId = @source "
            + "AND FinanceAllocationId IS NULL",
            ("@command", FinanceAllocationService.CreateCommandName), ("@source", draft.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Allocations WHERE Id = @id", ("@id", allocationId)));

        await using (var context = run.Context(CompanyA))
        {
            Assert.Equal(1, await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE finance.OpenItems SET OutstandingAmountText = {"500.00"}
                WHERE CompanyId = {CompanyA} AND Id = {openItem.Id}
                """));
        }

        FinanceAllocationCommandResult replay;
        await using (var allocation = run.AllocationService(CompanyA, actor, new SyntheticPermissions()))
        {
            replay = await allocation.Service.CreateAsync(allocation.Context, Key("allocation-terminal-refusal"),
                request, CancellationToken.None);
        }
        Assert.Equal(CommandOutcome.ValidationFailed, replay.Outcome);
        Assert.True(replay.IsReplay);
        Assert.Equal(refused.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(refused.Issues.ToArray(), replay.Issues.ToArray());

        FinanceAllocationCommandResult succeeded;
        await using (var allocation = run.AllocationService(CompanyA, actor, new SyntheticPermissions()))
        {
            succeeded = await allocation.Service.CreateAsync(allocation.Context, Key("allocation-after-mutation"),
                request, CancellationToken.None);
        }
        Assert.Equal(CommandOutcome.Succeeded, succeeded.Outcome);
        Assert.False(succeeded.IsReplay);
        Assert.Equal(allocationId, succeeded.Resource!.Id);
        Assert.Equal("250.10", succeeded.Resource.Amount);
        Assert.Equal("SENTETIK terminal refusal replay", succeeded.Resource.Reason);
        await using (var allocation = run.AllocationService(CompanyA, actor, new SyntheticPermissions()))
        {
            var replayed = await allocation.Service.CreateAsync(
                allocation.Context, Key("allocation-after-mutation"), request, CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, replayed.Outcome);
            Assert.True(replayed.IsReplay);
            Assert.Equal("250.10", replayed.Resource!.Amount);
            Assert.Equal("SENTETIK terminal refusal replay", replayed.Resource.Reason);
            Assert.Equal(succeeded.OriginalCorrelationId, replayed.OriginalCorrelationId);
        }

        var openItemVersion = (await run.ScalarAsync<long>(
            "SELECT Version FROM finance.OpenItems WHERE Id = @id", ("@id", openItem.Id)))
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        var releaseRequest = new ReleaseFinanceAllocationRequest(
            allocationId, succeeded.Resource.Version, openItemVersion, " SENTETIK release ");
        FinanceAllocationCommandResult released;
        await using (var allocation = run.AllocationService(CompanyA, actor, new SyntheticPermissions()))
        {
            released = await allocation.Service.ReleaseAsync(
                allocation.Context, Key("allocation-release"), releaseRequest, CancellationToken.None);
        }
        Assert.Equal(CommandOutcome.Succeeded, released.Outcome);
        Assert.Equal(FinanceAllocationStatus.Released, released.Resource!.Status);
        Assert.Equal("SENTETIK release", released.Resource.ReleaseReason);
        await using (var allocation = run.AllocationService(CompanyA, actor, new SyntheticPermissions()))
        {
            var replayed = await allocation.Service.ReleaseAsync(
                allocation.Context, Key("allocation-release"), releaseRequest, CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, replayed.Outcome);
            Assert.True(replayed.IsReplay);
            Assert.Equal("SENTETIK release", replayed.Resource!.ReleaseReason);
            Assert.Equal(released.OriginalCorrelationId, replayed.OriginalCorrelationId);
        }

        await using (var allocation = run.AllocationService(CompanyA, actor, new SyntheticPermissions()))
        {
            replay = await allocation.Service.CreateAsync(allocation.Context, Key("allocation-terminal-refusal"),
                request, CancellationToken.None);
        }
        Assert.Equal(CommandOutcome.ValidationFailed, replay.Outcome);
        Assert.True(replay.IsReplay);
        Assert.Equal(refused.Issues.ToArray(), replay.Issues.ToArray());
        Assert.Equal((1, 2, 1, 2), (
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM finance.Allocations WHERE Id = @id AND Status = 2", ("@id", allocationId)),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM finance.Commands WHERE CommandName = @command",
                ("@command", FinanceAllocationService.CreateCommandName)),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM finance.Commands WHERE CommandName = @command",
                ("@command", FinanceAllocationService.ReleaseCommandName)),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.Allocation'")));
    }

    [SqlServerFact]
    public async Task Concurrent_allocation_create_and_reversal_approval_commit_exactly_one_complete_outcome()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var draft = await run.SaveDraftAsync(owners, "1250.50");
        CommandResult recorded;
        await using (var recording = run.Service(CompanyA, owners))
        {
            recorded = await recording.Service.RecordAsync(recording.Context, Key("race-record"),
                new RecordRequest(draft.Id, draft.Version), CancellationToken.None);
        }
        Assert.Equal(CommandOutcome.Succeeded, recorded.Outcome);
        ReversalProposalCommandResult proposed;
        await using (var maker = run.ReversalService(CompanyA, run.Actor))
        {
            proposed = await maker.Service.ProposeAsync(maker.Context, Key("race-reversal-propose"),
                new ProposeReversalRequest(draft.Id, recorded.Resource!.Source.Version, OccurredOn.AddDays(2),
                    "SENTETIK-concurrent-allocation-reversal"), CancellationToken.None);
        }
        Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome);
        var openItem = await run.SeedOpenItemAsync(CompanyA, owners.Account, Currency, "1250.50");
        var allocationId = Guid.NewGuid();
        using var finalFence = new Barrier(2);
        var allocationPermission = new FinalWriteBarrierPermission(
            FinanceAction.WriteAllocation, 2, finalFence);
        var reversalPermission = new FinalWriteBarrierPermission(
            FinanceAction.ApproveReversal, 4, finalFence);

        async Task<FinanceAllocationCommandResult> AllocateAsync()
        {
            await using var allocation = run.AllocationService(
                CompanyA, Guid.NewGuid().ToString("D"), allocationPermission);
            return await allocation.Service.CreateAsync(allocation.Context, Key("race-allocation-create"),
                new CreateFinanceAllocationRequest(allocationId, draft.Id, recorded.Resource!.Source.Version,
                    openItem.Id, openItem.Version, "250.10", "SENTETIK concurrent allocation"),
                CancellationToken.None);
        }
        async Task<ReversalProposalCommandResult> ReverseAsync()
        {
            await using var checker = run.ReversalService(
                CompanyA, Guid.NewGuid().ToString("D"), permissions: reversalPermission);
            return await checker.Service.ApproveAsync(checker.Context, Key("race-reversal-approve"),
                new ApproveReversalRequest(proposed.Resource!.Id, proposed.Resource.Version),
                CancellationToken.None);
        }
        var allocationTask = Task.Run(AllocateAsync);
        var reversalTask = Task.Run(ReverseAsync);
        await Task.WhenAll((Task)allocationTask, reversalTask);
        var allocationResult = await allocationTask;
        var reversalResult = await reversalTask;
        var outcomes = new[] { allocationResult.Outcome, reversalResult.Outcome };
        Assert.Single(outcomes, outcome => outcome == CommandOutcome.Succeeded);
        Assert.Single(outcomes, outcome => outcome == CommandOutcome.VersionConflict);
        var loserIssues = allocationResult.Outcome == CommandOutcome.VersionConflict
            ? allocationResult.Issues : reversalResult.Issues;
        Assert.Equal("concurrent_change", Assert.Single(loserIssues).Code);
        var allocationWon = allocationResult.Outcome == CommandOutcome.Succeeded;
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.SourceAllocationStates WHERE CompanyId = @company AND SourceId = @source",
            ("@company", CompanyA), ("@source", draft.Id)));
        Assert.Equal(allocationWon ? "1000.40" : "0.00", await run.ScalarAsync<string>(
            "SELECT UnallocatedAmountText FROM finance.SourceAllocationStates WHERE CompanyId = @company AND SourceId = @source",
            ("@company", CompanyA), ("@source", draft.Id)));
        Assert.Equal(allocationWon ? 1 : 0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Allocations WHERE Id = @id AND Status = 1", ("@id", allocationId)));
        Assert.Equal(allocationWon ? 0 : 1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.ReversalProposals WHERE Id = @id AND Status = 1 AND ReversalSourceId IS NOT NULL",
            ("@id", proposed.Resource!.Id)));
        Assert.Equal(allocationWon ? 1 : 2,
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal(allocationWon ? "1000.40" : "1250.50", await run.ScalarAsync<string>(
            "SELECT OutstandingAmountText FROM finance.OpenItems WHERE Id = @id", ("@id", openItem.Id)));
        Assert.Equal(allocationWon ? 1 : 0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE CommandName = @command",
            ("@command", FinanceAllocationService.CreateCommandName)));
        Assert.Equal(allocationWon ? 0 : 1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE CommandName = @command",
            ("@command", ReversalProposalService.ApproveCommand)));
    }

    [SqlServerFact]
    public async Task Synthetic_approved_source_admits_once_and_native_cutover_reuses_immutable_identity()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var row = new ApprovedFinanceOpenItemSourceRow(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "invoice-v1",
            FinanceOpenItemSourceKinds.OfficialInvoiceInstallmentDocument,
            owners.Party, owners.Account, Currency, "1250.50", new DateOnly(2026, 10, 31));
        var firstReference = new FinanceOpenItemSourceReference(
            Guid.NewGuid(), "official-file-v1", new string('a', 64));
        var firstBatch = new ApprovedFinanceOpenItemSourceBatch(
            firstReference.BatchId, CompanyA, firstReference.BatchVersion, firstReference.BatchChecksum, [row]);
        await using (var first = run.OpenItemSourceService(CompanyA, owners, firstBatch))
        {
            var admitted = await first.Service.AdmitAsync(first.Context, Key("open-item-source-1"),
                firstReference, CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, admitted.Outcome);
            Assert.False(admitted.IsReplay);
            Assert.Equal(row.OpenItemId, Assert.Single(admitted.OpenItemIds));
        }

        var nativeReference = new FinanceOpenItemSourceReference(
            Guid.NewGuid(), "native-cutover-v1", new string('b', 64));
        var nativeBatch = new ApprovedFinanceOpenItemSourceBatch(
            nativeReference.BatchId, CompanyA, nativeReference.BatchVersion, nativeReference.BatchChecksum, [row]);
        await using (var native = run.OpenItemSourceService(CompanyA, owners, nativeBatch))
        {
            var reused = await native.Service.AdmitAsync(native.Context, Key("open-item-source-2"),
                nativeReference, CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, reused.Outcome);
            Assert.True(reused.IsReplay);
            Assert.Equal(row.OpenItemId, Assert.Single(reused.OpenItemIds));
        }

        var conflictingReference = new FinanceOpenItemSourceReference(
            Guid.NewGuid(), "conflicting-file-v1", new string('c', 64));
        var conflictingBatch = new ApprovedFinanceOpenItemSourceBatch(
            conflictingReference.BatchId, CompanyA, conflictingReference.BatchVersion,
            conflictingReference.BatchChecksum, [row with { Amount = "1250.51" }]);
        await using (var conflict = run.OpenItemSourceService(CompanyA, owners, conflictingBatch))
        {
            var refused = await conflict.Service.AdmitAsync(conflict.Context, Key("open-item-source-3"),
                conflictingReference, CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
            Assert.Equal("finance_open_item_source_identity_conflict", Assert.Single(refused.Issues).Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItems"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItemSourceBatches"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal("1250.50", await run.ScalarAsync<string>(
            "SELECT OriginalAmountText FROM finance.OpenItems WHERE Id = @id", ("@id", row.OpenItemId)));
    }

    [SqlServerFact]
    public async Task Synthetic_authority_revoked_during_source_read_refuses_before_open_item_save()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var reference = new FinanceOpenItemSourceReference(
            Guid.NewGuid(), "revoked-source-v1", new string('d', 64));
        var batch = new ApprovedFinanceOpenItemSourceBatch(
            reference.BatchId, CompanyA, reference.BatchVersion, reference.BatchChecksum,
            [new ApprovedFinanceOpenItemSourceRow(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "invoice-v1",
                FinanceOpenItemSourceKinds.OfficialInvoiceInstallmentDocument,
                owners.Party, owners.Account, Currency, "1250.50", OccurredOn)]);

        await using (var scope = run.OpenItemSourceService(
            CompanyA, owners, batch, new RevokeOpenItemSourceAuthorityAfterFirstCheck()))
        {
            var refused = await scope.Service.AdmitAsync(
                scope.Context, Key("open-item-source-revoked"), reference, CancellationToken.None);
            Assert.Equal(CommandOutcome.Denied, refused.Outcome);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItems"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItemSourceBatches"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.open-item-source.admitted.v1'"));
    }

    [SqlServerFact]
    public async Task Synthetic_missing_party_mapping_refuses_source_batch_without_writes()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var reference = new FinanceOpenItemSourceReference(
            Guid.NewGuid(), "missing-mapping-v1", new string('e', 64));
        var batch = new ApprovedFinanceOpenItemSourceBatch(
            reference.BatchId, CompanyA, reference.BatchVersion, reference.BatchChecksum,
            [new ApprovedFinanceOpenItemSourceRow(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "invoice-v1",
                FinanceOpenItemSourceKinds.OfficialInvoiceInstallmentDocument,
                owners.Party, owners.Account, Currency, "1250.50", OccurredOn)]);

        await using (var scope = run.OpenItemSourceService(
            CompanyA, owners, batch, parties: new MissingOpenItemParty()))
        {
            var refused = await scope.Service.AdmitAsync(
                scope.Context, Key("open-item-source-missing-mapping"), reference, CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
            Assert.Equal("party_account_not_found", Assert.Single(refused.Issues).Code);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItems"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItemSourceBatches"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.open-item-source.admitted.v1'"));
    }

    [SqlServerFact]
    public async Task Synthetic_order_source_is_refused_without_open_item_write()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var reference = new FinanceOpenItemSourceReference(
            Guid.NewGuid(), "order-source-v1", new string('f', 64));
        var batch = new ApprovedFinanceOpenItemSourceBatch(
            reference.BatchId, CompanyA, reference.BatchVersion, reference.BatchChecksum,
            [new ApprovedFinanceOpenItemSourceRow(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "order-v1", "order",
                owners.Party, owners.Account, Currency, "1250.50", OccurredOn)]);

        await using (var scope = run.OpenItemSourceService(CompanyA, owners, batch))
        {
            var refused = await scope.Service.AdmitAsync(
                scope.Context, Key("open-item-source-order"), reference, CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
            Assert.Equal(
                "finance_open_item_source_document_kind_invalid",
                Assert.Single(refused.Issues).Code);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItems"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItemSourceBatches"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.open-item-source.admitted.v1'"));
    }

    [SqlServerFact]
    public async Task Concurrent_source_admission_commits_one_open_item_and_returns_one_durable_replay()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var reference = new FinanceOpenItemSourceReference(
            Guid.NewGuid(), "concurrent-source-v1", new string('a', 64));
        var row = new ApprovedFinanceOpenItemSourceRow(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "invoice-v1",
            FinanceOpenItemSourceKinds.OfficialInvoiceInstallmentDocument,
            owners.Party, owners.Account, Currency, "1250.50", OccurredOn);
        var batch = new ApprovedFinanceOpenItemSourceBatch(
            reference.BatchId, CompanyA, reference.BatchVersion, reference.BatchChecksum, [row]);
        using var finalFence = new Barrier(2);

        async Task<FinanceOpenItemAdmissionResult> AdmitAsync()
        {
            await using var scope = run.OpenItemSourceService(
                CompanyA, owners, batch, new FinalOpenItemSourceAuthority(5, finalFence));
            return await scope.Service.AdmitAsync(
                scope.Context, Key("open-item-source-concurrent"), reference, CancellationToken.None);
        }

        var firstTask = Task.Run(AdmitAsync);
        var secondTask = Task.Run(AdmitAsync);
        await Task.WhenAll((Task)firstTask, secondTask);
        var results = new[] { await firstTask, await secondTask };

        Assert.All(results, result => Assert.Equal(CommandOutcome.Succeeded, result.Outcome));
        Assert.Single(results, result => !result.IsReplay);
        Assert.Single(results, result => result.IsReplay);
        Assert.All(results, result => Assert.Equal(row.OpenItemId, Assert.Single(result.OpenItemIds)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItems"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItemSourceBatches"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.open-item-source.admitted.v1'"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
    }

    [SqlServerFact]
    public async Task Foreign_company_source_batch_is_refused_without_open_item_or_audit()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyB);
        var reference = new FinanceOpenItemSourceReference(
            Guid.NewGuid(), "foreign-company-v1", new string('b', 64));
        var batch = new ApprovedFinanceOpenItemSourceBatch(
            reference.BatchId, CompanyA, reference.BatchVersion, reference.BatchChecksum,
            [new ApprovedFinanceOpenItemSourceRow(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "invoice-v1",
                FinanceOpenItemSourceKinds.OfficialInvoiceInstallmentDocument,
                owners.Party, owners.Account, Currency, "1250.50", OccurredOn)]);

        await using (var scope = run.OpenItemSourceService(CompanyB, owners, batch))
        {
            var refused = await scope.Service.AdmitAsync(
                scope.Context, Key("open-item-source-foreign-company"), reference, CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
            Assert.Equal(
                "finance_open_item_source_metadata_mismatch",
                Assert.Single(refused.Issues).Code);
        }

        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItemSourceBatches"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.open-item-source.admitted.v1'"));
    }

    [SqlServerFact]
    public async Task Same_admission_key_with_another_batch_is_an_idempotency_conflict_without_second_effect()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var firstReference = new FinanceOpenItemSourceReference(
            Guid.NewGuid(), "first-batch-v1", new string('c', 64));
        var firstBatch = new ApprovedFinanceOpenItemSourceBatch(
            firstReference.BatchId, CompanyA, firstReference.BatchVersion, firstReference.BatchChecksum,
            [new ApprovedFinanceOpenItemSourceRow(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "invoice-v1",
                FinanceOpenItemSourceKinds.OfficialInvoiceInstallmentDocument,
                owners.Party, owners.Account, Currency, "1250.50", OccurredOn)]);
        var key = Key("open-item-source-idempotency-conflict");
        await using (var first = run.OpenItemSourceService(CompanyA, owners, firstBatch))
        {
            var admitted = await first.Service.AdmitAsync(
                first.Context, key, firstReference, CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, admitted.Outcome);
        }

        var secondReference = new FinanceOpenItemSourceReference(
            Guid.NewGuid(), "second-batch-v1", new string('d', 64));
        var secondBatch = new ApprovedFinanceOpenItemSourceBatch(
            secondReference.BatchId, CompanyA, secondReference.BatchVersion, secondReference.BatchChecksum,
            [new ApprovedFinanceOpenItemSourceRow(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "invoice-v2",
                FinanceOpenItemSourceKinds.OfficialInvoiceInstallmentDocument,
                owners.Party, owners.Account, Currency, "500.00", OccurredOn)]);
        await using (var second = run.OpenItemSourceService(CompanyA, owners, secondBatch))
        {
            var refused = await second.Service.AdmitAsync(
                second.Context, key, secondReference, CancellationToken.None);
            Assert.Equal(CommandOutcome.IdempotencyConflict, refused.Outcome);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItems"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItemSourceBatches"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.open-item-source.admitted.v1'"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
    }

    [SqlServerFact]
    public async Task Unavailable_approved_file_adapter_refuses_without_open_item_batch_or_audit()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var reference = new FinanceOpenItemSourceReference(
            Guid.NewGuid(), "unavailable-file-v1", new string('e', 64));
        var placeholderBatch = new ApprovedFinanceOpenItemSourceBatch(
            reference.BatchId, CompanyA, reference.BatchVersion, reference.BatchChecksum,
            [new ApprovedFinanceOpenItemSourceRow(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "invoice-v1",
                FinanceOpenItemSourceKinds.OfficialInvoiceInstallmentDocument,
                owners.Party, owners.Account, Currency, "1250.50", OccurredOn)]);

        await using (var scope = run.OpenItemSourceService(
            CompanyA, owners, placeholderBatch, sourcePort: new UnavailableApprovedOpenItemSource()))
        {
            var refused = await scope.Service.AdmitAsync(
                scope.Context, Key("open-item-source-unavailable-file"), reference, CancellationToken.None);
            Assert.Equal(CommandOutcome.Unavailable, refused.Outcome);
            Assert.Equal(
                "finance_open_item_source_unavailable",
                Assert.Single(refused.Issues).Code);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItems"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItemSourceBatches"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.open-item-source.admitted.v1'"));
    }

    [SqlServerFact]
    public async Task Approved_batch_version_or_checksum_drift_is_refused_without_writes()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var requested = new FinanceOpenItemSourceReference(
            Guid.NewGuid(), "requested-v1", new string('a', 64));
        var driftedBatch = new ApprovedFinanceOpenItemSourceBatch(
            requested.BatchId, CompanyA, "adapter-v2", new string('b', 64),
            [new ApprovedFinanceOpenItemSourceRow(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "invoice-v1",
                FinanceOpenItemSourceKinds.OfficialInvoiceInstallmentDocument,
                owners.Party, owners.Account, Currency, "1250.50", OccurredOn)]);

        await using (var scope = run.OpenItemSourceService(CompanyA, owners, driftedBatch))
        {
            var refused = await scope.Service.AdmitAsync(
                scope.Context, Key("open-item-source-metadata-drift"), requested, CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
            Assert.Equal(
                "finance_open_item_source_metadata_mismatch",
                Assert.Single(refused.Issues).Code);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItems"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItemSourceBatches"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.open-item-source.admitted.v1'"));
    }

    private static CommandMetadata Key(string key) => new CommandMetadata(key);

    private static DraftSaveRequest Save(Owners owners, string amount, Guid? draftId = null, string? expectedVersion = null,
        DateOnly? occurredOn = null, DateOnly? valueOn = null, PosCollectionInput? pos = null) =>
        new DraftSaveRequest(draftId, expectedVersion, new ReceiptPreparation(
            owners.Party, owners.Account, owners.Bank, new MoneyText(amount, Currency),
            new ProfileReference(ProfileId.ToString("D"), "1"), occurredOn ?? OccurredOn, valueOn ?? ValueOn, "SENTETIK-REF", null,
            pos));

    private sealed record Owners(Guid Company, Guid Bank, Guid Party, Guid Account)
    {
        public static Owners For(Guid company) => new Owners(company, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    }

    private sealed record SavedDraft(Guid Id, string Version);

    private sealed record SavedOpenItem(Guid Id, string Version);

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public string Actor { get; } = Guid.NewGuid().ToString("D");
        public string Correlation { get; } = $"fin-t05a-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            await using var context = run.Context(CompanyA);
            await context.Database.MigrateAsync();
            return run;
        }

        public FinanceDbContext Context(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .Options, companyContext);
        }

        public async Task<SavedDraft> SaveDraftAsync(Owners owners, string amount,
            DateOnly? occurredOn = null, DateOnly? valueOn = null)
        {
            await using var a = Service(owners.Company, owners);
            var saved = await a.Service.DraftSaveAsync(a.Context, Key($"save-{Guid.NewGuid():N}"),
                Save(owners, amount, occurredOn: occurredOn, valueOn: valueOn), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, saved.Outcome);
            return new SavedDraft(saved.Resource!.Source.Id, saved.Resource.Source.Version);
        }

        /// <summary>
        /// A service scope. By default its period rule is the <see cref="SyntheticOpenPeriodRule"/> test input;
        /// <paramref name="productRule"/> uses the module's fallback rule, which decides nothing and refuses.
        /// </summary>
        public ServiceScope Service(Guid company, Owners owners, string? actor = null, string? correlation = null,
            bool productRule = false, IFinanceRecordPeriodRule? rule = null)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var currentUser = new FixedCurrentUser(actor ?? Actor, company);
            var collector = new AuditEventCollector();
            var options = new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext))
                .Options;
            var dbContext = new FinanceDbContext(options, companyContext);
            var service = new ReceiptDraftService(dbContext, companyContext, currentUser,
                new SyntheticPermissions(), new SyntheticBank(owners), new SyntheticParty(owners), new SyntheticSettings(company),
                collector, new FinanceReceiptDraftBinding(Operation),
                new FinanceAuditEnvelope(ReceiptDraftService.SaveDraftCommand, ["SENTETIK-FINANCE"], "Web"),
                TimeProvider.System,
                new FinanceAuditEnvelope(ReceiptDraftService.RecordCommand, ["SENTETIK-FINANCE"], "Web"),
                rule ?? (productRule ? new UndecidedFinanceRecordPeriodRule() : new SyntheticOpenPeriodRule()));
            return new ServiceScope(dbContext, service, new FinanceContext(company, actor ?? Actor, correlation ?? Correlation, null));
        }

        public ReversalServiceScope ReversalService(Guid company, string actor,
            IFinanceRecordPeriodRule? rule = null, string? correlation = null,
            IFinanceReversalAllocationGate? allocationGate = null,
            IFinancePermissionPort? permissions = null)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var currentUser = new FixedCurrentUser(actor, company);
            var collector = new AuditEventCollector();
            var options = new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext))
                .Options;
            var dbContext = new FinanceDbContext(options, companyContext);
            var service = new ReversalProposalService(dbContext, companyContext, currentUser,
                permissions ?? new SyntheticPermissions(), rule ?? new SyntheticOpenPeriodRule(),
                allocationGate ?? new FinanceAllocationReversalGate(dbContext, companyContext, currentUser), collector,
                new FinanceReversalEnvelopes(
                    new FinanceAuditEnvelope(ReversalProposalService.ProposeCommand, ["SENTETIK-FINANCE"], "Web"),
                    new FinanceAuditEnvelope(ReversalProposalService.ApproveCommand, ["SENTETIK-FINANCE"], "Web")),
                TimeProvider.System);
            return new ReversalServiceScope(dbContext, service,
                new FinanceContext(company, actor, correlation ?? $"fin-t06-{Guid.NewGuid():N}", null));
        }

        public AllocationServiceScope AllocationService(Guid company, string actor, IFinancePermissionPort permissions)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var currentUser = new FixedCurrentUser(actor, company);
            var collector = new AuditEventCollector();
            var dbContext = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext))
                .Options, companyContext);
            var service = new FinanceAllocationService(
                dbContext, companyContext, currentUser, permissions, collector,
                new FinanceAuditEnvelope(FinanceAllocationService.CreateCommandName, ["SENTETIK-FINANCE"], "Web"),
                TimeProvider.System);
            return new AllocationServiceScope(dbContext, service,
                new FinanceContext(company, actor, $"fin-f03-{Guid.NewGuid():N}", null));
        }

        public OpenItemSourceServiceScope OpenItemSourceService(
            Guid company,
            Owners owners,
            ApprovedFinanceOpenItemSourceBatch batch,
            IFinanceOpenItemSourceAuthority? authority = null,
            IFinancePartyAccountPort? parties = null,
            IApprovedFinanceOpenItemSourcePort? sourcePort = null)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var currentUser = new FixedCurrentUser(Actor, company);
            var collector = new AuditEventCollector();
            var dbContext = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext))
                .Options, companyContext);
            var service = new FinanceOpenItemSourceAdmissionService(
                dbContext, companyContext, currentUser,
                authority ?? new SyntheticOpenItemSourceAuthority(),
                sourcePort ?? new FixedApprovedOpenItemSource(batch),
                parties ?? new ActiveSyntheticParty(owners),
                collector,
                new FinanceAuditEnvelope(
                    "synthetic.finance.open-item-source.admit", ["SYNTHETIC-FINANCE"], "Web"),
                TimeProvider.System);
            return new OpenItemSourceServiceScope(
                dbContext, service, new FinanceContext(company, Actor, $"f03-{Guid.NewGuid():N}", null));
        }

        public async Task<SavedOpenItem> SeedOpenItemAsync(
            Guid company, Guid companyAccountId, Guid currencyId, string amount)
        {
            await using var dbContext = Context(company);
            var batchId = Guid.NewGuid();
            dbContext.OpenItemSourceBatches.Add(SyntheticSourceBatch(
                company, batchId, "synthetic.receivable.v1"));
            var row = new FinanceOpenItemRow
            {
                Id = Guid.NewGuid(), CompanyId = company, SourceKind = "synthetic.receivable.v1",
                SourceId = Guid.NewGuid(), SourceLineId = Guid.NewGuid(), SourceVersion = "1",
                SourceBatchId = batchId, SourceBatchVersion = "synthetic-v1",
                SourceBatchChecksum = new string('f', 64), PartyId = Guid.NewGuid(),
                CompanyAccountId = companyAccountId, CurrencyId = currencyId,
                OriginalAmountText = amount, OutstandingAmountText = amount,
                Status = FinanceOpenItemStatuses.Open, Version = 1,
                CreatedBy = "synthetic-finance-open-item-fixture", CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedBy = "synthetic-finance-open-item-fixture", UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            dbContext.OpenItems.Add(row);
            await dbContext.SaveChangesAsync();
            return new SavedOpenItem(row.Id, row.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// F-03 predecessor fixture: persists the owner row through FinanceDbContext so the reversal
        /// gate performs a real company-filtered SQL read. This is not an allocation command or a
        /// user/grant acceptance; the command/open-item owner path is a separate pending slice.
        /// </summary>
        public async Task<Guid> SeedActiveAllocationAsync(
            Guid company,
            Guid sourceId,
            Guid companyAccountId,
            Guid currencyId,
            string sourceAmount,
            string amount)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            await using var dbContext = new FinanceDbContext(
                new DbContextOptionsBuilder<FinanceDbContext>()
                    .UseSqlServer(Factory.DatabaseConnectionString,
                        sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                    .Options,
                companyContext);
            var id = Guid.NewGuid();
            var openItemId = Guid.NewGuid();
            var batchId = Guid.NewGuid();
            dbContext.OpenItemSourceBatches.Add(SyntheticSourceBatch(
                company, batchId, "synthetic.receivable.v1"));
            dbContext.OpenItems.Add(new FinanceOpenItemRow
            {
                Id = openItemId,
                CompanyId = company,
                SourceKind = "synthetic.receivable.v1",
                SourceId = Guid.NewGuid(),
                SourceLineId = Guid.NewGuid(),
                SourceVersion = "1",
                SourceBatchId = batchId,
                SourceBatchVersion = "synthetic-v1",
                SourceBatchChecksum = new string('f', 64),
                PartyId = Guid.NewGuid(),
                CompanyAccountId = companyAccountId,
                CurrencyId = currencyId,
                OriginalAmountText = sourceAmount,
                OutstandingAmountText = "1000.40",
                Status = FinanceOpenItemStatuses.Open,
                Version = 2,
                CreatedBy = "synthetic-finance-open-item-fixture",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedBy = "synthetic-finance-allocation-fixture",
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            });
            dbContext.SourceAllocationStates.Add(new FinanceSourceAllocationStateRow
            {
                CompanyId = company,
                SourceId = sourceId,
                CurrencyId = currencyId,
                OriginalAmountText = sourceAmount,
                UnallocatedAmountText = "1000.40",
                Version = 1,
                UpdatedBy = "synthetic-finance-allocation-fixture",
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            });
            dbContext.Allocations.Add(new FinanceAllocationRow
            {
                Id = id,
                CompanyId = company,
                SourceId = sourceId,
                OpenItemId = openItemId,
                CurrencyId = currencyId,
                AmountText = amount,
                Reason = "SENTETIK persisted reversal gate allocation",
                Status = FinanceAllocationStatuses.Active,
                Version = 1,
                CreatedBy = "synthetic-finance-allocation-fixture",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await dbContext.SaveChangesAsync();
            return id;
        }

        private static FinanceOpenItemSourceBatchRow SyntheticSourceBatch(
            Guid company, Guid batchId, string sourceKind) => new()
        {
            Id = batchId,
            CompanyId = company,
            SourceKind = sourceKind,
            SourceVersion = "synthetic-v1",
            SourceChecksum = new string('f', 64),
            IdempotencyKeyHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            PayloadHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            ActorId = "synthetic-finance-open-item-fixture",
            ResultJson = "{}",
            CorrelationId = "synthetic-finance-open-item-fixture",
            AdmittedAtUtc = DateTimeOffset.UtcNow,
        };

        /// <summary>The Accounting tables in the SAME database, so a period and a money event are one system.</summary>
        public async Task MigrateAccountingAsync()
        {
            await using var context = AccountingContext(CompanyA);
            await context.Database.MigrateAsync();
        }

        public AccountingDbContext AccountingContext(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AccountingDbContext.Schema))
                .Options, companyContext);
        }

        /// <summary>
        /// A REAL period of the company through the real store: proposed by one actor, approved by ANOTHER
        /// (Ö4), and closed when asked. No period is written by hand.
        /// </summary>
        public async Task<Guid> PeriodAsync(Guid company, DateOnly startsOn, DateOnly endsOn, bool close)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            await using var dbContext = AccountingContext(company);
            var settings = new ApprovedSettingsPrerequisiteProvider(companyContext, new FixedCurrentUser(Actor, company));
            var store = new AccountingStore(dbContext, companyContext, new AuditEventCollector(), TimeProvider.System,
                new AccountingAmountScaleVerifier(settings));
            var preparer = Guid.NewGuid();
            var approver = Guid.NewGuid();
            var periodId = Guid.NewGuid();
            var (proposed, _) = await store.ProposePeriodAsync(periodId, startsOn, endsOn, preparer, PeriodMetadata());
            var approved = await store.ApprovePeriodAsync(periodId, proposed.Version, approver, PeriodMetadata());
            if (close)
            {
                await store.ClosePeriodAsync(periodId, approved.Version, approver, PeriodMetadata());
            }

            return periodId;
        }

        private AccountingCommandMetadata PeriodMetadata() => new AccountingCommandMetadata(
            Guid.NewGuid().ToString("D"), "synthetic.accounting.test-write", ["SYNTHETIC-FINANCE-T05-TEST"],
            $"fin-t05-{Guid.NewGuid():N}", null, Guid.NewGuid().ToString("N"), "Web");

        /// <summary>
        /// The PRODUCT's rule over the PRODUCT's port, against the real Accounting rows. The permission the
        /// port measures is a synthetic TEST grant (never a Sinan or production grant); `granted: false` is an
        /// actor that does not hold `accounting.periods.read`, and `swapDuringRead` is another actor taking
        /// the context while the owner answers.
        /// </summary>
        public IFinanceRecordPeriodRule RealRule(Guid company, bool granted = true, bool swapDuringRead = false,
            string? actor = null)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var user = new SwappableUser(actor ?? Actor, company);
            var dbContext = AccountingContext(company);
            var settings = new ApprovedSettingsPrerequisiteProvider(companyContext, new FixedCurrentUser(Actor, company));
            var store = new AccountingStore(dbContext, companyContext, new AuditEventCollector(), TimeProvider.System,
                new AccountingAmountScaleVerifier(settings));
            var reads = swapDuringRead
                ? (IAccountingPeriodStore)new SwappingPeriodStore(store, user)
                : store;
            return new OccurredOnFinanceRecordPeriodRule(
                new AccountingPeriodStatusPort(companyContext, user, new SyntheticAuthority(granted), reads));
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
    }

    private sealed class ServiceScope(FinanceDbContext dbContext, ReceiptDraftService service, FinanceContext context)
        : IAsyncDisposable
    {
        public IManualReceiptService Service => service;
        public FinanceContext Context => context;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class ReversalServiceScope(
        FinanceDbContext dbContext,
        ReversalProposalService service,
        FinanceContext context) : IAsyncDisposable
    {
        public IReversalProposalService Service => service;
        public FinanceContext Context => context;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class AllocationServiceScope(
        FinanceDbContext dbContext, FinanceAllocationService service, FinanceContext context) : IAsyncDisposable
    {
        public IFinanceAllocationService Service => service;
        public FinanceContext Context => context;
        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class OpenItemSourceServiceScope(
        FinanceDbContext dbContext,
        FinanceOpenItemSourceAdmissionService service,
        FinanceContext context) : IAsyncDisposable
    {
        public IFinanceOpenItemSourceAdmissionService Service => service;
        public FinanceContext Context => context;
        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class SyntheticOpenItemSourceAuthority : IFinanceOpenItemSourceAuthority
    {
        public Task<PermissionDecision> CheckAsync(
            FinanceContext context, Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult(PermissionDecision.Allowed);
    }

    private sealed class RevokeOpenItemSourceAuthorityAfterFirstCheck : IFinanceOpenItemSourceAuthority
    {
        private int checkCount;

        public Task<PermissionDecision> CheckAsync(
            FinanceContext context, Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult(Interlocked.Increment(ref checkCount) == 1
                ? PermissionDecision.Allowed
                : PermissionDecision.Denied);
    }

    private sealed class FinalOpenItemSourceAuthority(
        int waitAtCheck,
        Barrier barrier) : IFinanceOpenItemSourceAuthority
    {
        private int checkCount;

        public Task<PermissionDecision> CheckAsync(
            FinanceContext context, Guid batchId, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref checkCount) == waitAtCheck
                && !barrier.SignalAndWait(TimeSpan.FromSeconds(30), cancellationToken))
            {
                throw new TimeoutException(
                    "Concurrent open-item admissions did not reach the final save fence.");
            }
            return Task.FromResult(PermissionDecision.Allowed);
        }
    }

    private sealed class FixedApprovedOpenItemSource(ApprovedFinanceOpenItemSourceBatch batch)
        : IApprovedFinanceOpenItemSourcePort
    {
        public Task<ReadResult<ApprovedFinanceOpenItemSourceBatch>> GetApprovedAsync(
            FinanceContext context,
            FinanceOpenItemSourceReference source,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ReadResult<ApprovedFinanceOpenItemSourceBatch>(ReadOutcome.Found, batch));
    }

    private sealed class UnavailableApprovedOpenItemSource : IApprovedFinanceOpenItemSourcePort
    {
        public Task<ReadResult<ApprovedFinanceOpenItemSourceBatch>> GetApprovedAsync(
            FinanceContext context,
            FinanceOpenItemSourceReference source,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ReadResult<ApprovedFinanceOpenItemSourceBatch>(
                ReadOutcome.Unavailable, null));
    }

    private sealed class ActiveSyntheticParty(Owners owners) : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(
            FinanceContext context,
            Guid partyId,
            Guid companyAccountId,
            CancellationToken cancellationToken) =>
            Task.FromResult(partyId == owners.Party && companyAccountId == owners.Account
                && context.CompanyId == owners.Company
                ? new ReadResult<PartyAccountEligibility>(ReadOutcome.Found,
                    new PartyAccountEligibility(
                        owners.Party, owners.Account, owners.Company, true, IsPartyActive: true))
                : new ReadResult<PartyAccountEligibility>(ReadOutcome.NotFound, null));
    }

    private sealed class MissingOpenItemParty : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(
            FinanceContext context,
            Guid partyId,
            Guid companyAccountId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ReadResult<PartyAccountEligibility>(ReadOutcome.NotFound, null));
    }

    private sealed class FixedAllocationGate(ReversalAllocationAnswer answer) : IFinanceReversalAllocationGate
    {
        public Task<ReversalAllocationAnswer> CheckAsync(FinanceContext context, Guid originalSourceId,
            CancellationToken cancellationToken) => Task.FromResult(answer);
    }

    /// <summary>TEST INPUT: answers "open" for any date. Not a period policy — the product's rule (K1, #66 5748258299)
    /// is what the case below measures against real Accounting periods.</summary>
    private sealed class SyntheticOpenPeriodRule : IFinanceRecordPeriodRule
    {
        public Task<RecordPeriodAnswer> CheckAsync(FinanceContext context, DateOnly occurredOn, DateOnly? valueOn,
            DateTimeOffset recordedAtUtc, CancellationToken cancellationToken) =>
            Task.FromResult(new RecordPeriodAnswer(RecordPeriodOutcome.Open, null));
    }

    /// <summary>SENTETIK: allows every Finance action. Not a grant.</summary>
    private sealed class SyntheticPermissions : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }

    /// <summary>SENTETIK: Start is allowed, then the same actor/company loses the action before SaveChanges.</summary>
    private sealed class RevokeAfterFirstPermissionCheck : IFinancePermissionPort
    {
        private int _checkCount;

        public int CheckCount => Volatile.Read(ref _checkCount);

        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(
            Interlocked.Increment(ref _checkCount) == 1
                ? PermissionDecision.Allowed
                : PermissionDecision.Denied);
    }

    private sealed class FinalWriteBarrierPermission(
        FinanceAction action, int waitAtCheck, Barrier barrier) : IFinancePermissionPort
    {
        private int checkCount;
        public Task<PermissionDecision> CheckAsync(
            FinanceContext context, FinanceAction requestedAction, Guid? sourceId,
            CancellationToken cancellationToken)
        {
            if (requestedAction == action && Interlocked.Increment(ref checkCount) == waitAtCheck
                && !barrier.SignalAndWait(TimeSpan.FromSeconds(30), cancellationToken))
            {
                throw new TimeoutException("The concurrent Finance writers did not reach the final save fence.");
            }
            return Task.FromResult(PermissionDecision.Allowed);
        }
    }

    /// <summary>SENTETIK: one usable bank account of the company in the test currency.</summary>
    private sealed class SyntheticBank(Owners owners) : IFinanceBankAccountReader
    {
        public Task<ReadResult<BankAccountPage>> ListAuthorizedAsync(FinanceContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReadResult<BankAccountView>> GetAuthorizedAsync(FinanceContext context, Guid accountId,
            CancellationToken cancellationToken) =>
            Task.FromResult(accountId == owners.Bank && context.CompanyId == owners.Company
                ? new ReadResult<BankAccountView>(ReadOutcome.Found,
                    new BankAccountView(owners.Bank, owners.Company, Currency, "SENTETIK banka", true, true, "1"))
                : new ReadResult<BankAccountView>(ReadOutcome.NotFound, null));
    }

    /// <summary>SENTETIK: one active party account of the company.</summary>
    private sealed class SyntheticParty(Owners owners) : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
            Guid companyAccountId, CancellationToken cancellationToken) =>
            Task.FromResult(partyId == owners.Party && companyAccountId == owners.Account && context.CompanyId == owners.Company
                ? new ReadResult<PartyAccountEligibility>(ReadOutcome.Found,
                    new PartyAccountEligibility(owners.Party, owners.Account, owners.Company, true))
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

    /// <summary>The caller's own authority as a synthetic TEST grant: `accounting.periods.read`, company-wide.</summary>
    private sealed class SyntheticAuthority(bool granted) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(granted && permission == AccountingPermissionManifestV1.PeriodsRead
                ? new PermissionScope(true, true, new HashSet<string>(StringComparer.Ordinal) { "SYNTHETIC-FINANCE-T05-TEST" },
                    new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
    }

    /// <summary>An actor whose identity can change mid-request, as a second actor taking the context would.</summary>
    private sealed class SwappableUser(string userId, Guid company) : ICurrentUser
    {
        private readonly HashSet<Guid> _allowed = [company];

        public string? UserId { get; private set; } = userId;

        public bool IsAuthenticated => true;

        public IReadOnlySet<Guid> AllowedCompanyIds => _allowed;

        public bool CanAccessCompany(Guid companyId) => _allowed.Contains(companyId);

        /// <summary>Another actor with access to the SAME company takes the context.</summary>
        public void Swap() => UserId = $"synthetic-other-{Guid.NewGuid():N}";
    }

    /// <summary>
    /// The real period store, with the context swapped WHILE it answers: the shape the adapter's fence
    /// exists for (#366). It stores nothing and changes no row; it only moves the swap into the await.
    /// </summary>
    private sealed class SwappingPeriodStore(IAccountingPeriodStore inner, SwappableUser user) : IAccountingPeriodStore
    {
        public async Task<AccountingPeriodRecord?> FindPeriodCoveringAsync(DateOnly on, CancellationToken cancellationToken = default)
        {
            var record = await inner.FindPeriodCoveringAsync(on, cancellationToken);
            user.Swap();
            return record;
        }

        public Task<(AccountingPeriodRecord Record, AccountingWriteOutcome Outcome)> ProposePeriodAsync(Guid periodId,
            DateOnly startsOn, DateOnly endsOn, Guid preparedByUserId, AccountingCommandMetadata metadata,
            CancellationToken cancellationToken = default) =>
            inner.ProposePeriodAsync(periodId, startsOn, endsOn, preparedByUserId, metadata, cancellationToken);

        public Task<AccountingPeriodRecord> ApprovePeriodAsync(Guid periodId, long expectedVersion, Guid approvedByUserId,
            AccountingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            inner.ApprovePeriodAsync(periodId, expectedVersion, approvedByUserId, metadata, cancellationToken);

        public Task<AccountingPeriodRecord> ClosePeriodAsync(Guid periodId, long expectedVersion, Guid closedByUserId,
            AccountingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            inner.ClosePeriodAsync(periodId, expectedVersion, closedByUserId, metadata, cancellationToken);

        public Task<AccountingPeriodRecord?> FindPeriodAsync(Guid periodId, CancellationToken cancellationToken = default) =>
            inner.FindPeriodAsync(periodId, cancellationToken);

        public Task<AccountingPage<AccountingPeriodRecord>> ListPeriodsAsync(int pageSize, string? cursor,
            CancellationToken cancellationToken = default) =>
            inner.ListPeriodsAsync(pageSize, cursor, cancellationToken);
    }

    private sealed class FixedCurrentUser(string userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
