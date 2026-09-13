using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

public sealed class SalesReferenceReadHandlerTests
{
    [Fact]
    public async Task Missing_policy_denies_without_touching_owner_reads()
    {
        var f = new Fixture();
        var handler = f.Handler(new());
        Assert.Equal(403, (await handler.GetAccountsAsync(new(), "server-session")).StatusCode);
        Assert.Equal(403, (await handler.GetCurrencyAsync(f.CurrencyQuery)).StatusCode);
        Assert.Equal(403, (await handler.GetProductAsync(f.ProductQuery)).StatusCode);
        Assert.Equal(0, f.Accounts.Calls + f.Settings.Calls + f.Products.Calls);
    }

    [Fact]
    public async Task Anonymous_or_foreign_company_cannot_probe_references()
    {
        var f = new Fixture();
        f.User.IsAuthenticated = false;
        Assert.Equal(401, (await f.Handler().GetAccountsAsync(new(), "session")).StatusCode);
        f.User.IsAuthenticated = true;
        f.Context.Set(Guid.NewGuid());
        Assert.Equal(403, (await f.Handler().GetCurrencyAsync(f.CurrencyQuery)).StatusCode);
        Assert.Equal(0, f.Settings.Calls + f.Accounts.Calls);
    }

    [Fact]
    public async Task Accounts_use_real_read_port_active_pair_filter_and_roles_zero()
    {
        var f = new Fixture();
        var result = await f.Handler().GetAccountsAsync(new("search", 10), "server-session");
        Assert.Equal(200, result.StatusCode);
        var account = Assert.Single(result.Value!.Accounts);
        Assert.Equal(f.Company, result.Value.CompanyId);
        Assert.Equal("C001 - Example Party", account.Label);
        Assert.Equal("9007199254740993", account.AccountVersion);
        Assert.Equal(PartyRole.None, f.Accounts.Request!.Query.Roles);
        Assert.Equal(CounterpartyRecordState.Active, f.Accounts.Request.Query.PartyState);
        Assert.Equal(CounterpartyRecordState.Active, f.Accounts.Request.Query.AccountState);
        Assert.Equal("actor", f.Accounts.Request.ActorId);
        Assert.Equal("server-session", f.Accounts.Request.SessionBinding);
    }

    [Theory]
    [InlineData(CounterpartyPageResult.Forbidden, 403)]
    [InlineData(CounterpartyPageResult.InvalidCursor, 400)]
    [InlineData(CounterpartyPageResult.ServiceUnavailable, 503)]
    public async Task Owner_failure_never_becomes_empty_success(CounterpartyPageResult ownerStatus, int expected)
    {
        var f = new Fixture();
        f.Accounts.Result = new(ownerStatus, null);
        var result = await f.Handler().GetAccountsAsync(new(), "session");
        Assert.Equal(expected, result.StatusCode);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Authorized_empty_page_remains_distinct_from_unavailable()
    {
        var f = new Fixture();
        f.Accounts.Result = CounterpartyPageExecution.Succeeded(new([], null, false, null, null));
        var result = await f.Handler().GetAccountsAsync(new(), "session");
        Assert.Equal(200, result.StatusCode);
        Assert.Empty(result.Value!.Accounts);
    }

    [Fact]
    public async Task Late_company_change_discards_account_response()
    {
        var f = new Fixture();
        f.Accounts.OnRead = () => f.Context.Set(Guid.NewGuid());
        var result = await f.Handler().GetAccountsAsync(new(), "session");
        Assert.Equal(403, result.StatusCode);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Current_permission_is_rechecked_after_provider_read()
    {
        var f = new Fixture();
        f.Settings.OnRead = () => f.Auth.Allowed = false;
        var result = await f.Handler().GetCurrencyAsync(f.CurrencyQuery);
        Assert.Equal(403, result.StatusCode);
        Assert.Null(result.Value);
        Assert.Equal(2, f.Auth.Calls);
    }

    [Fact]
    public async Task Currency_resolves_owner_code_and_exact_profile_version_not_fake_name()
    {
        var f = new Fixture();
        var result = await f.Handler().GetCurrencyAsync(f.CurrencyQuery);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("XYZ", result.Value!.Label);
        Assert.Equal(result.Value.Code, result.Value.Label);
        Assert.Equal("9007199254740993", result.Value.ProfileVersion);
        Assert.Equal("controlled.operation", f.Settings.LastOperation);
    }

    [Theory]
    [InlineData("settings_access_denied", 403)]
    [InlineData("settings_provider_unavailable", 503)]
    public async Task Settings_denial_and_unavailable_are_distinct(string code, int status)
    {
        var f = new Fixture();
        f.Settings.Error = new SettingsPrerequisiteException(code);
        var result = await f.Handler().GetCurrencyAsync(f.CurrencyQuery);
        Assert.Equal(status, result.StatusCode);
        Assert.Equal(code, result.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Unbound_money_operation_cannot_call_provider_with_guess()
    {
        var f = new Fixture();
        var result = await f.Handler(Fixture.Bindings with { MoneyOperation = null }).GetCurrencyAsync(f.CurrencyQuery);
        Assert.Equal(503, result.StatusCode);
        Assert.Equal(0, f.Settings.Calls);
    }

    [Fact]
    public async Task Passive_expired_or_foreign_currency_cannot_be_offered()
    {
        var f = new Fixture();
        f.Settings.Money = f.Settings.Money with { IsActive = false };
        Assert.Equal(422, (await f.Handler().GetCurrencyAsync(f.CurrencyQuery)).StatusCode);
        f.Settings.Money = f.Settings.Money with { IsActive = true, EffectiveThrough = f.Day.AddDays(-1) };
        Assert.Equal(422, (await f.Handler().GetCurrencyAsync(f.CurrencyQuery)).StatusCode);
        f.Settings.Money = f.Settings.Money with { CompanyId = Guid.NewGuid() };
        Assert.Equal(403, (await f.Handler().GetCurrencyAsync(f.CurrencyQuery)).StatusCode);
    }

    [Fact]
    public async Task Missing_product_runtime_or_empty_precondition_errors_are_unavailable()
    {
        var f = new Fixture();
        var result = await f.Handler(productMissing: true).GetProductAsync(f.ProductQuery);
        Assert.Equal(503, result.StatusCode);
        f.Products.Result = new(null, []);
        result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(503, result.StatusCode);
        Assert.Null(result.Value);
        Assert.Equal(0, f.Settings.Calls);
    }

    [Fact]
    public async Task Owner_usable_snapshot_and_all_three_unit_bindings_are_revalidated()
    {
        var f = new Fixture();
        var result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(new ProductSalesSelectionV1(f.Product, "7", f.Unit, f.Day), f.Products.Selection);
        Assert.Equal("P001 - Example Product", result.Value!.Label);
        Assert.Equal("owner-unit", Assert.Single(result.Value.OrderUnits).Label);
        Assert.Equal(3, f.Settings.Calls);
        Assert.Equal(5, f.Auth.Calls);
    }

    [Fact]
    public async Task Stale_product_or_profile_version_has_no_positive_option()
    {
        var f = new Fixture();
        f.Products.Result = new(f.Products.Result.Snapshot! with { ProductVersion = "8" }, []);
        Assert.Equal(409, (await f.Handler().GetProductAsync(f.ProductQuery)).StatusCode);
        f.Products.Result = new(f.Products.Result.Snapshot! with { ProductVersion = "7" }, []);
        f.Settings.Unit = f.Settings.Unit with { Profile = new(f.Profile, 2) };
        var result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(503, result.StatusCode);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task No_global_product_permission_fallback_on_owner_denial()
    {
        var f = new Fixture();
        f.Products.Error = new ProductOperationException("private-detail", 403);
        var result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(403, result.StatusCode);
        Assert.Null(result.Code);
        Assert.Equal(0, f.Settings.Calls);
    }

    private sealed class Fixture
    {
        public static readonly SalesReferenceReadBindings Bindings = new(
            "controlled.accounts", "controlled.currency", "controlled.product", "controlled.operation");
        public Guid Company { get; } = Guid.NewGuid();
        public Guid Currency { get; } = Guid.NewGuid();
        public Guid Product { get; } = Guid.NewGuid();
        public Guid Unit { get; } = Guid.NewGuid();
        public Guid Profile { get; } = Guid.NewGuid();
        public DateOnly Day { get; } = new(2026, 9, 13);
        public Context Context { get; } = new();
        public User User { get; } = new();
        public Auth Auth { get; } = new();
        public Accounts Accounts { get; } = new();
        public Settings Settings { get; } = new();
        public Products Products { get; } = new();
        public SalesCurrencyQueryV1 CurrencyQuery => new(Currency, Day);
        public SalesProductQueryV1 ProductQuery => new(Product, "7", Unit, Day);
        public Fixture()
        {
            Context.Set(Company);
            User.Company = Company;
            var active = new CounterpartyListAccount(Guid.NewGuid(), Company, PartyRole.None, true, 9007199254740993);
            var foreign = active with { Id = Guid.NewGuid(), CompanyId = Guid.NewGuid() };
            var passive = active with { Id = Guid.NewGuid(), IsActive = false };
            var party = new CounterpartyListItem(Guid.NewGuid(), "C001", (PartyKind)1,
                "Example Party", null, true, 1, [active, foreign, passive]);
            Accounts.Result = CounterpartyPageExecution.Succeeded(new([party, party with { IsActive = false }], null, false, null, null));
            Settings.Money = new(Company, Currency, "XYZ", "controlled.operation",
                new(Profile, 9007199254740993), 2, true, Day.AddDays(-1), null);
            Settings.Unit = new(Company, Unit, "owner-unit", "owner-dimension", false,
                new(Profile, 1), 3, true, Day.AddDays(-1), null);
            var unit = new ProductSalesUnitProfileV1(Company, Unit, Profile, "1");
            Products.Result = new(new(Product, "7", "P001", "Example Product", unit, unit, unit, Day), []);
        }
        public SalesReferenceReadHandler Handler(SalesReferenceReadBindings? bindings = null, bool productMissing = false) =>
            new(Context, User, Auth, Accounts, Settings, productMissing ? null : Products, bindings ?? Bindings);
    }

    private sealed class Context : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; }
        public void Set(Guid companyId) => CompanyId = companyId;
    }
    private sealed class User : ICurrentUser
    {
        public Guid Company { get; set; }
        public bool IsAuthenticated { get; set; } = true;
        public string? UserId => "actor";
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }
    private sealed class Auth : IErpAuthorizationService
    {
        public bool Allowed { get; set; } = true;
        public int Calls { get; private set; }
        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(Allowed); }
        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId, IReadOnlyCollection<string> permissions,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Accounts : ICounterpartyReadService
    {
        public int Calls { get; private set; }
        public Action? OnRead { get; set; }
        public CounterpartyReadRequest? Request { get; private set; }
        public CounterpartyPageExecution Result { get; set; } = CounterpartyPageExecution.Forbidden();
        public Task<CounterpartyPageExecution> GetAuthorizedPageAsync(CounterpartyReadRequest request, CancellationToken cancellationToken = default)
        { Calls++; Request = request; OnRead?.Invoke(); return Task.FromResult(Result); }
        public Task<CounterpartyDetailView?> GetAuthorizedPartyAsync(Guid partyId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Settings : ISettingsPrerequisiteProvider
    {
        public int Calls { get; private set; }
        public Action? OnRead { get; set; }
        public Exception? Error { get; set; }
        public MoneyProfileV1 Money { get; set; } = null!;
        public UnitProfileV1 Unit { get; set; } = null!;
        public string? LastOperation { get; private set; }
        private void Read(CancellationToken token) { token.ThrowIfCancellationRequested(); Calls++; OnRead?.Invoke(); if (Error is not null) throw Error; }
        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default)
        { Read(cancellationToken); return Task.FromResult(Unit); }
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn, CancellationToken cancellationToken = default)
        { Read(cancellationToken); LastOperation = operation; return Task.FromResult(Money); }
        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Products : IProductSalesEligibilityV1
    {
        public int Calls { get; private set; }
        public ProductSalesSelectionV1? Selection { get; private set; }
        public ProductSalesEligibilityV1 Result { get; set; } = new(null, []);
        public Exception? Error { get; set; }
        public Task<ProductSalesEligibilityV1> EvaluateAsync(ProductSalesSelectionV1 selection, CancellationToken cancellationToken)
        { Calls++; Selection = selection; if (Error is not null) throw Error; return Task.FromResult(Result); }
    }
}
