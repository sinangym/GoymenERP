using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

public sealed class PrerequisiteContractTests
{
    [Theory]
    [InlineData("1000", 0)]
    [InlineData("0.001", 3)]
    [InlineData("1234567890123456789012345678901234567890.0001", 4)]
    [InlineData("001.20", 2)]
    public void Positive_values_preserve_every_digit(string value, int syntheticScale)
    {
        Assert.Equal(value, ExactPositiveDecimalV1.Parse(value, syntheticScale).Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("00.00")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1e3")]
    [InlineData("1,000")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData(".1")]
    [InlineData("1.")]
    [InlineData("1.2.3")]
    public void Invalid_values_are_not_coerced(string? value)
    {
        Assert.Equal("settings_decimal_invalid",
            Assert.Throws<SettingsPrerequisiteException>(() => ExactPositiveDecimalV1.Parse(value, 3)).Code);
    }

    [Theory]
    [InlineData("1.001", 2)]
    [InlineData("1.000", 2)]
    [InlineData("1.0", 0)]
    public void Excess_scale_including_trailing_zero_is_rejected(string value, int syntheticScale)
    {
        Assert.Equal("settings_decimal_scale_exceeded",
            Assert.Throws<SettingsPrerequisiteException>(() => ExactPositiveDecimalV1.Parse(value, syntheticScale)).Code);
    }

    [Fact]
    public void Missing_valid_scale_does_not_default_to_currency_convention()
    {
        Assert.Equal("settings_profile_invalid",
            Assert.Throws<SettingsPrerequisiteException>(() => ExactPositiveDecimalV1.Parse("1", -1)).Code);
    }

    [Theory]
    [InlineData(true, true, true, "settings_provider_unavailable")]
    [InlineData(false, true, true, "settings_access_denied")]
    [InlineData(true, false, true, "settings_access_denied")]
    [InlineData(true, true, false, "settings_access_denied")]
    public async Task All_sources_fail_explicitly_after_company_access_check(
        bool authenticated, bool resolved, bool allowed, string expectedCode)
    {
        var company = Guid.NewGuid();
        var provider = new UnavailableSettingsPrerequisiteProvider(
            new CompanyContext(company, resolved), new CurrentUser(authenticated, allowed ? company : Guid.NewGuid()));
        var date = new DateOnly(2026, 9, 13);

        Assert.Equal(expectedCode, (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetUnitProfileAsync(Guid.NewGuid(), date))).Code);
        Assert.Equal(expectedCode, (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetMoneyProfileAsync(Guid.NewGuid(), "test.operation", date))).Code);
        Assert.Equal(expectedCode, (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetPeriodEligibilityAsync("test.operation", date, null))).Code);
    }

    [Fact]
    public async Task Canceled_resolution_never_returns_a_profile()
    {
        var company = Guid.NewGuid();
        var provider = new UnavailableSettingsPrerequisiteProvider(
            new CompanyContext(company, true), new CurrentUser(true, company));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetUnitProfileAsync(Guid.NewGuid(), new DateOnly(2026, 9, 13), new CancellationToken(true)));
    }

    private sealed class CompanyContext(Guid companyId, bool resolved) : ICompanyContext
    {
        public bool IsResolved => resolved;
        public Guid CompanyId => companyId;
        public void Set(Guid id) => throw new NotSupportedException();
    }

    private sealed class CurrentUser(bool authenticated, Guid allowedCompany) : ICurrentUser
    {
        public bool IsAuthenticated => authenticated;
        public string? UserId => authenticated ? "synthetic-settings-test-actor" : null;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { allowedCompany };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
