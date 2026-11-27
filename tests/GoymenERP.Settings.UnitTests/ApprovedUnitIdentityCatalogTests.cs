using System.Security.Cryptography;
using System.Text;
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
    public async Task Standard_unit_identities_are_explicit_and_only_kilogram_and_piece_carry_company_profiles()
    {
        var catalog = new ApprovedSettingsUnitIdentityCatalog();

        var expected = new UnitIdentityV1(Kg, "kg", "weight", IsKilogram: true);
        Assert.Equal(new[] {
            new UnitIdentityV1(Guid.Parse("5e0cbe45-61cc-541a-834a-366355de36e5"), "adet", "count", false), expected,
            new UnitIdentityV1(Guid.Parse("564b68b4-02a8-5e72-af2a-4f5cb9b91702"), "m", "length", false),
            new UnitIdentityV1(Guid.Parse("ab3cea5c-850e-515f-8de5-16f192ad7771"), "t", "weight", false),
            new UnitIdentityV1(Guid.Parse("c92ed9ca-7d92-5a55-a199-6f2d580b87f6"), "l", "volume", false),
            new UnitIdentityV1(Guid.Parse("c6ff7aab-f645-50df-ad31-dbd2fb30fbc5"), "takım", "count", false),
            new UnitIdentityV1(Guid.Parse("7bb486df-a533-5f54-b5fa-a86277488bc9"), "set", "count", false),
            new UnitIdentityV1(Guid.Parse("addb2ea5-bf3f-519b-98d4-7e5e568f7aaf"), "saat", "time", false),
            new UnitIdentityV1(Guid.Parse("ac628f64-616f-5099-a22a-43620e571e63"), "gün", "time", false),
            new UnitIdentityV1(Guid.Parse("da00d1f1-4649-59e1-889e-f8d9accd280a"), "hizmet", "count", false),
        }.OrderBy(identity => identity.Code, StringComparer.Ordinal), await catalog.ListAsync());
        // AK-1: kg and piece carry company profiles; metre and tonne remain identities only.
        Assert.Equal(6, ApprovedUnitProfileCatalog.Approved.Entries.Count);
        Assert.All(ApprovedUnitProfileCatalog.Approved.Entries,
            entry => Assert.Contains(entry.UnitId, new[] { Kg, SettingsUnitIdentitiesV1.Piece }));
        Assert.Equal(expected, await catalog.ReadAsync(Kg));
    }

    public static TheoryData<string, Guid> TechnicalUnitNames => new()
    {
        { "kg", SettingsUnitIdentitiesV1.Kilogram }, { "tonne", SettingsUnitIdentitiesV1.Tonne },
        { "metre", SettingsUnitIdentitiesV1.Metre }, { "piece", SettingsUnitIdentitiesV1.Piece },
        { "litre", SettingsUnitIdentitiesV1.Litre }, { "kit", SettingsUnitIdentitiesV1.Kit },
        { "set", SettingsUnitIdentitiesV1.Set }, { "hour", SettingsUnitIdentitiesV1.Hour },
        { "day", SettingsUnitIdentitiesV1.Day }, { "service", SettingsUnitIdentitiesV1.Service },
    };

    [Theory]
    [MemberData(nameof(TechnicalUnitNames))]
    public void Unit_identities_keep_the_documented_uuid_v5_namespace_and_stable_technical_names(string name, Guid identity)
    {
        // Same RFC UUIDv5/network-byte-order pattern as ExchangeRateCurrencyIdentitiesV1;
        // the existing four fixed identities also anchor the namespace and recipe.
        var space = Guid.Parse("18b5cbb9-98ca-5999-b418-aa53c41f0ffb");
        var hash = SHA1.HashData([.. space.ToByteArray(bigEndian: true), .. Encoding.UTF8.GetBytes("unit:" + name)]);
        var bytes = hash.AsSpan(0, 16).ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        Assert.Equal(new Guid(bytes, bigEndian: true), identity);
        Assert.Equal(5, identity.Version);
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
