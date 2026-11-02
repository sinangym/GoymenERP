using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// Üretim U1-a: the reviewed URT-D6/D7/D9/D10 starting values (data, never code constants in a calculator) and the pure value
/// rules of the parameter key registry and the tolerance percents.
/// </summary>
public sealed class ManufacturingProfileCatalogAndRulesTests
{
    private static readonly Guid[] SeedCompanies =
    [
        ApprovedUnitProfileCatalog.GdcCompanyId,
        ApprovedUnitProfileCatalog.GspCompanyId,
        ApprovedUnitProfileCatalog.GinCompanyId,
    ];

    [Fact]
    public void Parameter_catalog_carries_the_urt_d9_and_d10_values_for_each_seed_company_as_version_0()
    {
        Assert.Equal(12, ApprovedManufacturingProfileCatalog.ParameterProfiles.Count);
        Assert.Equal(0, ApprovedManufacturingProfileCatalog.CatalogProfileVersion);
        Assert.Equal(new DateOnly(2026, 11, 1), ApprovedManufacturingProfileCatalog.EffectiveFrom);
        foreach (var company in SeedCompanies)
        {
            var profiles = ApprovedManufacturingProfileCatalog.ParametersFor(company).ToDictionary(entry => entry.Process);
            Assert.Equal(
                [SettingsManufacturingProcessesV1.ColdRolling, SettingsManufacturingProcessesV1.CutToLength,
                    SettingsManufacturingProcessesV1.Paint, SettingsManufacturingProcessesV1.Slitting],
                profiles.Keys.Order(StringComparer.Ordinal));
            Assert.Equal(
                [("densityKgPerCubicMetre", "kg/m3", 7850m), ("edgeTrimMm", "mm", 0m)],
                Numbers(profiles[SettingsManufacturingProcessesV1.Slitting]));
            Assert.Equal([("densityKgPerCubicMetre", "kg/m3", 7850m)],
                Numbers(profiles[SettingsManufacturingProcessesV1.ColdRolling]));
            Assert.Equal([("densityKgPerCubicMetre", "kg/m3", 7850m), ("kerfMm", "mm", 0m)],
                Numbers(profiles[SettingsManufacturingProcessesV1.CutToLength]));
            Assert.Equal(
                [
                    ("filmMicrons", "um", 80m), ("lineSpeedMetresPerMinute", "m/min", 20m), ("scrapFactor", "factor", 1.10m),
                    ("simultaneousProfiles", "count", 3m), ("solidsRatio", "ratio", 0.55m),
                ],
                Numbers(profiles[SettingsManufacturingProcessesV1.Paint]));
            Assert.All(profiles.Values, profile => Assert.Equal(ApprovedManufacturingProfileCatalog.EffectiveFrom,
                profile.EffectiveFrom));
        }

        // No line speed is shipped for slitting or tube (entered on the plan), and no kerf mode / trims / diameters (undecided).
        Assert.DoesNotContain(ApprovedManufacturingProfileCatalog.ParameterProfiles,
            entry => entry.Process == SettingsManufacturingProcessesV1.TubeProfile);
        Assert.DoesNotContain(ApprovedManufacturingProfileCatalog.ParameterProfiles.SelectMany(entry => entry.Values),
            value => value.Key is ManufacturingParameterKeysV1.KerfMode or ManufacturingParameterKeysV1.HeadTrimMm
                or ManufacturingParameterKeysV1.TailTrimMm or ManufacturingParameterKeysV1.InnerDiameterMm
                or ManufacturingParameterKeysV1.MaximumOuterDiameterMm or ManufacturingParameterKeysV1.ToleranceMetres);
        Assert.DoesNotContain(ApprovedManufacturingProfileCatalog.ParameterProfiles
                .Where(entry => entry.Process == SettingsManufacturingProcessesV1.Slitting).SelectMany(entry => entry.Values),
            value => value.Key == ManufacturingParameterKeysV1.LineSpeedMetresPerMinute);
        Assert.Empty(ApprovedManufacturingProfileCatalog.ParametersFor(Guid.NewGuid()));
        Assert.Equal(12, ApprovedManufacturingProfileCatalog.ParameterProfiles.Select(entry => entry.ProfileId).Distinct().Count());
        Assert.All(ApprovedManufacturingProfileCatalog.ParameterProfiles, entry =>
            Assert.All(entry.Values, value => Assert.True(ManufacturingProfileValueRulesV1.IsWellFormed(entry.Process, value))));
    }

    [Fact]
    public void Tolerance_starting_values_are_urt_d7_five_percent_fire_and_urt_d6_two_percent_mass_balance()
    {
        Assert.Equal(15, ApprovedManufacturingProfileCatalog.ToleranceStartingValues.Count);
        foreach (var company in SeedCompanies)
        {
            var values = ApprovedManufacturingProfileCatalog.ToleranceFor(company);
            Assert.Equal(5, values.Count);
            Assert.True(values.Select(value => value.Process).Order(StringComparer.Ordinal).SequenceEqual(
                [SettingsManufacturingProcessesV1.ColdRolling, SettingsManufacturingProcessesV1.CutToLength,
                    SettingsManufacturingProcessesV1.Paint, SettingsManufacturingProcessesV1.Slitting,
                    SettingsManufacturingProcessesV1.TubeProfile]));
            Assert.All(values, value => Assert.Equal((5m, 2m), (value.MaximumFirePercent, value.MassBalanceDifferenceTolerancePercent)));
        }

        Assert.Equal(15, ApprovedManufacturingProfileCatalog.ToleranceStartingValues.Select(entry => entry.ProfileId).Distinct().Count());
        Assert.Empty(ApprovedManufacturingProfileCatalog.ToleranceFor(Guid.NewGuid()));
        // The existing approved tolerance catalog stays empty: the close path reads published versions ≥ 1 only.
        Assert.Empty(ApprovedManufacturingToleranceCatalog.Approved.Entries);
    }

    [Fact]
    public void Every_key_is_declared_once_with_a_unit_and_at_least_one_canonical_process()
    {
        var keys = ManufacturingParameterKeysV1.All;
        Assert.Equal(keys.Count, keys.Select(key => key.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(keys, key =>
        {
            Assert.False(string.IsNullOrWhiteSpace(key.Unit));
            Assert.NotEmpty(key.Processes);
            Assert.All(key.Processes, process => Assert.True(SettingsManufacturingProcessesV1.IsCanonical(process)));
            Assert.Equal(key.Kind == ManufacturingParameterValueKindV1.Choice, key.Choices.Count > 0);
            Assert.True(key.Key.All(char.IsAsciiLetterOrDigit));
        });
        Assert.Equal([ManufacturingParameterKeysV1.KerfModePerPiece, ManufacturingParameterKeysV1.KerfModeBetweenPieces],
            ManufacturingParameterKeysV1.Find(ManufacturingParameterKeysV1.KerfMode)!.Choices);
    }

    [Theory]
    [InlineData("densityKgPerCubicMetre", "7850", true)]
    [InlineData("densityKgPerCubicMetre", "7850.000000", true)]
    [InlineData("densityKgPerCubicMetre", "7850.0000001", false)]
    [InlineData("densityKgPerCubicMetre", "0", false)]
    [InlineData("densityKgPerCubicMetre", "-1", false)]
    [InlineData("densityKgPerCubicMetre", "1e3", false)]
    [InlineData("densityKgPerCubicMetre", "7,85", false)]
    [InlineData("densityKgPerCubicMetre", " 7850", false)]
    [InlineData("densityKgPerCubicMetre", "07850", false)]
    [InlineData("densityKgPerCubicMetre", "1234567890123", false)]
    [InlineData("kerfMm", "0", true)]
    [InlineData("kerfMm", "2.125", true)]
    [InlineData("kerfMm", "2.1255", false)]
    [InlineData("solidsRatio", "0.55", true)]
    [InlineData("solidsRatio", "1", true)]
    [InlineData("solidsRatio", "1.01", false)]
    [InlineData("simultaneousProfiles", "3", true)]
    [InlineData("simultaneousProfiles", "3.5", false)]
    [InlineData("simultaneousProfiles", "0", false)]
    [InlineData("kerfMode", "PerPiece", true)]
    [InlineData("kerfMode", "perpiece", false)]
    [InlineData("kerfMode", "1", false)]
    public void Values_are_validated_by_the_key_kind_and_scale_and_never_rounded(string key, string text, bool valid)
    {
        var definition = ManufacturingParameterKeysV1.Find(key)!;
        Assert.Equal(valid, ManufacturingProfileValueRulesV1.TryReadValue(definition, text, out var value));
        if (valid && value.Number is { } number)
        {
            Assert.Equal(decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture), number);
        }
    }

    [Fact]
    public void A_profile_refuses_unknown_keys_keys_of_another_process_duplicates_and_an_empty_list()
    {
        Assert.False(ManufacturingProfileValueRulesV1.TryReadValues(SettingsManufacturingProcessesV1.Paint,
            [new("unknownKey", "1")], out _, out _));
        Assert.False(ManufacturingProfileValueRulesV1.TryReadValues(SettingsManufacturingProcessesV1.Paint,
            [new(ManufacturingParameterKeysV1.KerfMm, "0")], out _, out var otherProcess));
        Assert.Contains("kerfMm", otherProcess, StringComparison.Ordinal);
        Assert.False(ManufacturingProfileValueRulesV1.TryReadValues(SettingsManufacturingProcessesV1.Paint,
            [new(ManufacturingParameterKeysV1.FilmMicrons, "80"), new(ManufacturingParameterKeysV1.FilmMicrons, "90")], out _, out _));
        Assert.False(ManufacturingProfileValueRulesV1.TryReadValues(SettingsManufacturingProcessesV1.Paint, [], out _, out _));
        Assert.False(ManufacturingProfileValueRulesV1.TryReadValues("paint", [new(ManufacturingParameterKeysV1.FilmMicrons, "80")],
            out _, out _));
        Assert.True(ManufacturingProfileValueRulesV1.TryReadValues(SettingsManufacturingProcessesV1.CutToLength,
            [new(ManufacturingParameterKeysV1.KerfMode, "BetweenPieces"), new(ManufacturingParameterKeysV1.KerfMm, "1.5")],
            out var values, out _));
        Assert.Equal([ManufacturingParameterKeysV1.KerfMm, ManufacturingParameterKeysV1.KerfMode], values.Select(value => value.Key));
        Assert.Equal(("mm", 1.5m, (string?)null), (values[0].Unit, values[0].Number!.Value, values[0].Choice));
        Assert.Equal(("choice", (decimal?)null, "BetweenPieces"), (values[1].Unit, values[1].Number, values[1].Choice));
    }

    [Theory]
    [InlineData("5", true)]
    [InlineData("0", true)]
    [InlineData("100", true)]
    [InlineData("2.123456", true)]
    [InlineData("2.1234567", false)]
    [InlineData("100.000001", false)]
    [InlineData("-1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Tolerance_percents_are_0_to_100_with_at_most_six_decimals(string? text, bool valid) =>
        Assert.Equal(valid, ManufacturingProfileValueRulesV1.TryReadPercent(text, out _));

    [Fact]
    public void Format_is_lossless_invariant_text()
    {
        Assert.Equal("7850", ManufacturingProfileValueRulesV1.Format(7850.000000m));
        Assert.Equal("1.1", ManufacturingProfileValueRulesV1.Format(1.10m));
        Assert.Equal("0.55", ManufacturingProfileValueRulesV1.Format(0.550000m));
    }

    [Fact]
    public void The_parameter_lifecycle_reuses_the_tolerance_lifecycle_policy_maker_checker()
    {
        var at = new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
        var company = ApprovedUnitProfileCatalog.GdcCompanyId;
        var draft = ManufacturingToleranceProfileLifecyclePolicyV1.Prepare(new PrepareManufacturingToleranceProfileV1(
            Guid.NewGuid(), SettingsManufacturingProcessesV1.Paint, 1,
            new ManufacturingToleranceProfileCommandContextV1(company, "maker", 0, "k1", "taslak", at)));
        Assert.Equal(ManufacturingProfileStatusesV1.Draft, draft.Status.ToString());
        Assert.Equal(ManufacturingToleranceProfileLifecycleCodesV1.MakerSeparationRequired,
            Assert.Throws<ManufacturingToleranceProfileLifecycleException>(() =>
                ManufacturingToleranceProfileLifecyclePolicyV1.Approve(draft,
                    new ManufacturingToleranceProfileCommandContextV1(company, "maker", 1, "k2", "onay", at))).Code);
        var approved = ManufacturingToleranceProfileLifecyclePolicyV1.Approve(draft,
            new ManufacturingToleranceProfileCommandContextV1(company, "checker", 1, "k3", "onay", at));
        var published = ManufacturingToleranceProfileLifecyclePolicyV1.Publish(approved,
            new ManufacturingToleranceProfileCommandContextV1(company, "checker", 2, "k4", "yayın", at));
        Assert.Equal((ManufacturingProfileStatusesV1.Published, 3L), (published.Status.ToString(), published.StateVersion));
    }

    [Fact]
    public void Action_names_are_exact_and_distinct()
    {
        Assert.Equal(
            [
                "settings.manufacturing-parameter-profiles.read", "settings.manufacturing-parameter-profiles.prepare",
                "settings.manufacturing-parameter-profiles.approve", "settings.manufacturing-parameter-profiles.publish",
                "settings.manufacturing-tolerance-profiles.read",
            ],
            [
                ManufacturingParameterProfileActionsV1.Read, ManufacturingParameterProfileActionsV1.Prepare,
                ManufacturingParameterProfileActionsV1.Approve, ManufacturingParameterProfileActionsV1.Publish,
                ManufacturingToleranceProfileReadActionV1.Read,
            ]);
    }

    private static (string Key, string Unit, decimal Number)[] Numbers(ApprovedManufacturingParameterProfileEntry entry) =>
        entry.Values.Select(value => (value.Key, value.Unit, value.Number!.Value)).ToArray();
}
