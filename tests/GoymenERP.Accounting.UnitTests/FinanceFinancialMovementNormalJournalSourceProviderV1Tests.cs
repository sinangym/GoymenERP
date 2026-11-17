using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;
using GoymenERP.Modules.Accounting.Infrastructure;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Accounting.UnitTests;

public sealed class FinanceFinancialMovementNormalJournalSourceProviderV1Tests
{
    private static readonly Guid Company = Guid.Parse("b2800000-0000-4000-8000-000000000001");
    private static readonly Guid Source = Guid.Parse("b2800000-0000-4000-8000-000000000002");
    private static readonly Guid Currency = Guid.Parse("b2800000-0000-4000-8000-000000000003");
    private static readonly Guid Actor = Guid.Parse("b2800000-0000-4000-8000-000000000004");
    private static readonly DateTimeOffset At = new(2026, 11, 17, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 11, 17);
    private static readonly ProfileReference Profile = new("SENTETIK-owner-profile", "7");

    [Theory]
    [InlineData(NormalJournalSourceKindsV1.RecordedExpensePayment, FinanceAction.ReadExpensePayments)]
    [InlineData(NormalJournalSourceKindsV1.RecordedCashTransfer, FinanceAction.ReadCashTransfers)]
    public async Task Exact_approved_kind_routes_only_its_real_recorded_owner_and_rechecks_its_read_authority(string kind, FinanceAction action)
    {
        var fixture = new Fixture(kind);
        var answer = await fixture.Provider.ResolveAsync(Company, Source);
        Assert.NotNull(answer); Assert.Same(fixture.Rule, answer.Rule); Assert.Equal(Currency, answer.CurrencyId);
        Assert.Equal("3", answer.SourceVersion); Assert.Equal(Profile.ProfileId, answer.OwnerProfileId);
        Assert.Equal("10.25", answer.ExactSourceAmount);
        Assert.Equal([action], fixture.Permission.Actions);
        Assert.Equal(kind == NormalJournalSourceKindsV1.RecordedExpensePayment ? 1 : 0, fixture.Expense.Calls);
        Assert.Equal(kind == NormalJournalSourceKindsV1.RecordedCashTransfer ? 1 : 0, fixture.Transfer.Calls);
    }

    [Fact]
    public async Task Missing_or_ambiguous_kind_reads_no_financial_owner_and_missing_binding_has_no_default_rule()
    {
        var missing = new Fixture(null); Assert.Null(await missing.Provider.ResolveAsync(Company, Source));
        Assert.Equal(0, missing.Expense.Calls + missing.Transfer.Calls);
        var unmatched = new Fixture(NormalJournalSourceKindsV1.RecordedExpensePayment, bindingVersion: "4");
        Assert.Null(await unmatched.Provider.ResolveAsync(Company, Source));
    }

    [Fact]
    public async Task Draft_unconfirmed_self_approved_foreign_or_unavailable_expense_is_not_a_journal_source()
    {
        foreach (var value in new[] { Expense() with { Status = ExpensePaymentStatus.Draft }, Expense() with { PaymentConfirmed = false },
            Expense() with { ApprovedBy = Expense().PreparedBy }, Expense() with { CompanyId = Guid.NewGuid() } })
        {
            var fixture = new Fixture(NormalJournalSourceKindsV1.RecordedExpensePayment); fixture.Expense.Value = value;
            Assert.Null(await fixture.Provider.ResolveAsync(Company, Source));
        }
        var unavailable = new Fixture(NormalJournalSourceKindsV1.RecordedExpensePayment); unavailable.Expense.Outcome = ReadOutcome.Unavailable;
        Assert.Null(await unavailable.Provider.ResolveAsync(Company, Source));
    }

    [Fact]
    public async Task Exchange_or_mismatched_transfer_legs_do_not_invent_exchange_difference_or_a_second_source()
    {
        foreach (var preparation in new[] { Transfer().Preparation with { Kind = CashTransferKind.CurrencyExchange, Rate = "2" },
            Transfer().Preparation with { TargetMoney = new("11.25", Currency) }, Transfer().Preparation with { TargetProfile = new("", "") } })
        {
            var fixture = new Fixture(NormalJournalSourceKindsV1.RecordedCashTransfer); fixture.Transfer.Value = Transfer() with { Preparation = preparation };
            Assert.Null(await fixture.Provider.ResolveAsync(Company, Source));
        }
    }

    [Fact]
    public async Task Late_actor_company_or_permission_change_releases_no_account_rule()
    {
        var actor = new Fixture(NormalJournalSourceKindsV1.RecordedExpensePayment); actor.Expense.DuringRead = () => actor.User.UserId = Guid.NewGuid().ToString("D");
        Assert.Null(await actor.Provider.ResolveAsync(Company, Source));
        var company = new Fixture(NormalJournalSourceKindsV1.RecordedCashTransfer); company.Transfer.DuringRead = () => company.Company.Set(Guid.NewGuid());
        Assert.Null(await company.Provider.ResolveAsync(Company, Source));
        var denied = new Fixture(NormalJournalSourceKindsV1.RecordedExpensePayment); denied.Permission.Decision = PermissionDecision.Denied;
        Assert.Null(await denied.Provider.ResolveAsync(Company, Source));
    }

    private static ExpensePaymentView Expense() => new(Source, Company, ExpensePaymentStatus.Recorded,
        new(ExpensePaymentSourceKind.Bank, Guid.NewGuid(), Guid.NewGuid(), new("10.25", Currency), Profile, Day, null, null, "SENTETIK kaynak açıklaması"),
        "SENTETIK-maker", At, "SENTETIK-checker", At, Actor.ToString("D"), At, true, "3");
    private static CashTransferView Transfer() => new(Source, Company,
        new(CashTransferKind.SameCurrency, CashTransferAccountKind.Bank, Guid.NewGuid(), new("10.25", Currency), Profile,
            CashTransferAccountKind.Cash, Guid.NewGuid(), new("10.25", Currency), new("SENTETIK-target-profile", "5"), null, Day, null, null, null),
        Actor.ToString("D"), At, true, "3");

    private sealed class Fixture
    {
        public MutableCompanyContext Company { get; } = new();
        public CurrentUser User { get; } = new();
        public ExpenseOwner Expense { get; } = new();
        public TransferOwner Transfer { get; } = new();
        public Permission Permission { get; } = new();
        public NormalJournalPostingRuleV1 Rule { get; }
        public FinanceFinancialMovementNormalJournalSourceProviderV1 Provider { get; }
        public Fixture(string? kind, string bindingVersion = "3")
        {
            Company.Set(FinanceFinancialMovementNormalJournalSourceProviderV1Tests.Company);
            var debit = new AccountRef(FinanceFinancialMovementNormalJournalSourceProviderV1Tests.Company, Guid.NewGuid());
            var credit = new AccountRef(FinanceFinancialMovementNormalJournalSourceProviderV1Tests.Company, Guid.NewGuid());
            var mirror = new AccountPlanMirrorV1(debit.CompanyId, "SENTETIK", new string('a', 64), Actor, At,
                [new(debit, "SENTETIK-1", "SENTETIK debit"), new(credit, "SENTETIK-2", "SENTETIK credit")]);
            Rule = new(mirror, "SENTETIK-purpose", "1", Actor, At, [new("SENTETIK-debit", PostingSide.Debit, debit), new("SENTETIK-credit", PostingSide.Credit, credit)]);
            var bindings = new ApprovedNormalJournalSourceBindingCatalogV1(kind is null ? [] : [new(debit.CompanyId, Source, kind, bindingVersion, Profile.ProfileId, Profile.Version, Currency, Rule)]);
            var correlation = new CorrelationContext(); correlation.Set("SENTETIK", null, "Test", null, null);
            var receipt = new FinanceManualReceiptNormalJournalSourceProviderV1(new UnusedReceipt(), Permission, bindings, Company, User, correlation);
            Provider = new(new KindCatalog(kind), receipt, Expense, Transfer, bindings, Permission, Company, User, correlation);
        }
    }
    private sealed class MutableCompanyContext : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; }
        public void Set(Guid value) => CompanyId = value;
    }
    private sealed class KindCatalog(string? value) : IApprovedNormalJournalSourceKindCatalogV1
    { public Task<string?> ResolveKindAsync(Guid companyId, Guid sourceId, CancellationToken cancellationToken = default) => Task.FromResult(value); }
    private sealed class CurrentUser : ICurrentUser
    {
        public bool IsAuthenticated => true; public string? UserId { get; set; } = Actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }
    private sealed class Permission : IFinancePermissionPort
    {
        public PermissionDecision Decision { get; set; } = PermissionDecision.Allowed;
        public List<FinanceAction> Actions { get; } = [];
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId, CancellationToken cancellationToken)
        { Actions.Add(action); return Task.FromResult(Decision); }
    }
    private sealed class ExpenseOwner : IExpensePaymentService
    {
        public ExpensePaymentView Value { get; set; } = Expense(); public ReadOutcome Outcome { get; set; } = ReadOutcome.Found;
        public Action? DuringRead { get; set; } public int Calls { get; private set; }
        public Task<ReadResult<ExpensePaymentView>> DetailAuthorizedAsync(FinanceContext context, Guid paymentId, CancellationToken cancellationToken)
        { Calls++; DuringRead?.Invoke(); return Task.FromResult(new ReadResult<ExpensePaymentView>(Outcome, Outcome == ReadOutcome.Found ? Value : null)); }
        public Task<ExpensePaymentCommandResult> DraftSaveAsync(FinanceContext c, CommandMetadata m, ExpensePaymentDraftSaveRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<ExpensePaymentCommandResult> ApproveAsync(FinanceContext c, CommandMetadata m, ApproveExpensePaymentRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<ExpensePaymentCommandResult> RecordAsync(FinanceContext c, CommandMetadata m, RecordExpensePaymentRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<ExpensePaymentPage>> ListAuthorizedAsync(FinanceContext c, string? cursor, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<ExpenseItemCatalogView>> ExpenseItemsAuthorizedAsync(FinanceContext c, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<ExpensePaymentDraftContextView>> DraftContextAuthorizedAsync(FinanceContext c, ExpensePaymentSourceKind k, Guid id, DateOnly d, CancellationToken t) => throw new NotSupportedException();
    }
    private sealed class TransferOwner : ICashTransferService
    {
        public CashTransferView Value { get; set; } = Transfer(); public Action? DuringRead { get; set; } public int Calls { get; private set; }
        public Task<ReadResult<CashTransferView>> DetailAuthorizedAsync(FinanceContext c, Guid id, CancellationToken t)
        { Calls++; DuringRead?.Invoke(); return Task.FromResult(new ReadResult<CashTransferView>(ReadOutcome.Found, Value)); }
        public Task<CashTransferPreviewResult> PreviewAsync(FinanceContext c, CashTransferPreparation p, CancellationToken t) => throw new NotSupportedException();
        public Task<CashTransferCommandResult> CommitAsync(FinanceContext c, CommandMetadata m, CommitCashTransferRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<CashTransferPage>> ListAuthorizedAsync(FinanceContext c, string? cursor, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<CashTransferAccountContextView>> AccountContextAuthorizedAsync(FinanceContext c, CashTransferAccountKind k, Guid id, DateOnly d, CancellationToken t) => throw new NotSupportedException();
    }
    private sealed class UnusedReceipt : IManualReceiptService
    {
        public Task<ReadResult<SourceDetail>> DetailAuthorizedAsync(FinanceContext c, SourceDetailRequest r, CancellationToken t) => throw new InvalidOperationException("Only the approved owner may be queried.");
        public Task<CommandResult> DraftSaveAsync(FinanceContext c, CommandMetadata m, DraftSaveRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<CommandResult> RecordAsync(FinanceContext c, CommandMetadata m, RecordRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<CommandResult> ReverseAsync(FinanceContext c, CommandMetadata m, ReverseRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<SourcePage>> ListAuthorizedAsync(FinanceContext c, SourceListRequest r, CancellationToken t) => throw new NotSupportedException();
    }
}
