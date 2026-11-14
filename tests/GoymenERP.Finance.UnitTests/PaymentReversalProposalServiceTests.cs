using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

/// <summary>API-23 payment reversal service behavior in memory. SENTETIK / NOT_ENV; not SQL or browser evidence.</summary>
public sealed class PaymentReversalProposalServiceTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly DateTimeOffset At = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly ReversalOn = new(2026, 11, 12);

    [Fact]
    public async Task Propose_then_approve_by_another_user_appends_one_reversal_and_closes_the_allocation_state()
    {
        await using var run = await Run.StartAsync();
        PaymentReversalProposalCommandResult proposed;
        await using (var maker = run.Scope("maker"))
        {
            proposed = await maker.Service.ProposeAsync(maker.Context, Key("propose-1"), Propose(run),
                CancellationToken.None);
        }
        Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome);
        Assert.Equal(ReversalProposalStatus.Pending, proposed.Resource!.Status);
        Assert.Equal("1", proposed.Resource.Version);

        await using (var verify = run.Context())
        {
            Assert.Equal(0, await verify.OutgoingMoneyEventReversals.CountAsync());
        }

        PaymentReversalProposalCommandResult approved;
        await using (var checker = run.Scope("checker"))
        {
            approved = await checker.Service.ApproveAsync(checker.Context, Key("approve-1"),
                new ApprovePaymentReversalRequest(proposed.Resource.Id, "1"), CancellationToken.None);
        }
        Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
        Assert.Equal(ReversalProposalStatus.Approved, approved.Resource!.Status);
        Assert.Equal("checker", approved.Resource.DecidedBy);
        Assert.NotNull(approved.Resource.ReversalEventId);

        await using var after = run.Context();
        var reversal = Assert.Single(await after.OutgoingMoneyEventReversals.ToListAsync());
        Assert.Equal(approved.Resource.ReversalEventId, reversal.Id);
        Assert.Equal(run.PaymentId, reversal.PaymentId);
        Assert.Equal(run.EventId, reversal.OriginalEventId);
        Assert.Equal("1250.50", reversal.AmountText);
        Assert.Equal(ReversalOn, reversal.OccurredOn);
        var state = Assert.Single(await after.PaymentAllocationStates.ToListAsync());
        Assert.Equal("0.00", state.UnallocatedAmountText);
        Assert.Equal(1, await after.OutgoingMoneyEvents.CountAsync());
        var payment = await after.ManualBankPayments.SingleAsync();
        Assert.Equal(ManualBankPaymentStatuses.Recorded, payment.Status);
        Assert.Equal(3, payment.Version);
        Assert.Equal(2, await after.Commands.CountAsync());
        var audits = await after.Set<AuditEvent>().OrderBy(row => row.EventType).ToListAsync();
        Assert.Equal(
            ["finance.payment-reversal.approved.v1", "finance.payment-reversal.proposed.v1"],
            audits.Select(row => row.EventType).ToArray());
    }

    [Fact]
    public async Task The_maker_cannot_approve_and_nothing_is_written()
    {
        await using var run = await Run.StartAsync();
        var proposal = await run.ProposeAsync("maker");
        await using (var maker = run.Scope("maker"))
        {
            var refused = await maker.Service.ApproveAsync(maker.Context, Key("self-approve"),
                new ApprovePaymentReversalRequest(proposal, "1"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Denied, refused.Outcome);
            Assert.Contains(refused.Issues, issue => issue.Code == "finance_approver_is_proposer");
        }
        await using var verify = run.Context();
        Assert.Equal(0, await verify.OutgoingMoneyEventReversals.CountAsync());
        Assert.Equal(ReversalProposalStatuses.Pending, (await verify.PaymentReversalProposals.SingleAsync()).Status);
        Assert.Equal(1, await verify.Commands.CountAsync());
    }

    [Fact]
    public async Task A_replayed_approval_returns_the_stored_answer_without_a_second_reversal()
    {
        await using var run = await Run.StartAsync();
        var proposal = await run.ProposeAsync("maker");
        var request = new ApprovePaymentReversalRequest(proposal, "1");
        PaymentReversalProposalCommandResult first;
        await using (var checker = run.Scope("checker"))
        {
            first = await checker.Service.ApproveAsync(checker.Context, Key("approve-replay"), request,
                CancellationToken.None);
        }
        await using (var checker = run.Scope("checker"))
        {
            var replay = await checker.Service.ApproveAsync(checker.Context, Key("approve-replay"), request,
                CancellationToken.None);
            Assert.True(replay.IsReplay);
            Assert.Equal(first.Resource, replay.Resource);

            var conflict = await checker.Service.ApproveAsync(checker.Context, Key("approve-replay"),
                request with { ExpectedVersion = "2" }, CancellationToken.None);
            Assert.Equal(CommandOutcome.IdempotencyConflict, conflict.Outcome);
        }
        await using var verify = run.Context();
        Assert.Equal(1, await verify.OutgoingMoneyEventReversals.CountAsync());
        Assert.Equal(2, await verify.Commands.CountAsync());
    }

    [Fact]
    public async Task A_second_proposal_is_refused_while_one_is_pending_and_after_the_payment_is_reversed()
    {
        await using var run = await Run.StartAsync();
        var proposal = await run.ProposeAsync("maker");
        await using (var other = run.Scope("other"))
        {
            var pending = await other.Service.ProposeAsync(other.Context, Key("propose-2"), Propose(run),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, pending.Outcome);
            Assert.Contains(pending.Issues, issue => issue.Code == "finance_reversal_proposal_already_pending");
        }
        await using (var checker = run.Scope("checker"))
        {
            var approved = await checker.Service.ApproveAsync(checker.Context, Key("approve-2"),
                new ApprovePaymentReversalRequest(proposal, "1"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
        }
        await using (var other = run.Scope("other"))
        {
            var reversed = await other.Service.ProposeAsync(other.Context, Key("propose-3"), Propose(run),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, reversed.Outcome);
            Assert.Contains(reversed.Issues, issue => issue.Code == "finance_source_already_reversed");
        }
        await using var verify = run.Context();
        Assert.Equal(1, await verify.PaymentReversalProposals.CountAsync());
        Assert.Equal(1, await verify.OutgoingMoneyEventReversals.CountAsync());
    }

    [Fact]
    public async Task An_applied_allocation_blocks_both_the_proposal_and_the_approval()
    {
        await using var run = await Run.StartAsync();
        var proposal = await run.ProposeAsync("maker");
        await using (var seed = run.Context())
        {
            seed.PaymentAllocations.Add(new FinancePaymentAllocationRow
            {
                Id = Guid.NewGuid(), CompanyId = Company, PaymentId = run.PaymentId, OpenItemId = Guid.NewGuid(),
                CurrencyId = run.CurrencyId, AmountText = "100.00", Reason = "SENTETIK",
                Status = FinanceAllocationStatuses.Active, Version = 1, CreatedBy = "allocator", CreatedAtUtc = At,
            });
            await seed.SaveChangesAsync();
        }
        await using (var checker = run.Scope("checker"))
        {
            var approval = await checker.Service.ApproveAsync(checker.Context, Key("approve-allocated"),
                new ApprovePaymentReversalRequest(proposal, "1"), CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, approval.Outcome);
            Assert.Contains(approval.Issues, issue => issue.Code == "finance_reversal_has_allocations");
        }
        await using (var other = run.Scope("other"))
        {
            await other.Service.RejectAsync(other.Context, Key("reject-allocated"),
                new RejectPaymentReversalRequest(proposal, "1", "SENTETIK"), CancellationToken.None);
            var again = await other.Service.ProposeAsync(other.Context, Key("propose-allocated"), Propose(run),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, again.Outcome);
            Assert.Contains(again.Issues, issue => issue.Code == "finance_reversal_has_allocations");
        }
        await using var verify = run.Context();
        Assert.Equal(0, await verify.OutgoingMoneyEventReversals.CountAsync());
    }

    [Fact]
    public async Task Rejection_by_another_user_records_the_reason_and_allows_a_new_proposal()
    {
        await using var run = await Run.StartAsync();
        var proposal = await run.ProposeAsync("maker");
        await using (var maker = run.Scope("maker"))
        {
            var self = await maker.Service.RejectAsync(maker.Context, Key("self-reject"),
                new RejectPaymentReversalRequest(proposal, "1", "SENTETIK"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Denied, self.Outcome);
        }
        await using (var checker = run.Scope("checker"))
        {
            var rejected = await checker.Service.RejectAsync(checker.Context, Key("reject-1"),
                new RejectPaymentReversalRequest(proposal, "1", "Ödeme doğru"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, rejected.Outcome);
            Assert.Equal(ReversalProposalStatus.Rejected, rejected.Resource!.Status);
            Assert.Equal("Ödeme doğru", rejected.Resource.DecisionReason);
            Assert.Null(rejected.Resource.ReversalEventId);
        }
        var second = await run.ProposeAsync("maker", "propose-after-reject");
        Assert.NotEqual(proposal, second);
        await using var verify = run.Context();
        Assert.Equal(0, await verify.OutgoingMoneyEventReversals.CountAsync());
        Assert.Equal(2, await verify.PaymentReversalProposals.CountAsync());
    }

    [Fact]
    public async Task A_stale_proposal_version_is_a_recorded_conflict_and_an_unknown_payment_is_not_found()
    {
        await using var run = await Run.StartAsync();
        await using (var maker = run.Scope("maker"))
        {
            var stale = await maker.Service.ProposeAsync(maker.Context, Key("propose-stale"),
                Propose(run) with { ExpectedVersion = "2" }, CancellationToken.None);
            Assert.Equal(CommandOutcome.VersionConflict, stale.Outcome);
            var missing = await maker.Service.ProposeAsync(maker.Context, Key("propose-missing"),
                Propose(run) with { PaymentId = Guid.NewGuid() }, CancellationToken.None);
            Assert.Contains(missing.Issues, issue => issue.Code == "finance_payment_not_found");
        }
        await using var verify = run.Context();
        Assert.Equal(0, await verify.PaymentReversalProposals.CountAsync());
        Assert.Equal(2, await verify.Commands.CountAsync());
    }

    [Fact]
    public async Task A_reversal_dated_before_the_payment_is_refused_and_nothing_is_written()
    {
        await using var run = await Run.StartAsync();
        await using (var maker = run.Scope("maker"))
        {
            var early = await maker.Service.ProposeAsync(maker.Context, Key("propose-early"),
                Propose(run) with { OccurredOn = new DateOnly(2026, 11, 4) }, CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, early.Outcome);
            var issue = Assert.Single(early.Issues);
            Assert.Equal(("occurredOn", "finance_reversal_before_payment"), (issue.Field, issue.Code));
        }
        await using var verify = run.Context();
        Assert.Equal(0, await verify.PaymentReversalProposals.CountAsync());
        Assert.Equal(0, await verify.Commands.CountAsync());
    }

    [Theory]
    [InlineData(PermissionDecision.Denied, CommandOutcome.Denied)]
    [InlineData(PermissionDecision.Unavailable, CommandOutcome.Unavailable)]
    public async Task Without_the_permission_nothing_is_proposed(PermissionDecision decision, CommandOutcome outcome)
    {
        await using var run = await Run.StartAsync();
        await using (var maker = run.Scope("maker", new FixedPermission(decision)))
        {
            var refused = await maker.Service.ProposeAsync(maker.Context, Key("propose-denied"), Propose(run),
                CancellationToken.None);
            Assert.Equal(outcome, refused.Outcome);
            var read = await maker.Service.PendingAuthorizedAsync(maker.Context, CancellationToken.None);
            Assert.NotEqual(ReadOutcome.Found, read.Outcome);
        }
        await using var verify = run.Context();
        Assert.Equal(0, await verify.PaymentReversalProposals.CountAsync());
        Assert.Equal(0, await verify.Commands.CountAsync());
    }

    [Fact]
    public async Task Pending_and_detail_reads_show_the_proposal()
    {
        await using var run = await Run.StartAsync();
        var proposal = await run.ProposeAsync("maker");
        await using var reader = run.Scope("reader");
        var pending = await reader.Service.PendingAuthorizedAsync(reader.Context, CancellationToken.None);
        Assert.Equal(ReadOutcome.Found, pending.Outcome);
        Assert.Equal(proposal, Assert.Single(pending.Resource!.Items).Id);
        var detail = await reader.Service.DetailAuthorizedAsync(reader.Context, proposal, CancellationToken.None);
        Assert.Equal(ReadOutcome.Found, detail.Outcome);
        Assert.Equal(run.PaymentId, detail.Resource!.PaymentId);
        var missing = await reader.Service.DetailAuthorizedAsync(reader.Context, Guid.NewGuid(),
            CancellationToken.None);
        Assert.Equal(ReadOutcome.NotFound, missing.Outcome);
    }

    private static CommandMetadata Key(string value) => new(value);

    private static ProposePaymentReversalRequest Propose(Run run) =>
        new(run.PaymentId, "3", ReversalOn, "Hatalı ödeme");

    private sealed class Run(string databaseName) : IAsyncDisposable
    {
        public Guid PaymentId { get; } = Guid.NewGuid();
        public Guid EventId { get; } = Guid.NewGuid();
        public Guid CurrencyId { get; } = Guid.NewGuid();

        public static async Task<Run> StartAsync()
        {
            var run = new Run($"finance-api23-{Guid.NewGuid():N}");
            await using var context = run.Context();
            var payment = new ManualBankPaymentRow
            {
                Id = run.PaymentId, CompanyId = Company, Version = 3, PartyId = Guid.NewGuid(),
                CompanyAccountId = Guid.NewGuid(), TreasuryAccountId = Guid.NewGuid(), CurrencyId = run.CurrencyId,
                AmountText = "1250.50", ProfileId = "SENTETIK-profile", ProfileVersion = "1",
                OccurredOn = new DateOnly(2026, 11, 5), Status = ManualBankPaymentStatuses.Recorded,
                PreparedBy = "preparer", PreparedAtUtc = At, ApprovedBy = "approver", ApprovedAtUtc = At,
                RecordedBy = "recorder", RecordedAtUtc = At, BankResultConfirmed = true,
            };
            context.ManualBankPayments.Add(payment);
            context.OutgoingMoneyEvents.Add(new OutgoingMoneyEventRow
            {
                Id = run.EventId, CompanyId = Company, PaymentId = payment.Id, PartyId = payment.PartyId,
                CompanyAccountId = payment.CompanyAccountId, TreasuryAccountId = payment.TreasuryAccountId,
                CurrencyId = payment.CurrencyId, AmountText = payment.AmountText, ProfileId = payment.ProfileId,
                ProfileVersion = payment.ProfileVersion, OccurredOn = payment.OccurredOn, RecordedAtUtc = At,
                RecordedBy = "recorder", CorrelationId = "corr-0",
            });
            await context.SaveChangesAsync();
            return run;
        }

        public async Task<Guid> ProposeAsync(string actor, string key = "propose-seed")
        {
            await using var scope = Scope(actor);
            var result = await scope.Service.ProposeAsync(scope.Context, Key(key),
                new ProposePaymentReversalRequest(PaymentId, "3", ReversalOn, "Hatalı ödeme"),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            return result.Resource!.Id;
        }

        public FinanceDbContext Context()
        {
            var company = new CompanyContext();
            company.Set(Company);
            return new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(databaseName).Options, company);
        }

        public Scope Scope(string actor, IFinancePermissionPort? permission = null)
        {
            var company = new CompanyContext();
            company.Set(Company);
            var collector = new AuditEventCollector();
            var context = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(databaseName)
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options, company);
            var service = new PaymentReversalProposalService(context, company, new User(actor),
                permission ?? new FixedPermission(PermissionDecision.Allowed), new OpenPeriod(), collector,
                new FinancePaymentReversalEnvelopes(
                    new FinanceAuditEnvelope(FinancePermissionNames.PaymentReversePropose, ["SENTETIK-FINANCE"], "Web"),
                    new FinanceAuditEnvelope(FinancePermissionNames.PaymentReverseApprove, ["SENTETIK-FINANCE"], "Web")),
                TimeProvider.System);
            return new Scope(context, service,
                new FinanceContext(Company, actor, $"api23-{Guid.NewGuid():N}", null));
        }

        public async ValueTask DisposeAsync()
        {
            await using var context = Context();
            await context.Database.EnsureDeletedAsync();
        }
    }

    private sealed class Scope(FinanceDbContext db, PaymentReversalProposalService service, FinanceContext context)
        : IAsyncDisposable
    {
        public IPaymentReversalProposalService Service => service;
        public FinanceContext Context => context;
        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    private sealed class FixedPermission(PermissionDecision decision) : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(decision);
    }

    private sealed class OpenPeriod : IFinanceRecordPeriodRule
    {
        public Task<RecordPeriodAnswer> CheckAsync(FinanceContext context, DateOnly occurredOn, DateOnly? valueOn,
            DateTimeOffset recordedAtUtc, CancellationToken cancellationToken) =>
            Task.FromResult(new RecordPeriodAnswer(RecordPeriodOutcome.Open, null));
    }

    private sealed class User(string actor) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }
}
