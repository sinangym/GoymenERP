using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// ÜRÜN #278 P3-2 / Ö2: a line's unit and quantity are verified against the Settings unit
/// profile through the owner's port. The stub below answers only what a test sets; every
/// identity is synthetic, none is the approved GDC profile.
/// </summary>
public sealed class PurchaseUnitVerifierTests
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
        var verifier = new PurchaseUnitVerifier(settings);

        var result = await verifier.VerifyAsync(Company, Unit, 12.500m, Day);

        Assert.True(result.IsVerified);
        Assert.Null(result.Code);
        Assert.Equal((Unit, "kg", ProfileId, "4", 3), (result.Unit!.UnitId, result.Unit.UnitCode, result.Unit.ProfileId, result.Unit.ProfileVersion, result.Unit.Scale));
        Assert.Equal(Quantity.Of(12.5m, QuantityUnit.Kilogram), result.Unit.Quantity);
        Assert.Equal([(Unit, Day)], settings.UnitCalls);
    }

    [Theory]
    [InlineData("settings_access_denied")]
    [InlineData("settings_provider_unavailable")]
    [InlineData(SettingsPrerequisiteCodesV1.UnitProfileNotFound)]
    [InlineData("settings_profile_invalid")]
    public async Task Every_settings_failure_is_a_refusal_with_the_owners_code_kept_apart(string code)
    {
        var settings = new StubSettings { Error = new SettingsPrerequisiteException(code) };

        var result = await new PurchaseUnitVerifier(settings).VerifyAsync(Company, Unit, 1m, Day);

        Assert.False(result.IsVerified);
        Assert.Null(result.Unit);
        Assert.Equal(code, result.Code);
    }

    [Fact]
    public async Task A_profile_of_another_company_is_refused()
    {
        var settings = new StubSettings { Profile = KgProfile() with { CompanyId = Guid.NewGuid() } };
        var result = await new PurchaseUnitVerifier(settings).VerifyAsync(Company, Unit, 1m, Day);
        Assert.Equal(PurchaseUnitCodes.UnitCompanyMismatch, result.Code);
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
        var result = await new PurchaseUnitVerifier(new StubSettings { Profile = profile }).VerifyAsync(Company, Unit, 1m, Day);
        Assert.Equal(PurchaseUnitCodes.UnitProfileInvalid, result.Code);
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
        var result = await new PurchaseUnitVerifier(new StubSettings { Profile = profile }).VerifyAsync(Company, Unit, 1m, Day);
        Assert.Equal(PurchaseUnitCodes.UnitUnsupported, result.Code);
    }

    [Theory]
    [InlineData(3, "1.0005")]
    [InlineData(2, "1.005")]
    [InlineData(5, "1.0004")]
    public async Task More_decimals_than_the_profile_or_o2_allow_are_refused_never_rounded(int scale, string text)
    {
        // Scale 5 is above Ö2's kilogram cap of 3, so 1.0004 is still refused: the profile can
        // narrow the precision, never widen it past the decision.
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var result = await new PurchaseUnitVerifier(new StubSettings { Profile = KgProfile(scale) }).VerifyAsync(Company, Unit, value, Day);
        Assert.Equal(PurchaseUnitCodes.QuantityScaleExceeded, result.Code);
    }

    [Fact]
    public async Task Trailing_zeros_are_not_extra_precision_and_a_narrower_profile_scale_is_honoured()
    {
        var narrow = await new PurchaseUnitVerifier(new StubSettings { Profile = KgProfile(1) }).VerifyAsync(Company, Unit, 12.5000m, Day);
        Assert.True(narrow.IsVerified);
        Assert.Equal(12.5m, narrow.Unit!.Quantity.Value);
        Assert.Equal((1, 1), (narrow.Unit.ProfileScale, narrow.Unit.Scale));

        var tooFine = await new PurchaseUnitVerifier(new StubSettings { Profile = KgProfile(1) }).VerifyAsync(Company, Unit, 12.55m, Day);
        Assert.Equal(PurchaseUnitCodes.QuantityScaleExceeded, tooFine.Code);
    }

    [Fact]
    public async Task The_result_reports_the_applied_scale_beside_the_profile_scale()
    {
        // ÜRÜN #288 P3-2: a profile wider than Ö2's cap verifies 1.25 but must not advertise 5
        // decimals, because the domain's Quantity accepts at most 3.
        var wide = await new PurchaseUnitVerifier(new StubSettings { Profile = KgProfile(5) }).VerifyAsync(Company, Unit, 1.25m, Day);

        Assert.True(wide.IsVerified);
        Assert.Equal((5, Quantity.KilogramScale), (wide.Unit!.ProfileScale, wide.Unit.Scale));
        Assert.Equal(1.25m, wide.Unit.Quantity.Value);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task A_non_positive_quantity_is_refused_before_the_owner_is_asked(string text)
    {
        var settings = new StubSettings { Profile = KgProfile() };
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var result = await new PurchaseUnitVerifier(settings).VerifyAsync(Company, Unit, value, Day);
        Assert.Equal(PurchaseUnitCodes.QuantityNotPositive, result.Code);
        Assert.Empty(settings.UnitCalls);
    }

    [Fact]
    public async Task Missing_company_or_unit_identity_is_refused_before_the_owner_is_asked()
    {
        var settings = new StubSettings { Profile = KgProfile() };
        var verifier = new PurchaseUnitVerifier(settings);
        Assert.Equal(PurchaseUnitCodes.ReferenceMissing, (await verifier.VerifyAsync(Guid.Empty, Unit, 1m, Day)).Code);
        Assert.Equal(PurchaseUnitCodes.ReferenceMissing, (await verifier.VerifyAsync(Company, Guid.Empty, 1m, Day)).Code);
        Assert.Empty(settings.UnitCalls);
    }

    [Fact]
    public async Task Cancellation_is_not_a_refusal()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var verifier = new PurchaseUnitVerifier(new StubSettings { Profile = KgProfile() });
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
