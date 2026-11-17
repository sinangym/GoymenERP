using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Finance.UnitTests;

public sealed class SettingsFinanceCollateralTypeCatalogReaderV1Tests
{
    private static readonly Guid Company = Guid.Parse("f4710000-0000-4000-8000-000000000051");
    private static readonly Guid Type = Guid.Parse("f4710000-0000-4000-8000-000000000052");
    private static readonly DateTimeOffset At = new(2026, 11, 17, 22, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly BusinessDay = new(2026, 11, 18);

    [Fact]
    public async Task Current_company_catalog_preserves_inactive_type_rate_and_version_without_a_default()
    {
        var owner = new Owner(new(Company, [Entry() with { Active = false, RatePercent = "45.125", Version = "3" }]));
        var reader = Reader(owner, out _, out _);
        var answer = await reader.ListAsync(Company, BusinessDay, default);
        Assert.Equal(FinanceCollateralTypeCatalogState.Available, answer.State);
        var item = Assert.Single(answer.Items);
        Assert.Equal((Type, "45.125", "3", false, false),
            (item.CollateralTypeId, item.RatePercent, item.Version, item.IsActive, item.IsNegotiableInstrument));
        owner.Answer = new(Company, []);
        Assert.Empty((await reader.ListAsync(Company, BusinessDay, default)).Items);
        Assert.Equal(2, owner.Calls);
    }

    [Fact]
    public async Task Historical_or_foreign_reads_never_call_current_owner_and_late_authority_change_discards_answer()
    {
        var owner = new Owner(new(Company, [Entry()]));
        var reader = Reader(owner, out var company, out var user);
        Assert.Equal(FinanceCollateralTypeCatalogState.Unavailable, (await reader.ListAsync(Company, BusinessDay.AddDays(-1), default)).State);
        Assert.Equal(FinanceCollateralTypeCatalogState.Unavailable, (await reader.ListAsync(Guid.NewGuid(), BusinessDay, default)).State);
        Assert.Equal(0, owner.Calls);
        owner.AfterRead = () => user.Authenticated = false;
        Assert.Equal(FinanceCollateralTypeCatalogState.Unavailable, (await reader.ListAsync(Company, BusinessDay, default)).State);
        user.Authenticated = true; owner.AfterRead = () => company.Set(Guid.NewGuid());
        Assert.Equal(FinanceCollateralTypeCatalogState.Unavailable, (await reader.ListAsync(Company, BusinessDay, default)).State);
    }

    [Fact]
    public async Task Wrong_company_duplicate_type_unknown_kind_and_invalid_rates_fail_closed()
    {
        var owner = new Owner(new(Company, [])); var reader = Reader(owner, out _, out _);
        foreach (var answer in new[]
        {
            new SettingsCollateralTypesViewV1(Guid.NewGuid(), [Entry()]),
            new(Company, [Entry(), Entry()]),
            new(Company, [Entry() with { Kind = (SettingsCollateralKindV1)99 }]),
            new(Company, [Entry() with { RatePercent = "100.001" }]),
            new(Company, [Entry() with { RatePercent = "-1" }]),
            new(Company, [Entry() with { Version = "0" }]),
        })
        { owner.Answer = answer; Assert.Equal(FinanceCollateralTypeCatalogState.Unavailable, (await reader.ListAsync(Company, BusinessDay, default)).State); }
    }

    [Fact]
    public async Task Owner_failure_is_unavailable_and_cancellation_is_not_hidden()
    {
        var owner = new Owner(new(Company, [])) { Failure = new InvalidOperationException("SENTETIK") };
        var reader = Reader(owner, out _, out _);
        Assert.Equal(FinanceCollateralTypeCatalogState.Unavailable, (await reader.ListAsync(Company, BusinessDay, default)).State);
        owner.Failure = new OperationCanceledException();
        await Assert.ThrowsAsync<OperationCanceledException>(() => reader.ListAsync(Company, BusinessDay, default));
    }

    private static SettingsCollateralTypeViewV1 Entry() => new(Type, "SENTETIK", "Sentetik teminat",
        SettingsCollateralKindV1.GuaranteeLetter, true, "100", "1", "SENTETIK", At);
    private static SettingsFinanceCollateralTypeCatalogReaderV1 Reader(Owner owner, out MutableCompany company, out User user)
    { company = new(); company.Set(Company); user = new(); return new(company, user, owner, new Clock()); }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => At; }
    private sealed class MutableCompany : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; }
        public void Set(Guid companyId) => CompanyId = companyId;
    }
    private sealed class User : ICurrentUser
    {
        public bool Authenticated { get; set; } = true;
        public bool IsAuthenticated => Authenticated;
        public string? UserId => "SENTETIK";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }
    private sealed class Owner(SettingsCollateralTypesViewV1 answer) : ISettingsCollateralTypeCatalogV1
    {
        public SettingsCollateralTypesViewV1 Answer { get; set; } = answer;
        public Action? AfterRead { get; set; }
        public Exception? Failure { get; set; }
        public int Calls { get; private set; }
        public Task<SettingsCollateralTypesViewV1> ListAsync(Guid companyId, CancellationToken cancellationToken = default)
        { Calls++; if (Failure is { } failure) throw failure; AfterRead?.Invoke(); return Task.FromResult(Answer); }
    }
}
