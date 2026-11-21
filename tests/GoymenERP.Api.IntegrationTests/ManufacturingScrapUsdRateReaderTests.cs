using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ManufacturingScrapUsdRateReaderTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("missing")]
    [InlineData("wrong-day")]
    [InlineData("wrong-currency")]
    [InlineData("actor")]
    public async Task The_usd_display_rate_preserves_the_exact_owner_record_and_refuses_missing_or_drifted_evidence(string fault)
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid()); var user = new User(company.CompanyId);
        var day = new DateOnly(2026, 11, 16); var id = Guid.NewGuid();
        var answer = new ExchangeRateResolutionV1(day, ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD"), SettingsCurrencyIdentitiesV1.TurkishLira,
            "USD", "Resolved", "Tcmb", false, null, day, "40.1234567890", "Official", id, "5", null, null);
        if (fault == "missing") answer = answer with { State = "Unavailable", SellingRate = null, RecordId = null, RecordVersion = null };
        if (fault == "wrong-day") answer = answer with { RateDay = day.AddDays(1) };
        if (fault == "wrong-currency") answer = answer with { SourceCurrencyId = Guid.NewGuid() };
        var owner = new Owner(answer, fault == "actor" ? () => user.Actor = "synthetic-other-actor" : null);
        var result = await new ManufacturingScrapUsdRateReader(company, user, owner).ReadAsync(company.CompanyId, day, CancellationToken.None);
        if (fault != "valid") Assert.Null(result);
        else Assert.Equal((day, 40.1234567890m, id, "5"), (result!.WeighingDay, result.TryPerUsd, result.RecordId, result.RecordVersion));
    }
    private sealed class Owner(ExchangeRateResolutionV1 result, Action? before) : ISettingsExchangeRateResolverV1
    {
        public Task<ExchangeRateResolutionV1> ResolveAsync(DateOnly day, Guid source, Guid target, CancellationToken cancellationToken = default)
        { before?.Invoke(); return Task.FromResult(result); }
    }
    private sealed class User(Guid company) : ICurrentUser
    {
        public string Actor { get; set; } = "synthetic-usd-reader";
        public bool IsAuthenticated => true; public string UserId => Actor; public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid value) => value == company;
    }
}
