using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// PRD-UNIT-1: the company-independent unit identities, derived only from the approved unit-profile
/// catalog. Every value asserted here is <see cref="ApprovedUnitProfileCatalog.GdcKilogramV1"/>'s.
/// </summary>
public sealed class ApprovedUnitIdentityCatalogTests
{
    private static readonly Guid Kg = Guid.Parse("c5ef643e-a6e8-51f3-89f2-8e68b00cb983");
    private static readonly Guid Gsp = Guid.Parse("10000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task The_approved_catalog_holds_exactly_the_kilogram_identity()
    {
        var catalog = new ApprovedSettingsUnitIdentityCatalog();

        var expected = new UnitIdentityV1(Kg, "kg", "weight", IsKilogram: true);
        Assert.Equal([expected], await catalog.ListAsync());
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
