using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// MOD-01 / API-06 over SYNTHETIC owners: the risk check reads open receivables, the credit limit and collateral from the
/// customer-finance owner's port, the approved-unshipped remainder from the customer's open approved orders and the delivery
/// owner's shipped totals, and "shipped but not invoiced" is "NoSource" (S5). Any part whose owner cannot answer makes the result
/// "Unavailable" — never "Within"; amounts cross only with the price read; the read writes nothing and is fenced before, after
/// the draft and after each owner.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private static readonly Guid OtherParty = Guid.Parse("3a3a3a3a-3a3a-4a3a-8a3a-3a3a3a3a3a3a");

    [Fact]
    public async Task The_risk_check_reports_every_part_with_its_source_and_never_says_within_while_a_part_has_no_source()
    {
        var world = new World();
        // An approved order of the customer: 1.25 kg at 12.50 − 5 % = 11.875 net; 0.25 kg shipped → 1 kg left = 11.875 → 11.88.
        var approved = await PricedApprovedAsync(world, "a");
        var line = approved.Lines[0].LineId;
        var deliveries = new FakeDeliveries(order => new(Company, order, [new(line, KgUnit, 0.25m, 0.25m)]));
        world.Deliveries = deliveries;
        var partyRisk = new FakePartyRisk(FinanceLike([new(Currency, 100m)]));
        world.PartyRisk = partyRisk;
        // Another customer's order and this customer's unapproved order never enter the remainder.
        await world.Service().CreateDraftAsync(Priced() with { PartyId = OtherParty }, Metadata("k-other"));
        await world.Service().CreateDraftAsync(Priced(), Metadata("k-unapproved"));
        var (attempts, envelopes, transitions) = (world.Store.Attempts, world.Store.Envelopes.Count, world.Store.Transitions.Count);
        world.Authority.Measured.Clear();

        var check = await world.Service().GetDraftRiskCheckAsync(approved.Draft.Id);

        Assert.Equal(("4", Party, Account, Currency), (check.DraftVersion, check.PartyId, check.CompanyAccountId, check.CurrencyId));
        Assert.Equal(("Unavailable", SalesPriceDisclosure.Visible, (string?)null), (check.Result, check.AmountDisclosure, check.GrossRisk));
        Assert.Equal(
            [
                new SalesDraftRiskReasonV1("ShippedNotInvoiced", SalesDraftRiskReasonCodesV1.InvoicingOwnerMissing),
                new SalesDraftRiskReasonV1("Check", "tax_basis_mismatch"),
                new SalesDraftRiskReasonV1("CreditLimit", "finance_credit_limit_owner_missing"),
            ],
            check.Reasons);
        Assert.Equal(
            [
                ("OpenReceivables", "finance.open-items", "Available", "IncludingTax", "100.00", null),
                ("ShippedNotInvoiced", SalesDraftRiskSourcesV1.Invoicing, "NoSource", null, null, SalesDraftRiskReasonCodesV1.InvoicingOwnerMissing),
                ("ApprovedUnshippedRemainder", SalesDraftRiskSourcesV1.ApprovedOrderRemainders, "Available", "ExcludingTax", "11.88", null),
            ],
            check.Components.Select(Flat));
        Assert.Equal(("CreditLimit", "finance.party-credit-limit", "NoSource", null, null, "finance_credit_limit_owner_missing"), Flat(check.CreditLimit));
        Assert.Equal(("Collateral", "finance.party-collateral", "NoSource", null, null, "finance_collateral_owner_missing"), Flat(check.Collateral));

        // The owners were asked for this customer and its one open approved order only.
        Assert.Equal([(Party, Account)], partyRisk.Asked);
        Assert.Equal([approved.Draft.Id], deliveries.Asked);
        Assert.Equal([(Party, Account, SalesDraftService.MaximumRiskOrders + 1)], world.Store.OpenApprovedOrderReads);
        // The draft read before, after the draft, after the finance owner, after the order list and after the delivery owner;
        // then the price read, last.
        Assert.Equal([ReadPermission, ReadPermission, ReadPermission, ReadPermission, ReadPermission, PriceReadPermission],
            world.Authority.Measured);
        // Read-only.
        Assert.Equal((attempts, envelopes, transitions), (world.Store.Attempts, world.Store.Envelopes.Count, world.Store.Transitions.Count));
        Assert.Empty(world.Store.Refusals);

        // Fully shipped: the line adds nothing (its shipped part is the not-yet-invoiced stage's), a recorded zero.
        deliveries.Answer = order => new(Company, order, [new(line, KgUnit, 1.25m, 1m)]);
        var shipped = await world.Service().GetDraftRiskCheckAsync(approved.Draft.Id);
        Assert.Equal("0.00", Assert.Single(shipped.Components[2].Amounts!).Amount);
    }

    [Fact]
    public async Task Without_the_price_read_every_amount_is_withheld_but_the_states_reasons_and_result_stay()
    {
        var world = new World();
        var approved = await PricedApprovedAsync(world, "a");
        world.Deliveries = new FakeDeliveries(order => new(Company, order, []));
        world.PartyRisk = new FakePartyRisk(FinanceLike([new(Currency, 100m)]));
        world.Authority.Denied.Add(PriceReadPermission);

        var check = await world.Service().GetDraftRiskCheckAsync(approved.Draft.Id);

        Assert.Equal((SalesPriceDisclosure.Withheld, "Unavailable", (string?)null), (check.AmountDisclosure, check.Result, check.GrossRisk));
        Assert.All(check.Components.Append(check.CreditLimit).Append(check.Collateral), figure => Assert.Null(figure.Amounts));
        Assert.Equal(("Available", "Available"), (check.Components[0].State, check.Components[2].State));
        Assert.Equal(3, check.Reasons.Count);
    }

    [Fact]
    public async Task An_absent_failing_or_foreign_finance_owner_makes_its_three_figures_unavailable_never_zero()
    {
        var world = new World();
        var approved = await PricedApprovedAsync(world, "a");
        world.Deliveries = new FakeDeliveries(order => new(Company, order, []));

        // Not composed.
        var absent = await world.Service().GetDraftRiskCheckAsync(approved.Draft.Id);
        AssertPartyUnavailable(absent, SalesDraftRiskReasonCodesV1.PartyRiskProviderUnavailable);

        // Failing.
        var partyRisk = new FakePartyRisk((_, _) => throw new InvalidOperationException("SENTETIK finance outage"));
        world.PartyRisk = partyRisk;
        AssertPartyUnavailable(await world.Service().GetDraftRiskCheckAsync(approved.Draft.Id),
            SalesDraftRiskReasonCodesV1.PartyRiskProviderUnavailable);

        // Another company's, another customer's, or a malformed answer.
        foreach (var malformed in new Func<SalesPartyRiskReadAnswerV1, SalesPartyRiskReadAnswerV1>[]
                 {
                     answer => answer with { CompanyId = Guid.NewGuid() },
                     answer => answer with { PartyId = OtherParty },
                     answer => answer with { CompanyAccountId = Guid.NewGuid() },
                     answer => answer with { OpenReceivables = null! },
                     answer => answer with { CreditLimit = answer.CreditLimit with { Source = " " } },
                     answer => answer with { Collateral = answer.Collateral with { Amounts = null! } },
                     answer => answer with { OpenReceivables = answer.OpenReceivables with { Amounts = [null!] } },
                 })
        {
            partyRisk.Answer = (party, account) => malformed(FinanceLike([new(Currency, 1m)])(party, account));
            AssertPartyUnavailable(await world.Service().GetDraftRiskCheckAsync(approved.Draft.Id),
                SalesDraftRiskReasonCodesV1.PartyRiskAnswerInvalid);
        }

        partyRisk.Answer = (_, _) => null!;
        AssertPartyUnavailable(await world.Service().GetDraftRiskCheckAsync(approved.Draft.Id),
            SalesDraftRiskReasonCodesV1.PartyRiskAnswerInvalid);

        // The owner's own "Unavailable" keeps its reason; the remainder is still answered beside it.
        partyRisk.Answer = (party, account) => FinanceLike([])(party, account) with
        {
            OpenReceivables = new(SalesPartyRiskFigureStateV1.Unavailable, "finance.open-items", [], null, "finance_open_item_unreadable"),
        };
        var ownerUnavailable = await world.Service().GetDraftRiskCheckAsync(approved.Draft.Id);
        Assert.Equal(("Unavailable", "finance_open_item_unreadable"), (ownerUnavailable.Components[0].State, ownerUnavailable.Components[0].ReasonCode));
        Assert.Equal(("Available", "14.84"), (ownerUnavailable.Components[2].State, Assert.Single(ownerUnavailable.Components[2].Amounts!).Amount));
        Assert.Contains(new SalesDraftRiskReasonV1("OpenReceivables", "finance_open_item_unreadable"), ownerUnavailable.Reasons);
        Assert.Equal("Unavailable", ownerUnavailable.Result);
    }

    [Fact]
    public async Task The_remainder_is_unavailable_when_its_delivery_owner_or_an_order_line_cannot_answer_and_a_recorded_zero_otherwise()
    {
        var world = new World();
        world.PartyRisk = new FakePartyRisk(FinanceLike([]));
        // A draft that is not approved, and no other order: the customer has no open approved order — a recorded zero,
        // and the delivery owner (not composed) is never needed.
        var unapproved = (await world.Service().CreateDraftAsync(Priced(), Metadata("k-create"))).Detail.Draft;
        var none = await world.Service().GetDraftRiskCheckAsync(unapproved.Id);
        Assert.Equal(("Available", "ExcludingTax"), (none.Components[2].State, none.Components[2].TaxBasis));
        Assert.Empty(none.Components[2].Amounts!);
        Assert.Equal("Available", none.Components[0].State);
        Assert.Empty(none.Components[0].Amounts!);

        // An approved order and no delivery owner: never "nothing shipped".
        var approved = await PricedApprovedAsync(world, "b");
        var check = await world.Service().GetDraftRiskCheckAsync(unapproved.Id);
        Assert.Equal(("Unavailable", SalesDraftRiskReasonCodesV1.DeliveryProviderUnavailable, (IReadOnlyList<SalesDraftRiskAmountV1>?)null),
            (check.Components[2].State, check.Components[2].ReasonCode, check.Components[2].Amounts));
        Assert.Contains(new SalesDraftRiskReasonV1("ApprovedUnshippedRemainder", SalesDraftRiskReasonCodesV1.DeliveryProviderUnavailable), check.Reasons);

        var line = approved.Lines[0].LineId;
        var deliveries = new FakeDeliveries(_ => throw new InvalidOperationException("SENTETIK logistics outage"));
        world.Deliveries = deliveries;
        Assert.Equal(SalesDraftRiskReasonCodesV1.DeliveryProviderUnavailable,
            (await world.Service().GetDraftRiskCheckAsync(unapproved.Id)).Components[2].ReasonCode);
        foreach (var malformed in new Func<Guid, SalesOrderDeliveryReadAnswerV1>[]
                 {
                     order => new(Guid.NewGuid(), order, []),
                     _ => new(Company, Guid.NewGuid(), []),
                     order => new(Company, order, [new(Guid.NewGuid(), KgUnit, 1m, 0m)]),
                     order => new(Company, order, [new(line, Guid.NewGuid(), 1m, 0m)]),
                     order => new(Company, order, [new(line, KgUnit, -1m, 0m)]),
                 })
        {
            deliveries.Answer = malformed;
            Assert.Equal(SalesDraftRiskReasonCodesV1.DeliveryAnswerInvalid,
                (await world.Service().GetDraftRiskCheckAsync(unapproved.Id)).Components[2].ReasonCode);
        }

        deliveries.Answer = order => new(Company, order, []);
        var priced = await world.Service().GetDraftRiskCheckAsync(unapproved.Id);
        Assert.Equal(("Available", "14.84"), (priced.Components[2].State, Assert.Single(priced.Components[2].Amounts!).Amount));
    }

    [Fact]
    public async Task An_unpriced_open_line_or_a_price_in_another_unit_is_unavailable_never_free()
    {
        var world = new World();
        world.PartyRisk = new FakePartyRisk(FinanceLike([]));
        var unpriced = await ApprovedAsync(world);
        var line = unpriced.Lines[0].LineId;
        var deliveries = new FakeDeliveries(order => new(Company, order, []));
        world.Deliveries = deliveries;

        var check = await world.Service().GetDraftRiskCheckAsync(unpriced.Draft.Id);
        Assert.Equal(("Unavailable", SalesDraftRiskReasonCodesV1.OrderLineUnpriced), (check.Components[2].State, check.Components[2].ReasonCode));

        // Shipped in full, the unpriced line has nothing left: it adds nothing (a recorded zero), priced or not.
        deliveries.Answer = order => new(Company, order, [new(line, KgUnit, 1.25m, 1.25m)]);
        var done = await world.Service().GetDraftRiskCheckAsync(unpriced.Draft.Id);
        Assert.Equal(("Available", "0.00"), (done.Components[2].State, Assert.Single(done.Components[2].Amounts!).Amount));
    }

    [Fact]
    public async Task While_a_revision_awaits_re_approval_the_remainder_uses_the_last_approved_quantities_and_prices()
    {
        var world = new World();
        world.PartyRisk = new FakePartyRisk(FinanceLike([]));
        var approved = await PricedApprovedAsync(world, "a");
        var line = approved.Lines[0].LineId;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, []));
        Owners(world);

        // Rev.1 asks 2 kg and awaits re-approval: the remainder is still the APPROVED 1.25 kg × 11.875 = 14.84, never 2 kg.
        await world.Service().ReviseQuantityAsync(approved.Draft.Id,
            new SalesDraftQuantityRevisionRequest("4", "SENTETIK revizyon", [new SalesDraftLineQuantityV1(line, "2")]), Metadata("k-quantity"));
        Assert.Equal("2", (await world.Service().GetDraftDetailAsync(approved.Draft.Id)).Lines[0].Quantity);
        var pending = await world.Service().GetDraftRiskCheckAsync(approved.Draft.Id);
        Assert.Equal("14.84", Assert.Single(pending.Components[2].Amounts!).Amount);
    }

    [Fact]
    public async Task More_open_approved_orders_than_one_read_takes_is_unavailable_never_a_truncated_sum()
    {
        var world = new World();
        world.PartyRisk = new FakePartyRisk(FinanceLike([]));
        world.Deliveries = new FakeDeliveries(order => new(Company, order, []));
        for (var index = 0; index <= SalesDraftService.MaximumRiskOrders; index++)
        {
            await PricedApprovedAsync(world, $"n{index}");
        }

        var first = (await world.Store.ListOpenApprovedOrdersAsync(Party, Account, 1))[0];
        var check = await world.Service().GetDraftRiskCheckAsync(first.Id);

        Assert.Equal(("Unavailable", SalesDraftRiskReasonCodesV1.OrderReadBoundExceeded),
            (check.Components[2].State, check.Components[2].ReasonCode));
    }

    [Fact]
    public async Task The_risk_check_refuses_before_anything_is_read_and_another_companys_or_an_unknown_draft_is_404()
    {
        var world = new World();
        var partyRisk = new FakePartyRisk(FinanceLike([]));
        world.PartyRisk = partyRisk;
        var approved = await PricedApprovedAsync(world, "a");

        world.Authority.Denied.Add(ReadPermission);
        var forbidden = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftRiskCheckAsync(approved.Draft.Id));
        Assert.Equal(403, forbidden.Status);
        world.Authority.Denied.Clear();

        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftRiskCheckAsync(Guid.Empty))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftRiskCheckAsync(Guid.NewGuid()))).Status);
        Assert.Empty(partyRisk.Asked);
        Assert.Empty(world.Store.OpenApprovedOrderReads);

        // The draft read lost after the finance owner answered (the next fence measures it): 403, nothing returned.
        world.Authority.LostLater = (ReadPermission, world.Authority.Calls + 3);
        var lost = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftRiskCheckAsync(approved.Draft.Id));
        Assert.Equal(403, lost.Status);
        world.Authority.LostLater = null;

        // The Sales store failing to list the customer's orders is the read's own 503, never an empty remainder.
        world.Store.Fault = "synthetic_store_outage";
        var store = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftRiskCheckAsync(approved.Draft.Id));
        Assert.Equal(503, store.Status);
    }

    // ---- helpers --------------------------------------------------------------------------------------------------------

    private static (string Kind, string Source, string State, string? TaxBasis, string? Amount, string? Reason) Flat(SalesDraftRiskFigureV1 figure) =>
        (figure.Kind, figure.Source, figure.State, figure.TaxBasis, figure.Amounts is null ? null : string.Join("|", figure.Amounts.Select(amount => amount.Amount)),
            figure.ReasonCode);

    private static void AssertPartyUnavailable(SalesOrderDraftRiskCheckResponse check, string reason)
    {
        Assert.Equal("Unavailable", check.Result);
        foreach (var figure in new[] { check.Components[0], check.CreditLimit, check.Collateral })
        {
            Assert.Equal((SalesDraftRiskSourcesV1.PartyRiskOwner, "Unavailable", reason, (IReadOnlyList<SalesDraftRiskAmountV1>?)null),
                (figure.Source, figure.State, figure.ReasonCode, figure.Amounts));
        }

        Assert.Contains(new SalesDraftRiskReasonV1("OpenReceivables", reason), check.Reasons);
        Assert.Contains(new SalesDraftRiskReasonV1("CreditLimit", reason), check.Reasons);
    }

    /// <summary>A priced one-line order (1.25 kg at 12.50, 5 % discount), submitted and approved: version 4.</summary>
    private static async Task<SalesOrderDraftDetailView> PricedApprovedAsync(World world, string key)
    {
        var created = (await world.Service().CreateDraftAsync(Priced(), Metadata($"{key}-create"))).Detail.Draft;
        await world.Service().SubmitDraftAsync(created.Id, Command("2"), Metadata($"{key}-submit"));
        return (await world.Service().ApproveDraftAsync(created.Id, Command("3"), Metadata($"{key}-approve"))).Detail;
    }

    /// <summary>The answer shape of the Finance owner on main: receivables as given (taxes included), limit and collateral without a master.</summary>
    private static Func<Guid, Guid, SalesPartyRiskReadAnswerV1> FinanceLike(IReadOnlyList<SalesPartyRiskAmountV1> receivables) =>
        (party, account) => new SalesPartyRiskReadAnswerV1(Company, party, account,
            new SalesPartyRiskFigureV1(SalesPartyRiskFigureStateV1.Available, "finance.open-items", receivables, true, null),
            new SalesPartyRiskFigureV1(SalesPartyRiskFigureStateV1.NoSource, "finance.party-credit-limit", [], null, "finance_credit_limit_owner_missing"),
            new SalesPartyRiskFigureV1(SalesPartyRiskFigureStateV1.NoSource, "finance.party-collateral", [], null, "finance_collateral_owner_missing"));

    /// <summary>A SYNTHETIC customer-finance owner: answers what the case gives it and records the customers asked.</summary>
    private sealed class FakePartyRisk(Func<Guid, Guid, SalesPartyRiskReadAnswerV1> answer) : ISalesPartyRiskReadPortV1
    {
        public Func<Guid, Guid, SalesPartyRiskReadAnswerV1> Answer { get; set; } = answer;

        public List<(Guid PartyId, Guid CompanyAccountId)> Asked { get; } = [];

        public Task<SalesPartyRiskReadAnswerV1> ReadPartyRiskAsync(Guid partyId, Guid companyAccountId, CancellationToken cancellationToken = default)
        {
            Asked.Add((partyId, companyAccountId));
            return Task.FromResult(Answer(partyId, companyAccountId));
        }
    }
}
