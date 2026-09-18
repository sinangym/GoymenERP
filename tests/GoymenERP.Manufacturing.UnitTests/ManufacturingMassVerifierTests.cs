using System.Globalization;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// The kilogram unit/scale bond through the Settings owner's port (Contracts.V1): fail-closed,
/// every refusal with its own code, scale = min(profile, Ö2's 3), nothing rounded. The provider
/// is a synthetic stand-in for the port, not the Settings implementation.
/// </summary>
public sealed class ManufacturingMassVerifierTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherCompany = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid Kilogram = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid ProfileId = Guid.Parse("4a790fdc-2a6b-5126-835f-d3a13d28b71c");
    private static readonly DateOnly Day = new(2026, 9, 18);

    private static UnitProfileV1 KgProfile() =>
        new(Company, Kilogram, "kg", "mass", true, new ProfileVersionV1(ProfileId, 1), 3, true, new DateOnly(2026, 9, 18), null);

    private static ManufacturingMassVerifier Verifier(UnitProfileV1 profile) => new(new Port(() => profile));

    [Fact]
    public async Task A_mass_within_the_kilogram_profile_is_verified_with_its_snapshot()
    {
        var result = await Verifier(KgProfile()).VerifyAsync(Company, 9_700.125m, Day);

        Assert.True(result.IsVerified);
        Assert.Null(result.Code);
        Assert.Equal(9_700.125m, result.Mass!.Mass.Value);
        Assert.Equal((Kilogram, ProfileId, 1L, 3),
            (result.Mass.UnitId, result.Mass.ProfileId, result.Mass.ProfileVersion, result.Mass.Scale));
    }

    [Fact]
    public async Task The_port_is_asked_for_the_kilogram_identity_only()
    {
        var port = new Port(KgProfile);
        await new ManufacturingMassVerifier(port).VerifyAsync(Company, 1m, Day);

        Assert.Equal(new[] { Kilogram }, port.AskedUnits);
        Assert.Equal(new[] { Day }, port.AskedDays);
    }

    [Theory]
    [InlineData("settings_access_denied")]
    [InlineData("settings_provider_unavailable")]
    [InlineData(SettingsPrerequisiteCodesV1.UnitProfileNotFound)]
    [InlineData("settings_profile_invalid")]
    public async Task Every_settings_failure_is_a_refusal_with_the_owners_code_kept_apart(string code)
    {
        var verifier = new ManufacturingMassVerifier(new Port(() => throw new SettingsPrerequisiteException(code)));

        var result = await verifier.VerifyAsync(Company, 1m, Day);

        Assert.False(result.IsVerified);
        Assert.Null(result.Mass);
        Assert.Equal(code, result.Code);
    }

    [Fact]
    public async Task A_profile_of_another_company_is_refused()
    {
        var result = await Verifier(KgProfile() with { CompanyId = OtherCompany }).VerifyAsync(Company, 1m, Day);

        Assert.Equal(ManufacturingMassCodes.ProfileCompanyMismatch, result.Code);
    }

    [Theory]
    [InlineData("other-unit")]
    [InlineData("not-kilogram")]
    [InlineData("empty-profile-id")]
    [InlineData("zero-version")]
    [InlineData("negative-scale")]
    public async Task A_kilogram_answer_that_is_not_a_well_formed_kilogram_profile_is_invalid(string fault)
    {
        var profile = fault switch
        {
            "other-unit" => KgProfile() with { UnitId = Guid.NewGuid() },
            "not-kilogram" => KgProfile() with { IsKilogram = false, DimensionCode = "count", UnitCode = "adet" },
            "empty-profile-id" => KgProfile() with { Profile = new ProfileVersionV1(Guid.Empty, 1) },
            "zero-version" => KgProfile() with { Profile = new ProfileVersionV1(ProfileId, 0) },
            _ => KgProfile() with { Scale = -1 },
        };

        var result = await Verifier(profile).VerifyAsync(Company, 1m, Day);

        Assert.Equal(ManufacturingMassCodes.ProfileInvalid, result.Code);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("before-window")]
    [InlineData("after-window")]
    public async Task A_profile_not_in_effect_on_the_day_is_refused(string fault)
    {
        var profile = fault switch
        {
            "inactive" => KgProfile() with { IsActive = false },
            "before-window" => KgProfile() with { EffectiveFrom = Day.AddDays(1) },
            _ => KgProfile() with { EffectiveThrough = Day.AddDays(-1) },
        };

        var result = await Verifier(profile).VerifyAsync(Company, 1m, Day);

        Assert.Equal(ManufacturingMassCodes.ProfileNotInEffect, result.Code);
    }

    [Fact]
    public async Task The_last_day_of_the_window_is_still_in_effect()
    {
        var result = await Verifier(KgProfile() with { EffectiveThrough = Day }).VerifyAsync(Company, 1m, Day);

        Assert.True(result.IsVerified);
    }

    [Theory]
    [InlineData(3, "1.0005")]
    [InlineData(2, "1.005")]
    [InlineData(5, "1.0004")]
    [InlineData(0, "1.5")]
    public async Task More_decimals_than_the_profile_or_O2_allow_are_refused_never_rounded(int scale, string text)
    {
        var mass = decimal.Parse(text, CultureInfo.InvariantCulture);

        var result = await Verifier(KgProfile() with { Scale = scale }).VerifyAsync(Company, mass, Day);

        Assert.Equal(ManufacturingMassCodes.ScaleExceeded, result.Code);
        Assert.Null(result.Mass);
    }

    [Fact]
    public async Task Trailing_zeros_are_not_extra_precision_and_a_narrower_profile_scale_is_honoured()
    {
        var narrow = Verifier(KgProfile() with { Scale = 2 });

        var padded = await narrow.VerifyAsync(Company, decimal.Parse("12.5000", CultureInfo.InvariantCulture), Day);
        var twoDecimals = await narrow.VerifyAsync(Company, 12.25m, Day);
        var tooFine = await narrow.VerifyAsync(Company, 12.125m, Day);

        Assert.Equal(12.5m, padded.Mass!.Mass.Value);
        Assert.Equal(12.25m, twoDecimals.Mass!.Mass.Value);
        Assert.Equal(ManufacturingMassCodes.ScaleExceeded, tooFine.Code);
    }

    [Fact]
    public async Task A_zero_reading_is_a_valid_mass_and_a_negative_one_is_refused_before_the_owner_is_asked()
    {
        var port = new Port(KgProfile);
        var verifier = new ManufacturingMassVerifier(port);

        var zero = await verifier.VerifyAsync(Company, 0m, Day);
        var negative = await verifier.VerifyAsync(Company, -0.001m, Day);

        Assert.Equal(0m, zero.Mass!.Mass.Value);
        Assert.Equal(ManufacturingMassCodes.MassNegative, negative.Code);
        Assert.Single(port.AskedUnits);
    }

    [Fact]
    public async Task A_missing_company_is_refused_before_the_owner_is_asked()
    {
        var port = new Port(KgProfile);

        var result = await new ManufacturingMassVerifier(port).VerifyAsync(Guid.Empty, 1m, Day);

        Assert.Equal(ManufacturingMassCodes.CompanyMissing, result.Code);
        Assert.Empty(port.AskedUnits);
    }

    [Fact]
    public async Task Cancellation_is_not_a_refusal()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var verifier = new ManufacturingMassVerifier(new Port(KgProfile));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => verifier.VerifyAsync(Company, 1m, Day, cancelled.Token));
    }

    private sealed class Port(Func<UnitProfileV1> answer) : ISettingsPrerequisiteProvider
    {
        public List<Guid> AskedUnits { get; } = [];

        public List<DateOnly> AskedDays { get; } = [];

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AskedUnits.Add(unitId);
            AskedDays.Add(effectiveOn);
            return Task.FromResult(answer());
        }

        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn,
            DateOnly? valueOn, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
