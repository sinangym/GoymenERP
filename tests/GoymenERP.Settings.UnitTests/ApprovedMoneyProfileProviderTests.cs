using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// T02-b: the real money-profile provider for the one approved profile, GDC/TRY (Sinan's Ö2, #66
/// 5722637822). Every value asserted here is traced to a source in
/// <see cref="ApprovedMoneyProfileCatalog.GdcTurkishLiraV1"/>; nothing is a fixture posing as data.
/// </summary>
public sealed class ApprovedMoneyProfileProviderTests
{
    private static readonly Guid Gdc = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Gsp = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid Gin = Guid.Parse("10000000-0000-0000-0000-000000000003");
    // Not a seed company: GDC, GSP and GIN all have approved TRY profiles since SAT-D18.
    private static readonly Guid Unserved = Guid.Parse("7d0c7c2e-2b0e-4d8e-9b2a-2f1f0c9a6e11");
    private static readonly DateOnly ExtraApproval = new(2026, 10, 26);
    private static readonly Guid Try = Guid.Parse("9e60dcf0-f4bd-5bca-b464-f9a2db98e31a");
    private static readonly DateOnly Approval = new(2026, 9, 18);
    private const string FinancePayment = "finance.manual-bank-payment.v1";
    private const string FinanceReceipt = "finance.manual-bank-receipt.v1";
    private const string SalesDraft = "sales.order-draft.v1";
    // ACC-V0a: the Accounting owner's key on the same approved profile (a journal amount is a final TRY amount, Ö2).
    private const string AccountingJournalAmount = "accounting.journal-amount.v1";

    [Theory]
    [InlineData(FinancePayment)]
    [InlineData(FinanceReceipt)]
    [InlineData(SalesDraft)]
    public async Task GDC_TRY_for_an_approved_operation_is_the_exact_approved_profile(string operation)
    {
        var profile = await Provider(Gdc).GetMoneyProfileAsync(Try, operation, Approval);

        Assert.Equal(Gdc, profile.CompanyId);
        Assert.Equal(Try, profile.CurrencyId);
        Assert.Equal("TRY", profile.CurrencyCode);
        // Answered for the operation asked: consumers check it, and another operation is another answer.
        Assert.Equal(operation, profile.Operation);
        Assert.Equal(new ProfileVersionV1(Guid.Parse("7f836492-a54f-512f-a8f4-5f556b3f0f9d"), 1), profile.Profile);
        // Ö2: final amount 2 decimals.
        Assert.Equal(2, profile.Scale);
        Assert.True(profile.IsActive);
        Assert.Equal(Approval, profile.EffectiveFrom);
        Assert.Null(profile.EffectiveThrough);
    }

    [Theory]
    [InlineData("2026-09-19")]
    [InlineData("2099-12-31")]
    public async Task The_open_ended_version_answers_every_later_date(string date)
    {
        var profile = await Provider(Gdc).GetMoneyProfileAsync(Try, FinanceReceipt, DateOnly.Parse(date));

        Assert.Equal(1, profile.Profile.Version);
        Assert.Null(profile.EffectiveThrough);
    }

    [Fact]
    public async Task A_date_before_approval_gets_the_real_window_so_the_consumer_rejects_it()
    {
        var dayBefore = Approval.AddDays(-1);

        var profile = await Provider(Gdc).GetMoneyProfileAsync(Try, SalesDraft, dayBefore);

        // Never widened to fit the question; Finance's draft read and Sales' reference read both
        // refuse a profile not in effect on their date.
        Assert.Equal(Approval, profile.EffectiveFrom);
        Assert.True(dayBefore < profile.EffectiveFrom);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("any other currency")]
    public async Task A_currency_without_an_approved_profile_is_not_found_distinct_from_unavailable(string currency)
    {
        // USD and EUR have no approved profile (Ö2 approves GDC/TRY only), so no identity is minted
        // for them here: any currency id other than TRY is the same answer.
        var currencyId = currency == "empty" ? Guid.Empty : Guid.Parse("0f3bd0a1-6e2c-4d77-a3a5-0b1c2d3e4f51");

        var failure = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => Provider(Gdc).GetMoneyProfileAsync(currencyId, FinanceReceipt, Approval));

        Assert.Equal(SettingsPrerequisiteCodesV1.MoneyProfileNotFound, failure.Code);
        Assert.Equal("settings_money_profile_not_found", failure.Code);
    }

    [Theory]
    [InlineData("synthetic-operation")]
    [InlineData("")]
    [InlineData("FINANCE.MANUAL-BANK-RECEIPT.V1")]
    [InlineData("finance.manual-bank-receipt.v2")]
    public async Task An_operation_no_approved_profile_answers_for_is_unavailable(string operation)
    {
        // Exact, ordinal: a near miss is not an approved key.
        var failure = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => Provider(Gdc).GetMoneyProfileAsync(Try, operation, Approval));

        Assert.Equal("settings_provider_unavailable", failure.Code);
    }

    [Fact]
    public async Task A_company_without_an_approved_money_source_is_unavailable_not_given_GDC_s_profile()
    {
        var failure = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => Provider(Unserved).GetMoneyProfileAsync(Try, SalesDraft, Approval));

        Assert.Equal("settings_provider_unavailable", failure.Code);
    }

    [Theory]
    [InlineData("10000000-0000-0000-0000-000000000002", "5b863ed2-e21a-5c43-b219-fa113dcc50b3")] // GSP
    [InlineData("10000000-0000-0000-0000-000000000003", "d78eee0d-4940-52e3-bd80-3b5ebdb6d760")] // GIN
    public async Task GSP_and_GIN_TRY_answers_the_Sales_draft_operation_from_SAT_D18(string company, string profileId)
    {
        var companyId = Guid.Parse(company);

        var profile = await Provider(companyId).GetMoneyProfileAsync(Try, SalesDraft, ExtraApproval);

        Assert.Equal(companyId, profile.CompanyId);
        Assert.Equal((Try, "TRY", SalesDraft), (profile.CurrencyId, profile.CurrencyCode, profile.Operation));
        // UUIDv5 name "money-profile:<company>:TRY:v1" in the money-profiles namespace.
        Assert.Equal(new ProfileVersionV1(Guid.Parse(profileId), 1), profile.Profile);
        Assert.Equal(2, profile.Scale);
        Assert.True(profile.IsActive);
        Assert.Equal(ExtraApproval, profile.EffectiveFrom);
        Assert.Null(profile.EffectiveThrough);
    }

    [Theory]
    [InlineData("gsp", AccountingJournalAmount)]
    [InlineData("gsp", FinancePayment)]
    [InlineData("gsp", FinanceReceipt)]
    [InlineData("gin", AccountingJournalAmount)]
    [InlineData("gin", FinancePayment)]
    [InlineData("gin", FinanceReceipt)]
    public async Task GSP_and_GIN_Accounting_and_Finance_operations_stay_unavailable(string company, string operation)
    {
        // SAT-D18: GSP/GIN's TRY profile covers only sales.order-draft.v1; GDC's Accounting and
        // Finance keys need a separate decision and are not borrowed from GDC.
        var failure = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => Provider(company == "gsp" ? Gsp : Gin).GetMoneyProfileAsync(Try, operation, ExtraApproval));

        Assert.Equal("settings_provider_unavailable", failure.Code);
    }

    [Fact]
    public void The_approved_money_catalog_holds_exactly_the_approved_profiles()
    {
        var entries = ApprovedMoneyProfileCatalog.Approved.Entries;

        Assert.Equal(3, entries.Count);
        Assert.Contains(ApprovedMoneyProfileCatalog.GdcTurkishLiraV1, entries);
        Assert.Contains(ApprovedMoneyProfileCatalog.GspTurkishLiraV1, entries);
        Assert.Contains(ApprovedMoneyProfileCatalog.GinTurkishLiraV1, entries);
        Assert.Equal([SalesDraft], ApprovedMoneyProfileCatalog.GspTurkishLiraV1.Operations.ToArray());
        Assert.Equal([SalesDraft], ApprovedMoneyProfileCatalog.GinTurkishLiraV1.Operations.ToArray());
    }

    [Theory]
    [InlineData("unauthenticated")]
    [InlineData("no actor id")]
    [InlineData("unresolved company")]
    [InlineData("not a member")]
    public async Task Access_is_checked_before_anything_about_the_source_is_revealed(string fault)
    {
        var provider = fault switch
        {
            "unauthenticated" => new ApprovedSettingsPrerequisiteProvider(new Context(Gdc, true), new Actor(false, "x", Gdc)),
            "no actor id" => new ApprovedSettingsPrerequisiteProvider(new Context(Gdc, true), new Actor(true, " ", Gdc)),
            "unresolved company" => new ApprovedSettingsPrerequisiteProvider(new Context(Gdc, false), new Actor(true, "x", Gdc)),
            _ => new ApprovedSettingsPrerequisiteProvider(new Context(Gdc, true), new Actor(true, "x", Gsp)),
        };

        var failure = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetMoneyProfileAsync(Try, FinanceReceipt, Approval));

        Assert.Equal("settings_access_denied", failure.Code);
    }

    [Fact]
    public async Task A_cancelled_caller_gets_no_profile()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Provider(Gdc).GetMoneyProfileAsync(Try, FinanceReceipt, Approval, cancellation.Token));
    }

    [Fact]
    public void GDC_s_approved_profile_keeps_its_owner_keys()
    {
        var entry = Assert.Single(ApprovedMoneyProfileCatalog.Approved.Entries, entry => entry.CompanyId == Gdc);
        Assert.Same(ApprovedMoneyProfileCatalog.GdcTurkishLiraV1, entry);
        Assert.Equal(new[] { FinancePayment, FinanceReceipt, SalesDraft, AccountingJournalAmount }.Order(StringComparer.Ordinal),
            entry.Operations.Order(StringComparer.Ordinal));
        Assert.Contains("5722637822", entry.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_identities_are_the_documented_ones()
    {
        // Documented UUIDv5 derivations (SettingsCurrencyIdentitiesV1, README); pinned so a change
        // is a reviewed contract change, never an accident.
        Assert.Equal(Try, SettingsCurrencyIdentitiesV1.TurkishLira);
        Assert.Equal(Guid.Parse("7f836492-a54f-512f-a8f4-5f556b3f0f9d"), ApprovedMoneyProfileCatalog.GdcTurkishLiraV1.ProfileId);
        Assert.Equal(Gdc, ApprovedMoneyProfileCatalog.GdcTurkishLiraV1.CompanyId);
    }

    [Theory]
    [InlineData("empty currency")]
    [InlineData("blank code")]
    [InlineData("no operation")]
    [InlineData("blank operation")]
    [InlineData("negative scale")]
    [InlineData("zero version")]
    [InlineData("window ends before it starts")]
    [InlineData("repeated version")]
    [InlineData("overlapping versions")]
    public void A_malformed_catalog_is_refused_whole_as_invalid(string malformation)
    {
        var good = ApprovedMoneyProfileCatalog.GdcTurkishLiraV1;
        ApprovedMoneyProfileEntry[] entries = malformation switch
        {
            "empty currency" => [good with { CurrencyId = Guid.Empty }],
            "blank code" => [good with { CurrencyCode = " " }],
            "no operation" => [good with { Operations = new HashSet<string>() }],
            "blank operation" => [good with { Operations = new HashSet<string> { " " } }],
            "negative scale" => [good with { Scale = -1 }],
            "zero version" => [good with { Version = 0 }],
            "window ends before it starts" => [good with { EffectiveThrough = good.EffectiveFrom.AddDays(-1) }],
            "repeated version" => [good with { EffectiveThrough = good.EffectiveFrom.AddDays(1) }, good with { EffectiveFrom = good.EffectiveFrom.AddDays(2) }],
            _ => [good, good with { Version = 2, EffectiveFrom = good.EffectiveFrom.AddDays(10) }],
        };

        var failure = Assert.Throws<SettingsPrerequisiteException>(() => new ApprovedMoneyProfileCatalog(entries));
        Assert.Equal("settings_profile_invalid", failure.Code);
    }

    [Fact]
    public async Task A_later_approved_version_takes_over_from_its_own_start_and_the_old_one_keeps_its_window()
    {
        var v1 = ApprovedMoneyProfileCatalog.GdcTurkishLiraV1 with { EffectiveThrough = new DateOnly(2027, 1, 31) };
        var v2 = v1 with { Version = 2, Scale = 3, EffectiveFrom = new DateOnly(2027, 2, 1), EffectiveThrough = null };
        var provider = new ApprovedSettingsPrerequisiteProvider(new Context(Gdc, true), new Actor(true, "x", Gdc),
            ApprovedUnitProfileCatalog.Approved, new ApprovedMoneyProfileCatalog([v1, v2]));

        Assert.Equal(1, (await provider.GetMoneyProfileAsync(Try, FinanceReceipt, new DateOnly(2027, 1, 31))).Profile.Version);
        Assert.Equal(2, (await provider.GetMoneyProfileAsync(Try, FinanceReceipt, new DateOnly(2027, 2, 1))).Profile.Version);
    }

    [Fact]
    public async Task GDC_s_candidates_are_exactly_its_approved_currency_labelled_by_its_code()
    {
        ISettingsMoneyCandidateReader reader = Provider(Gdc);

        var candidates = await reader.GetMoneyCurrencyCandidatesAsync();

        // No longer label is approved anywhere, so the code is the label; nothing is defaulted.
        Assert.Equal(new MoneyCurrencyCandidateV1(Try, "TRY", "TRY"), Assert.Single(candidates));
    }

    [Theory]
    [InlineData("gsp")]
    [InlineData("gin")]
    public async Task GSP_and_GIN_candidates_are_exactly_their_approved_TRY(string company)
    {
        ISettingsMoneyCandidateReader reader = Provider(company == "gsp" ? Gsp : Gin);

        var candidates = await reader.GetMoneyCurrencyCandidatesAsync();

        // Listed is not validity: an Accounting/Finance consumer still gets unavailable for TRY there.
        Assert.Equal(new MoneyCurrencyCandidateV1(Try, "TRY", "TRY"), Assert.Single(candidates));
    }

    [Fact]
    public async Task A_company_without_an_approved_money_source_gets_unavailable_not_an_empty_list()
    {
        ISettingsMoneyCandidateReader reader = Provider(Unserved);

        var failure = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => reader.GetMoneyCurrencyCandidatesAsync());

        Assert.Equal("settings_provider_unavailable", failure.Code);
    }

    [Theory]
    [InlineData("unauthenticated")]
    [InlineData("unresolved company")]
    [InlineData("not a member")]
    public async Task Candidates_check_access_first(string fault)
    {
        ISettingsMoneyCandidateReader reader = fault switch
        {
            "unauthenticated" => new ApprovedSettingsPrerequisiteProvider(new Context(Gdc, true), new Actor(false, "x", Gdc)),
            "unresolved company" => new ApprovedSettingsPrerequisiteProvider(new Context(Gdc, false), new Actor(true, "x", Gdc)),
            _ => new ApprovedSettingsPrerequisiteProvider(new Context(Gdc, true), new Actor(true, "x", Gsp)),
        };

        var failure = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => reader.GetMoneyCurrencyCandidatesAsync());

        Assert.Equal("settings_access_denied", failure.Code);
    }

    [Fact]
    public async Task A_cancelled_caller_gets_no_candidates()
    {
        ISettingsMoneyCandidateReader reader = Provider(Gdc);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.GetMoneyCurrencyCandidatesAsync(new CancellationToken(true)));
    }

    [Fact]
    public async Task A_currency_with_several_approved_versions_is_one_candidate()
    {
        var v1 = ApprovedMoneyProfileCatalog.GdcTurkishLiraV1 with { EffectiveThrough = new DateOnly(2027, 1, 31) };
        var v2 = v1 with { Version = 2, EffectiveFrom = new DateOnly(2027, 2, 1), EffectiveThrough = null };
        ISettingsMoneyCandidateReader reader = new ApprovedSettingsPrerequisiteProvider(
            new Context(Gdc, true), new Actor(true, "x", Gdc),
            ApprovedUnitProfileCatalog.Approved, new ApprovedMoneyProfileCatalog([v1, v2]));

        Assert.Single(await reader.GetMoneyCurrencyCandidatesAsync());
    }

    [Fact]
    public void Only_the_reviewed_catalog_is_reachable_from_outside_Settings()
    {
        Assert.Empty(typeof(ApprovedMoneyProfileCatalog).GetConstructors());
        Assert.Single(typeof(ApprovedSettingsPrerequisiteProvider).GetConstructors());
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
