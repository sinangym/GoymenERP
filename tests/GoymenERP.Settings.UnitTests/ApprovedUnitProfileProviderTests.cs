using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// T02-a: the real unit-profile provider for the one approved profile, GDC kg (Sinan's Ö1/Ö2,
/// #66 5722637822). Every value asserted here is traced to a source in
/// <see cref="ApprovedUnitProfileCatalog.GdcKilogramV1"/>; nothing is a fixture posing as data.
/// </summary>
public sealed class ApprovedUnitProfileProviderTests
{
    private static readonly Guid Gdc = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Gsp = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid Kg = Guid.Parse("c5ef643e-a6e8-51f3-89f2-8e68b00cb983");
    private static readonly DateOnly Approval = new(2026, 9, 18);

    [Fact]
    public async Task GDC_kilogram_on_the_approval_date_is_the_exact_approved_profile()
    {
        var profile = await Provider(Gdc).GetUnitProfileAsync(Kg, Approval);

        Assert.Equal(Gdc, profile.CompanyId);
        Assert.Equal(Kg, profile.UnitId);
        Assert.Equal("kg", profile.UnitCode);
        Assert.Equal("weight", profile.DimensionCode);
        Assert.True(profile.IsKilogram);
        Assert.Equal(new ProfileVersionV1(Guid.Parse("4a790fdc-2a6b-5126-835f-d3a13d28b71c"), 1), profile.Profile);
        Assert.Equal(3, profile.Scale);
        Assert.True(profile.IsActive);
        Assert.Equal(Approval, profile.EffectiveFrom);
        // Ö2: no end date means explicitly open-ended, not "unknown".
        Assert.Null(profile.EffectiveThrough);
    }

    [Theory]
    [InlineData("2026-09-19")]
    [InlineData("2027-01-01")]
    [InlineData("2099-12-31")]
    public async Task The_open_ended_version_answers_every_later_date(string date)
    {
        var profile = await Provider(Gdc).GetUnitProfileAsync(Kg, DateOnly.Parse(date));

        Assert.Equal(1, profile.Profile.Version);
        Assert.Null(profile.EffectiveThrough);
    }

    [Fact]
    public async Task A_date_before_approval_gets_the_real_window_not_a_backdated_one()
    {
        var dayBefore = Approval.AddDays(-1);

        var profile = await Provider(Gdc).GetUnitProfileAsync(Kg, dayBefore);

        // The version is served with its true start, so a consumer's date check (required by the
        // port) rejects it. The provider never widens a window to fit the question.
        Assert.Equal(Approval, profile.EffectiveFrom);
        Assert.True(dayBefore < profile.EffectiveFrom);
    }

    [Fact]
    public async Task The_identity_is_the_documented_Settings_unit_identity()
    {
        Assert.Equal(Kg, SettingsUnitIdentitiesV1.Kilogram);
        Assert.Equal(Gdc, ApprovedUnitProfileCatalog.GdcCompanyId);
        Assert.Equal(Kg, (await Provider(Gdc).GetUnitProfileAsync(SettingsUnitIdentitiesV1.Kilogram, Approval)).UnitId);
    }

    [Fact]
    public void The_approved_catalog_holds_exactly_the_one_approved_profile()
    {
        // No fixture, example or second unit rides along with the approved data.
        var entry = Assert.Single(ApprovedUnitProfileCatalog.Approved.Entries);
        Assert.Same(ApprovedUnitProfileCatalog.GdcKilogramV1, entry);
        Assert.False(string.IsNullOrWhiteSpace(entry.Source));
    }

    [Theory]
    [InlineData("10000000-0000-0000-0000-000000000002")] // GSP: same group, no approved profile yet
    [InlineData("10000000-0000-0000-0000-000000000003")] // GIN
    [InlineData("7d0c7c2e-2b0e-4d8e-9b2a-2f1f0c9a6e11")] // a company the catalog has never heard of
    public async Task A_company_without_an_approved_source_is_unavailable_not_given_GDC_s_profile(string company)
    {
        var companyId = Guid.Parse(company);

        var failure = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => Provider(companyId).GetUnitProfileAsync(Kg, Approval));

        Assert.Equal("settings_provider_unavailable", failure.Code);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("0f3bd0a1-6e2c-4d77-a3a5-0b1c2d3e4f50")]
    public async Task An_unknown_unit_in_GDC_is_not_found_distinct_from_unavailable_and_invalid(string unit)
    {
        var failure = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => Provider(Gdc).GetUnitProfileAsync(Guid.Parse(unit), Approval));

        Assert.Equal(ApprovedSettingsPrerequisiteProvider.UnitProfileNotFoundCode, failure.Code);
        Assert.NotEqual("settings_provider_unavailable", failure.Code);
        Assert.NotEqual("settings_profile_invalid", failure.Code);
    }

    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public async Task Access_is_checked_before_anything_about_the_source_is_revealed(
        bool authenticated, bool hasActorId, bool resolved, bool member)
    {
        var provider = new ApprovedSettingsPrerequisiteProvider(
            new Context(Gdc, resolved),
            new Actor(authenticated, hasActorId ? "synthetic-settings-actor" : " ", member ? Gdc : Gsp));

        var failure = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetUnitProfileAsync(Kg, Approval));

        Assert.Equal("settings_access_denied", failure.Code);
    }

    [Fact]
    public async Task An_empty_company_is_denied_even_for_an_actor_allowed_that_identifier()
    {
        var provider = new ApprovedSettingsPrerequisiteProvider(
            new Context(Guid.Empty, true), new Actor(true, "synthetic-settings-actor", Guid.Empty));

        Assert.Equal("settings_access_denied", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetUnitProfileAsync(Kg, Approval))).Code);
    }

    [Fact]
    public async Task A_cancelled_caller_gets_no_profile()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Provider(Gdc).GetUnitProfileAsync(Kg, Approval, new CancellationToken(true)));
    }

    [Fact]
    public async Task Money_and_period_stay_unavailable_behind_the_same_access_check()
    {
        var provider = Provider(Gdc);

        Assert.Equal("settings_provider_unavailable", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetMoneyProfileAsync(Guid.NewGuid(), "synthetic-operation", Approval))).Code);
        Assert.Equal("settings_provider_unavailable", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetPeriodEligibilityAsync("synthetic-operation", Approval, null))).Code);

        var outsider = new ApprovedSettingsPrerequisiteProvider(
            new Context(Gdc, true), new Actor(true, "synthetic-settings-actor", Gsp));
        Assert.Equal("settings_access_denied", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => outsider.GetMoneyProfileAsync(Guid.NewGuid(), "synthetic-operation", Approval))).Code);
    }

    [Theory]
    [InlineData("empty company")]
    [InlineData("empty unit")]
    [InlineData("empty profile id")]
    [InlineData("blank unit code")]
    [InlineData("blank dimension")]
    [InlineData("blank source")]
    [InlineData("negative scale")]
    [InlineData("zero version")]
    [InlineData("reversed window")]
    [InlineData("repeated version")]
    [InlineData("overlapping versions")]
    [InlineData("open version followed by another")]
    public void A_malformed_catalog_is_refused_whole_as_invalid(string malformation)
    {
        var v1 = ApprovedUnitProfileCatalog.GdcKilogramV1;
        ApprovedUnitProfileEntry[] entries = malformation switch
        {
            "empty company" => [v1 with { CompanyId = Guid.Empty }],
            "empty unit" => [v1 with { UnitId = Guid.Empty }],
            "empty profile id" => [v1 with { ProfileId = Guid.Empty }],
            "blank unit code" => [v1 with { UnitCode = " " }],
            "blank dimension" => [v1 with { DimensionCode = "" }],
            "blank source" => [v1 with { Source = " " }],
            "negative scale" => [v1 with { Scale = -1 }],
            "zero version" => [v1 with { Version = 0 }],
            "reversed window" => [v1 with { EffectiveThrough = v1.EffectiveFrom.AddDays(-1) }],
            "repeated version" =>
            [
                v1 with { EffectiveThrough = new DateOnly(2027, 1, 1) },
                v1 with { EffectiveFrom = new DateOnly(2027, 1, 2), ProfileId = Guid.NewGuid() },
            ],
            "overlapping versions" =>
            [
                v1 with { EffectiveThrough = new DateOnly(2027, 1, 1) },
                v1 with { Version = 2, EffectiveFrom = new DateOnly(2026, 12, 1), ProfileId = Guid.NewGuid() },
            ],
            _ =>
            [
                v1,
                v1 with { Version = 2, EffectiveFrom = new DateOnly(2027, 1, 1), ProfileId = Guid.NewGuid() },
            ],
        };

        Assert.Equal("settings_profile_invalid",
            Assert.Throws<SettingsPrerequisiteException>(() => new ApprovedUnitProfileCatalog(entries)).Code);
    }

    [Fact]
    public async Task A_later_approved_version_takes_over_from_its_own_start_and_the_old_one_keeps_its_window()
    {
        // Synthetic second version, only to pin version selection; it is not approved data.
        var v1 = ApprovedUnitProfileCatalog.GdcKilogramV1 with { EffectiveThrough = new DateOnly(2027, 1, 1) };
        var v2 = ApprovedUnitProfileCatalog.GdcKilogramV1 with
        {
            Version = 2,
            ProfileId = Guid.Parse("11111111-2222-5333-8444-555555555555"),
            EffectiveFrom = new DateOnly(2027, 1, 2),
        };
        var provider = new ApprovedSettingsPrerequisiteProvider(
            new Context(Gdc, true), new Actor(true, "synthetic-settings-actor", Gdc),
            new ApprovedUnitProfileCatalog([v2, v1]));

        Assert.Equal(1, (await provider.GetUnitProfileAsync(Kg, new DateOnly(2027, 1, 1))).Profile.Version);
        Assert.Equal(2, (await provider.GetUnitProfileAsync(Kg, new DateOnly(2027, 1, 2))).Profile.Version);
        // A historical question keeps its historical answer: the new version does not rewrite it.
        Assert.Equal(new DateOnly(2027, 1, 1), (await provider.GetUnitProfileAsync(Kg, Approval)).EffectiveThrough);
    }

    [Fact]
    public void Only_the_reviewed_catalog_is_reachable_from_outside_Settings()
    {
        // FİNANS #279 P3-1: a module that references Settings must not be able to build an
        // "approved" provider or catalog over profiles it made up.
        var providerConstructors = typeof(ApprovedSettingsPrerequisiteProvider).GetConstructors();
        var publicProvider = Assert.Single(providerConstructors);
        Assert.Equal(
            [typeof(ICompanyContext), typeof(ICurrentUser)],
            publicProvider.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Empty(typeof(ApprovedUnitProfileCatalog).GetConstructors());
    }

    private static ApprovedSettingsPrerequisiteProvider Provider(Guid company) =>
        new(new Context(company, true), new Actor(true, "synthetic-settings-actor", company));

    private sealed class Context(Guid companyId, bool resolved) : ICompanyContext
    {
        public bool IsResolved => resolved;
        public Guid CompanyId => companyId;
        public void Set(Guid id) => throw new NotSupportedException();
    }

    private sealed class Actor(bool authenticated, string userId, Guid allowedCompany) : ICurrentUser
    {
        public bool IsAuthenticated => authenticated;
        public string? UserId => authenticated ? userId : null;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { allowedCompany };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
