using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    // Real Sales commands/domain; Finance, Logistics and authority are the existing SYNTHETIC fixture owners.
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Second_shipment_counts_a_finalized_share_once_using_the_same_finance_answer(bool cancelled, bool processed)
    {
        var (invoicing, id, orders) = await FinalizedInvoiceAsync();
        var world = invoicing.World;
        var order = Assert.Single(orders);
        var posted = invoicing.Shipments.LastAnswer!;
        invoicing.Shipments.Answer = _ => posted;
        world.PostedShares = invoicing.Shipments;
        world.InvoiceRecords = invoicing.Invoices;
        world.Deliveries = new FakeDeliveries(orderId => new(Company, orderId,
            [new(order.Lines[0].LineId, KgUnit, 1.25m, 0m)]));
        var owner = new ProcessedRiskOwner(processed ? [id] : []);
        world.PartyRisk = owner;
        if (cancelled)
        {
            var invoice = invoicing.Invoices.Stored[id];
            var now = DateTimeOffset.UtcNow;
            invoice.RecordOfficialCancellation(invoice.Version, "synthetic-canceller", "SENTETIK",
                new(Company, id, invoice.Match!.OfficialNo, invoice.Match.Ettn, "SENTETIK", "status", "1", new string('a', 64), now), now);
        }

        var result = await world.Service().GetDraftRiskCheckAsync(order.Draft.Id);

        Assert.Equal([id], Assert.Single(owner.Queries).SalesInvoiceIds);
        Assert.Equal("Available", result.Components[UnbilledIndex].State);
        Assert.Equal(!processed, result.Components[UnbilledIndex].Amounts!.Count > 0);
        Assert.Equal(!processed && !cancelled, result.Components[AwaitingFinanceIndex].Amounts!.Count > 0);
        Assert.Equal(0, owner.LegacyReads);
    }

    [Fact]
    public async Task Second_shipment_preserves_legacy_no_source_when_native_invoice_identity_is_unproved()
    {
        var (world, approved, deliveries, shares, invoices) = await UnbilledWorldAsync();
        var share = Share(approved, 1.25m);
        deliveries.Answer = order => new(Company, order, [new(approved.Lines[0].LineId, KgUnit, 1.25m, 0m)]);
        shares.Answer = _ => SharesOf(share);
        invoices.FinalizedShipments.Add(share.ShipmentId);
        await AssertUnbilledAsync(world, approved.Draft.Id, SalesDraftRiskReasonCodesV1.FinanceProcessedSetUnavailable);
        // Legacy read cannot turn an unproved native invoice identity into an empty processed set.
        world.PartyRisk = new ProcessedRiskOwner([]);
        await AssertUnbilledAsync(world, approved.Draft.Id, SalesDraftRiskReasonCodesV1.FinanceProcessedSetUnavailable);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Second_shipment_refuses_foreign_and_unavailable_finance_membership(bool foreign)
    {
        var (invoicing, id, orders) = await FinalizedInvoiceAsync();
        var order = Assert.Single(orders);
        var posted = invoicing.Shipments.LastAnswer!;
        invoicing.Shipments.Answer = _ => posted;
        var world = invoicing.World;
        world.PostedShares = invoicing.Shipments;
        world.InvoiceRecords = invoicing.Invoices;
        world.Deliveries = new FakeDeliveries(orderId => new(Company, orderId,
            [new(order.Lines[0].LineId, KgUnit, 1.25m, 0m)]));
        var owner = new ProcessedRiskOwner([id])
        {
            AnswerOverride = answer => foreign
                ? answer with { CompanyId = Guid.NewGuid() }
                : answer with { ProcessedSalesInvoices = new(SalesPartyRiskProcessedSetStateV2.Unavailable,
                    "SENTETIK.same-snapshot", [], "synthetic_unavailable") },
        };
        world.PartyRisk = owner;

        var result = await world.Service().GetDraftRiskCheckAsync(order.Draft.Id);

        Assert.Equal([id], Assert.Single(owner.Queries).SalesInvoiceIds); // Valid native identity reached V2.
        Assert.Equal(SalesPartyRiskFigureStateV1.Available, owner.LastAnswer!.OpenReceivables.State);
        Assert.Equal(SalesDraftRiskResultsV1.Unavailable, result.Result);
        foreach (var index in new[] { UnbilledIndex, AwaitingFinanceIndex })
        {
            Assert.Equal("Unavailable", result.Components[index].State);
            Assert.Equal(SalesDraftRiskReasonCodesV1.FinanceProcessedSetUnavailable, result.Components[index].ReasonCode);
            Assert.Null(result.Components[index].Amounts);
        }
        Assert.NotNull(result.InLocalCurrency);
        Assert.Null(result.InLocalCurrency.GrossRisk);
        Assert.Null(result.InLocalCurrency.GrossRiskWithThisOrder);
        if (result.InLimitCurrency is { } limit)
        {
            Assert.Null(limit.GrossRisk);
            Assert.Null(limit.GrossRiskWithThisOrder);
        }
        Assert.Equal(0, owner.LegacyReads);
    }

    private sealed class ProcessedRiskOwner(IReadOnlyList<Guid> processed) : ISalesPartyRiskReadPortV1, ISalesPartyRiskReadPortV2
    {
        public List<SalesPartyRiskReadQueryV2> Queries { get; } = [];
        public int LegacyReads { get; private set; }
        public Func<SalesPartyRiskReadAnswerV2, SalesPartyRiskReadAnswerV2>? AnswerOverride { get; init; }
        public SalesPartyRiskReadAnswerV2? LastAnswer { get; private set; }
        public Task<SalesPartyRiskReadAnswerV1> ReadPartyRiskAsync(Guid partyId, Guid accountId, CancellationToken token = default)
        {
            LegacyReads++;
            return Task.FromResult(FinanceLike([])(partyId, accountId));
        }
        public Task<SalesPartyRiskReadAnswerV2> ReadPartyRiskAsync(SalesPartyRiskReadQueryV2 query, CancellationToken token = default)
        {
            Queries.Add(query);
            var figures = FinanceLike([])(query.PartyId, query.CompanyAccountId); // Includes the fully-settled case: zero receivables, membership retained.
            var answer = new SalesPartyRiskReadAnswerV2(Company, Party, Account, figures.OpenReceivables,
                new(SalesPartyRiskFigureStateV1.Available, "SENTETIK.limit", [new(Lira, 10000m)], null, null),
                new(SalesPartyRiskFigureStateV1.Available, "SENTETIK.collateral", [], null, null),
                new(SalesPartyRiskProcessedSetStateV2.Available, "SENTETIK.same-snapshot", processed, null));
            LastAnswer = AnswerOverride?.Invoke(answer) ?? answer;
            return Task.FromResult(LastAnswer);
        }
    }
}
