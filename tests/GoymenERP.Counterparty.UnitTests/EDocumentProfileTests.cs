using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;

namespace GoymenERP.Counterparty.UnitTests;

/// <summary>
/// SAT-D102: the e-belge status and the scenario override on the company account (domain), and the one rule that
/// resolves the effective scenario (override if listed → Settings default → Unresolved). The codes are synthetic: the
/// real list is Settings data and is empty today.
/// </summary>
public sealed class EDocumentProfileTests
{
    private static readonly Guid Company = Guid.Parse("8e1d5a2c-41f3-4c35-9a5b-6c8a0d4f2b11");
    private static readonly DateTimeOffset CheckedAt = new(2026, 10, 28, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_account_is_Unknown_with_no_check_and_no_override()
    {
        var account = NewAccount();

        Assert.Equal(EDocumentStatus.Unknown, account.EDocumentStatus);
        Assert.Null(account.EDocumentStatusCheckedAtUtc);
        Assert.Null(account.EDocumentStatusCheckedByUserId);
        Assert.Null(account.EInvoiceScenarioOverrideCode);
    }

    [Fact]
    public void A_known_status_records_the_check_and_every_recheck_moves_the_version()
    {
        var account = NewAccount();

        Assert.True(account.RecordEDocumentStatus(EDocumentStatus.EInvoiceTaxpayer, " checker-1 ", CheckedAt));
        Assert.Equal(EDocumentStatus.EInvoiceTaxpayer, account.EDocumentStatus);
        Assert.Equal(CheckedAt, account.EDocumentStatusCheckedAtUtc);
        Assert.Equal("checker-1", account.EDocumentStatusCheckedByUserId);
        Assert.Equal(2, account.Version);

        // The same value again is a new check (a later date and checker), not a no-op.
        Assert.True(account.RecordEDocumentStatus(EDocumentStatus.EInvoiceTaxpayer, "checker-2", CheckedAt.AddDays(1)));
        Assert.Equal("checker-2", account.EDocumentStatusCheckedByUserId);
        Assert.Equal(3, account.Version);

        Assert.True(account.RecordEDocumentStatus(EDocumentStatus.EArchive, "checker-2", CheckedAt.AddDays(2)));
        Assert.Equal(EDocumentStatus.EArchive, account.EDocumentStatus);
    }

    [Fact]
    public void Reset_to_Unknown_clears_the_check_and_Unknown_to_Unknown_is_a_no_op()
    {
        var account = NewAccount();
        Assert.False(account.RecordEDocumentStatus(EDocumentStatus.Unknown, "checker", CheckedAt));
        Assert.Equal(1, account.Version);

        account.RecordEDocumentStatus(EDocumentStatus.EArchive, "checker", CheckedAt);
        Assert.True(account.RecordEDocumentStatus(EDocumentStatus.Unknown, "checker", CheckedAt));
        Assert.Equal(EDocumentStatus.Unknown, account.EDocumentStatus);
        Assert.Null(account.EDocumentStatusCheckedAtUtc);
        Assert.Null(account.EDocumentStatusCheckedByUserId);
        Assert.Equal(3, account.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_known_status_without_a_checker_is_refused(string? checker) =>
        Assert.Throws<ArgumentException>(
            () => NewAccount().RecordEDocumentStatus(EDocumentStatus.EInvoiceTaxpayer, checker!, CheckedAt));

    [Fact]
    public void An_undefined_status_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NewAccount().RecordEDocumentStatus((EDocumentStatus)7, "checker", CheckedAt));

    [Fact]
    public void The_override_is_set_cleared_and_an_unchanged_value_is_a_no_op()
    {
        var account = NewAccount();

        Assert.True(account.SetEInvoiceScenarioOverride("CODE_A"));
        Assert.Equal("CODE_A", account.EInvoiceScenarioOverrideCode);
        Assert.False(account.SetEInvoiceScenarioOverride("CODE_A"));
        Assert.Equal(2, account.Version);
        Assert.True(account.SetEInvoiceScenarioOverride(null));
        Assert.Null(account.EInvoiceScenarioOverrideCode);
        Assert.False(account.SetEInvoiceScenarioOverride(null));
        Assert.Equal(3, account.Version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("code_a")]
    [InlineData(" CODE")]
    [InlineData("9CODE")]
    [InlineData("TİCARİ")]
    [InlineData("CODE-A")]
    public void A_malformed_override_is_refused(string code) =>
        Assert.Throws<ArgumentException>(() => NewAccount().SetEInvoiceScenarioOverride(code));

    [Fact]
    public void A_33_character_override_is_refused_and_32_is_kept()
    {
        Assert.Throws<ArgumentException>(() => NewAccount().SetEInvoiceScenarioOverride(new string('A', 33)));
        Assert.True(NewAccount().SetEInvoiceScenarioOverride(new string('A', 32)));
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
        var resolution = Resolve(null, NotSet, [], NotSet, null);

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

    private static CompanyPartyAccount NewAccount() =>
        new(Guid.NewGuid(), Company, Guid.NewGuid(), PartyRole.Customer);
}
