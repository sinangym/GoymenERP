using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// KIM-S26 (FNS-D48): the expense payment uses its own money-operation key, <c>finance.expense-payment.v1</c>, managed
/// apart from the manual bank payment's <c>finance.manual-bank-payment.v1</c>. No approved profile names the key yet
/// (its values are not chosen by Settings), so every read for it stays fail-closed and is never answered from the
/// manual bank payment's profile; a reviewed version that names it answers it with that version's own values.
/// </summary>
public sealed class ApprovedMoneyProfileExpensePaymentKeyTests
{
    // The Finance owner's key (FNS-D48). Settings references no owner module; the literal is the decision's text.
    private const string ExpensePayment = "finance.expense-payment.v1";
    private const string ManualBankPayment = "finance.manual-bank-payment.v1";
    private static readonly DateOnly AfterEveryApproval = new(2026, 11, 15);

    [Theory]
    [InlineData("gdc")]
    [InlineData("gsp")]
    [InlineData("gin")]
    public async Task The_expense_payment_key_has_no_approved_profile_and_is_unavailable_in_every_seed_company(string company)
    {
        var failure = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => Provider(Company(company)).GetMoneyProfileAsync(
                SettingsCurrencyIdentitiesV1.TurkishLira, ExpensePayment, AfterEveryApproval));

        // Not "not found" and never a defaulted profile: nothing is approved to ask (fail-closed).
        Assert.Equal("settings_provider_unavailable", failure.Code);
    }

    [Theory]
    [InlineData("gdc")]
    [InlineData("gsp")]
    [InlineData("gin")]
    public async Task The_expense_payment_key_lists_no_currency_candidate_in_any_seed_company(string company)
    {
        ISettingsMoneyCandidateReaderV2 reader = Provider(Company(company));

        var failure = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => reader.GetMoneyCurrencyCandidatesAsync(ExpensePayment));

        Assert.Equal("settings_provider_unavailable", failure.Code);
    }

    [Fact]
    public async Task The_expense_payment_key_is_not_borrowed_from_the_manual_bank_payment_profile()
    {
        // FNS-D48: the two keys are managed apart. GDC's manual bank payment keeps its approved answer, and that
        // answer is never handed out for the expense payment key.
        Assert.DoesNotContain(ApprovedMoneyProfileCatalog.Approved.Entries, entry => entry.Operations.Contains(ExpensePayment));
        var provider = Provider(ApprovedUnitProfileCatalog.GdcCompanyId);

        var manual = await provider.GetMoneyProfileAsync(
            SettingsCurrencyIdentitiesV1.TurkishLira, ManualBankPayment, AfterEveryApproval);

        Assert.Equal(ManualBankPayment, manual.Operation);
        Assert.Equal(ApprovedMoneyProfileCatalog.GdcTurkishLiraV1.ProfileId, manual.Profile.ProfileId);
        Assert.Equal("settings_provider_unavailable", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetMoneyProfileAsync(
                SettingsCurrencyIdentitiesV1.TurkishLira, ExpensePayment, AfterEveryApproval))).Code);
    }

    [Fact]
    public async Task The_unapproved_key_leaves_GDC_s_operation_less_candidates_as_they_were()
    {
        // The key is listed on no profile, so it does not narrow what "unrestricted" means: GDC's TRY bank-account
        // currency check (Finance FS0) keeps its single TRY candidate.
        ISettingsMoneyCandidateReader reader = Provider(ApprovedUnitProfileCatalog.GdcCompanyId);

        var candidates = await reader.GetMoneyCurrencyCandidatesAsync();

        Assert.Equal(
            new MoneyCurrencyCandidateV1(SettingsCurrencyIdentitiesV1.TurkishLira, "TRY", "TRY"),
            Assert.Single(candidates));
    }

    [Fact]
    public async Task Access_is_checked_before_the_expense_payment_key_is_looked_up()
    {
        var gdc = ApprovedUnitProfileCatalog.GdcCompanyId;
        var provider = new ApprovedSettingsPrerequisiteProvider(
            new Context(gdc), new Actor(ApprovedUnitProfileCatalog.GspCompanyId));

        var failure = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetMoneyProfileAsync(SettingsCurrencyIdentitiesV1.TurkishLira, ExpensePayment, AfterEveryApproval));

        Assert.Equal("settings_access_denied", failure.Code);
    }

    [Fact]
    public async Task A_reviewed_version_that_names_the_key_answers_it_with_its_own_values_only()
    {
        // The approved-profile flow: a new reviewed catalog version naming the key, with its OWN profile identity and
        // values. Every value below is synthetic test data, not an approved policy.
        var syntheticOwnProfile = ApprovedMoneyProfileCatalog.GdcTurkishLiraV1 with
        {
            Operations = new HashSet<string>(StringComparer.Ordinal) { ExpensePayment },
            ProfileId = Guid.Parse("6b1f3c2a-9d4e-4f7a-8c5b-1e2d3f4a5b6c"),
            Scale = 3,
            UnitPriceScales = null,
            Source = "SENTETIK test-only expense payment profile",
        };
        var gdc = ApprovedUnitProfileCatalog.GdcCompanyId;
        var provider = new ApprovedSettingsPrerequisiteProvider(new Context(gdc), new Actor(gdc),
            ApprovedUnitProfileCatalog.Approved, new ApprovedMoneyProfileCatalog([syntheticOwnProfile]));

        var profile = await provider.GetMoneyProfileAsync(
            SettingsCurrencyIdentitiesV1.TurkishLira, ExpensePayment, AfterEveryApproval);

        Assert.Equal((ExpensePayment, 3, (int?)null), (profile.Operation, profile.Scale, profile.UnitPriceScale));
        Assert.Equal(new ProfileVersionV1(syntheticOwnProfile.ProfileId, syntheticOwnProfile.Version), profile.Profile);
        // The manual bank payment is not listed on that version, so it does not inherit the expense payment's values.
        Assert.Equal("settings_provider_unavailable", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetMoneyProfileAsync(
                SettingsCurrencyIdentitiesV1.TurkishLira, ManualBankPayment, AfterEveryApproval))).Code);
    }

    private static Guid Company(string company) => company switch
    {
        "gdc" => ApprovedUnitProfileCatalog.GdcCompanyId,
        "gsp" => ApprovedUnitProfileCatalog.GspCompanyId,
        _ => ApprovedUnitProfileCatalog.GinCompanyId,
    };

    private static ApprovedSettingsPrerequisiteProvider Provider(Guid company) =>
        new(new Context(company), new Actor(company));

    private sealed class Context(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId => companyId;
        public void Set(Guid id) => throw new NotSupportedException();
    }

    private sealed class Actor(Guid allowedCompany) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => "synthetic-settings-actor";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { allowedCompany };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
