using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

/// <summary>FNS-D25 service behavior in memory. SENTETIK / NOT_ENV; not SQL or browser evidence.</summary>
public sealed class ExpensePaymentServiceTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Currency = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Bank = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Profile = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly DateOnly OccurredOn = new(2026, 11, 10);

    [Fact]
    public async Task Draft_approve_record_and_replay_keep_one_recorded_row_and_no_second_movement()
    {
        await using var run = await Run.StartAsync();
        var approved = await DraftAndApproveAsync(run);

        var request = new RecordExpensePaymentRequest(approved.Id, approved.Version, PaymentConfirmed: true);
        ExpensePaymentCommandResult recorded;
        await using (var recorder = run.Scope("checker", new AllowAll(), new FixedBalance(TreasuryBalanceOutcome.Sufficient)))
        {
            recorded = await recorder.Service.RecordAsync(recorder.Context, Key("record-1"), request,
                CancellationToken.None);
        }

        Assert.Equal((CommandOutcome.Succeeded, ExpensePaymentStatus.Recorded, true, "3"),
            (recorded.Outcome, recorded.Resource!.Status, recorded.Resource.PaymentConfirmed, recorded.Resource.Version));

        await using (var replayActor = run.Scope("checker", new AllowAll(),
                         new FixedBalance(TreasuryBalanceOutcome.Sufficient)))
        {
            var replay = await replayActor.Service.RecordAsync(replayActor.Context, Key("record-1"), request,
                CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, true, recorded.OriginalCorrelationId),
                (replay.Outcome, replay.IsReplay, replay.OriginalCorrelationId));
        }

        await using var verify = run.Context();
        var row = await verify.ExpensePayments.SingleAsync();
        Assert.Equal((ExpensePaymentStatuses.Recorded, SingleExpenseItemCatalog.GeneralExpenseId, "SENTETIK kirtasiye"),
            (row.Status, row.ExpenseItemId, row.Description));
        Assert.Equal(3, await verify.ExpensePaymentCommands.CountAsync());
        Assert.Equal(3, await verify.Set<AuditEvent>().CountAsync());
        Assert.Equal(0, await verify.OutgoingMoneyEvents.CountAsync());
    }

    [Fact]
    public async Task Registered_unavailable_balance_refuses_the_record_and_keeps_the_payment_approved()
    {
        await using var run = await Run.StartAsync();
        var approved = await DraftAndApproveAsync(run);

        await using (var recorder = run.Scope("checker", new AllowAll(), new UnavailableTreasuryBalanceGuard()))
        {
            var refused = await recorder.Service.RecordAsync(recorder.Context, Key("record-1"),
                new RecordExpensePaymentRequest(approved.Id, approved.Version, PaymentConfirmed: true),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Unavailable, refused.Outcome);
            Assert.Null(refused.Resource);
            Assert.Equal(TreasuryBalanceAnswer.UnavailableCode, Assert.Single(refused.Issues).Code);
        }

        await using var verify = run.Context();
        Assert.Equal(ExpensePaymentStatuses.Approved, (await verify.ExpensePayments.SingleAsync()).Status);
        Assert.Equal(2, await verify.ExpensePaymentCommands.CountAsync());
    }

    [Fact]
    public async Task Insufficient_balance_is_refused_never_clipped()
    {
        await using var run = await Run.StartAsync();
        var approved = await DraftAndApproveAsync(run);

        await using (var recorder = run.Scope("checker", new AllowAll(),
                         new FixedBalance(TreasuryBalanceOutcome.Insufficient)))
        {
            var refused = await recorder.Service.RecordAsync(recorder.Context, Key("record-1"),
                new RecordExpensePaymentRequest(approved.Id, approved.Version, PaymentConfirmed: true),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
            Assert.Equal(TreasuryBalanceAnswer.InsufficientCode, Assert.Single(refused.Issues).Code);
        }

        await using var verify = run.Context();
        var row = await verify.ExpensePayments.SingleAsync();
        Assert.Equal((ExpensePaymentStatuses.Approved, "1250.50", false), (row.Status, row.AmountText,
            row.PaymentConfirmed));
    }

    [Fact]
    public async Task Unconfirmed_record_is_refused_and_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        var approved = await DraftAndApproveAsync(run);

        await using (var recorder = run.Scope("checker", new AllowAll(),
                         new FixedBalance(TreasuryBalanceOutcome.Sufficient)))
        {
            var refused = await recorder.Service.RecordAsync(recorder.Context, Key("record-1"),
                new RecordExpensePaymentRequest(approved.Id, approved.Version, PaymentConfirmed: false),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
        }

        await using var verify = run.Context();
        Assert.Equal(ExpensePaymentStatuses.Approved, (await verify.ExpensePayments.SingleAsync()).Status);
        Assert.Equal(2, await verify.ExpensePaymentCommands.CountAsync());
    }

    [Fact]
    public async Task Preparer_cannot_approve_their_own_draft()
    {
        await using var run = await Run.StartAsync();
        await using var maker = run.Scope("maker", new AllowAll(), new UnavailableTreasuryBalanceGuard());
        var draft = await maker.Service.DraftSaveAsync(maker.Context, Key("draft-1"), Draft(), CancellationToken.None);
        var sameActor = await maker.Service.ApproveAsync(maker.Context, Key("approve-maker"),
            new ApproveExpensePaymentRequest(draft.Resource!.Id, draft.Resource.Version), CancellationToken.None);

        Assert.Equal(CommandOutcome.ValidationFailed, sameActor.Outcome);
        Assert.Equal("payment_approval_invalid", Assert.Single(sameActor.Issues).Code);
        await using var verify = run.Context();
        Assert.Equal(ExpensePaymentStatuses.Draft, (await verify.ExpensePayments.SingleAsync()).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Draft_without_a_description_is_refused_and_writes_nothing(string description)
    {
        await using var run = await Run.StartAsync();
        await using (var maker = run.Scope("maker", new AllowAll(), new UnavailableTreasuryBalanceGuard()))
        {
            var refused = await maker.Service.DraftSaveAsync(maker.Context, Key("draft-1"),
                Draft(description: description), CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
            Assert.Equal("expense_payment_description_required", Assert.Single(refused.Issues).Code);
        }

        await using var verify = run.Context();
        Assert.Equal(0, await verify.ExpensePayments.CountAsync());
        Assert.Equal(0, await verify.ExpensePaymentCommands.CountAsync());
    }

    [Fact]
    public async Task Draft_naming_an_unknown_expense_item_is_refused()
    {
        await using var run = await Run.StartAsync();
        await using (var maker = run.Scope("maker", new AllowAll(), new UnavailableTreasuryBalanceGuard()))
        {
            var refused = await maker.Service.DraftSaveAsync(maker.Context, Key("draft-1"),
                Draft(expenseItemId: Guid.Parse("88888888-8888-4888-8888-888888888888")), CancellationToken.None);
            Assert.Equal("expense_item_unknown", Assert.Single(refused.Issues).Code);
        }

        await using var verify = run.Context();
        Assert.Equal(0, await verify.ExpensePayments.CountAsync());
    }

    [Fact]
    public async Task Same_key_with_another_payload_is_an_idempotency_conflict()
    {
        await using var run = await Run.StartAsync();
        await using var maker = run.Scope("maker", new AllowAll(), new UnavailableTreasuryBalanceGuard());
        var first = await maker.Service.DraftSaveAsync(maker.Context, Key("draft-1"), Draft(), CancellationToken.None);
        var second = await maker.Service.DraftSaveAsync(maker.Context, Key("draft-1"),
            Draft(description: "SENTETIK baska aciklama"), CancellationToken.None);

        Assert.Equal((CommandOutcome.Succeeded, CommandOutcome.IdempotencyConflict), (first.Outcome, second.Outcome));
        await using var verify = run.Context();
        Assert.Equal(1, await verify.ExpensePayments.CountAsync());
    }

    [Fact]
    public async Task Stale_version_is_a_recorded_conflict_that_the_same_key_replays()
    {
        await using var run = await Run.StartAsync();
        ExpensePaymentCommandResult draft;
        await using (var maker = run.Scope("maker", new AllowAll(), new UnavailableTreasuryBalanceGuard()))
        {
            draft = await maker.Service.DraftSaveAsync(maker.Context, Key("draft-1"), Draft(), CancellationToken.None);
        }

        var stale = new ApproveExpensePaymentRequest(draft.Resource!.Id, "7");
        await using (var checker = run.Scope("checker", new AllowAll(), new UnavailableTreasuryBalanceGuard()))
        {
            var conflict = await checker.Service.ApproveAsync(checker.Context, Key("approve-1"), stale,
                CancellationToken.None);
            Assert.Equal(CommandOutcome.VersionConflict, conflict.Outcome);
        }

        await using (var checker = run.Scope("checker", new AllowAll(), new UnavailableTreasuryBalanceGuard()))
        {
            var replay = await checker.Service.ApproveAsync(checker.Context, Key("approve-1"), stale,
                CancellationToken.None);
            Assert.Equal((CommandOutcome.VersionConflict, true), (replay.Outcome, replay.IsReplay));
        }

        await using var verify = run.Context();
        Assert.Equal(ExpensePaymentStatuses.Draft, (await verify.ExpensePayments.SingleAsync()).Status);
    }

    [Fact]
    public async Task Denied_permission_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        await using (var maker = run.Scope("maker", new FixedPermission(PermissionDecision.Denied),
                         new UnavailableTreasuryBalanceGuard()))
        {
            var denied = await maker.Service.DraftSaveAsync(maker.Context, Key("draft-1"), Draft(),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Denied, denied.Outcome);
        }

        await using var verify = run.Context();
        Assert.Equal(0, await verify.ExpensePayments.CountAsync());
        Assert.Equal(0, await verify.ExpensePaymentCommands.CountAsync());
    }

    [Fact]
    public async Task Catalog_offers_the_single_item_by_identity()
    {
        await using var run = await Run.StartAsync();
        await using var reader = run.Scope("reader", new AllowAll(), new UnavailableTreasuryBalanceGuard());
        var catalog = await reader.Service.ExpenseItemsAuthorizedAsync(reader.Context, CancellationToken.None);

        Assert.Equal(ReadOutcome.Found, catalog.Outcome);
        var item = Assert.Single(catalog.Resource!.Items);
        Assert.Equal((SingleExpenseItemCatalog.GeneralExpenseId, SingleExpenseItemCatalog.GeneralExpenseCode),
            (item.Id, item.Code));
    }

    private static async Task<ExpensePaymentView> DraftAndApproveAsync(Run run)
    {
        ExpensePaymentCommandResult draft;
        await using (var maker = run.Scope("maker", new AllowAll(), new UnavailableTreasuryBalanceGuard()))
        {
            draft = await maker.Service.DraftSaveAsync(maker.Context, Key("draft-1"), Draft(), CancellationToken.None);
        }

        Assert.Equal((CommandOutcome.Succeeded, ExpensePaymentStatus.Draft), (draft.Outcome, draft.Resource!.Status));
        ExpensePaymentCommandResult approved;
        await using (var checker = run.Scope("checker", new AllowAll(), new UnavailableTreasuryBalanceGuard()))
        {
            approved = await checker.Service.ApproveAsync(checker.Context, Key("approve-1"),
                new ApproveExpensePaymentRequest(draft.Resource.Id, draft.Resource.Version), CancellationToken.None);
        }

        Assert.Equal((CommandOutcome.Succeeded, ExpensePaymentStatus.Approved),
            (approved.Outcome, approved.Resource!.Status));
        return approved.Resource;
    }

    private static CommandMetadata Key(string value) => new(value);

    private static ExpensePaymentDraftSaveRequest Draft(string description = "SENTETIK kirtasiye",
        Guid? expenseItemId = null) => new(null, null,
        new ExpensePaymentPreparation(ExpensePaymentSourceKind.Bank, Bank,
            expenseItemId ?? SingleExpenseItemCatalog.GeneralExpenseId, new MoneyText("1250.50", Currency),
            new ProfileReference(Profile.ToString("D"), "1"), OccurredOn, null, null, description));

    private sealed class Run(string databaseName) : IAsyncDisposable
    {
        public static async Task<Run> StartAsync()
        {
            var run = new Run($"finance-fns-d25-{Guid.NewGuid():N}");
            await using var context = run.Context();
            context.BankAccounts.Add(new BankAccountRow
            {
                Id = Bank,
                CompanyId = Company,
                CurrencyId = Currency,
                DisplayName = "SENTETIK banka",
                BankName = "SENTETIK",
                AccountType = "Vadesiz",
                Iban = "TR330006100519786457841326",
                MoneyMovementEligible = true,
                Status = 2,
                Version = 2,
                ProposedBy = "bank-maker",
                ProposedAtUtc = DateTimeOffset.UtcNow,
                ApprovedBy = "bank-checker",
                ApprovedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            });
            await context.SaveChangesAsync();
            return run;
        }

        public FinanceDbContext Context()
        {
            var company = new CompanyContext();
            company.Set(Company);
            return new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(databaseName).Options, company);
        }

        public Scope Scope(string actor, IFinancePermissionPort permission, IFinanceTreasuryBalanceGuard balance)
        {
            var company = new CompanyContext();
            company.Set(Company);
            var collector = new AuditEventCollector();
            var context = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(databaseName)
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options, company);
            var service = new ExpensePaymentService(context, company, new User(actor), permission,
                new SingleExpenseItemCatalog(), balance, new SettingsPort(), new OpenPeriod(), collector,
                new ExpensePaymentBinding(FinanceMoneyOperations.ManualBankPaymentV1),
                new ExpensePaymentAuditEnvelopes(
                    Envelope(FinancePermissionNames.ExpensePaymentDraftSave),
                    Envelope(FinancePermissionNames.ExpensePaymentApprove),
                    Envelope(FinancePermissionNames.ExpensePaymentRecord)),
                TimeProvider.System);
            return new Scope(context, service,
                new FinanceContext(Company, actor, $"fns-d25-{Guid.NewGuid():N}", null));
        }

        public async ValueTask DisposeAsync()
        {
            await using var context = Context();
            await context.Database.EnsureDeletedAsync();
        }

        private static FinanceAuditEnvelope Envelope(string permission) =>
            new(permission, ["SENTETIK-FINANCE"], "Web");
    }

    private sealed class Scope(FinanceDbContext db, ExpensePaymentService service, FinanceContext context)
        : IAsyncDisposable
    {
        public IExpensePaymentService Service => service;
        public FinanceContext Context => context;
        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    private sealed class AllowAll : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }

    private sealed class FixedPermission(PermissionDecision decision) : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(decision);
    }

    private sealed class FixedBalance(TreasuryBalanceOutcome outcome) : IFinanceTreasuryBalanceGuard
    {
        public Task<TreasuryBalanceAnswer> CheckOutgoingAsync(FinanceContext context,
            ExpensePaymentSourceKind sourceKind, Guid treasuryAccountId, MoneyText money,
            CancellationToken cancellationToken) => Task.FromResult(new TreasuryBalanceAnswer(outcome,
            outcome switch
            {
                TreasuryBalanceOutcome.Insufficient => TreasuryBalanceAnswer.InsufficientCode,
                TreasuryBalanceOutcome.Unavailable => TreasuryBalanceAnswer.UnavailableCode,
                _ => null,
            }));
    }

    private sealed class SettingsPort : ISettingsPrerequisiteProvider
    {
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => Task.FromResult(new MoneyProfileV1(
            Company, Currency, "TRY", FinanceMoneyOperations.ManualBankPaymentV1,
            new ProfileVersionV1(Profile, 1), 2, true, new DateOnly(2026, 1, 1), null));

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn,
            DateOnly? valueOn, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
