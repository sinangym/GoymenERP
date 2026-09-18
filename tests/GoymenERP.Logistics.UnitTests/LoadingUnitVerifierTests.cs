using GoymenERP.Modules.Logistics.Application;
using GoymenERP.Modules.Logistics.Domain.Quantities;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// A loading line's unit and quantity are verified against the Settings unit profile through the
/// owner's port (same pattern as Purchasing). Every identity is synthetic, none is the approved GDC profile.
/// </summary>
public sealed class LoadingUnitVerifierTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Unit = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid ProfileId = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly DateOnly Day = new(2026, 9, 18);

    private static UnitProfileV1 KgProfile(int scale = 3) => new(Company, Unit, "kg", "weight", true,
        new ProfileVersionV1(ProfileId, 4), scale, true, new DateOnly(2026, 9, 1), null);

    [Fact]
    public async Task A_kilogram_profile_verifies_the_unit_and_keeps_the_exact_quantity()
    {
        var settings = new StubSettings { Profile = KgProfile() };
        var result = await new LoadingUnitVerifier(settings).VerifyAsync(Company, Unit, 3_800.000m, Day);

        Assert.True(result.IsVerified);
        Assert.Null(result.Code);
        Assert.Equal((Unit, "kg", ProfileId, "4", 3, 3), (result.Unit!.UnitId, result.Unit.UnitCode, result.Unit.ProfileId, result.Unit.ProfileVersion, result.Unit.ProfileScale, result.Unit.Scale));
        Assert.Equal(Quantity.Of(3_800m, QuantityUnit.Kilogram), result.Unit.Quantity);
        Assert.Equal([(Unit, Day)], settings.UnitCalls);
    }

    [Theory]
    [InlineData("settings_access_denied")]
    [InlineData("settings_provider_unavailable")]
    [InlineData(SettingsPrerequisiteCodesV1.UnitProfileNotFound)]
    [InlineData("settings_profile_invalid")]
    public async Task Every_settings_failure_is_a_refusal_with_the_owners_code_kept_apart(string code)
    {
        var result = await new LoadingUnitVerifier(new StubSettings { Error = new SettingsPrerequisiteException(code) })
            .VerifyAsync(Company, Unit, 1m, Day);
        Assert.False(result.IsVerified);
        Assert.Equal(code, result.Code);
    }

    [Fact]
    public async Task A_profile_of_another_company_is_refused()
    {
        var result = await new LoadingUnitVerifier(new StubSettings { Profile = KgProfile() with { CompanyId = Guid.NewGuid() } })
            .VerifyAsync(Company, Unit, 1m, Day);
        Assert.Equal(LoadingUnitCodes.UnitCompanyMismatch, result.Code);
    }

    [Theory]
    [InlineData("other-unit")]
    [InlineData("empty-profile-id")]
    [InlineData("zero-version")]
    [InlineData("negative-scale")]
    [InlineData("blank-code")]
    public async Task An_inconsistent_profile_is_refused_as_invalid(string fault)
    {
        var profile = fault switch
        {
            "other-unit" => KgProfile() with { UnitId = Guid.NewGuid() },
            "empty-profile-id" => KgProfile() with { Profile = new ProfileVersionV1(Guid.Empty, 4) },
            "zero-version" => KgProfile() with { Profile = new ProfileVersionV1(ProfileId, 0) },
            "negative-scale" => KgProfile(-1),
            _ => KgProfile() with { UnitCode = " " },
        };
        var result = await new LoadingUnitVerifier(new StubSettings { Profile = profile }).VerifyAsync(Company, Unit, 1m, Day);
        Assert.Equal(LoadingUnitCodes.UnitProfileInvalid, result.Code);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("before-window")]
    [InlineData("after-window")]
    [InlineData("not-kilogram")]
    public async Task An_inactive_out_of_window_or_non_kilogram_profile_is_unsupported(string fault)
    {
        var profile = fault switch
        {
            "inactive" => KgProfile() with { IsActive = false },
            "before-window" => KgProfile() with { EffectiveFrom = Day.AddDays(1) },
            "after-window" => KgProfile() with { EffectiveThrough = Day.AddDays(-1) },
            _ => KgProfile() with { UnitCode = "adet", DimensionCode = "count", IsKilogram = false, Scale = 0 },
        };
        var result = await new LoadingUnitVerifier(new StubSettings { Profile = profile }).VerifyAsync(Company, Unit, 1m, Day);
        Assert.Equal(LoadingUnitCodes.UnitUnsupported, result.Code);
    }

    [Theory]
    [InlineData(3, "1.0005")]
    [InlineData(2, "1.005")]
    [InlineData(5, "1.0004")]
    public async Task More_decimals_than_the_profile_or_o2_allow_are_refused_never_rounded(int scale, string text)
    {
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var result = await new LoadingUnitVerifier(new StubSettings { Profile = KgProfile(scale) }).VerifyAsync(Company, Unit, value, Day);
        Assert.Equal(LoadingUnitCodes.QuantityScaleExceeded, result.Code);
    }

    [Theory]
    [InlineData(5, 3)]
    [InlineData(3, 3)]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    public async Task The_result_reports_the_applied_scale_beside_the_profile_scale(int profileScale, int applied)
    {
        // LOG-3: a consumer offering input precision must read the APPLIED scale; a profile of 5
        // still allows only Ö2's 3 kilogram decimals (the PUR-3/MFG-3 shape).
        var result = await new LoadingUnitVerifier(new StubSettings { Profile = KgProfile(profileScale) }).VerifyAsync(Company, Unit, 7m, Day);

        Assert.True(result.IsVerified);
        Assert.Equal((profileScale, applied), (result.Unit!.ProfileScale, result.Unit.Scale));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task A_non_positive_quantity_is_refused_before_the_owner_is_asked(string text)
    {
        var settings = new StubSettings { Profile = KgProfile() };
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var result = await new LoadingUnitVerifier(settings).VerifyAsync(Company, Unit, value, Day);
        Assert.Equal(LoadingUnitCodes.QuantityNotPositive, result.Code);
        Assert.Empty(settings.UnitCalls);
    }

    [Fact]
    public async Task Missing_company_or_unit_identity_is_refused_before_the_owner_is_asked()
    {
        var settings = new StubSettings { Profile = KgProfile() };
        var verifier = new LoadingUnitVerifier(settings);
        Assert.Equal(LoadingUnitCodes.ReferenceMissing, (await verifier.VerifyAsync(Guid.Empty, Unit, 1m, Day)).Code);
        Assert.Equal(LoadingUnitCodes.ReferenceMissing, (await verifier.VerifyAsync(Company, Guid.Empty, 1m, Day)).Code);
        Assert.Empty(settings.UnitCalls);
    }

    [Fact]
    public async Task Cancellation_is_not_a_refusal()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var verifier = new LoadingUnitVerifier(new StubSettings { Profile = KgProfile() });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => verifier.VerifyAsync(Company, Unit, 1m, Day, cancelled.Token));
    }

    private sealed class StubSettings : ISettingsPrerequisiteProvider
    {
        public List<(Guid UnitId, DateOnly EffectiveOn)> UnitCalls { get; } = [];
        public UnitProfileV1? Profile { get; set; }
        public Exception? Error { get; set; }

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UnitCalls.Add((unitId, effectiveOn));
            if (Error is not null) throw Error;
            return Task.FromResult(Profile ?? throw new InvalidOperationException("Unexpected unit profile read."));
        }

        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
