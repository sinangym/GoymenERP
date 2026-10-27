using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.PriceLists;

namespace GoymenERP.Product.UnitTests;

/// <summary>
/// MOD-02 (SAT-D9 / SAT-D10): the price tier chain — agreement &gt; customer-specific &gt; customer group &gt; company — over
/// published lists, the Europe/Istanbul business day around midnight UTC, inactive lists and company isolation. SENTETIK.
/// </summary>
public sealed class PriceTierChainTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherCompany = Guid.Parse("12121212-1212-4212-8212-121212121212");
    private static readonly Guid Product = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Kg = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Party = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Account = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid OtherParty = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid OtherAccount = Guid.Parse("88888888-8888-4888-8888-888888888888");
    private static readonly DateOnly Day = new(2026, 10, 1);
    private static readonly PriceTierCustomer Customer = new(Party, Account);

    [Fact]
    public void A_customer_specific_list_wins_over_the_company_list_and_the_lower_tiers_are_not_reached()
    {
        var company = Published("Şirket", 100m);
        var own = Published("Cari özel", 90m, PriceListScope.Customer(Party, Account));

        var result = Resolve([company, own], Customer);

        Assert.Equal((PriceTierChainOutcome.Resolved, (PriceTier?)PriceTier.Customer), (result.Outcome, result.MatchedTier));
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal((own.Id, 1, 90m, PriceTier.Customer), (candidate.List.Id, candidate.Version.VersionNo, candidate.Line.UnitPrice, candidate.Tier));
        Assert.Equal(
            [
                (PriceTier.Agreement, PriceTierStatus.Unavailable, (string?)PriceTierChain.AgreementOwnerMissing),
                (PriceTier.Customer, PriceTierStatus.Matched, (string?)null),
                (PriceTier.CustomerGroup, PriceTierStatus.NotReached, (string?)null),
                (PriceTier.Company, PriceTierStatus.NotReached, (string?)null),
            ],
            result.Tiers.Select(tier => (tier.Tier, tier.Status, tier.Reason)));
    }

    [Fact]
    public void Another_customers_list_never_answers_and_the_company_list_does()
    {
        var company = Published("Şirket", 100m);
        var others = Published("Başka cari", 80m, PriceListScope.Customer(OtherParty, OtherAccount));
        // Same account, another party: both identities must match.
        var mixed = Published("Karışık", 70m, PriceListScope.Customer(OtherParty, Account));

        var result = Resolve([others, mixed, company], Customer);

        Assert.Equal((PriceTierChainOutcome.Resolved, (PriceTier?)PriceTier.Company), (result.Outcome, result.MatchedTier));
        Assert.Equal(company.Id, Assert.Single(result.Candidates).List.Id);
        Assert.Equal(
            [PriceTierStatus.Unavailable, PriceTierStatus.NoMatch, PriceTierStatus.Unavailable, PriceTierStatus.Matched],
            result.Tiers.Select(tier => tier.Status));
        Assert.Equal(PriceTierChain.CustomerGroupOwnerMissing, result.Tiers[2].Reason);
    }

    [Fact]
    public void Without_a_customer_the_customer_tier_is_not_requested_and_no_customer_list_answers()
    {
        var company = Published("Şirket", 100m);
        var own = Published("Cari özel", 90m, PriceListScope.Customer(Party, Account));

        var result = Resolve([own, company], customer: null);

        Assert.Equal(company.Id, Assert.Single(result.Candidates).List.Id);
        Assert.Equal((PriceTierStatus.NotRequested, PriceTierChain.CustomerNotGiven), (result.Tiers[1].Status, result.Tiers[1].Reason));
    }

    [Fact]
    public void Several_lists_in_the_answering_tier_are_ambiguous_none_is_picked_and_a_lower_tier_never_breaks_the_tie()
    {
        var first = Published("A şirket", 100m);
        var second = Published("B şirket", 95m);

        var ambiguous = Resolve([second, first], Customer);
        Assert.Equal((PriceTierChainOutcome.Ambiguous, (PriceTier?)PriceTier.Company), (ambiguous.Outcome, ambiguous.MatchedTier));
        Assert.Equal([first.Id, second.Id], ambiguous.Candidates.Select(candidate => candidate.List.Id));
        Assert.Equal(PriceTierStatus.Ambiguous, ambiguous.Tiers[3].Status);

        // Two company lists do not matter once the customer's own list answers.
        var own = Published("Cari özel", 90m, PriceListScope.Customer(Party, Account));
        var resolved = Resolve([second, first, own], Customer);
        Assert.Equal((PriceTierChainOutcome.Resolved, own.Id), (resolved.Outcome, Assert.Single(resolved.Candidates).List.Id));

        // Two of the customer's own lists are ambiguous at the customer tier; the company list is not reached.
        var ownSecond = Published("Cari özel 2", 85m, PriceListScope.Customer(Party, Account));
        var ownAmbiguous = Resolve([first, own, ownSecond], Customer);
        Assert.Equal((PriceTierChainOutcome.Ambiguous, (PriceTier?)PriceTier.Customer), (ownAmbiguous.Outcome, ownAmbiguous.MatchedTier));
        Assert.Equal(PriceTierStatus.NotReached, ownAmbiguous.Tiers[3].Status);
    }

    [Fact]
    public void No_tier_pricing_the_line_is_no_price_never_a_zero_or_a_default()
    {
        var result = Resolve([Published("Şirket", 100m)], Customer, product: Guid.NewGuid());

        Assert.Equal((PriceTierChainOutcome.NoPrice, (PriceTier?)null), (result.Outcome, result.MatchedTier));
        Assert.Empty(result.Candidates);
        Assert.Equal(
            [PriceTierStatus.Unavailable, PriceTierStatus.NoMatch, PriceTierStatus.Unavailable, PriceTierStatus.NoMatch],
            result.Tiers.Select(tier => tier.Status));
        Assert.Equal(PriceTierChainOutcome.NoPrice, PriceTierChain.Resolve([], Company, Product, Kg, 1m, "TRY", Day, Customer).Outcome);
    }

    [Fact]
    public void Inactive_lists_never_answer_and_the_chain_falls_to_the_next_tier()
    {
        var company = Published("Şirket", 100m);
        var draftOnly = Draft("Taslak cari", 60m, PriceListScope.Customer(Party, Account));
        var ended = Published("Biten cari", 70m, PriceListScope.Customer(Party, Account), validFrom: Day.AddDays(-30), validThrough: Day.AddDays(-1));
        var planned = Published("Planlı cari", 80m, PriceListScope.Customer(Party, Account), validFrom: Day.AddDays(1));

        var result = Resolve([draftOnly, ended, planned, company], Customer);

        Assert.Equal((PriceTierChainOutcome.Resolved, company.Id), (result.Outcome, Assert.Single(result.Candidates).List.Id));
        Assert.Equal(PriceTierStatus.NoMatch, result.Tiers[1].Status);
        // The planned list answers from its own start; the ended one does not come back.
        Assert.Equal(planned.Id, Assert.Single(Resolve([draftOnly, ended, planned, company], Customer, on: Day.AddDays(1)).Candidates).List.Id);
    }

    [Fact]
    public void A_list_of_another_company_or_currency_or_outside_the_quantity_tier_never_answers()
    {
        var foreign = Published("Yabancı", 50m, PriceListScope.Customer(Party, Account), company: OtherCompany);
        var dollar = Published("Dolar", 40m, PriceListScope.Customer(Party, Account), currency: "USD");
        var company = Published("Şirket", 100m, upperQuantity: 10m);

        var result = Resolve([foreign, dollar, company], Customer);
        Assert.Equal(company.Id, Assert.Single(result.Candidates).List.Id);
        Assert.Equal(PriceTierStatus.NoMatch, result.Tiers[1].Status);

        // The quantity tier is lower-inclusive / upper-exclusive; past it nothing answers (no adjacent tier).
        Assert.Equal(PriceTierChainOutcome.NoPrice, Resolve([foreign, dollar, company], Customer, quantity: 10m).Outcome);
        Assert.Equal(PriceTierChainOutcome.NoPrice, PriceTierChain.Resolve([company], Guid.Empty, Product, Kg, 1m, "TRY", Day, Customer).Outcome);
    }

    [Theory]
    // 2026-10-31T20:59:59Z is 23:59:59 on 31 October in Istanbul (UTC+3); 21:00:00Z is already 1 November there.
    [InlineData("2026-10-31T20:59:59Z", "2026-10-31")]
    [InlineData("2026-10-31T21:00:00Z", "2026-11-01")]
    [InlineData("2026-11-01T00:30:00+03:00", "2026-11-01")]
    [InlineData("2026-11-01T20:59:59.9999999Z", "2026-11-01")]
    [InlineData("2026-11-01T21:00:00Z", "2026-11-02")]
    public void The_business_day_is_the_Istanbul_calendar_day_of_the_instant(string instant, string expected)
    {
        var day = PriceListBusinessDay.DateOf(DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), day);
        Assert.Equal(day, ProductPriceBusinessDayV2.DateOf(DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal("Europe/Istanbul", ProductPriceBusinessDayV2.TimeZoneId);
    }

    [Fact]
    public void Validity_boundaries_follow_the_Istanbul_day_around_midnight_UTC()
    {
        var november = new DateOnly(2026, 11, 1);
        var list = Published("Kasım", 100m, validFrom: november, validThrough: november);

        // 20:59:59Z on 31 October is still 31 October in Istanbul: not yet valid. A UTC reading would agree here …
        Assert.Equal(PriceTierChainOutcome.NoPrice, ResolveAt([list], "2026-10-31T20:59:59Z").Outcome);
        // … but at 21:00Z (UTC still 31 October) Istanbul is on 1 November and the version answers.
        Assert.Equal(new DateOnly(2026, 10, 31), DateOnly.FromDateTime(DateTimeOffset.Parse("2026-10-31T21:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture).UtcDateTime));
        Assert.Equal(PriceTierChainOutcome.Resolved, ResolveAt([list], "2026-10-31T21:00:00Z").Outcome);
        // The inclusive end: valid through the last Istanbul second of 1 November, gone at 21:00Z (UTC still 1 November).
        Assert.Equal(PriceTierChainOutcome.Resolved, ResolveAt([list], "2026-11-01T20:59:59Z").Outcome);
        Assert.Equal(PriceTierChainOutcome.NoPrice, ResolveAt([list], "2026-11-01T21:00:00Z").Outcome);
    }

    [Fact]
    public void Publication_today_is_the_Istanbul_day_so_a_start_that_is_today_in_UTC_can_already_be_yesterday()
    {
        var at = DateTimeOffset.Parse("2026-10-31T21:30:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var today = PriceListBusinessDay.DateOf(at);
        Assert.Equal(new DateOnly(2026, 11, 1), today);

        var backdated = PriceList.Create(Guid.NewGuid(), Company, "Geri tarihli", "TRY", PriceListChannel.Sales, PriceListVatBasis.ExcludingVat,
            Guid.NewGuid(), new DateOnly(2026, 10, 31), null, [Line(100m)], "preparer", at);
        Assert.Equal(PriceListRuleCodes.ValidFromInPast,
            Assert.Throws<PriceListRuleException>(() => backdated.Publish(1, 1, today, "publisher", at)).Code);

        var onTime = PriceList.Create(Guid.NewGuid(), Company, "Zamanında", "TRY", PriceListChannel.Sales, PriceListVatBasis.ExcludingVat,
            Guid.NewGuid(), today, null, [Line(100m)], "preparer", at);
        onTime.Publish(1, 1, today, "publisher", at);
        Assert.Equal(PriceListVersionStatus.Active, onTime.StatusOf(onTime.Versions[0], today));
    }

    [Fact]
    public void A_list_scope_is_company_or_one_customer_and_tiers_without_an_owner_master_are_refused()
    {
        Assert.Same(PriceListScope.Company, PriceListScope.From(PriceListScopeKind.Company, null, null));
        Assert.True(PriceListScope.From(PriceListScopeKind.Customer, Party, Account).IsCustomer(Party, Account));
        Assert.False(PriceListScope.From(PriceListScopeKind.Customer, Party, Account).IsCustomer(Party, OtherAccount));
        Assert.Equal(PriceListRuleCodes.InvalidScope,
            Assert.Throws<PriceListRuleException>(() => PriceListScope.From(PriceListScopeKind.Company, Party, null)).Code);
        Assert.Equal(PriceListRuleCodes.InvalidScope,
            Assert.Throws<PriceListRuleException>(() => PriceListScope.From(PriceListScopeKind.Customer, Party, null)).Code);
        Assert.Equal(PriceListRuleCodes.InvalidScope,
            Assert.Throws<PriceListRuleException>(() => PriceListScope.Customer(Guid.Empty, Account)).Code);
        Assert.Equal(PriceListRuleCodes.ScopeUnavailable,
            Assert.Throws<PriceListRuleException>(() => PriceListScope.From(PriceListScopeKind.CustomerGroup, Guid.NewGuid(), null)).Code);
        Assert.Equal(PriceListRuleCodes.ScopeUnavailable,
            Assert.Throws<PriceListRuleException>(() => PriceListScope.From(PriceListScopeKind.Agreement, null, null)).Code);
        Assert.Equal(PriceListRuleCodes.InvalidScope,
            Assert.Throws<PriceListRuleException>(() => PriceListScope.From((PriceListScopeKind)9, null, null)).Code);

        var list = Published("Cari özel", 90m, PriceListScope.Customer(Party, Account));
        var restored = PriceList.Restore(list.Id, list.CompanyId, list.Name, list.CurrencyCode, list.Channel, list.VatBasis, list.Version,
            list.CreatedBy, list.CreatedAtUtc, list.UpdatedAtUtc, list.Versions, list.Scope);
        Assert.Equal(list.Scope, restored.Scope);
        Assert.Equal(PriceListScope.Company, PriceList.Restore(list.Id, list.CompanyId, list.Name, list.CurrencyCode, list.Channel,
            list.VatBasis, list.Version, list.CreatedBy, list.CreatedAtUtc, list.UpdatedAtUtc, list.Versions).Scope);
    }

    [Fact]
    public void The_V2_contract_values_mirror_the_domain_chain()
    {
        Assert.Equal(Enum.GetValues<PriceTier>().Select(value => ((int)value, value.ToString())),
            Enum.GetValues<ProductPriceTierV2>().Select(value => ((int)value, value.ToString())));
        Assert.Equal(Enum.GetValues<PriceTierStatus>().Select(value => ((int)value, value.ToString())),
            Enum.GetValues<ProductPriceTierStatusV2>().Select(value => ((int)value, value.ToString())));
        Assert.Equal(Enum.GetValues<PriceTierChainOutcome>().Select(value => ((int)value, value.ToString())),
            Enum.GetValues<ProductPriceResolutionOutcomeV2>().Select(value => ((int)value, value.ToString())));
    }

    private static PriceTierChainResult Resolve(IEnumerable<PriceList> lists, PriceTierCustomer? customer, Guid? product = null,
        decimal quantity = 1m, DateOnly? on = null) =>
        PriceTierChain.Resolve(lists, Company, product ?? Product, Kg, quantity, "TRY", on ?? Day, customer);

    private static PriceTierChainResult ResolveAt(IEnumerable<PriceList> lists, string instant) =>
        Resolve(lists, Customer, on: PriceListBusinessDay.DateOf(DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture)));

    private static PriceList Published(string name, decimal price, PriceListScope? scope = null, DateOnly? validFrom = null,
        DateOnly? validThrough = null, Guid? company = null, string currency = "TRY", decimal? upperQuantity = null)
    {
        var start = validFrom ?? Day;
        var now = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var list = PriceList.Create(Guid.NewGuid(), company ?? Company, name, currency, PriceListChannel.Sales, PriceListVatBasis.ExcludingVat,
            Guid.NewGuid(), start, validThrough, [Line(price, upperQuantity)], "preparer", now, scope);
        list.Publish(1, 1, start, "publisher", now);
        return list;
    }

    private static PriceList Draft(string name, decimal price, PriceListScope scope) =>
        PriceList.Create(Guid.NewGuid(), Company, name, "TRY", PriceListChannel.Sales, PriceListVatBasis.ExcludingVat, Guid.NewGuid(), Day, null,
            [Line(price)], "preparer", new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), scope);

    private static PriceListLineInput Line(decimal price, decimal? upperQuantity = null) =>
        new(Guid.NewGuid(), Product, Kg, price, null, 0m, upperQuantity);
}
