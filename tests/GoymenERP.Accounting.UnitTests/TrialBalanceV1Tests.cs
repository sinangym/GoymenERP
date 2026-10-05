using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Accounting.UnitTests;

public sealed class TrialBalanceV1Tests
{
    private static readonly Guid Company = Guid.Parse("1f43de6c-c927-43fb-93a9-37648b9b8179");
    private static readonly Guid OtherCompany = Guid.Parse("26de37f5-2fbf-42c5-b9db-0051b64f93df");
    private static readonly Guid CurrencyA = Guid.Parse("3fe493f3-16cc-49da-82b0-55dc4d452bb7");
    private static readonly Guid CurrencyB = Guid.Parse("40db1e70-86b1-48aa-803e-c755457e92e1");
    private static readonly Guid AccountA = Guid.Parse("10e301c4-3eb3-4597-adbc-7a9ec75763f1");
    private static readonly Guid AccountB = Guid.Parse("20afed77-6691-4fd0-bb3e-e83ec40a5eef");
    private const string Actor = "c12f6dc0-8772-44b5-8794-3c57acec8e02";

    [Fact]
    public async Task Posted_effects_are_grouped_per_account_and_currency_without_cross_currency_netting()
    {
        var store = new FakeStore([
            new(AccountA, CurrencyA, 125m, 100m),
            new(AccountB, CurrencyA, 100m, 125m),
            new(AccountA, CurrencyB, 50.00m, 0m),
            new(AccountB, CurrencyB, 0m, 50.00m),
        ]);
        var service = Service(store, new SequencedAuthority(Granted(), Granted()));

        var result = await service.ReadAsync(Request());

        Assert.Equal(AccountingApiStatus.Ok, result.Status);
        var report = Assert.IsType<TrialBalanceReportV1>(result.Resource);
        Assert.Equal(4, report.Lines.Count);
        Assert.Equal(("125", "100", "Debit", "25"), Values(report.Lines[0]));
        Assert.Equal(("100", "125", "Credit", "25"), Values(report.Lines[1]));
        Assert.Equal(("50", "0", "Debit", "50"), Values(report.Lines[2]));
        Assert.Equal(("0", "50", "Credit", "50"), Values(report.Lines[3]));
        Assert.All(report.CurrencyTotals, total => Assert.True(total.IsBalanced));
        Assert.Equal([(CurrencyA, "225", "225"), (CurrencyB, "50", "50")],
            report.CurrencyTotals.Select(total => (total.CurrencyId, total.Debit, total.Credit)));
        Assert.Equal(TrialBalanceReadReceiptValuesV1.SourceId, report.Receipt.SourceId);
        Assert.Matches("^sha256:[0-9A-F]{64}$", report.Receipt.SourceVersion);
        Assert.Equal(Company, report.Receipt.CompanyId);
        Assert.Equal(Actor, report.Receipt.ActorId);
        Assert.NotEqual(default, report.Receipt.ServerAsOfUtc);
        Assert.Equal(TrialBalanceReadReceiptValuesV1.CurrencyAmount, report.Receipt.Unit);
        Assert.Equal(TrialBalanceReadReceiptValuesV1.Fact, report.Receipt.FactPlan);
        Assert.Equal(TrialBalanceReadReceiptValuesV1.Complete, report.Receipt.Completeness);
        Assert.Equal(new[] { CurrencyA, CurrencyB }, report.Receipt.CurrencyIds);
    }

    [Fact]
    public async Task Receipt_version_is_stable_for_the_same_scope_and_content_and_changes_with_either()
    {
        var rows = new TrialBalanceReadRowV1[]
        {
            new(AccountA, CurrencyA, 125m, 100m),
            new(AccountB, CurrencyA, 100m, 125m),
        };

        var first = Assert.IsType<TrialBalanceReportV1>((await Service(
            new FakeStore(rows), new SequencedAuthority(Granted(), Granted())).ReadAsync(Request())).Resource);
        var replay = Assert.IsType<TrialBalanceReportV1>((await Service(
            new FakeStore(rows), new SequencedAuthority(Granted(), Granted())).ReadAsync(Request())).Resource);
        var changedContent = Assert.IsType<TrialBalanceReportV1>((await Service(
            new FakeStore([new(AccountA, CurrencyA, 126m, 100m), rows[1]]),
            new SequencedAuthority(Granted(), Granted())).ReadAsync(Request())).Resource);
        var changedScope = Assert.IsType<TrialBalanceReportV1>((await Service(
            new FakeStore(rows), new SequencedAuthority(Granted(), Granted())).ReadAsync(
            new TrialBalanceRequestV1(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), CurrencyA))).Resource);

        Assert.Equal(first.Receipt.SourceVersion, replay.Receipt.SourceVersion);
        Assert.NotEqual(first.Receipt.SourceVersion, changedContent.Receipt.SourceVersion);
        Assert.NotEqual(first.Receipt.SourceVersion, changedScope.Receipt.SourceVersion);
    }

    [Fact]
    public async Task Exact_date_and_currency_filter_are_forwarded_to_the_store()
    {
        var store = new FakeStore([]);
        var service = Service(store, new SequencedAuthority(Granted(), Granted()));
        var request = new TrialBalanceRequestV1(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), CurrencyB);

        var result = await service.ReadAsync(request);

        Assert.Equal(AccountingApiStatus.Ok, result.Status);
        Assert.Equal((request.FromAccountingDate, request.ThroughAccountingDate, request.CurrencyId), store.LastRequest);
        Assert.Empty(result.Resource!.Lines);
        Assert.Empty(result.Resource.CurrencyTotals);
    }

    [Fact]
    public async Task Invalid_dates_and_empty_currency_refuse_before_authority_or_store()
    {
        foreach (var request in new TrialBalanceRequestV1?[]
                 {
                     null,
                     new(default, new DateOnly(2026, 9, 30)),
                     new(new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 30)),
                     new(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), Guid.Empty),
                 })
        {
            var store = new FakeStore([]);
            var authority = new SequencedAuthority(Granted());
            var result = await Service(store, authority).ReadAsync(request);
            Assert.Equal(AccountingApiStatus.Invalid, result.Status);
            Assert.Equal(0, authority.Calls);
            Assert.Equal(0, store.Calls);
        }
    }

    [Fact]
    public async Task Missing_company_wide_authority_refuses_before_the_read()
    {
        var store = new FakeStore([]);
        var result = await Service(store, new SequencedAuthority(PermissionScope.Denied)).ReadAsync(Request());

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Null(result.Resource);
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task Permission_revoked_during_the_read_discards_the_report()
    {
        var store = new FakeStore([new(AccountA, CurrencyA, 10m, 0m)]);
        var authority = new SequencedAuthority(Granted(), PermissionScope.Denied);

        var result = await Service(store, authority).ReadAsync(Request());

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Null(result.Resource);
        Assert.Equal(1, store.Calls);
        Assert.Equal(2, authority.Calls);
    }

    [Fact]
    public async Task Company_or_actor_change_during_the_read_discards_the_report()
    {
        var company = new MutableCompany(Company);
        var user = new MutableUser(Actor, Company);
        var store = new FakeStore([], () => company.Set(OtherCompany));
        var service = new AccountingTrialBalanceServiceV1(store,
            new SequencedAuthority(Granted(), Granted()), company, user);

        var result = await service.ReadAsync(Request());

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Null(result.Resource);
    }

    private static AccountingTrialBalanceServiceV1 Service(
        IAccountingTrialBalanceReadStore store,
        IErpAuthorityRevalidationService authority) =>
        new(store, authority, new MutableCompany(Company), new MutableUser(Actor, Company));

    private static TrialBalanceRequestV1 Request() =>
        new(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

    private static (string Debit, string Credit, string Side, string Balance) Values(TrialBalanceLineV1 line) =>
        (line.Debit, line.Credit, line.BalanceSide, line.Balance);

    private static PermissionScope Granted() => new(
        true,
        true,
        new HashSet<string>(["accounting-reader"], StringComparer.Ordinal),
        new HashSet<Guid>(),
        new HashSet<Guid>(),
        new HashSet<Guid>());

    private sealed class FakeStore(
        IReadOnlyList<TrialBalanceReadRowV1> rows,
        Action? afterRead = null) : IAccountingTrialBalanceReadStore
    {
        public int Calls { get; private set; }
        public (DateOnly From, DateOnly Through, Guid? Currency)? LastRequest { get; private set; }

        public Task<IReadOnlyList<TrialBalanceReadRowV1>> ReadAsync(
            DateOnly fromAccountingDate,
            DateOnly throughAccountingDate,
            Guid? currencyId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastRequest = (fromAccountingDate, throughAccountingDate, currencyId);
            afterRead?.Invoke();
            return Task.FromResult(rows);
        }
    }

    private sealed class SequencedAuthority(params PermissionScope[] scopes) : IErpAuthorityRevalidationService
    {
        public int Calls { get; private set; }

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(AccountingPermissionManifestV1.JournalsRead, permission);
            Assert.Equal(Company, companyId);
            cancellationToken.ThrowIfCancellationRequested();
            var answer = scopes[Math.Min(Calls, scopes.Length - 1)];
            Calls++;
            return Task.FromResult(answer);
        }
    }

    private sealed class MutableCompany(Guid companyId) : ICompanyContext
    {
        public bool IsResolved { get; private set; } = true;
        public Guid CompanyId { get; private set; } = companyId;

        public void Set(Guid value)
        {
            CompanyId = value;
            IsResolved = value != Guid.Empty;
        }
    }

    private sealed class MutableUser(string actorId, Guid companyId) : ICurrentUser
    {
        public bool IsAuthenticated { get; set; } = true;
        public string? UserId { get; set; } = actorId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { companyId };
        public bool CanAccessCompany(Guid value) => AllowedCompanyIds.Contains(value);
    }
}
