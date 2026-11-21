using GoymenERP.Api.FinanceRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Owner-port composition contract sources, not SQL, live TCMB or granted-user acceptance.</summary>
public sealed class FinanceCashExchangeSourceTests
{
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly Guid Try = SettingsCurrencyIdentitiesV1.TurkishLira;
    private static readonly Guid Usd = SettingsCurrencyIdentitiesV1.UnitedStatesDollar;
    private static readonly DateOnly Day = new(2026, 11, 17);

    [Fact]
    public async Task An_unauthorized_exchange_reads_neither_profile_nor_comparison_rate()
    {
        var run = new SourceRun { Granted = false };
        var answer = await run.Reader().CheckAsync(run.Context, new("100", Usd), new("3300", Try), null, Day, default);
        Assert.Equal(CurrencyExchangeRateOutcome.Denied, answer.Outcome);
        Assert.Equal((0, 0), (run.ProfileReads, run.RateReads));
    }

    [Fact]
    public async Task Actual_owner_quotes_keep_the_two_amounts_and_TCMB_source_versions_separate_from_profit()
    {
        var run = new SourceRun();
        var answer = await run.Reader().CheckAsync(run.Context, new("100", Usd), new("3300", Try), null, Day, default);
        Assert.Equal(CurrencyExchangeRateOutcome.Consistent, answer.Outcome);
        var snapshot = Assert.IsType<CashTransferExchangeSnapshotV1>(answer.Snapshot);
        Assert.Equal(("33.00", "34.00", "-1.00"), (snapshot.TransactionRate, snapshot.ComparisonRate, snapshot.RateDeviation));
        Assert.Equal(run.RateId, snapshot.SourceComparison.RecordId);
        Assert.Equal("7", snapshot.SourceComparison.RecordVersion);
        Assert.Equal((2, 1), (run.ProfileReads, run.RateReads));
        Assert.All(run.Operations, operation => Assert.Equal(FinanceMoneyOperations.CashTransferV1, operation));
    }

    [Fact]
    public async Task Losing_authority_during_the_profile_read_prevents_the_next_owner_read()
    {
        var run = new SourceRun { RevokeOnProfile = true };
        var answer = await run.Reader().CheckAsync(run.Context, new("100", Usd), new("3300", Try), null, Day, default);
        Assert.Equal(CurrencyExchangeRateOutcome.Denied, answer.Outcome);
        Assert.Equal((1, 0), (run.ProfileReads, run.RateReads));
    }

    [Fact]
    public async Task An_owner_read_failure_does_not_mask_authority_revoked_during_that_read()
    {
        var run = new SourceRun { RevokeOnProfile = true, FailOnProfile = true };
        var answer = await run.Reader().CheckAsync(run.Context, new("100", Usd), new("3300", Try), null, Day, default);
        Assert.Equal(CurrencyExchangeRateOutcome.Denied, answer.Outcome);
        Assert.Null(answer.Snapshot);
        Assert.Equal((1, 0), (run.ProfileReads, run.RateReads));
    }

    [Fact]
    public async Task An_unavailable_or_same_day_comparison_is_not_substituted_with_a_rate_or_zero()
    {
        foreach (var state in new[] { "Unavailable", ExchangeRateResolutionStatesV1.Resolved })
        {
            var run = new SourceRun { State = state, BulletinDate = Day };
            var answer = await run.Reader().CheckAsync(run.Context, new("100", Usd), new("3300", Try), null, Day, default);
            Assert.Equal(CurrencyExchangeRateOutcome.Unavailable, answer.Outcome);
            Assert.Null(answer.Snapshot);
        }
    }

    private sealed class SourceRun : ICurrentUser, IFinancePermissionPort, ISettingsPrerequisiteProvider, ISettingsExchangeRateResolverV1
    {
        private readonly CompanyContext company = new();
        public SourceRun() => company.Set(Company);
        public bool Granted { get; set; } = true;
        public bool RevokeOnProfile { get; set; }
        public bool FailOnProfile { get; set; }
        public int ProfileReads { get; private set; }
        public int RateReads { get; private set; }
        public List<string> Operations { get; } = [];
        public Guid RateId { get; } = Guid.NewGuid();
        public string State { get; set; } = ExchangeRateResolutionStatesV1.Resolved;
        public DateOnly BulletinDate { get; set; } = Day.AddDays(-1);
        public bool IsAuthenticated => true;
        public string? UserId => "SENTETIK-exchange-actor";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid id) => id == Company;
        public FinanceContext Context => new(Company, UserId!, "SENTETIK-exchange", null);
        public SettingsFinanceCashExchangeRateRuleV1 Reader() => new(this, this, company, this, this);
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId, CancellationToken ct)
            => Task.FromResult(Granted ? PermissionDecision.Allowed : PermissionDecision.Denied);
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn, CancellationToken ct = default)
        {
            ProfileReads++; Operations.Add(operation); if (RevokeOnProfile) Granted = false;
            if (FailOnProfile) throw new SettingsPrerequisiteException("settings_provider_unavailable");
            return Task.FromResult(new MoneyProfileV1(Company, currencyId, currencyId == Try ? "TRY" : "USD", operation,
                new(Guid.Parse("66666666-6666-4666-8666-666666666666"), 1), 2, true, Day.AddDays(-1), null));
        }
        public Task<ExchangeRateResolutionV1> ResolveAsync(DateOnly rateDay, Guid sourceCurrencyId, Guid targetCurrencyId, CancellationToken ct = default)
        {
            RateReads++;
            return Task.FromResult(new ExchangeRateResolutionV1(rateDay, sourceCurrencyId, targetCurrencyId, "USD", State,
                ExchangeRateSourcesV1.Tcmb, false, BulletinDate, BulletinDate, "34", "SENTETIK / NOT_ENV", RateId, "7", null, null));
        }
        public Task<UnitProfileV1> GetUnitProfileAsync(Guid id, DateOnly on, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string op, DateOnly on, DateOnly? value, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
