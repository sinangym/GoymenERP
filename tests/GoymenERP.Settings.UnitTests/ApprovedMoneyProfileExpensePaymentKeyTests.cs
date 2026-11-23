using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// KIM-S26 (FNS-D48, FNS-D55): the expense payment uses its own money-operation key, <c>finance.expense-payment.v1</c>,
/// managed apart from the manual bank payment's <c>finance.manual-bank-payment.v1</c>. FNS-D55 chose its values — "ayrı
/// satır; değerleri manuel banka ödemesiyle aynıdır (GDC/GSP/GIN şirket profilleri ayrı, TRY 2 ondalık)" — so every seed
/// company's own approved TRY entry answers it (scale 2, no unit-price scale) without any human-authored profile, and the
/// key never opens another Finance key for GSP/GIN.
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
    public async Task The_expense_payment_key_is_answered_by_each_seed_company_s_own_approved_TRY_row(string company)
    {
        var companyId = Company(company);
        var profile = await Provider(companyId).GetMoneyProfileAsync(
            SettingsCurrencyIdentitiesV1.TurkishLira, ExpensePayment, AfterEveryApproval);

        // FNS-D55: the manual bank payment's values — TRY, scale 2 — on the company's own profile identity.
        Assert.Equal((companyId, ExpensePayment, "TRY", 2, (int?)null, true),
            (profile.CompanyId, profile.Operation, profile.CurrencyCode, profile.Scale, profile.UnitPriceScale, profile.IsActive));
        Assert.Equal(Entry(company).ProfileId, profile.Profile.ProfileId);
    }

    [Theory]
    [InlineData("gdc")]
    [InlineData("gsp")]
    [InlineData("gin")]
    public async Task The_expense_payment_key_lists_TRY_as_its_only_currency_candidate(string company)
    {
        ISettingsMoneyCandidateReaderV2 reader = Provider(Company(company));

        var candidates = await reader.GetMoneyCurrencyCandidatesAsync(ExpensePayment);

        Assert.Equal(SettingsCurrencyIdentitiesV1.TurkishLira, Assert.Single(candidates).CurrencyId);
    }

    [Fact]
    public async Task The_expense_payment_row_carries_the_manual_bank_payment_values_and_GSP_GIN_get_no_other_finance_key()
    {
        var gdc = Provider(ApprovedUnitProfileCatalog.GdcCompanyId);
        var manual = await gdc.GetMoneyProfileAsync(SettingsCurrencyIdentitiesV1.TurkishLira, ManualBankPayment, AfterEveryApproval);
        var expense = await gdc.GetMoneyProfileAsync(SettingsCurrencyIdentitiesV1.TurkishLira, ExpensePayment, AfterEveryApproval);
        Assert.Equal((manual.CurrencyCode, manual.Scale, manual.UnitPriceScale), (expense.CurrencyCode, expense.Scale, expense.UnitPriceScale));
        Assert.Equal((ManualBankPayment, ExpensePayment), (manual.Operation, expense.Operation));

        foreach (var company in new[] { ApprovedUnitProfileCatalog.GspCompanyId, ApprovedUnitProfileCatalog.GinCompanyId })
        {
            Assert.Equal("settings_provider_unavailable", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
                () => Provider(company).GetMoneyProfileAsync(
                    SettingsCurrencyIdentitiesV1.TurkishLira, ManualBankPayment, AfterEveryApproval))).Code);
            Assert.DoesNotContain(MoneyProfileAuthoringCatalogV1.Targets(company),
                target => target.Operation is MoneyProfileAuthoringCatalogV1.CreditPlanOperation
                    or MoneyProfileAuthoringCatalogV1.CashTransferOperation);
            Assert.Contains(MoneyProfileAuthoringCatalogV1.Targets(company), target => target.Operation == ExpensePayment);
        }
    }

    [Fact]
    public async Task The_expense_payment_key_leaves_GDC_s_operation_less_candidates_as_they_were()
    {
        // GDC lists every key, so it stays unrestricted: GDC's TRY bank-account currency check (Finance FS0) keeps its
        // single TRY candidate.
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

    private static ApprovedMoneyProfileEntry Entry(string company) => company switch
    {
        "gdc" => ApprovedMoneyProfileCatalog.GdcTurkishLiraV1,
        "gsp" => ApprovedMoneyProfileCatalog.GspTurkishLiraV1,
        _ => ApprovedMoneyProfileCatalog.GinTurkishLiraV1,
    };

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
