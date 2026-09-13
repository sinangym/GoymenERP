using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Finance.UnitTests;

public sealed class SettingsAdapterTests
{
    private static readonly FinanceContext Context = new(Guid.NewGuid(), "SENTETIK", "NOT_ENV", null);
    private static readonly DateOnly Day = new(2026, 9, 13);

    [Theory]
    [InlineData("settings_access_denied")]
    [InlineData("settings_provider_unavailable")]
    public async Task PreservesDistinctOwnerErrors(string code)
    {
        var adapter = new SettingsReceiptPrerequisites(new Provider(null, code));
        var exception = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() =>
            adapter.RequirePeriodAsync(Context, "SENTETIK-operation", Day, null));
        Assert.Equal(code, exception.Code);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("operation")]
    [InlineData("date")]
    [InlineData("valueOn")]
    public async Task RejectsMismatchedPeriodSnapshot(string mismatch)
    {
        var period = Period();
        period = mismatch switch
        {
            "company" => period with { CompanyId = Guid.NewGuid() },
            "operation" => period with { Operation = "other" },
            "date" => period with { OccurredOn = Day.AddDays(1) },
            _ => period with { ValueOn = Day }
        };
        var exception = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() =>
            new SettingsReceiptPrerequisites(new Provider(period, null))
                .RequirePeriodAsync(Context, "SENTETIK-operation", Day, null));
        Assert.Equal("finance_period_scope_mismatch", exception.Code);
    }

    [Fact]
    public async Task RejectsClosedPeriod()
    {
        var exception = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() =>
            new SettingsReceiptPrerequisites(new Provider(Period() with { IsEligible = false, DenialCode = "SENTETIK-closed" }, null))
                .RequirePeriodAsync(Context, "SENTETIK-operation", Day, null));
        Assert.Equal("SENTETIK-closed", exception.Code);
    }

    private static PeriodEligibilityV1 Period() => new(Context.CompanyId, Guid.NewGuid(),
        new(Guid.NewGuid(), 9007199254740993L), "SENTETIK-operation", Day, null, true, null);

    private sealed class Provider(PeriodEligibilityV1? period, string? error) : ISettingsPrerequisiteProvider
    {
        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn,
            DateOnly? valueOn, CancellationToken cancellationToken = default) => error is not null
                ? Task.FromException<PeriodEligibilityV1>(new SettingsPrerequisiteException(error))
                : Task.FromResult(period!);
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
