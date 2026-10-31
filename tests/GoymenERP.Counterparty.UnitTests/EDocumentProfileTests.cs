using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;

namespace GoymenERP.Counterparty.UnitTests;

/// <summary>
/// SAT-D102 / SAT-D119: the VKN-keyed, group-shared customer e-belge profile (domain), and the one rule that resolves
/// the effective scenario (override if listed → Settings default → Unresolved). The codes are synthetic in the
/// resolver cases; the real Settings list is SAT-D120's standard set.
/// </summary>
public sealed class EDocumentProfileTests
{
    private const string Vkn = "1234567890";
    private static readonly DateTimeOffset CheckedAt = new(2026, 10, 31, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_profile_is_Unknown_at_version_zero_and_the_first_change_moves_it_to_one()
    {
        var profile = NewProfile();

        Assert.Equal(Vkn, profile.TaxIdentityNumber);
        Assert.Equal(EDocumentStatus.Unknown, profile.EDocumentStatus);
        Assert.Null(profile.EDocumentStatusCheckedAtUtc);
        Assert.Null(profile.EDocumentStatusCheckedByUserId);
        Assert.Null(profile.EInvoiceScenarioOverrideCode);
        // Version 0: a profile is only persisted after its first real change (then version 1).
        Assert.Equal(0, profile.Version);
        Assert.True(profile.SetScenarioOverride("TEMELFATURA"));
        Assert.Equal(1, profile.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    [InlineData("12345678901234")]
    [InlineData("12345abcde")]
    public void An_invalid_tax_identity_number_is_refused(string? vkn) =>
        Assert.ThrowsAny<ArgumentException>(() => new CustomerEDocumentProfile(Guid.NewGuid(), vkn!));

    [Fact]
    public void A_known_status_records_the_check_and_every_recheck_moves_the_version()
    {
        var profile = NewProfile();

        Assert.True(profile.RecordStatus(EDocumentStatus.EInvoiceTaxpayer, " checker-1 ", CheckedAt));
        Assert.Equal(EDocumentStatus.EInvoiceTaxpayer, profile.EDocumentStatus);
        Assert.Equal(CheckedAt, profile.EDocumentStatusCheckedAtUtc);
        Assert.Equal("checker-1", profile.EDocumentStatusCheckedByUserId);
        Assert.Equal(1, profile.Version);

        Assert.True(profile.RecordStatus(EDocumentStatus.EInvoiceTaxpayer, "checker-2", CheckedAt.AddDays(1)));
        Assert.Equal("checker-2", profile.EDocumentStatusCheckedByUserId);
        Assert.Equal(2, profile.Version);
    }

    [Fact]
    public void Reset_to_Unknown_clears_the_check_and_Unknown_to_Unknown_is_a_no_op()
    {
        var profile = NewProfile();
        Assert.False(profile.RecordStatus(EDocumentStatus.Unknown, "checker", CheckedAt));
        Assert.Equal(0, profile.Version);

        profile.RecordStatus(EDocumentStatus.EArchive, "checker", CheckedAt);
        Assert.True(profile.RecordStatus(EDocumentStatus.Unknown, "checker", CheckedAt));
        Assert.Equal(EDocumentStatus.Unknown, profile.EDocumentStatus);
        Assert.Null(profile.EDocumentStatusCheckedAtUtc);
        Assert.Null(profile.EDocumentStatusCheckedByUserId);
        Assert.Equal(2, profile.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_known_status_without_a_checker_is_refused(string? checker) =>
        Assert.Throws<ArgumentException>(
            () => NewProfile().RecordStatus(EDocumentStatus.EInvoiceTaxpayer, checker!, CheckedAt));

    [Fact]
    public void An_undefined_status_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NewProfile().RecordStatus((EDocumentStatus)7, "checker", CheckedAt));

    [Fact]
    public void The_override_is_set_cleared_and_an_unchanged_value_is_a_no_op()
    {
        var profile = NewProfile();

        Assert.True(profile.SetScenarioOverride("TICARIFATURA"));
        Assert.Equal("TICARIFATURA", profile.EInvoiceScenarioOverrideCode);
        Assert.False(profile.SetScenarioOverride("TICARIFATURA"));
        Assert.Equal(1, profile.Version);
        Assert.True(profile.SetScenarioOverride(null));
        Assert.Null(profile.EInvoiceScenarioOverrideCode);
        Assert.False(profile.SetScenarioOverride(null));
        Assert.Equal(2, profile.Version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("code_a")]
    [InlineData(" CODE")]
    [InlineData("9CODE")]
    [InlineData("TİCARİ")]
    [InlineData("CODE-A")]
    public void A_malformed_override_is_refused(string code) =>
        Assert.Throws<ArgumentException>(() => NewProfile().SetScenarioOverride(code));

    [Fact]
    public void A_33_character_override_is_refused_and_32_is_kept()
    {
        Assert.Throws<ArgumentException>(() => NewProfile().SetScenarioOverride(new string('A', 33)));
        Assert.True(NewProfile().SetScenarioOverride(new string('A', 32)));
    }

    [Fact]
    public void A_listed_override_wins_over_the_default()
    {
        var resolution = Resolve("CODE_A", Set, ["CODE_A", "CODE_B"], Set, "CODE_B");

        Assert.Equal(CounterpartyEInvoiceScenarioOutcomeV1.Resolved, resolution.Outcome);
        Assert.Equal(CounterpartyEInvoiceScenarioSourceV1.CustomerOverride, resolution.Source);
        Assert.Equal("CODE_A", resolution.ScenarioCode);
    }

    [Fact]
    public void Without_an_override_the_default_applies()
    {
        var resolution = Resolve(null, Set, ["CODE_A", "CODE_B"], Set, "CODE_B");

        Assert.Equal(CounterpartyEInvoiceScenarioOutcomeV1.Resolved, resolution.Outcome);
        Assert.Equal(CounterpartyEInvoiceScenarioSourceV1.SettingsDefault, resolution.Source);
        Assert.Equal("CODE_B", resolution.ScenarioCode);
    }

    [Fact]
    public void Without_an_override_and_an_empty_default_it_is_unresolved_never_a_guess()
    {
        var resolution = Resolve(null, Set, ["CODE_A"], NotSet, null);

        Assert.Equal(CounterpartyEInvoiceScenarioOutcomeV1.Unresolved, resolution.Outcome);
        Assert.Equal(CounterpartyEInvoiceScenarioSourceV1.None, resolution.Source);
        Assert.Null(resolution.ScenarioCode);
        Assert.Equal(CounterpartyEInvoiceScenarioUnresolvedReasonV1.SettingsDefaultNotSet, resolution.UnresolvedReason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_override_the_list_no_longer_carries_is_unresolved_and_does_not_fall_back(bool listExists)
    {
        var resolution = listExists
            ? Resolve("CODE_A", Set, ["CODE_B"], Set, "CODE_B")
            : Resolve("CODE_A", NotSet, [], Set, "CODE_B");

        Assert.Equal(CounterpartyEInvoiceScenarioOutcomeV1.Unresolved, resolution.Outcome);
        Assert.Equal(CounterpartyEInvoiceScenarioSourceV1.CustomerOverride, resolution.Source);
        Assert.Null(resolution.ScenarioCode);
        Assert.Equal(CounterpartyEInvoiceScenarioUnresolvedReasonV1.CustomerOverrideNotListed, resolution.UnresolvedReason);
    }

    [Fact]
    public void Settings_that_cannot_be_read_make_the_scenario_unavailable()
    {
        Assert.Equal(CounterpartyEInvoiceScenarioOutcomeV1.Unavailable,
            Resolve("CODE_A", Unavailable, [], Set, "CODE_B").Outcome);
        Assert.Equal(CounterpartyEInvoiceScenarioOutcomeV1.Unavailable,
            Resolve(null, Set, ["CODE_B"], Unavailable, null).Outcome);
    }

    [Fact]
    public void The_permission_is_the_registered_company_wide_name_and_not_the_basic_edit()
    {
        Assert.Equal(ErpPermissions.CounterpartyAccountsEDocumentUpdate, CounterpartyEDocumentPermissionsV1.Update);
        Assert.NotEqual(ErpPermissions.CounterpartyPartiesUpdateBasic, CounterpartyEDocumentPermissionsV1.Update);
        Assert.True(ErpPermissions.IsKnown(CounterpartyEDocumentPermissionsV1.Update));
        Assert.True(ErpPermissions.RequiresCompanyWideScope(CounterpartyEDocumentPermissionsV1.Update));
    }

    private const CounterpartySettingsSourceStateV1 Set = CounterpartySettingsSourceStateV1.Set;
    private const CounterpartySettingsSourceStateV1 NotSet = CounterpartySettingsSourceStateV1.NotSet;
    private const CounterpartySettingsSourceStateV1 Unavailable = CounterpartySettingsSourceStateV1.Unavailable;

    private static CounterpartyEInvoiceScenarioResolutionV1 Resolve(
        string? overrideCode,
        CounterpartySettingsSourceStateV1 listState,
        string[] codes,
        CounterpartySettingsSourceStateV1 defaultState,
        string? defaultCode) =>
        CounterpartyEInvoiceScenarioResolverV1.Resolve(overrideCode, listState, codes, defaultState, defaultCode);

    private static CustomerEDocumentProfile NewProfile() => new(Guid.NewGuid(), Vkn);
}
