using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

// Synthetic owner answers; real Sales persistence is covered by the separate SQL fact.
public sealed class ManufacturingSalesOrderInputRateReadPortTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Order = Guid.Parse("72222222-2222-4222-8222-222222222222");
    private static readonly Guid Line = Guid.Parse("73333333-3333-4333-8333-333333333333");
    private static readonly Guid Usd = ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD");
    private static readonly Guid Tl = SettingsCurrencyIdentitiesV1.TurkishLira;
    private static readonly DateOnly Day = new(2026, 11, 17);

    [Fact]
    public async Task Exact_approved_order_day_and_requested_currency_are_resolved_by_Settings_and_source_is_rechecked()
    {
        var owner = new Orders(); var rates = new Rates(); var user = new User();
        var answer = await Host(owner, rates, user).ReadAsync(Company, Order, Line, 0, Usd, Tl, default);
        Assert.Equal(ManufacturingInputOrderRateOutcomeV1.Available, answer.Outcome);
        Assert.Equal(34.5678912345m, answer.Rate);
        Assert.Equal((Day, Usd, Tl), rates.Asked);
        Assert.Equal(2, owner.Reads);
        Assert.Null(answer.RateRatio);
        Assert.Null(answer.Reason);
    }

    [Theory]
    [InlineData("foreign-order")]
    [InlineData("unapproved")]
    [InlineData("wrong-revision")]
    [InlineData("source-changed")]
    [InlineData("wrong-rate-day")]
    [InlineData("same-day-bulletin")]
    [InlineData("wrong-currency")]
    [InlineData("missing-version")]
    [InlineData("unresolved")]
    [InlineData("revoked-user")]
    [InlineData("changed-actor")]
    [InlineData("changed-company")]
    [InlineData("owner-failed")]
    [InlineData("overprecise-rate")]
    [InlineData("negative-rate")]
    public async Task Unreadable_or_changed_order_and_rate_proofs_never_supply_an_alternative_rate(string fault)
    {
        var owner = new Orders { Fault = fault }; var rates = new Rates { Fault = fault }; var user = new User();
        var context = new MutableCompany();
        if (fault == "revoked-user") rates.AfterRead = () => user.Active = false;
        if (fault == "changed-actor") rates.AfterRead = () => user.Actor = "SENTETIK-other";
        if (fault == "changed-company") rates.AfterRead = () => context.CompanyId = Guid.NewGuid();
        var answer = await new ManufacturingSalesOrderInputRateReadPortV1(context, user, owner, rates)
            .ReadAsync(Company, Order, Line, 0, Usd, Tl, default);
        Assert.Equal(ManufacturingInputOrderRateOutcomeV1.Unavailable, answer.Outcome);
        Assert.Null(answer.Rate); Assert.Null(answer.RateRatio);
        if (fault is "foreign-order" or "unapproved" or "wrong-revision" or "owner-failed") Assert.Equal(0, rates.Reads);
        if (fault is "revoked-user" or "changed-actor" or "changed-company") Assert.Equal(1, owner.Reads);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_disguised_as_an_unavailable_rate()
    {
        var owner = new Orders { Fault = "cancelled" };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Host(owner, new Rates(), new User())
            .ReadAsync(Company, Order, Line, 0, Usd, Tl, new CancellationToken(true)));
    }

    private static ManufacturingSalesOrderInputRateReadPortV1 Host(Orders orders, Rates rates, User user)
    {
        var context = new CompanyContext(); context.Set(Company);
        return new(context, user, orders, rates);
    }
    private sealed class User : ICurrentUser
    {
        public bool Active = true;
        public string Actor = "SENTETIK";
        public bool IsAuthenticated => Active;
        public string? UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => Active && companyId == Company;
    }
    private sealed class MutableCompany : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId { get; set; } = Company;
        public void Set(Guid companyId) => CompanyId = companyId;
    }
    private sealed class Orders : ISalesOrderCostRateSourceReadPortV1
    {
        public string? Fault; public int Reads;
        public Task<SalesOrderCostRateSourceV1> GetAsync(Guid companyId, Guid orderId, Guid lineId, int revision, CancellationToken ct = default)
        {
            Reads++; ct.ThrowIfCancellationRequested();
            if (Fault == "owner-failed") throw new InvalidOperationException("SENTETIK");
            return Task.FromResult(new SalesOrderCostRateSourceV1(Fault == "unapproved" ? SalesOrderCostRateSourceOutcomeV1.Unavailable : SalesOrderCostRateSourceOutcomeV1.Available,
                Fault == "foreign-order" ? Guid.NewGuid() : Company, Order, Line, Fault == "wrong-revision" ? 1 : 0,
                Fault == "source-changed" && Reads > 1 ? 5 : 4, Usd, Day, "TcmbForexSelling", "SENTETIK-order-v4"));
        }
    }
    private sealed class Rates : ISettingsExchangeRateResolverV1
    {
        public string? Fault; public Action? AfterRead; public int Reads;
        public (DateOnly Day, Guid Source, Guid Target) Asked;
        public Task<ExchangeRateResolutionV1> ResolveAsync(DateOnly day, Guid source, Guid target, CancellationToken ct = default)
        {
            Reads++; Asked = (day, source, target); AfterRead?.Invoke();
            return Task.FromResult(new ExchangeRateResolutionV1(Fault == "wrong-rate-day" ? Day.AddDays(-1) : Day,
                Fault == "wrong-currency" ? Guid.NewGuid() : Usd, Tl, "USD", Fault == "unresolved" ? "Unavailable" : "Resolved", "Tcmb",
                false, null, Fault == "same-day-bulletin" ? Day : Day.AddDays(-1),
                Fault == "overprecise-rate" ? "34.56789123450000000000000000001" : Fault == "negative-rate" ? "-1" : "34.5678912345", "Official", Order,
                Fault == "missing-version" ? null : "1", null, null));
        }
    }
}
