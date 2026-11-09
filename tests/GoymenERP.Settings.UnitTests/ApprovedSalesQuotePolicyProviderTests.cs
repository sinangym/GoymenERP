using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// SAT-D25 / SAT-D39: the Settings-owned Sales quote policies. SAT-D234 (2026-11-09) decides the content of the reviewed
/// catalog: five standard loss reasons and a 15-day default validity for each SAT-D14 company. Synthetic entries are used only
/// for catalog validation and window behaviour.
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
    private static readonly DateOnly DecisionDate = new(2026, 11, 9);

    [Theory]
    [InlineData("10000000-0000-0000-0000-000000000001")]
    [InlineData("10000000-0000-0000-0000-000000000002")]
    [InlineData("10000000-0000-0000-0000-000000000003")]
    public async Task The_approved_catalog_serves_each_SAT_D14_company_the_five_SAT_D234_reasons_and_fifteen_days(string company)
    {
        var companyId = Guid.Parse(company);
        Assert.Contains(companyId, new[] { Gdc, Gsp, Gin });
        var provider = Provider(companyId);

        var list = await provider.GetSalesQuoteLossReasonsAsync(DecisionDate);
        Assert.Equal((companyId, 1L, true, DecisionDate, (DateOnly?)null),
            (list.CompanyId, list.Profile.Version, list.IsActive, list.EffectiveFrom, list.EffectiveThrough));
        Assert.Equal(
            [
                ("price_too_high", "Fiyat yüksek", false, true),
                ("delivery_not_suitable", "Termin uygun değil", false, true),
                ("competitor_chosen", "Rakip seçildi", false, true),
                ("customer_withdrew", "Müşteri vazgeçti", false, true),
                ("other", "Diğer", true, true),
            ],
            list.Reasons.Select(reason => (reason.Code, reason.Label, reason.RequiresExplanation, reason.IsActive)));

        var validity = await provider.GetSalesQuoteDefaultValidityAsync(DecisionDate.AddDays(30));
        Assert.Equal((companyId, 1L, 15, true, DecisionDate, (DateOnly?)null),
            (validity.CompanyId, validity.Profile.Version, validity.ValidityDays, validity.IsActive, validity.EffectiveFrom,
                validity.EffectiveThrough));
    }

    [Fact]
    public async Task The_SAT_D234_versions_have_their_own_ids_per_company_and_are_not_in_force_before_the_decision_date()
    {
        var lists = new List<SalesQuoteLossReasonListV1>();
        var validities = new List<SalesQuoteDefaultValidityPolicyV1>();
        foreach (var company in new[] { Gdc, Gsp, Gin })
        {
            lists.Add(await Provider(company).GetSalesQuoteLossReasonsAsync(DecisionDate));
            validities.Add(await Provider(company).GetSalesQuoteDefaultValidityAsync(DecisionDate));
        }

        var ids = lists.Select(list => list.Profile.ProfileId).Concat(validities.Select(validity => validity.Profile.ProfileId))
            .Concat(lists.SelectMany(list => list.Reasons.Select(reason => reason.ReasonId))).ToArray();
        Assert.Equal(3 + 3 + 15, ids.Length);
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, ids);

        // Asked before its window, the version comes back with its REAL window, so the Sales consumer's own date check refuses it.
        var early = await Provider(Gdc).GetSalesQuoteLossReasonsAsync(DecisionDate.AddDays(-1));
        Assert.True(early.EffectiveFrom > DecisionDate.AddDays(-1));
        Assert.Equal(new Guid("ba9544f6-a1ab-52f2-accd-1ffa79cccdbc"), ApprovedSalesQuotePolicyCatalog.GdcLossReasonsV1.ProfileId);
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
