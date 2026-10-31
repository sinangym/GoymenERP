using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// SAT-D25 / SAT-D39: the Settings-owned Sales quote policies. The reviewed catalog is EMPTY on purpose — the list content and
/// the day count are Settings records Sinan enters (EK3/EK4) — so the served companies answer "not found" and nothing is a
/// code constant. Synthetic entries are used only for catalog validation and window behaviour.
/// </summary>
public sealed class ApprovedSalesQuotePolicyProviderTests
{
    private static readonly Guid Gdc = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Gsp = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid Gin = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private static readonly Guid Unserved = Guid.Parse("71000000-0000-4000-8000-0000000000ee");
    private static readonly Guid SyntheticProfile = Guid.Parse("71000000-0000-4000-8000-0000000000ab");
    private static readonly Guid SyntheticReason = Guid.Parse("71000000-0000-4000-8000-0000000000a1");
    private static readonly DateOnly From = new(2026, 10, 27);

    [Theory]
    [InlineData("10000000-0000-0000-0000-000000000001")]
    [InlineData("10000000-0000-0000-0000-000000000002")]
    [InlineData("10000000-0000-0000-0000-000000000003")]
    public async Task The_approved_catalog_serves_the_SAT_D14_companies_with_no_list_and_no_default_validity(string company)
    {
        var provider = Provider(Guid.Parse(company));

        Assert.Equal(SettingsPrerequisiteCodesV1.SalesPolicyNotFound,
            (await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => provider.GetSalesQuoteLossReasonsAsync(From))).Code);
        Assert.Equal(SettingsPrerequisiteCodesV1.SalesPolicyNotFound,
            (await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => provider.GetSalesQuoteDefaultValidityAsync(From))).Code);
        Assert.Contains(Guid.Parse(company), new[] { Gdc, Gsp, Gin });
    }

    [Fact]
    public async Task Access_comes_first_and_an_unserved_company_is_unavailable_never_empty()
    {
        var anonymous = new ApprovedSettingsSalesQuotePolicyProvider(new Context(Gdc, true), new Actor(false, null, Gdc));
        Assert.Equal("settings_access_denied",
            (await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => anonymous.GetSalesQuoteLossReasonsAsync(From))).Code);
        var otherMember = new ApprovedSettingsSalesQuotePolicyProvider(new Context(Gdc, true), new Actor(true, "x", Gsp));
        Assert.Equal("settings_access_denied",
            (await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => otherMember.GetSalesQuoteDefaultValidityAsync(From))).Code);
        Assert.Equal("settings_provider_unavailable",
            (await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => Provider(Unserved).GetSalesQuoteLossReasonsAsync(From))).Code);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider(Gdc).GetSalesQuoteLossReasonsAsync(From, cancelled.Token));
    }

    [Fact]
    public async Task A_listed_version_answers_with_its_entries_and_its_real_window_and_another_company_never_sees_it()
    {
        var catalog = new ApprovedSalesQuotePolicyCatalog([Gdc, Gsp], [List() with { EffectiveThrough = From.AddDays(9) }, List() with
        {
            ProfileId = Guid.Parse("71000000-0000-4000-8000-0000000000ac"), Version = 2, EffectiveFrom = From.AddDays(10),
            Reasons = [Reason() with { IsActive = false }],
        }], [Validity()]);

        var first = await Provider(Gdc, catalog).GetSalesQuoteLossReasonsAsync(From.AddDays(1));
        Assert.Equal((new ProfileVersionV1(SyntheticProfile, 1), true), (first.Profile, first.IsActive));
        Assert.Equal(new SalesQuoteLossReasonV1(SyntheticReason, "SENTETIK-DIGER", "Sentetik diğer", true, true), Assert.Single(first.Reasons));
        var second = await Provider(Gdc, catalog).GetSalesQuoteLossReasonsAsync(From.AddDays(20));
        Assert.Equal((2L, false), (second.Profile.Version, second.Reasons[0].IsActive));
        // A date before every window answers the first version with its real window: the consumer's own check refuses it.
        Assert.Equal(From, (await Provider(Gdc, catalog).GetSalesQuoteLossReasonsAsync(From.AddDays(-3))).EffectiveFrom);

        var validity = await Provider(Gdc, catalog).GetSalesQuoteDefaultValidityAsync(From);
        Assert.Equal((Gdc, 30), (validity.CompanyId, validity.ValidityDays));
        Assert.Equal(SettingsPrerequisiteCodesV1.SalesPolicyNotFound,
            (await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => Provider(Gsp, catalog).GetSalesQuoteLossReasonsAsync(From))).Code);
    }

    [Theory]
    [InlineData("no-reasons")]
    [InlineData("empty-reason-id")]
    [InlineData("blank-code")]
    [InlineData("long-label")]
    [InlineData("untrimmed-label")]
    [InlineData("duplicate-id")]
    [InlineData("duplicate-code")]
    [InlineData("blank-source")]
    [InlineData("zero-version")]
    [InlineData("overlap")]
    [InlineData("zero-days")]
    [InlineData("empty-company")]
    public void A_malformed_catalog_is_refused_as_a_whole(string fault)
    {
        var lists = new List<ApprovedSalesQuoteLossReasonListEntry> { List() };
        var validities = new List<ApprovedSalesQuoteDefaultValidityEntry> { Validity() };
        var served = new List<Guid> { Gdc };
        switch (fault)
        {
            case "no-reasons": lists[0] = List() with { Reasons = [] }; break;
            case "empty-reason-id": lists[0] = List() with { Reasons = [Reason() with { ReasonId = Guid.Empty }] }; break;
            case "blank-code": lists[0] = List() with { Reasons = [Reason() with { Code = " " }] }; break;
            case "long-label": lists[0] = List() with { Reasons = [Reason() with { Label = new string('x', 201) }] }; break;
            case "untrimmed-label": lists[0] = List() with { Reasons = [Reason() with { Label = " Sentetik" }] }; break;
            case "duplicate-id": lists[0] = List() with { Reasons = [Reason(), Reason() with { Code = "SENTETIK-2" }] }; break;
            case "duplicate-code": lists[0] = List() with { Reasons = [Reason(), Reason() with { ReasonId = Guid.NewGuid() }] }; break;
            case "blank-source": lists[0] = List() with { Source = " " }; break;
            case "zero-version": validities[0] = Validity() with { Version = 0 }; break;
            case "overlap": lists.Add(List() with { Version = 2, EffectiveFrom = From.AddDays(1) }); break;
            case "zero-days": validities[0] = Validity() with { ValidityDays = 0 }; break;
            case "empty-company": served.Add(Guid.Empty); break;
        }

        Assert.Equal("settings_profile_invalid",
            Assert.Throws<SettingsPrerequisiteException>(() => new ApprovedSalesQuotePolicyCatalog(served, lists, validities)).Code);
    }

    private static ApprovedSalesQuoteLossReasonEntry Reason() => new(SyntheticReason, "SENTETIK-DIGER", "Sentetik diğer", true, true);

    private static ApprovedSalesQuoteLossReasonListEntry List() => new(Gdc, SyntheticProfile, 1, [Reason()], true, From, null,
        "synthetic Settings unit test only");

    private static ApprovedSalesQuoteDefaultValidityEntry Validity() => new(Gdc, SyntheticProfile, 1, 30, true, From, null,
        "synthetic Settings unit test only");

    private static ApprovedSettingsSalesQuotePolicyProvider Provider(Guid company, ApprovedSalesQuotePolicyCatalog? catalog = null) =>
        catalog is null
            ? new(new Context(company, true), new Actor(true, "x", company))
            : new(new Context(company, true), new Actor(true, "x", company), catalog);

    private sealed class Context(Guid companyId, bool resolved) : ICompanyContext
    {
        public bool IsResolved => resolved;
        public Guid CompanyId => companyId;
        public void Set(Guid id) => throw new NotSupportedException();
    }

    private sealed class Actor(bool authenticated, string? userId, Guid allowedCompany) : ICurrentUser
    {
        public bool IsAuthenticated => authenticated;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { allowedCompany };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
