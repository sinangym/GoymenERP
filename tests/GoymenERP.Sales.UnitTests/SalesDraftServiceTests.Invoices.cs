using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// S5 API-32 (Fatura çekirdeği 1) over SYNTHETIC owners: an invoice draft is prepared from an approved, priced and taxed order's
/// approved shipment share with the invoice's rate written on it (SAT-D172) and the TL equivalent of EK42; the write and the
/// price read are measured at every fence; the same key replays the stored invoice; every wait (unknown or invoiced share,
/// withholding, unresolved rate) is one 409 with each reason and writes nothing; owners that are absent fail closed. SENTETIK.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private const string InvoiceWrite = SalesPermissionManifestV1.InvoicesWrite;

    /// <summary>The invoice service measures the real Sales price-read name (the draft tests' stand-in names do not apply).</summary>
    private const string InvoicePriceRead = SalesPermissionManifestV1.OrdersPriceRead;

    [Fact]
    public async Task An_invoice_draft_takes_the_approved_share_at_the_basis_price_with_the_invoice_rate_and_replays_under_the_same_key()
    {
        var invoicing = await InvoiceWorld.CreateAsync();
        invoicing.World.Authority.Measured.Clear();

        var created = await invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-invoice"));

        var invoice = created.Invoice;
        Assert.False(created.IsReplay);
        Assert.Equal(("Draft", "1", Party, Account, Currency, 2), (invoice.State, invoice.Version, invoice.PartyId, invoice.CompanyAccountId,
            invoice.CurrencyId, invoice.AmountScale));
        // 1.25 kg × 12.50 = 15.63; VAT 20 % = 3.13; no withholding; payable 18.76 (SAT-D11).
        Assert.Equal(new SalesInvoiceAmountsView("15.63", "3.13", "0.00", "18.76", "18.76"), invoice.Totals);
        // SAT-D172 / SAT-D226: the rate is written on the invoice; 15.63 × 34.5678 = 540.29, 3.13 × 34.5678 = 108.20 (SAT-D223).
        Assert.Equal(("34.5678", SettingsCurrencyIdentitiesV1.TurkishLira, 2), (invoice.ExchangeRate!.Rate,
            invoice.ExchangeRate.FunctionalCurrencyId, invoice.ExchangeRate.FunctionalScale));
        Assert.Equal(new SalesInvoiceAmountsView("540.29", "108.20", "0.00", "648.49", "648.49"), invoice.FunctionalTotals);
        var line = Assert.Single(invoice.Lines);
        Assert.Equal((invoicing.ShipmentId, invoicing.Order.Draft.Id, "Rev.0", "1.25", "12.5", "20"),
            (line.ShipmentId, line.SalesOrderId, line.PriceBasisRevision, line.Quantity, line.NetUnitPrice, line.VatPercent));
        var instalment = Assert.Single(invoice.Instalments);
        Assert.Equal(("18.76", Day.AddDays(30)), (instalment.Amount, instalment.DueDate));
        Assert.Equal([new SalesInvoiceableShipmentQueryV1(Account, SalesInvoiceApiV1.MaximumShares)], invoicing.Shipments.Asked);
        Assert.Equal(InvoiceWrite, invoicing.World.Authority.Measured[0]);
        Assert.Contains(InvoicePriceRead, invoicing.World.Authority.Measured);
        Assert.Single(invoicing.Invoices.Stored);

        var replay = await invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-invoice", "another-correlation"));

        Assert.True(replay.IsReplay);
        Assert.Equal(("synthetic-correlation", invoice.Id), (replay.OriginalCorrelationId, replay.Invoice.Id));
        Assert.Single(invoicing.Invoices.Stored);
        Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateInvoiceAsync(invoicing.Request() with { Shares = [new(Guid.NewGuid())] }, Metadata("k-invoice")))));

        var detail = await invoicing.Service().GetInvoiceAsync(invoice.Id);
        Assert.Equal(invoice with { IsReplay = false }, detail with { VatGroups = invoice.VatGroups, Instalments = invoice.Instalments, Lines = invoice.Lines });
        var page = await invoicing.Service().GetInvoicesAsync(new SalesInvoiceListQuery());
        Assert.Equal(("18.76", "648.49"), (Assert.Single(page.Items).PayableAmount, page.Items[0].FunctionalPayableAmount));
        Assert.Equal((404, "not_found"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().GetInvoiceAsync(Guid.NewGuid()))));
    }

    [Fact]
    public async Task Shares_that_cannot_be_invoiced_now_are_one_conflict_naming_every_reason_and_nothing_is_written()
    {
        var invoicing = await InvoiceWorld.CreateAsync();
        invoicing.Invoices.Invoiced.Add(invoicing.ShipmentId);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => invoicing.Service().CreateInvoiceAsync(
            invoicing.Request() with { Shares = [new(invoicing.ShipmentId), new(Guid.NewGuid())] }, Metadata("k-invoice-wait")));

        Assert.Equal((409, SalesInvoiceProblemCodesV1.InvoiceNotReady), Status(refused));
        Assert.Equal(
            [new("shares[0]", SalesInvoiceReasonCodesV1.ShareAlreadyInvoiced), new("shares[1]", SalesInvoiceReasonCodesV1.ShareNotInvoiceable)],
            refused.Errors);
        Assert.Empty(invoicing.Invoices.Stored);
        Assert.Empty(invoicing.Invoices.Attempts);
    }

    [Fact]
    public async Task A_shipped_uninvoiced_share_of_a_cancelled_order_is_invoiceable_EK43_pending()
    {
        // Sinan (relayed by the coordinator; EK43 pending): a cancelled order's shipped, uninvoiced dispatch note is invoiceable.
        var invoicing = await InvoiceWorld.CreateAsync();
        Owners(invoicing.World);
        var token = (await invoicing.World.Service().GetCancellationImpactAsync(invoicing.Order.Draft.Id)).ImpactToken;
        await invoicing.World.Service().CancelDraftAsync(invoicing.Order.Draft.Id,
            new SalesDraftCancelRequest("6", "neden", token), Metadata("k-invoice-cancel"));

        var created = await invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-invoice-cancelled"));

        Assert.Equal("18.76", created.Invoice.Totals.PayableAmount);
        Assert.Single(invoicing.Invoices.Stored);
    }

    [Fact]
    public async Task A_withholding_line_waits_for_the_threshold_decision_and_its_code_never_a_guessed_rule()
    {
        var invoicing = await InvoiceWorld.CreateAsync(withholding: true);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-invoice-withholding")));

        Assert.Equal(409, refused.Status);
        Assert.Contains(new SalesDraftFieldError("shares[0]", SalesInvoiceReasonCodesV1.WithholdingThresholdUndecided), refused.Errors);
        Assert.Contains(new SalesDraftFieldError("shares[0]", TaxResolutionReasonsV1.WithholdingCodeMissing), refused.Errors);
        Assert.Empty(invoicing.Invoices.Stored);
    }

    [Fact]
    public async Task A_foreign_currency_share_without_a_resolved_rate_waits_and_a_missing_owner_is_unavailable()
    {
        var invoicing = await InvoiceWorld.CreateAsync();
        invoicing.Rates.State = ExchangeRateResolutionStatesV1.Unavailable;

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-invoice-rate")));
        Assert.Equal([new SalesDraftFieldError("shares[0]", SalesInvoiceReasonCodesV1.RateUnresolved)], refused.Errors);

        invoicing.NoShipments = true;
        Assert.Equal((503, SalesDispatchProblemCodesV1.DispatchProviderUnavailable), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-invoice-owner")))));
        Assert.Empty(invoicing.Invoices.Stored);
    }

    [Fact]
    public async Task The_invoice_routes_need_their_own_name_and_the_price_read_measured_before_anything_is_read()
    {
        var invoicing = await InvoiceWorld.CreateAsync();
        invoicing.World.Authority.Denied.Add(InvoicePriceRead);
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-invoice-price")))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().GetInvoicesAsync(new SalesInvoiceListQuery()))).Status);
        Assert.Empty(invoicing.Shipments.Asked);

        invoicing.World.Authority.Denied.Clear();
        invoicing.World.Authority.Denied.Add(InvoiceWrite);
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-invoice-write")))).Status);

        // A grant lost after the owner read stops the command before the save.
        invoicing.World.Authority.Denied.Clear();
        invoicing.World.Authority.LostLater = (InvoiceWrite, invoicing.World.Authority.Calls + 3);
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-invoice-late")))).Status);
        Assert.Empty(invoicing.Invoices.Stored);

        invoicing.World.User.Authenticated = false;
        Assert.Equal((401, "unauthenticated"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().GetInvoiceAsync(Guid.NewGuid()))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task A_share_list_out_of_bounds_or_repeating_a_shipment_is_a_validation_failure(int count)
    {
        var invoicing = await InvoiceWorld.CreateAsync();
        var shares = Enumerable.Range(0, count).Select(_ => new SalesInvoiceShareRequest(Guid.NewGuid())).ToList();
        Assert.Equal((400, "validation_failed"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateInvoiceAsync(invoicing.Request() with { Shares = shares }, Metadata("k-invoice-bounds")))));
        var twice = await Assert.ThrowsAsync<SalesDraftServiceException>(() => invoicing.Service().CreateInvoiceAsync(
            invoicing.Request() with { Shares = [new(invoicing.ShipmentId), new(invoicing.ShipmentId)] }, Metadata("k-invoice-twice")));
        Assert.Equal([new SalesDraftFieldError("shares[1].shipmentId", "invalid")], twice.Errors);
        Assert.Empty(invoicing.Shipments.Asked);
    }

    /// <summary>An approved, priced (1.25 kg × 12.50), taxed (Steel 20 %) order and its one approved shipment share, in USD-like currency.</summary>
    private sealed class InvoiceWorld
    {
        private InvoiceWorld(World world, SalesOrderDraftDetailView order)
        {
            World = world;
            Order = order;
            Shipments.Answer = query => new(SalesDispatchReadOutcomeV1.Found, Company,
            [
                new SalesInvoiceableShipmentV1(Guid.NewGuid(), "IRS-2026-000001", null, Approved, Party, Account, 1, ShipmentId, Shipped,
                    order.Draft.Id, order.Lines[0].LineId, Product, KgUnit, 1.25m),
            ], false, SalesInvoicedStateSourceV1.NoSource);
        }

        public World World { get; }
        public SalesOrderDraftDetailView Order { get; }
        public Guid ShipmentId { get; } = Guid.NewGuid();
        public FakeInvoiceableShipments Shipments { get; } = new();
        public FakeInvoiceStore Invoices { get; } = new();
        public FakeExchangeRates Rates { get; } = new(ExchangeRateResolutionStatesV1.Resolved, "34.5678");
        public bool NoShipments { get; set; }

        public static async Task<InvoiceWorld> CreateAsync(bool withholding = false)
        {
            var world = new World
            {
                LineTaxes = new FakeLineTaxes
                {
                    Defaults =
                    [
                        new TaxItemTypeDefaultDataV1(Guid.NewGuid(), Company, TaxItemTypesV1.Steel, TaxDefaultSourcesV1.Record, 1, Day, true,
                            20m, null, withholding, withholding ? 50m : null, null),
                    ],
                },
            };
            var approved = await PricedApprovedAsync(world, "k-invoice-order");
            // Q9 / SAT-D114: the customer accepted the approved revision, so the invoicing gate is open on Rev.0.
            await world.Service().RecordCustomerAcceptanceAsync(approved.Draft.Id, Command(approved.Draft.Version), Metadata("k-invoice-accept"));
            return new InvoiceWorld(world, approved);
        }

        public CreateSalesInvoiceRequest Request() => new(Account, [new SalesInvoiceShareRequest(ShipmentId)]);

        public SalesInvoiceService Service()
        {
            var company = new CompanyContext();
            company.Set(Company);
            return new SalesInvoiceService(company, World.User, World.Authority, new FixedCorrelation(), Invoices, World.Store, World.Settings,
                World.Clock, NoShipments ? null : Shipments, Rates);
        }
    }

    /// <summary>An in-memory invoice store with the attempt rules of the real one (same key + content + actor replays; else 409).</summary>
    private sealed class FakeInvoiceStore : ISalesInvoiceStore
    {
        public Dictionary<Guid, SalesInvoice> Stored { get; } = [];
        public Dictionary<string, (string Hash, string Actor, Guid Invoice, string Correlation)> Attempts { get; } = [];
        public HashSet<Guid> Invoiced { get; } = [];

        public Task<SalesInvoiceStoreResult> StoreInvoiceAsync(SalesDraftWriteEnvelope envelope, string requestHash,
            Func<string, SalesInvoice> build, CancellationToken cancellationToken = default)
        {
            if (Replay(envelope, requestHash) is { } replay)
            {
                return Task.FromResult(replay);
            }

            var invoice = build($"SF-2026-{Stored.Count + 1:D6}");
            Stored[invoice.Id] = invoice;
            Attempts[envelope.IdempotencyKey] = (requestHash, envelope.ActorId, invoice.Id, envelope.CorrelationId);
            return Task.FromResult(new SalesInvoiceStoreResult(invoice, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId));
        }

        public Task<SalesInvoiceStoreResult?> FindInvoiceReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash,
            CancellationToken cancellationToken = default) => Task.FromResult(Replay(envelope, requestHash));

        public Task<SalesInvoice?> FindInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Stored.GetValueOrDefault(invoiceId));

        public Task<SalesInvoicePage> ListInvoicesAsync(int pageSize, string? cursor, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SalesInvoicePage([.. Stored.Values.Take(pageSize)], null));

        public Task<IReadOnlySet<Guid>> FindInvoicedShipmentsAsync(IReadOnlyCollection<Guid> shipmentIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<Guid>>(shipmentIds.Where(id => Invoiced.Contains(id)
                || Stored.Values.Any(invoice => invoice.Lines.Any(line => line.Share.ShipmentId == id))).ToHashSet());

        private SalesInvoiceStoreResult? Replay(SalesDraftWriteEnvelope envelope, string requestHash)
        {
            if (!Attempts.TryGetValue(envelope.IdempotencyKey, out var attempt))
            {
                return null;
            }

            return attempt.Hash == requestHash && attempt.Actor == envelope.ActorId
                ? new SalesInvoiceStoreResult(Stored[attempt.Invoice], SalesDraftWriteOutcome.Replayed, attempt.Correlation)
                : throw new SalesDraftStoreException(SalesDraftStoreCodes.SameKeyPayloadMismatch, "same key, other content");
        }
    }
}
