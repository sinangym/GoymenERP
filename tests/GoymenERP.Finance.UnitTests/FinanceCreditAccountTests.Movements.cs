using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class FinanceCreditAccountTests
{
    [Theory]
    [InlineData(CreditAccountGroup.Cash)]
    [InlineData(CreditAccountGroup.NonCash)]
    [InlineData(CreditAccountGroup.InstrumentAndFactoring)]
    [InlineData(CreditAccountGroup.Leasing)]
    public async Task Approved_field_lists_allow_actual_proposal_but_never_approve_the_preparers_own_movement(CreditAccountGroup group)
    {
        await using var run = new Run();
        var proposedAccount = (await run.ProposeAsync(Maker, Request() with {
            Group = group, Profile = Profile(group), Installments = group == CreditAccountGroup.NonCash ? [] : Plan()
        })).Resource!;
        var account = (await run.ApproveAsync(Checker, new(proposedAccount.Id, "1"))).Resource!;
        var proposed = await run.Movement(Maker, service => service.ProposeAsync(Ctx(Maker), new("approved-field-list"),
            Movement(account), default), gate: FinanceCreditGroupGateV1.ApprovedFieldListsV12);
        Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome);
        var selfApproval = await run.Movement(Maker, service => service.DecideAsync(Ctx(Maker), new("self-approval"),
            new(proposed.Resource!.Id, "1", true, null), default), gate: FinanceCreditGroupGateV1.ApprovedFieldListsV12);
        Assert.Equal(CommandOutcome.ValidationFailed, selfApproval.Outcome);
        Assert.Equal(("actor", "credit_movement_maker_cannot_approve"),
            (Assert.Single(selfApproval.Issues).Field, selfApproval.Issues[0].Code));
        await using var db = run.Memory(CompanyA);
        Assert.Empty(await db.OutboxMessages.ToArrayAsync());
        Assert.Equal(1, (await db.CreditMovements.SingleAsync()).Status);
        Assert.False(FinanceCreditGroupGateV1.ApprovedFieldListsV12.MovementsOpen(int.MaxValue));
    }

    [Fact]
    public async Task Planned_credit_does_not_move_money_and_actual_approval_emits_once_with_unknown_TL_kept_null()
    {
        await using var run = new Run();
        var account = await ApprovedCredit(run);
        await using (var before = run.Memory(CompanyA)) Assert.Empty(await before.OutboxMessages.ToListAsync());
        var input = Movement(account);
        var proposal = await run.Movement(Maker, service => service.ProposeAsync(Ctx(Maker), new("actual-p"), input, default));
        Assert.Equal(CommandOutcome.Succeeded, proposal.Outcome);
        var own = await run.Movement(Maker, service => service.DecideAsync(Ctx(Maker), new("own"), new(proposal.Resource!.Id, "1", true, null), default));
        Assert.Equal(CommandOutcome.ValidationFailed, own.Outcome);
        var approve = new CreditMovementDecisionV1(proposal.Resource!.Id, "1", true, null);
        var result = await run.Movement(Checker, service => service.DecideAsync(Ctx(Checker), new("actual-a"), approve, default));
        var replay = await run.Movement(Checker, service => service.DecideAsync(Ctx(Checker), new("actual-a"), approve, default));
        Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
        Assert.True(replay.IsReplay);
        await using var db = run.Memory(CompanyA);
        var outbox = Assert.Single(await db.OutboxMessages.ToListAsync());
        var recorded = JsonSerializer.Deserialize<FinanceCreditMovementRecordedV1>(outbox.PayloadJson, FinanceJson.CreateOptions())!;
        Assert.Equal(outbox.Id, recorded.EventId);
        Assert.Equal("100.00", Assert.Single(recorded.Components).Amount);
        Assert.Null(recorded.Components[0].FunctionalAmount);
        Assert.Null(recorded.BankEffect);
        Assert.Equal(2, (await db.CreditMovements.SingleAsync()).Version);
    }

    [Fact]
    public async Task Credit_group_without_an_approved_field_spec_keeps_its_movements_closed_until_approval()
    {
        await using var run = new Run();
        var account = await ApprovedCredit(run);
        var input = Movement(account);
        // Host default (FNS-D106): no group spec is approved, so the proposal is a recorded refusal and writes nothing.
        var closed = await run.Movement(Maker, service => service.ProposeAsync(Ctx(Maker), new("closed-p"), input, default),
            gate: FinanceCreditGroupGateV1.NoneApproved);
        Assert.Equal(CommandOutcome.ValidationFailed, closed.Outcome);
        Assert.Equal(("creditAccountId", FinanceCreditGroupGateV1.GroupClosedCode),
            (Assert.Single(closed.Issues).Field, closed.Issues[0].Code));
        var otherGroupOnly = await run.Movement(Maker, service => service.ProposeAsync(Ctx(Maker), new("other-p"), input, default),
            gate: new FinanceCreditGroupGateV1([CreditAccountGroup.Leasing]));
        Assert.Equal(CommandOutcome.ValidationFailed, otherGroupOnly.Outcome);

        // A proposal made while open cannot be approved once the gate is closed: approval re-checks the group.
        var proposal = await run.Movement(Maker, service => service.ProposeAsync(Ctx(Maker), new("open-p"), input, default));
        Assert.Equal(CommandOutcome.Succeeded, proposal.Outcome);
        var refused = await run.Movement(Checker, service => service.DecideAsync(Ctx(Checker), new("closed-a"),
            new(proposal.Resource!.Id, "1", true, null), default), gate: FinanceCreditGroupGateV1.NoneApproved);
        Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
        Assert.Equal(FinanceCreditGroupGateV1.GroupClosedCode, Assert.Single(refused.Issues).Code);

        await using var db = run.Memory(CompanyA);
        Assert.Equal(1, (await db.CreditMovements.SingleAsync()).Status);
        Assert.Empty(await db.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task Actual_source_duplicate_stale_version_and_foreign_company_never_create_a_second_fact()
    {
        await using var run = new Run();
        var account = await ApprovedCredit(run);
        var input = Movement(account);
        Assert.Equal(CommandOutcome.Succeeded, (await run.Movement(Maker, service => service.ProposeAsync(Ctx(Maker), new("one"), input, default))).Outcome);
        Assert.Equal(CommandOutcome.ValidationFailed, (await run.Movement(Maker, service => service.ProposeAsync(Ctx(Maker), new("duplicate"), input, default))).Outcome);
        Assert.Equal(CommandOutcome.VersionConflict, (await run.Movement(Maker, service => service.ProposeAsync(Ctx(Maker), new("stale"), input with { ExpectedCreditVersion = "1" }, default))).Outcome);
        var foreign = new FinanceContext(CompanyB, Maker, "SENTETIK", null);
        Assert.Equal(CommandOutcome.Denied, (await run.Movement(Maker, service => service.ProposeAsync(foreign, new("foreign"), input, default))).Outcome);
        await using var db = run.Memory(CompanyA);
        Assert.Single(await db.CreditMovements.ToListAsync()); Assert.Empty(await db.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task Closed_opening_correction_keeps_original_and_emits_reversal_plus_new_in_the_selected_open_period()
    {
        await using var run = new Run();
        var account = await ApprovedCredit(run);
        var periods = new MovementPeriods();
        var opening = Movement(account) with { Kind = CreditMovementKindV1.OpeningBalance };
        var proposed = await run.Movement(Maker, service => service.ProposeAsync(Ctx(Maker), new("opening"), opening, default), periods);
        var approved = await run.Movement(Checker, service => service.DecideAsync(Ctx(Checker), new("opening-a"), new(proposed.Resource!.Id, "1", true, null), default), periods);
        Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
        periods.Opening = OpeningPeriodState.Closed; periods.Version = "2";
        var correction = opening with { SourceDocument = opening.SourceDocument with { Revision = "2" },
            Components = [new("principal", "105.00", null)], AccountingDate = new(2026, 12, 1),
            ReplacesOpeningMovementId = approved.Resource!.Id, ExpectedOpeningMovementVersion = "2" };
        var correct = await run.Movement(Maker, service => service.ProposeAsync(Ctx(Maker), new("correction"), correction, default), periods);
        Assert.Equal(CommandOutcome.Succeeded, correct.Outcome);
        var done = await run.Movement(Checker, service => service.DecideAsync(Ctx(Checker), new("correction-a"), new(correct.Resource!.Id, "1", true, null), default), periods);
        Assert.Equal(CommandOutcome.Succeeded, done.Outcome);
        await using var db = run.Memory(CompanyA);
        var original = await db.CreditMovements.SingleAsync(x => x.Id == approved.Resource.Id);
        Assert.Equal(correct.Resource!.Id, original.ReversedByMovementId);
        Assert.Contains("100.00", original.SourceJson, StringComparison.Ordinal);
        var events = (await db.OutboxMessages.ToListAsync()).Select(x => JsonSerializer.Deserialize<FinanceCreditMovementRecordedV1>(x.PayloadJson, FinanceJson.CreateOptions())!).ToArray();
        Assert.Equal(3, events.Length);
        var reversed = Assert.Single(events, x => x.ReversesMovementId is not null);
        Assert.Equal(new DateOnly(2026, 12, 1), reversed.AccountingDate);
        Assert.Equal("100.00", reversed.Components[0].Amount);
        Assert.Equal("105.00", Assert.Single(events, x => x.MovementId == correct.Resource.Id).Components[0].Amount);
    }

    [Fact]
    public async Task Closed_accounting_period_and_late_authority_loss_leave_no_movement_audit_or_outbox()
    {
        await using var run = new Run();
        var account = await ApprovedCredit(run);
        var periods = new MovementPeriods { Accounting = RecordPeriodOutcome.NotOpen };
        var refused = await run.Movement(Maker, service => service.ProposeAsync(Ctx(Maker), new("closed"), Movement(account), default), periods);
        Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
        var permission = new MovementRevokedPermission();
        var revoked = await run.Movement(Maker, service => service.ProposeAsync(Ctx(Maker), new("late"), Movement(account), default),
            permissions: permission, auditObserved: () => permission.Revoked = true);
        Assert.Equal(CommandOutcome.Denied, revoked.Outcome);
        await using var db = run.Memory(CompanyA);
        Assert.Empty(await db.CreditMovements.ToListAsync()); Assert.Empty(await db.OutboxMessages.ToListAsync());
        Assert.Empty(await db.Set<AuditEvent>().Where(x => x.EntityType == "Finance.CreditMovement").ToListAsync());
    }

    [Fact]
    public void Movement_shape_does_not_accept_missing_money_or_turn_an_accrual_into_principal_or_bank_cash()
    {
        var source = Movement(new(Guid.NewGuid(), CompanyA, CreditAccountGroup.Cash, Lender, Contract, Currency, "100", Start,
            CreditAccountStatus.Approved, false, At, At, null, "2", null, []));
        Assert.Empty(FinanceCreditMovementPolicyV1.Validate(source));
        Assert.NotEmpty(FinanceCreditMovementPolicyV1.Validate(source with { Components = [new("principal", null!, null)] }));
        Assert.NotEmpty(FinanceCreditMovementPolicyV1.Validate(source with { Kind = CreditMovementKindV1.InterestAccrual }));
        Assert.NotEmpty(FinanceCreditMovementPolicyV1.Validate(source with { Kind = CreditMovementKindV1.OpeningBalance, BankEffect = new(Guid.NewGuid(), "10", "SOURCE") }));
        Assert.NotEmpty(FinanceCreditMovementPolicyV1.Validate(source with { AccountingDate = Start.AddDays(1) }));
    }

    private static FinanceContext Ctx(string actor) => new(CompanyA, actor, "SENTETIK-credit-actual", null);
    private static CreditMovementProposeV1 Movement(CreditAccountView account) => new(account.Id, account.Version,
        CreditMovementKindV1.Drawdown, new("SENTETIK actual bank document", Start, "1"), Start, Start,
        [new("principal", "100.00", null)], null, null, null, "SENTETIK bank source");
    private static async Task<CreditAccountView> ApprovedCredit(Run run)
    {
        var proposed = (await run.ProposeAsync(Maker, Request())).Resource!;
        return (await run.ApproveAsync(Checker, new(proposed.Id, "1"))).Resource!;
    }
    private sealed class MovementPeriods : IFinanceOpeningPeriodReader, IFinanceCreditAccountingPeriodPortV1
    {
        public OpeningPeriodState Opening { get; set; } = OpeningPeriodState.Open;
        public string Version { get; set; } = "1";
        public RecordPeriodOutcome Accounting { get; set; } = RecordPeriodOutcome.Open;
        public Task<FinanceOpeningPeriodAnswer> ReadAsync(Guid companyId, CancellationToken cancellationToken) => Task.FromResult(new FinanceOpeningPeriodAnswer(Opening, Version));
        public Task<RecordPeriodAnswer> ReadAsync(FinanceContext context, DateOnly accountingDate, CancellationToken cancellationToken) => Task.FromResult(new RecordPeriodAnswer(Accounting, null));
    }
    private sealed class SyntheticMovementBalance : IFinanceTreasuryBalanceGuard
    {
        public Task<TreasuryBalanceAnswer> CheckOutgoingAsync(FinanceContext context, ExpensePaymentSourceKind sourceKind,
            Guid treasuryAccountId, MoneyText money, CancellationToken cancellationToken) => Task.FromResult(new TreasuryBalanceAnswer(TreasuryBalanceOutcome.Sufficient, null));
    }
    /// <summary>SENTETIK: the movement mechanics are tested as if every group's FNS-D106 spec were approved; the host
    /// default (<see cref="FinanceCreditGroupGateV1.NoneApproved"/>) is tested separately.</summary>
    private static readonly FinanceCreditGroupGateV1 OpenGroups = new(Enum.GetValues<CreditAccountGroup>());

    private sealed partial class Run
    {
        public async Task<CreditMovementResultV1> Movement(string actor, Func<FinanceCreditMovementServiceV1, Task<CreditMovementResultV1>> call,
            MovementPeriods? periods = null, IFinancePermissionPort? permissions = null, Action? auditObserved = null,
            FinanceCreditGroupGateV1? gate = null)
        {
            var company = new CompanyContext(); company.Set(CompanyA);
            var collector = new AuditEventCollector();
            await using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase(databaseName)
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options, company);
            periods ??= new();
            return await call(new(db, company, new FixedUser(actor, CompanyA), permissions ?? new RecordingPermission(PermissionDecision.Allowed),
                new SyntheticCreditSources(CompanyA), periods, periods, new SyntheticMovementBalance(), new ObservedMovementAudit(collector, auditObserved), Envelopes(), new FixedClock(),
                groupGate: gate ?? OpenGroups));
        }
    }
    private sealed class ObservedMovementAudit(IAuditEventCollector inner, Action? observed) : IAuditEventCollector
    {
        public void Enqueue(AuditEventDraft value) { inner.Enqueue(value); observed?.Invoke(); }
    }
    private sealed class MovementRevokedPermission : IFinancePermissionPort
    {
        public bool Revoked { get; set; }
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId, CancellationToken cancellationToken) =>
            Task.FromResult(Revoked ? PermissionDecision.Denied : PermissionDecision.Allowed);
    }
}
