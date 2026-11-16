using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// PRD-UNIT-1 / Master §28.1 / ALS-D43/D51: global unit identities do not grant or create company profiles.
/// </summary>
public sealed class ApprovedUnitIdentityCatalogTests
{
    private static readonly Guid Kg = Guid.Parse("c5ef643e-a6e8-51f3-89f2-8e68b00cb983");
    private static readonly Guid Gsp = Guid.Parse("10000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task Standard_unit_identities_are_explicit_while_existing_company_profiles_remain_kilogram_only()
    {
        var catalog = new ApprovedSettingsUnitIdentityCatalog();

        var expected = new UnitIdentityV1(Kg, "kg", "weight", IsKilogram: true);
        Assert.Equal(new[] {
            new UnitIdentityV1(Guid.Parse("5e0cbe45-61cc-541a-834a-366355de36e5"), "adet", "count", false), expected,
            new UnitIdentityV1(Guid.Parse("564b68b4-02a8-5e72-af2a-4f5cb9b91702"), "m", "length", false),
            new UnitIdentityV1(Guid.Parse("ab3cea5c-850e-515f-8de5-16f192ad7771"), "t", "weight", false),
        }, await catalog.ListAsync());
        Assert.Equal(3, ApprovedUnitProfileCatalog.Approved.Entries.Count);
        Assert.All(ApprovedUnitProfileCatalog.Approved.Entries, entry => Assert.Equal(Kg, entry.UnitId));
        Assert.Equal(expected, await catalog.ReadAsync(Kg));
    }

    [Fact]
    public async Task An_unknown_or_empty_unit_is_null_not_a_default()
    {
        var catalog = new ApprovedSettingsUnitIdentityCatalog();

        Assert.Null(await catalog.ReadAsync(Guid.NewGuid()));
        Assert.Null(await catalog.ReadAsync(Guid.Empty));
    }

    [Fact]
    public async Task The_identity_is_company_independent_one_row_for_a_unit_approved_in_two_companies()
    {
        var gsp = ApprovedUnitProfileCatalog.GdcKilogramV1 with
        {
            CompanyId = Gsp,
            ProfileId = Guid.Parse("00000000-0000-4000-8000-00000000a001"),
        };
        var catalog = new ApprovedSettingsUnitIdentityCatalog(
            new ApprovedUnitProfileCatalog([ApprovedUnitProfileCatalog.GdcKilogramV1, gsp]));

        Assert.Equal([new UnitIdentityV1(Kg, "kg", "weight", true)], await catalog.ListAsync());
    }

    public static TheoryData<string> Disagreements => ["code", "dimension", "kilogram", "shared-code"];

    [Theory]
    [MemberData(nameof(Disagreements))]
    public void A_source_that_disagrees_about_an_identity_is_refused_whole(string change)
    {
        var gsp = ApprovedUnitProfileCatalog.GdcKilogramV1 with
        {
            CompanyId = Gsp,
            ProfileId = Guid.Parse("00000000-0000-4000-8000-00000000a002"),
        };
        var other = change switch
        {
            "code" => gsp with { UnitCode = "KG" },
            "dimension" => gsp with { DimensionCode = "mass" },
            "kilogram" => gsp with { IsKilogram = false },
            // Another unit may not reuse the code: a code is a label shown for exactly one identity.
            "shared-code" => gsp with { UnitId = Guid.Parse("00000000-0000-4000-8000-00000000b001") },
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };
        var profiles = new ApprovedUnitProfileCatalog([ApprovedUnitProfileCatalog.GdcKilogramV1, other]);

        Assert.Equal("settings_profile_invalid",
            Assert.Throws<SettingsPrerequisiteException>(() => new ApprovedSettingsUnitIdentityCatalog(profiles)).Code);
    }

    [Fact]
    public async Task The_list_is_ordered_by_code()
    {
        var ton = ApprovedUnitProfileCatalog.GdcKilogramV1 with
        {
            UnitId = Guid.Parse("00000000-0000-4000-8000-00000000c001"),
            UnitCode = "t",
            IsKilogram = false,
            ProfileId = Guid.Parse("00000000-0000-4000-8000-00000000c002"),
        };
        var catalog = new ApprovedSettingsUnitIdentityCatalog(
            new ApprovedUnitProfileCatalog([ton, ApprovedUnitProfileCatalog.GdcKilogramV1]));

        Assert.Equal(["kg", "t"], (await catalog.ListAsync()).Select(identity => identity.Code));
    }
}
