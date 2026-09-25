using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using static GoymenERP.Inventory.UnitTests.InventoryFixture;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// The kilogram scale comes from the company's approved Settings profile (T02-a, Ö2), never from
/// a constant, and never wider than Ö2's 3 decimals. Every profile below is synthetic.
/// </summary>
public sealed class InventoryKilogramScaleTests
{
    private static readonly DateOnly Day = new(2026, 9, 18);
    private static readonly Guid ProfileId = Guid.Parse("60000000-0000-0000-0000-000000000001");

    private static UnitProfileV1 Profile(int scale = 3) => new(CompanyA, SettingsUnitIdentitiesV1.Kilogram, "kg", "weight",
        true, new ProfileVersionV1(ProfileId, 2), scale, true, Day.AddDays(-1), null);

    [Fact]
    public async Task The_company_s_kilogram_profile_decides_and_asks_for_the_settings_kilogram_identity()
    {
        var settings = new StubSettings { Profile = Profile() };

        var check = await new InventoryKilogramScale(settings).CheckAsync(CompanyA, Day, [Kilograms.Of(12.125m)]);

        Assert.True(check.IsAccepted);
        Assert.Equal(new InventoryKilogramProfile(CompanyA, ProfileId, 2, 3), check.Profile);
        Assert.Equal([(SettingsUnitIdentitiesV1.Kilogram, Day)], settings.Requests);
    }

    [Theory]
    [InlineData(2, "1.005", false)]
    [InlineData(2, "1.05", true)]
    [InlineData(0, "7.5", false)]
    [InlineData(0, "7", true)]
    [InlineData(5, "1.125", true)]
    public async Task A_profile_can_narrow_the_scale_but_never_widen_it_past_three(int scale, string text, bool accepted)
    {
        // Kilograms itself already refuses a 4th decimal, so a wider profile changes nothing.
        var value = Kilograms.Of(decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture));
        var check = await new InventoryKilogramScale(new StubSettings { Profile = Profile(scale) }).CheckAsync(CompanyA, Day, [value]);

        Assert.Equal(accepted, check.IsAccepted);
        if (!accepted)
        {
            Assert.Equal(InventoryScaleCodes.ScaleExceeded, check.Code);
        }
        else
        {
            Assert.Equal(Math.Min(scale, 3), check.Profile!.Scale);
        }
    }

    [Theory]
    [InlineData("settings_access_denied")]
    [InlineData("settings_provider_unavailable")]
    [InlineData(SettingsPrerequisiteCodesV1.UnitProfileNotFound)]
    [InlineData("settings_profile_invalid")]
    public async Task Every_settings_failure_is_a_refusal_with_the_owner_s_code(string code)
    {
        var check = await new InventoryKilogramScale(new StubSettings { Failure = new SettingsPrerequisiteException(code) })
            .CheckAsync(CompanyA, Day, [Kilograms.Of(1m)]);

        Assert.False(check.IsAccepted);
        Assert.Equal(code, check.Code);
    }

    [Theory]
    [InlineData("other-company")]
    [InlineData("other-unit")]
    [InlineData("not-kilogram")]
    [InlineData("inactive")]
    [InlineData("not-yet")]
    [InlineData("expired")]
    [InlineData("no-profile-id")]
    [InlineData("negative-scale")]
    public async Task A_profile_that_is_not_this_company_s_usable_kilogram_is_refused(string fault)
    {
        var profile = fault switch
        {
            "other-company" => Profile() with { CompanyId = CompanyB },
            "other-unit" => Profile() with { UnitId = Guid.NewGuid() },
            "not-kilogram" => Profile() with { IsKilogram = false },
            "inactive" => Profile() with { IsActive = false },
            "not-yet" => Profile() with { EffectiveFrom = Day.AddDays(1) },
            "expired" => Profile() with { EffectiveThrough = Day.AddDays(-1) },
            "no-profile-id" => Profile() with { Profile = new ProfileVersionV1(Guid.Empty, 2) },
            _ => Profile(-1),
        };

        var check = await new InventoryKilogramScale(new StubSettings { Profile = profile }).CheckAsync(CompanyA, Day, [Kilograms.Of(1m)]);

        Assert.Equal(InventoryScaleCodes.ProfileUnusable, check.Code);
    }

    [Fact]
    public async Task No_company_is_refused_without_asking()
    {
        var settings = new StubSettings { Profile = Profile() };
        var check = await new InventoryKilogramScale(settings).CheckAsync(Guid.Empty, Day, [Kilograms.Of(1m)]);
        Assert.Equal(InventoryScaleCodes.ProfileUnusable, check.Code);
        Assert.Empty(settings.Requests);
    }

    private sealed class StubSettings : ISettingsPrerequisiteProvider
    {
        public UnitProfileV1? Profile { get; init; }
        public Exception? Failure { get; init; }
        public List<(Guid, DateOnly)> Requests { get; } = [];

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default)
        {
            Requests.Add((unitId, effectiveOn));
            return Failure is not null ? Task.FromException<UnitProfileV1>(Failure) : Task.FromResult(Profile!);
        }

        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
