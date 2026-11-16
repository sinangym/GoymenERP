using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// API-98/B-14 (KIM-D49, KIM-D51): the reference list input rules check only the technical shape of a code and a name, and the
/// kind table opens exactly the expense type, service type, paint color and scrap kind lists; the operation type list stays
/// closed. The codes and names below are rule inputs, not entries of any company list.
/// </summary>
public sealed class ReferenceListRulesV1Tests
{
    [Theory]
    [InlineData("A")]
    [InlineData("9")]
    [InlineData("NAKLIYE")]
    [InlineData("K-01")]
    [InlineData("K_01.B")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ012345")]
    public void A_code_of_upper_case_letters_digits_and_separators_is_accepted_as_written(string code)
    {
        Assert.True(ReferenceListRulesV1.IsCode(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" A")]
    [InlineData("A ")]
    [InlineData("a")]
    [InlineData("Ab")]
    [InlineData("-A")]
    [InlineData(".A")]
    [InlineData("_A")]
    [InlineData("A B")]
    [InlineData("A/B")]
    [InlineData("İ")]
    [InlineData("Ç")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456")]
    public void Any_other_code_is_refused(string? code)
    {
        Assert.False(ReferenceListRulesV1.IsCode(code));
    }

    [Theory]
    [InlineData("Nakliye", "Nakliye")]
    [InlineData("  Gümrük  ", "Gümrük")]
    [InlineData("Boya - kırmızı", "Boya - kırmızı")]
    public void A_name_is_trimmed_and_otherwise_kept(string name, string expected)
    {
        Assert.True(ReferenceListRulesV1.TryNormalizeName(name, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A\tB")]
    [InlineData("A\nB")]
    public void A_blank_name_or_one_with_control_characters_is_refused(string? name)
    {
        Assert.False(ReferenceListRulesV1.TryNormalizeName(name, out var normalized));
        Assert.Equal(string.Empty, normalized);
    }

    [Fact]
    public void A_name_is_limited_to_the_published_length()
    {
        Assert.True(ReferenceListRulesV1.TryNormalizeName(
            new string('a', SettingsReferenceListInputLimitsV1.NameMaximumLength), out _));
        Assert.False(ReferenceListRulesV1.TryNormalizeName(
            new string('a', SettingsReferenceListInputLimitsV1.NameMaximumLength + 1), out _));
    }

    [Theory]
    [InlineData("Nakliye", true)]
    [InlineData(" Nakliye", false)]
    [InlineData("Nakliye ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_stored_name_is_well_formed_only_in_its_normalized_form(string? name, bool expected)
    {
        Assert.Equal(expected, ReferenceListRulesV1.IsStoredName(name));
    }

    [Fact]
    public void Exactly_the_expense_service_paint_color_and_scrap_kind_lists_are_open()
    {
        Assert.Equal(
            [
                SettingsReferenceListKindV1.ExpenseType,
                SettingsReferenceListKindV1.ServiceType,
                SettingsReferenceListKindV1.PaintColor,
                SettingsReferenceListKindV1.ScrapKind,
            ],
            SettingsReferenceListKindsV1.Enabled);
        Assert.False(SettingsReferenceListKindsV1.IsEnabled(SettingsReferenceListKindV1.OperationType));
        Assert.Null(SettingsReferenceListKindsV1.CodeOf(SettingsReferenceListKindV1.OperationType));
        Assert.Null(SettingsReferenceListKindsV1.SlugOf(SettingsReferenceListKindV1.OperationType));
        Assert.Null(SettingsReferenceListKindsV1.PermissionOf(SettingsReferenceListKindV1.OperationType));
        Assert.Null(SettingsReferenceListKindsV1.FromCode("OperationType"));
    }

    [Fact]
    public void Every_open_list_round_trips_its_stored_code_and_has_a_distinct_slug()
    {
        var slugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kind in SettingsReferenceListKindsV1.Enabled)
        {
            var code = Assert.IsType<string>(SettingsReferenceListKindsV1.CodeOf(kind));
            Assert.Equal(kind, SettingsReferenceListKindsV1.FromCode(code));
            Assert.True(slugs.Add(Assert.IsType<string>(SettingsReferenceListKindsV1.SlugOf(kind))));
        }

        Assert.Null(SettingsReferenceListKindsV1.FromCode("expensetype"));
        Assert.Null(SettingsReferenceListKindsV1.FromCode(null));
    }

    [Fact]
    public void The_purchasing_and_production_lists_each_have_their_own_registered_company_wide_permission()
    {
        Assert.Equal(SettingsReferenceListPermissionManifestV1.PurchasingListsManage,
            SettingsReferenceListKindsV1.PermissionOf(SettingsReferenceListKindV1.ExpenseType));
        Assert.Equal(SettingsReferenceListPermissionManifestV1.PurchasingListsManage,
            SettingsReferenceListKindsV1.PermissionOf(SettingsReferenceListKindV1.ServiceType));
        Assert.Equal(SettingsReferenceListPermissionManifestV1.ProductionListsManage,
            SettingsReferenceListKindsV1.PermissionOf(SettingsReferenceListKindV1.PaintColor));
        Assert.Equal(SettingsReferenceListPermissionManifestV1.ProductionListsManage,
            SettingsReferenceListKindsV1.PermissionOf(SettingsReferenceListKindV1.ScrapKind));

        Assert.Equal(ErpPermissions.SettingsReferenceListsManage, SettingsReferenceListPermissionManifestV1.PurchasingListsManage);
        Assert.Equal(ErpPermissions.SettingsProductionReferenceListsManage,
            SettingsReferenceListPermissionManifestV1.ProductionListsManage);
        foreach (var permission in new[]
                 {
                     SettingsReferenceListPermissionManifestV1.PurchasingListsManage,
                     SettingsReferenceListPermissionManifestV1.ProductionListsManage,
                 })
        {
            Assert.Contains(permission, ErpPermissions.All);
            Assert.Contains(permission, ErpPermissions.CompanyWideOnly);
            Assert.DoesNotContain(permission, ErpPermissions.Critical);
        }
    }
}
