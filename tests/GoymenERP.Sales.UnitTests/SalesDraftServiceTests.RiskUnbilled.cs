using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// Plan S0 (DEC-FIN-007 second stage, SAT-D311) over SYNTHETIC owners: "shipped, not invoiced" is the delivery owner's definite
/// shipped quantity of every open approved order of the customer (and of every order with a posted share), priced at the approved
/// basis' payable amount, less the shares on a FINALIZED Sales invoice. The remainder subtracts the same definite shipments, so a
/// quantity is in exactly one stage. A finalized share needs Finance's processed set (SAT-D311), which the Finance answer does not
/// carry yet: both the stage and its sub-row are "Unavailable" then. Any owner not composed, failing or malformed is fail-closed.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private const int UnbilledIndex = 1;
    private const int RemainderIndex = 2;
    private const int AwaitingFinanceIndex = 3;

    [Fact]
    public async Task Plan_S0_c_a_definite_shipment_without_an_approved_note_is_counted_in_exactly_one_stage()
    {
        var (world, approved, deliveries, shares, _) = await UnbilledWorldAsync();
        var line = approved.Lines[0];
        var whole = (await world.Service().GetDraftTotalsAsync(approved.Draft.Id)).Totals.PayableAmount;
        Assert.NotNull(whole);

        // Nothing shipped: the whole approved order is the remainder, nothing is unbilled.
        var none = await world.Service().GetDraftRiskCheckAsync(approved.Draft.Id);
        Assert.Equal(("Available", ""), (none.Components[UnbilledIndex].State, AmountsOf(none.Components[UnbilledIndex])));
        Assert.Equal(("Available", whole), (none.Components[RemainderIndex].State, AmountsOf(none.Components[RemainderIndex])));

        // Shipped in full, its dispatch note not approved yet (no posted share): the whole amount moved to "shipped, not invoiced"
        // and left the remainder — never in both, never in neither.
        deliveries.Answer = order => new(Company, order, [new(line.LineId, KgUnit, 1.25m, 0m)]);
        var shipped = await world.Service().GetDraftRiskCheckAsync(approved.Draft.Id);
        Assert.Equal(("Available", "IncludingTax", whole), (shipped.Components[UnbilledIndex].State,
            shipped.Components[UnbilledIndex].TaxBasis, AmountsOf(shipped.Components[UnbilledIndex])));
        Assert.Equal(("Available", ""), (shipped.Components[RemainderIndex].State, AmountsOf(shipped.Components[RemainderIndex])));

        // The note approved (a posted share): the same amounts — approval moves nothing, only a finalized invoice does.
        shares.Answer = _ => SharesOf(Share(approved, 1.25m));
        var posted = await world.Service().GetDraftRiskCheckAsync(approved.Draft.Id);
        Assert.Equal(shipped.Components.Select(Flat), posted.Components.Select(Flat));

        // Partly shipped: both stages carry an amount, each from the one shipped fact.
        deliveries.Answer = order => new(Company, order, [new(line.LineId, KgUnit, 0.25m, 0m)]);
        shares.Answer = _ => SharesOf(Share(approved, 0.25m));
        var partly = await world.Service().GetDraftRiskCheckAsync(approved.Draft.Id);
        Assert.Single(partly.Components[UnbilledIndex].Amounts!);
        Assert.Single(partly.Components[RemainderIndex].Amounts!);
        Assert.Equal(("Available", ""), (partly.Components[AwaitingFinanceIndex].State, AmountsOf(partly.Components[AwaitingFinanceIndex])));
        Assert.DoesNotContain(partly.Reasons, reason => reason.Component is "ShippedNotInvoiced" or "ApprovedUnshippedRemainder");
    }

    [Fact]
    public async Task Plan_S0_a_a_share_on_a_draft_invoice_stays_unbilled_and_a_finalized_one_waits_for_the_finance_processed_set()
    {
        var (world, approved, deliveries, shares, invoices) = await UnbilledWorldAsync();
        var share = Share(approved, 1.25m);
        deliveries.Answer = order => new(Company, order, [new(approved.Lines[0].LineId, KgUnit, 1.25m, 0m)]);
        shares.Answer = _ => SharesOf(share);

        // A draft (or pending, approved, rejected, cancelled) invoice took the share: still shipped, not invoiced.
        invoices.Invoiced.Add(share.ShipmentId);
        var drafted = await world.Service().GetDraftRiskCheckAsync(approved.Draft.Id);
        Assert.Equal("Available", drafted.Components[UnbilledIndex].State);
        Assert.Single(drafted.Components[UnbilledIndex].Amounts!);
        Assert.Equal(1, invoices.FinalizedReads);

        // Finalized: Finance either processed it (open receivables) or not (the SAT-D311 sub-row) — which, only Finance's same
        // answer can say, and it does not carry it yet: both are unavailable, never a double count or a gap.
        invoices.FinalizedShipments.Add(share.ShipmentId);
        var finalized = await world.Service().GetDraftRiskCheckAsync(approved.Draft.Id);
        foreach (var index in new[] { UnbilledIndex, AwaitingFinanceIndex })
        {
            Assert.Equal(("Unavailable", SalesDraftRiskReasonCodesV1.FinanceProcessedSetUnavailable, (IReadOnlyList<SalesDraftRiskAmountV1>?)null),
                (finalized.Components[index].State, finalized.Components[index].ReasonCode, finalized.Components[index].Amounts));
        }

        Assert.Equal((SalesDraftRiskFigureKindsV1.InvoicedAwaitingFinance, SalesDraftRiskSourcesV1.FinalizedInvoices),
            (finalized.Components[AwaitingFinanceIndex].Kind, finalized.Components[AwaitingFinanceIndex].Source));
        Assert.Contains(new SalesDraftRiskReasonV1("ShippedNotInvoiced", SalesDraftRiskReasonCodesV1.FinanceProcessedSetUnavailable),
            finalized.Reasons);
        Assert.Equal(SalesDraftRiskResultsV1.Unavailable, finalized.Result);
        // The remainder beside it is still made on the same shipped fact.
        Assert.Equal("Available", finalized.Components[RemainderIndex].State);

        // Cancelled after finalization or released: the share is back in "shipped, not invoiced".
        invoices.FinalizedShipments.Clear();
        Assert.Equal("Available", (await world.Service().GetDraftRiskCheckAsync(approved.Draft.Id)).Components[UnbilledIndex].State);
    }

    [Fact]
    public async Task Plan_S0_the_unbilled_stage_is_fail_closed_when_an_owner_is_missing_failing_refusing_or_malformed()
    {
        var (world, approved, deliveries, shares, invoices) = await UnbilledWorldAsync();
        deliveries.Answer = order => new(Company, order, [new(approved.Lines[0].LineId, KgUnit, 1.25m, 0m)]);

        // The Logistics share owner not composed or failing.
        world.PostedShares = null;
        await AssertUnbilledAsync(world, approved.Draft.Id, SalesDraftRiskReasonCodesV1.ShipmentProviderUnavailable);
        world.PostedShares = new FakeInvoiceableShipments { Answer = _ => throw new InvalidOperationException("SENTETIK logistics outage") };
        await AssertUnbilledAsync(world, approved.Draft.Id, SalesDraftRiskReasonCodesV1.ShipmentProviderUnavailable);
        world.PostedShares = shares;

        // Its own "Unavailable", another company's answer, a malformed share, or more shares than one read takes.
        shares.Answer = _ => SharesOf() with { Outcome = SalesDispatchReadOutcomeV1.Unavailable };
        await AssertUnbilledAsync(world, approved.Draft.Id, SalesDraftRiskReasonCodesV1.ShipmentProviderUnavailable);
        foreach (var malformed in new Func<SalesInvoiceableShipmentAnswerV1>[]
                 {
                     () => SharesOf() with { CompanyId = Guid.NewGuid() },
                     () => SharesOf() with { Shares = null! },
                     () => SharesOf(Share(approved, 1m) with { Quantity = 0m }),
                     () => SharesOf(Share(approved, 1m) with { CompanyAccountId = Guid.NewGuid() }),
                     () => SharesOf(Share(approved, 1m) with { SystemNumber = " " }),
                     () => SharesOf(Share(approved, 1m) with { NoteLineNo = 0 }),
                     () => SharesOf(Share(approved, 1m) with { SalesOrderLineId = Guid.NewGuid() }),
                     () => SharesOf(Share(approved, 1m) with { UnitId = Guid.NewGuid() }),
                 })
        {
            shares.Answer = _ => malformed();
            await AssertUnbilledAsync(world, approved.Draft.Id, SalesDraftRiskReasonCodesV1.ShipmentAnswerInvalid);
        }

        var twice = Share(approved, 0.5m);
        shares.Answer = _ => SharesOf(twice, twice);
        await AssertUnbilledAsync(world, approved.Draft.Id, SalesDraftRiskReasonCodesV1.ShipmentAnswerInvalid);
        shares.Answer = _ => SharesOf(Share(approved, 1m)) with { HasMore = true };
        await AssertUnbilledAsync(world, approved.Draft.Id, SalesDraftRiskReasonCodesV1.ShipmentReadBoundExceeded);
        Assert.All(shares.Asked, query => Assert.Equal((Account, SalesDraftService.MaximumRiskShares), (query.CompanyAccountId, query.Limit)));

        // More on approved notes than the delivery owner's definite shipments: the two Logistics facts disagree.
        shares.Answer = _ => SharesOf(Share(approved, 1m), Share(approved, 0.5m));
        await AssertUnbilledAsync(world, approved.Draft.Id, SalesDraftRiskReasonCodesV1.ShipmentFactsInconsistent);

        // Another customer's share under the same company account is not this customer's risk.
        shares.Answer = _ => SharesOf(Share(approved, 9m) with { CustomerPartyId = OtherParty, SalesOrderId = Guid.NewGuid() });
        Assert.Equal("Available", (await world.Service().GetDraftRiskCheckAsync(approved.Draft.Id)).Components[UnbilledIndex].State);

        // Sales' own invoice records not composed.
        shares.Answer = _ => SharesOf(Share(approved, 1m));
        world.InvoiceRecords = null;
        await AssertUnbilledAsync(world, approved.Draft.Id, SalesDraftRiskReasonCodesV1.InvoiceRecordUnavailable);
        world.InvoiceRecords = invoices;

        // The delivery owner failing: no shipped fact, so neither stage is made.
        deliveries.Answer = _ => throw new InvalidOperationException("SENTETIK delivery outage");
        await AssertUnbilledAsync(world, approved.Draft.Id, SalesDraftRiskReasonCodesV1.DeliveryProviderUnavailable);

        // A refusal of the Logistics owner is the read's 403, never an empty stage.
        deliveries.Answer = order => new(Company, order, []);
        shares.Answer = _ => SharesOf() with { Outcome = SalesDispatchReadOutcomeV1.Denied };
        var denied = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftRiskCheckAsync(approved.Draft.Id));
        Assert.Equal(403, denied.Status);
    }

    [Fact]
    public async Task Plan_S0_a_shipped_line_without_an_approved_price_makes_the_unbilled_stage_unavailable_never_free()
    {
        var world = new World { PostedShares = new FakeInvoiceableShipments(), InvoiceRecords = new FakeInvoiceStore() };
        world.PartyRisk = new FakePartyRisk(FinanceLike([]));
        var unpriced = await ApprovedAsync(world);
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(unpriced.Lines[0].LineId, KgUnit, 1.25m, 0m)]));

        var check = await world.Service().GetDraftRiskCheckAsync(unpriced.Draft.Id);

        Assert.Equal(("Unavailable", SalesDraftRiskReasonCodesV1.OrderLineUnpriced),
            (check.Components[UnbilledIndex].State, check.Components[UnbilledIndex].ReasonCode));
        // Fully shipped, nothing is left: the remainder is a recorded zero.
        Assert.Equal("Available", check.Components[RemainderIndex].State);
    }

    // ---- helpers --------------------------------------------------------------------------------------------------------

    private static async Task<(World World, SalesOrderDraftDetailView Approved, FakeDeliveries Deliveries, FakeInvoiceableShipments Shares,
        FakeInvoiceStore Invoices)> UnbilledWorldAsync()
    {
        var shares = new FakeInvoiceableShipments();
        var invoices = new FakeInvoiceStore();
        var world = new World { LineTaxes = new FakeLineTaxes(), PostedShares = shares, InvoiceRecords = invoices };
        world.PartyRisk = new FakePartyRisk(FinanceLike([]));
        var approved = await PricedApprovedAsync(world, "u");
        var deliveries = new FakeDeliveries(order => new(Company, order, []));
        world.Deliveries = deliveries;
        return (world, approved, deliveries, shares, invoices);
    }

    private static SalesInvoiceableShipmentV1 Share(SalesOrderDraftDetailView order, decimal quantity) =>
        new(Guid.NewGuid(), "IRS-2026-000001", null, Approved, Party, Account, 1, Guid.NewGuid(), Shipped, order.Draft.Id,
            order.Lines[0].LineId, order.Lines[0].ProductId, order.Lines[0].OrderUnitId, quantity);

    private static SalesInvoiceableShipmentAnswerV1 SharesOf(params SalesInvoiceableShipmentV1[] shares) =>
        new(SalesDispatchReadOutcomeV1.Found, Company, shares, false, SalesInvoicedStateSourceV1.NoSource);

    private static string AmountsOf(SalesDraftRiskFigureV1 figure) =>
        string.Join("|", figure.Amounts!.Select(amount => amount.Amount));

    private static async Task AssertUnbilledAsync(World world, Guid draftId, string reason)
    {
        var check = await world.Service().GetDraftRiskCheckAsync(draftId);
        Assert.Equal(("Unavailable", reason, (IReadOnlyList<SalesDraftRiskAmountV1>?)null),
            (check.Components[UnbilledIndex].State, check.Components[UnbilledIndex].ReasonCode, check.Components[UnbilledIndex].Amounts));
        Assert.Equal(("Unavailable", reason), (check.Components[AwaitingFinanceIndex].State, check.Components[AwaitingFinanceIndex].ReasonCode));
        Assert.Contains(new SalesDraftRiskReasonV1("ShippedNotInvoiced", reason), check.Reasons);
        Assert.Equal(SalesDraftRiskResultsV1.Unavailable, check.Result);
    }
}
