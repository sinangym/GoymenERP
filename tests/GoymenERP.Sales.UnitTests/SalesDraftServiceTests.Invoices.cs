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
        Assert.Equal((SalesPriceDisclosure.Visible, "18.76", "648.49"), (Assert.Single(page.Items).AmountDisclosure,
            page.Items[0].PayableAmount, page.Items[0].FunctionalPayableAmount));
        Assert.Equal((404, "not_found"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().GetInvoiceAsync(Guid.NewGuid()))));
    }

    [Fact]
    public async Task The_invoice_list_without_the_price_read_shows_the_rows_with_every_amount_withheld_SAT_D238()
    {
        var invoicing = await InvoiceWorld.CreateAsync();
        var created = await invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-invoice-withheld"));
        invoicing.World.Authority.Denied.Add(InvoicePriceRead);
        var measuredBefore = invoicing.World.Authority.Measured.Count;

        var page = await invoicing.Service().GetInvoicesAsync(new SalesInvoiceListQuery());

        // SAT-D238 (EK48): the row is listed; both amounts are null BECAUSE they are withheld — never "0.00", never an omission.
        var row = Assert.Single(page.Items);
        Assert.Equal((created.Invoice.Id, created.Invoice.DocumentNo, SalesPriceDisclosure.Withheld, (string?)null, (string?)null, 1),
            (row.Id, row.DocumentNo, row.AmountDisclosure, row.PayableAmount, row.FunctionalPayableAmount, row.LineCount));
        // The invoice read is required first; the price read is asked last, after the store read.
        var measured = invoicing.World.Authority.Measured.Skip(measuredBefore).ToArray();
        Assert.Equal(InvoiceRead, measured[0]);
        Assert.Equal(InvoicePriceRead, measured[^1]);

        // The detail still requires the price read (SAT-D238 opens the list only).
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().GetInvoiceAsync(created.Invoice.Id))).Status);

        // Without the invoice read the list stays closed, and nothing is read.
        invoicing.World.Authority.Denied.Add(InvoiceRead);
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().GetInvoicesAsync(new SalesInvoiceListQuery()))).Status);
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
    public async Task A_shipped_uninvoiced_share_of_a_cancelled_order_is_invoiceable_SAT_D228()
    {
        // SAT-D228 (EK43): a cancelled order's shipped, uninvoiced dispatch note is invoiceable.
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
    public async Task A_withholding_line_without_its_code_waits_only_on_the_code_and_no_threshold_is_guessed()
    {
        var invoicing = await InvoiceWorld.CreateAsync(withholding: true);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-invoice-withholding")));

        Assert.Equal(409, refused.Status);
        Assert.Equal([new SalesDraftFieldError("shares[0]", TaxResolutionReasonsV1.WithholdingCodeMissing)], refused.Errors);
        Assert.Empty(invoicing.Invoices.Stored);
    }

    [Fact]
    public async Task A_coded_withholding_line_is_invoiced_and_its_tl_withholding_is_reachable_end_to_end()
    {
        var invoicing = await InvoiceWorld.CreateAsync(withholding: true, withholdingCode: "625");

        var created = await invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-invoice-withholding-coded"));

        var invoice = created.Invoice;
        // 1.25 kg x 12.50 = 15.63; VAT 20 % = 3.13; withheld 5/10 of the rounded VAT = 1.565 -> 1.57 (SAT-D75); payable 17.19.
        Assert.Equal(new SalesInvoiceAmountsView("15.63", "3.13", "1.57", "18.76", "17.19"), invoice.Totals);
        Assert.Equal("0.5", Assert.Single(invoice.Lines).WithholdingShare);
        // SAT-D224: the withholding TL is the invoice-currency withholding x the rate (1.57 x 34.5678 = 54.27); SAT-D225: payable TL =
        // net TL + VAT TL - withholding TL = 540.29 + 108.20 - 54.27 = 594.22.
        Assert.Equal(new SalesInvoiceAmountsView("540.29", "108.20", "54.27", "648.49", "594.22"), invoice.FunctionalTotals);
        Assert.Equal("17.19", Assert.Single(invoice.Instalments).Amount);
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

    [Fact]
    public async Task The_order_invoices_tab_lists_the_invoices_of_the_order_under_the_draft_read_without_any_amount_API_09()
    {
        var invoicing = await InvoiceWorld.CreateAsync();
        var created = (await invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-invoice-tab"))).Invoice;
        invoicing.World.Authority.Measured.Clear();

        var tab = await invoicing.Service().GetDraftInvoicesAsync(invoicing.Order.Draft.Id);

        Assert.False(tab.HasMore);
        var item = Assert.Single(tab.Items);
        Assert.Equal((created.Id, created.DocumentNo, "Draft", 1, 1, true), (item.Id, item.DocumentNo, item.State, item.OrderLineCount,
            item.InvoiceLineCount, item.HoldsShares));
        // Only the draft read is measured: the tab answers no amount, so neither the invoice read nor the price read is needed.
        Assert.All(invoicing.World.Authority.Measured, name => Assert.Equal(SalesPermissionManifestV1.DraftsRead, name));
        Assert.DoesNotContain("Amount", string.Join(',', typeof(SalesDraftInvoiceItemView).GetProperties().Select(property => property.Name)),
            StringComparison.Ordinal);

        // A rejected invoice stays in the history with its shares released.
        var submitted = await invoicing.Service().SubmitInvoiceAsync(created.Id, Command("1"), Metadata("k-invoice-tab-submit"));
        invoicing.World.User.Id = "synthetic-invoice-approver";
        await invoicing.Service().RejectInvoiceAsync(created.Id, new SalesDraftStateCommandRequest(submitted.Invoice.Version, "neden"),
            Metadata("k-invoice-tab-reject"));
        var after = Assert.Single((await invoicing.Service().GetDraftInvoicesAsync(invoicing.Order.Draft.Id)).Items);
        Assert.Equal(("Rejected", false), (after.State, after.HoldsShares));

        // Another order (or an unknown one) has no invoices; an unknown id is 404 and no invoice is read for it.
        var reads = invoicing.Invoices.OfOrderReads;
        Assert.Equal((404, "not_found"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().GetDraftInvoicesAsync(Guid.NewGuid()))));
        Assert.Equal((404, "not_found"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().GetDraftInvoicesAsync(Guid.Empty))));
        Assert.Equal(reads, invoicing.Invoices.OfOrderReads);
    }

    [Fact]
    public async Task The_order_invoices_tab_needs_the_draft_read_and_fails_closed_when_it_is_lost_API_09()
    {
        var invoicing = await InvoiceWorld.CreateAsync();
        var draftId = invoicing.Order.Draft.Id;
        invoicing.World.Authority.Denied.Add(SalesPermissionManifestV1.DraftsRead);
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => invoicing.Service().GetDraftInvoicesAsync(draftId))).Status);
        Assert.Equal(0, invoicing.Invoices.OfOrderReads);

        // The tab needs the draft read only: without the invoice read and the price read it still answers (no amount leaves).
        invoicing.World.Authority.Denied.Clear();
        invoicing.World.Authority.Denied.Add(SalesPermissionManifestV1.InvoicesRead);
        invoicing.World.Authority.Denied.Add(SalesPermissionManifestV1.OrdersPriceRead);
        Assert.Empty((await invoicing.Service().GetDraftInvoicesAsync(draftId)).Items);

        // A grant lost after the invoice read stops the answer before any invoice leaves.
        invoicing.World.Authority.Denied.Clear();
        invoicing.World.Authority.LostLater = (SalesPermissionManifestV1.DraftsRead, invoicing.World.Authority.Calls + 2);
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => invoicing.Service().GetDraftInvoicesAsync(draftId))).Status);

        invoicing.World.User.Authenticated = false;
        Assert.Equal((401, "unauthenticated"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().GetDraftInvoicesAsync(draftId))));
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
        public bool NoOfficial { get; set; }
        public FakeOfficialInvoices Official { get; } = new();
        public SalesInvoiceMatchTolerancePolicy? Tolerance { get; set; }
        public ISalesFinanceAdmissionPortV1? Finance { get; set; }
        public ISalesInvoiceDiagnosticsV1? Diagnostics { get; set; }

        /// <summary>An invoice world around an order the caller has put in the state under test.</summary>
        public static InvoiceWorld Around(World world, SalesOrderDraftDetailView order) => new(world, order);

        public static async Task<InvoiceWorld> CreateAsync(bool withholding = false, string? withholdingCode = null)
        {
            var world = new World
            {
                LineTaxes = new FakeLineTaxes
                {
                    Defaults =
                    [
                        new TaxItemTypeDefaultDataV1(Guid.NewGuid(), Company, TaxItemTypesV1.Steel, TaxDefaultSourcesV1.Record, 1, Day, true,
                            20m, null, withholding, withholding ? 50m : null, withholdingCode),
                    ],
                    WithholdingCodes = withholdingCode is null ? [] : [withholdingCode],
                },
            };
            var approved = await PricedApprovedAsync(world, "k-invoice-order");
            // Q9 / SAT-D114: the customer accepted the approved revision, so the invoicing gate is open on Rev.0.
            await world.Service().RecordCustomerAcceptanceAsync(approved.Draft.Id, Command(approved.Draft.Version), Metadata("k-invoice-accept"));
            var invoicing = new InvoiceWorld(world, approved);
            // SAT-D224: a coded 50 % withholding line carries 54.27 TL withholding (see the coded withholding test).
            invoicing.Official.Withholding = withholdingCode is null ? 0m : 54.27m;
            return invoicing;
        }

        public CreateSalesInvoiceRequest Request() => new(Account, [new SalesInvoiceShareRequest(ShipmentId)]);

        public SalesInvoiceService Service()
        {
            var company = new CompanyContext();
            company.Set(Company);
            return new SalesInvoiceService(company, World.User, World.Authority, new FixedCorrelation(), Invoices, World.Store, World.Settings,
                World.Clock, NoShipments ? null : Shipments, Rates, NoOfficial ? null : Official, Tolerance, Finance, Diagnostics);
        }
    }

    private const string InvoiceApprove = SalesPermissionManifestV1.InvoicesApprove;

    [Fact]
    public async Task An_invoice_is_submitted_then_approved_by_another_actor_and_each_step_replays_under_its_key()
    {
        var invoicing = await InvoiceWorld.CreateAsync();
        var created = (await invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-inv-create"))).Invoice;

        var submitted = await invoicing.Service().SubmitInvoiceAsync(created.Id, Command("1"), Metadata("k-inv-submit"));
        Assert.Equal(("PendingApproval", "2", false), (submitted.Invoice.State, submitted.Invoice.Version, submitted.IsReplay));

        invoicing.World.User.Id = "synthetic-invoice-approver";
        invoicing.World.Authority.Measured.Clear();
        var approved = await invoicing.Service().ApproveInvoiceAsync(created.Id, Command("2"), Metadata("k-inv-approve"));
        Assert.Equal(("Approved", "3", false), (approved.Invoice.State, approved.Invoice.Version, approved.IsReplay));
        Assert.Equal(InvoiceApprove, invoicing.World.Authority.Measured[0]);
        Assert.True(invoicing.World.Authority.Measured.Count(name => name == InvoiceApprove) >= 2);

        var replay = await invoicing.Service().ApproveInvoiceAsync(created.Id, Command("2"), Metadata("k-inv-approve", "another-correlation"));
        Assert.True(replay.IsReplay);
        Assert.Equal("Approved", replay.Invoice.State);
        Assert.Equal(1, invoicing.Invoices.StateAttempts.Count(entry => entry.Key == "k-inv-approve"));

        var sameKeyOtherContent = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().ApproveInvoiceAsync(created.Id, Command("9"), Metadata("k-inv-approve")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch), Status(sameKeyOtherContent));
    }

    [Theory]
    [InlineData("preparer")]
    [InlineData("submitter")]
    public async Task The_preparer_and_the_submitter_cannot_approve_and_the_refusal_is_recorded_and_replayed(string who)
    {
        var invoicing = await InvoiceWorld.CreateAsync();
        var created = (await invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-inv-create"))).Invoice;
        if (who == "submitter")
        {
            invoicing.World.User.Id = "synthetic-invoice-submitter";
        }

        await invoicing.Service().SubmitInvoiceAsync(created.Id, Command("1"), Metadata("k-inv-submit"));

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().ApproveInvoiceAsync(created.Id, Command("2"), Metadata("k-inv-self")));
        Assert.Equal((409, SalesInvoiceProblemCodesV1.SelfApproval), Status(refused));
        Assert.False(refused.IsReplay);

        var again = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().ApproveInvoiceAsync(created.Id, Command("2"), Metadata("k-inv-self", "another-correlation")));
        Assert.Equal((409, SalesInvoiceProblemCodesV1.SelfApproval), Status(again));
        Assert.True(again.IsReplay);
        Assert.Equal("PendingApproval", (await invoicing.Service().GetInvoiceAsync(created.Id)).State);
    }

    [Fact]
    public async Task An_approver_without_the_approve_permission_or_who_loses_it_before_the_save_writes_nothing()
    {
        var invoicing = await InvoiceWorld.CreateAsync();
        var created = (await invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-inv-create"))).Invoice;
        await invoicing.Service().SubmitInvoiceAsync(created.Id, Command("1"), Metadata("k-inv-submit"));
        invoicing.World.User.Id = "synthetic-invoice-approver";

        invoicing.World.Authority.Denied.Add(InvoiceApprove);
        var denied = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().ApproveInvoiceAsync(created.Id, Command("2"), Metadata("k-inv-denied")));
        Assert.Equal(403, denied.Status);
        Assert.Equal(0, invoicing.Invoices.DecideCalls - 1);

        invoicing.World.Authority.Denied.Clear();
        invoicing.World.Authority.LostLater = (InvoiceApprove, invoicing.World.Authority.Calls + 1);
        var lost = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().ApproveInvoiceAsync(created.Id, Command("2"), Metadata("k-inv-lost")));
        Assert.Equal(403, lost.Status);
        Assert.Equal(1, invoicing.Invoices.DecideCalls);
        Assert.Equal("PendingApproval", (await invoicing.Service().GetInvoiceAsync(created.Id)).State);
    }

    [Fact]
    public async Task A_draft_or_pending_invoice_is_cancelled_with_a_reason_and_an_approved_one_is_a_recorded_state_conflict()
    {
        var invoicing = await InvoiceWorld.CreateAsync();
        var created = (await invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-inv-create"))).Invoice;

        var cancelled = await invoicing.Service().CancelInvoiceAsync(created.Id, new CancelSalesInvoiceRequest("1", "Musteri vazgecti"), Metadata("k-inv-cancel"));
        Assert.Equal(("Cancelled", "2"), (cancelled.Invoice.State, cancelled.Invoice.Version));

        var stale = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CancelInvoiceAsync(created.Id, new CancelSalesInvoiceRequest("1", "Musteri vazgecti"), Metadata("k-inv-stale")));
        Assert.Equal(409, stale.Status);
        var staleAgain = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CancelInvoiceAsync(created.Id, new CancelSalesInvoiceRequest("1", "Musteri vazgecti"), Metadata("k-inv-stale", "another-correlation")));
        Assert.True(staleAgain.IsReplay);
        Assert.Equal((stale.Status, stale.Code), (staleAgain.Status, staleAgain.Code));
    }

    [Fact]
    public async Task An_invoice_state_command_with_a_bad_expected_version_is_refused_before_any_read()
    {
        var invoicing = await InvoiceWorld.CreateAsync();
        var created = (await invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-inv-create"))).Invoice;
        foreach (var bad in new[] { "0", "01x", "-1", "" })
        {
            var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                invoicing.Service().SubmitInvoiceAsync(created.Id, Command(bad), Metadata("k-inv-bad")));
            Assert.Equal((400, "validation_failed"), Status(refused));
        }

        Assert.Equal(0, invoicing.Invoices.DecideCalls);
    }

    private const string InvoiceMatch = SalesPermissionManifestV1.InvoicesMatch;
    private const string OfficialEttn = "0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d";

    private sealed class FakeOfficialInvoices : ISalesOfficialInvoiceReadPortV1
    {
        public SalesOfficialInvoiceAnswerV1 Answer { get; set; } = SalesOfficialInvoiceAnswerV1.NotFound;
        public int Asked { get; private set; }

        public Task<SalesOfficialInvoiceAnswerV1> LookupAsync(Guid companyId, string officialNo, string ettn, CancellationToken cancellationToken = default)
        {
            Asked++;
            return Task.FromResult(Answer);
        }

        /// <summary>SAT-D241: the official VAT and withholding TL totals; by default those of the world's invoice (VAT 108.20 TL).</summary>
        public decimal Vat { get; set; } = 108.20m;
        public decimal Withholding { get; set; }

        public void Has(decimal functionalAmount, Guid? customer = null, Guid? currency = null, string no = "GIB2026000000001",
            decimal? vat = null, decimal? withholding = null) =>
            Answer = new(SalesOfficialInvoiceOutcomeV1.Found, new SalesOfficialInvoiceFacts(no, OfficialEttn, customer ?? Party,
                currency ?? Currency, functionalAmount, functionalAmount, vat ?? Vat, withholding ?? Withholding));
    }

    private static MatchSalesInvoiceRequest MatchRequest(string version = "3", bool approve = false, string? reason = null,
        string no = "GIB2026000000001") => new(version, no, OfficialEttn, approve, reason);

    /// <summary>An approved invoice (prepared and submitted by the world's user, approved by another); the user left is the approver.</summary>
    private static async Task<(InvoiceWorld Invoicing, Guid Id, string Preparer)> ApprovedInvoiceAsync(bool withholding = false,
        string? withholdingCode = null)
    {
        var invoicing = await InvoiceWorld.CreateAsync(withholding, withholdingCode);
        var preparer = invoicing.World.User.Id;
        var created = (await invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-inv-create"))).Invoice;
        await invoicing.Service().SubmitInvoiceAsync(created.Id, Command("1"), Metadata("k-inv-submit"));
        invoicing.World.User.Id = "synthetic-invoice-approver";
        await invoicing.Service().ApproveInvoiceAsync(created.Id, Command("2"), Metadata("k-inv-approve"));
        invoicing.World.User.Id = "synthetic-invoice-matcher";
        return (invoicing, created.Id, preparer!);
    }

    [Theory]
    [InlineData("648.49", "0.00")]
    [InlineData("648.54", "0.05")]
    [InlineData("648.44", "0.05")]
    public async Task A_difference_within_the_inclusive_tolerance_is_matched_without_a_second_approval_and_replays(string official, string difference)
    {
        var (invoicing, id, _) = await ApprovedInvoiceAsync();
        invoicing.Official.Has(decimal.Parse(official, System.Globalization.CultureInfo.InvariantCulture));
        invoicing.World.Authority.Measured.Clear();

        var matched = await invoicing.Service().MatchInvoiceAsync(id, MatchRequest(), Metadata("k-inv-match"));

        var match = Assert.IsType<SalesInvoiceMatchView>(matched.Invoice.Match);
        Assert.Equal(("Approved", "3", false), (matched.Invoice.State, matched.Invoice.Version, matched.IsReplay));
        Assert.Equal(("648.49", official, difference, "0.05", "synthetic-invoice-matcher", null), (match.InvoiceAmount, match.OfficialAmount,
            match.Difference, match.Tolerance, match.MatchedBy, match.DifferenceApprovedBy));
        Assert.Equal(InvoiceMatch, invoicing.World.Authority.Measured[0]);
        Assert.DoesNotContain(InvoiceApprove, invoicing.World.Authority.Measured);

        var replay = await invoicing.Service().MatchInvoiceAsync(id, MatchRequest(), Metadata("k-inv-match", "another-correlation"));
        Assert.True(replay.IsReplay);
        Assert.Equal(1, invoicing.Invoices.MatchCalls);
        Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().MatchInvoiceAsync(id, MatchRequest(no: "GIB2026000000002"), Metadata("k-inv-match")))));
        Assert.Equal("Approved", (await invoicing.Service().GetInvoiceAsync(id)).State);
        Assert.NotNull((await invoicing.Service().GetInvoiceAsync(id)).Match);
    }

    [Fact]
    public async Task A_difference_above_the_tolerance_needs_a_separate_approval_with_a_reason_and_the_approve_permission()
    {
        var (invoicing, id, _) = await ApprovedInvoiceAsync();
        invoicing.Official.Has(648.55m);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().MatchInvoiceAsync(id, MatchRequest(), Metadata("k-inv-over")));
        Assert.Equal((409, SalesInvoiceProblemCodesV1.DifferenceApprovalRequired), Status(refused));
        Assert.Equal(0, invoicing.Invoices.MatchCalls);
        Assert.DoesNotContain("k-inv-over", invoicing.Invoices.StateAttempts.Keys);

        var noReason = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().MatchInvoiceAsync(id, MatchRequest(approve: true), Metadata("k-inv-noreason")));
        Assert.Equal(422, noReason.Status);

        invoicing.World.Authority.Denied.Add(InvoiceApprove);
        var forbidden = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().MatchInvoiceAsync(id, MatchRequest(approve: true, reason: "Kur farki"), Metadata("k-inv-forbid")));
        Assert.Equal(403, forbidden.Status);
        invoicing.World.Authority.Denied.Clear();

        var approved = await invoicing.Service().MatchInvoiceAsync(id, MatchRequest(approve: true, reason: "Kur farki"), Metadata("k-inv-approved"));
        var match = Assert.IsType<SalesInvoiceMatchView>(approved.Invoice.Match);
        Assert.Equal(("0.06", "synthetic-invoice-matcher", "Kur farki"), (match.Difference, match.DifferenceApprovedBy, match.DifferenceReason));
    }

    [Theory]
    [InlineData("vat")]
    [InlineData("withholding")]
    public async Task A_vat_or_withholding_difference_above_the_tolerance_is_refused_with_its_own_field_and_writes_nothing_SAT_D241(
        string field)
    {
        // SAT-D241 (EK48): the payable TL agrees exactly; the VAT (108.20 TL) or the withholding (54.27 TL) differs by 0.06 TL.
        var (invoicing, id, _) = await ApprovedInvoiceAsync(withholding: true, withholdingCode: "625");
        invoicing.Official.Has(594.22m, vat: field == "vat" ? 108.26m : null, withholding: field == "withholding" ? 54.21m : null);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().MatchInvoiceAsync(id, MatchRequest(), Metadata("k-inv-tax-over")));

        Assert.Equal((409, SalesInvoiceProblemCodesV1.DifferenceApprovalRequired), Status(refused));
        Assert.Equal([(field + "Difference", "exceeds_tolerance")], refused.Errors.Select(error => (error.Field, error.Code)));
        Assert.Equal(0, invoicing.Invoices.MatchCalls);
        Assert.DoesNotContain("k-inv-tax-over", invoicing.Invoices.StateAttempts.Keys);
        Assert.Null((await invoicing.Service().GetInvoiceAsync(id)).Match);

        var approved = await invoicing.Service().MatchInvoiceAsync(id, MatchRequest(approve: true, reason: "Vergi farki"),
            Metadata("k-inv-tax-approved"));
        var match = Assert.IsType<SalesInvoiceMatchView>(approved.Invoice.Match);
        Assert.Equal(("0.00", "synthetic-invoice-matcher"), (match.Difference, match.DifferenceApprovedBy));
        Assert.Equal(field == "vat"
                ? ("108.20", "108.26", "0.06", "54.27", "54.27", "0.00")
                : ("108.20", "108.20", "0.00", "54.27", "54.21", "0.06"),
            (match.InvoiceVatAmount, match.OfficialVatAmount, match.VatDifference, match.InvoiceWithholdingAmount,
                match.OfficialWithholdingAmount, match.WithholdingDifference));
    }

    [Fact]
    public async Task A_payable_vat_and_withholding_difference_above_the_tolerance_are_each_named_SAT_D241()
    {
        var (invoicing, id, _) = await ApprovedInvoiceAsync(withholding: true, withholdingCode: "625");
        invoicing.Official.Has(594.30m, vat: 108.10m, withholding: 54.40m);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().MatchInvoiceAsync(id, MatchRequest(), Metadata("k-inv-all-over")));

        Assert.Equal(["difference", "vatDifference", "withholdingDifference"], refused.Errors.Select(error => error.Field));
        Assert.Equal(0, invoicing.Invoices.MatchCalls);
    }

    [Theory]
    [InlineData("-0.01", "0")]
    [InlineData("0", "-0.01")]
    public async Task An_official_document_with_a_negative_vat_or_withholding_is_a_provider_failure_that_writes_nothing_SAT_D241(
        string vat, string withholding)
    {
        var (invoicing, id, _) = await ApprovedInvoiceAsync();
        invoicing.Official.Has(648.49m, vat: decimal.Parse(vat, System.Globalization.CultureInfo.InvariantCulture),
            withholding: decimal.Parse(withholding, System.Globalization.CultureInfo.InvariantCulture));

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().MatchInvoiceAsync(id, MatchRequest(), Metadata("k-inv-neg")));

        Assert.Equal(503, refused.Status);
        Assert.Equal(0, invoicing.Invoices.MatchCalls);
    }

    [Fact]
    public async Task Approving_a_difference_that_is_within_the_tolerance_is_refused()
    {
        var (invoicing, id, _) = await ApprovedInvoiceAsync();
        invoicing.Official.Has(648.50m);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().MatchInvoiceAsync(id, MatchRequest(approve: true, reason: "Gereksiz"), Metadata("k-inv-na")));
        Assert.Equal(422, refused.Status);
        Assert.Null((await invoicing.Service().GetInvoiceAsync(id)).Match);
    }

    [Fact]
    public async Task The_preparer_cannot_approve_a_difference_and_the_refusal_is_recorded_and_replayed()
    {
        var (invoicing, id, preparer) = await ApprovedInvoiceAsync();
        invoicing.Official.Has(700m);
        invoicing.World.User.Id = preparer;

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().MatchInvoiceAsync(id, MatchRequest(approve: true, reason: "Kur"), Metadata("k-inv-self-match")));
        Assert.Equal((409, SalesInvoiceProblemCodesV1.SelfApproval), Status(refused));
        var again = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().MatchInvoiceAsync(id, MatchRequest(approve: true, reason: "Kur"), Metadata("k-inv-self-match", "other")));
        Assert.True(again.IsReplay);
        Assert.Null((await invoicing.Service().GetInvoiceAsync(id)).Match);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("currency")]
    public async Task A_customer_or_currency_that_differs_is_a_mismatch_that_writes_and_records_nothing(string field)
    {
        var (invoicing, id, _) = await ApprovedInvoiceAsync();
        invoicing.Official.Has(648.49m, customer: field == "customer" ? Guid.NewGuid() : null, currency: field == "currency" ? Guid.NewGuid() : null);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().MatchInvoiceAsync(id, MatchRequest(), Metadata("k-inv-mismatch")));

        Assert.Equal((409, SalesInvoiceProblemCodesV1.OfficialMatchMismatch), Status(refused));
        Assert.Contains(refused.Errors, error => error.Field == field);
        Assert.Equal(0, invoicing.Invoices.MatchCalls);
        Assert.DoesNotContain("k-inv-mismatch", invoicing.Invoices.StateAttempts.Keys);
    }

    [Fact]
    public async Task An_official_document_that_is_unknown_or_used_by_another_invoice_is_refused_and_an_absent_provider_is_503()
    {
        var (invoicing, id, _) = await ApprovedInvoiceAsync();
        var unknown = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().MatchInvoiceAsync(id, MatchRequest(), Metadata("k-inv-unknown")));
        Assert.Equal((409, SalesInvoiceProblemCodesV1.OfficialDocumentNotFound), Status(unknown));

        invoicing.Official.Answer = SalesOfficialInvoiceAnswerV1.Unavailable;
        Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().MatchInvoiceAsync(id, MatchRequest(), Metadata("k-inv-503")))).Status);
        invoicing.NoOfficial = true;
        var absent = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().MatchInvoiceAsync(id, MatchRequest(), Metadata("k-inv-503b")));
        Assert.Equal((503, SalesInvoiceProblemCodesV1.OfficialInvoiceProviderUnavailable), Status(absent));
        Assert.Equal(0, invoicing.Invoices.MatchCalls);

        invoicing.NoOfficial = false;
        invoicing.Official.Has(648.49m);
        await invoicing.Service().MatchInvoiceAsync(id, MatchRequest(), Metadata("k-inv-first"));
        var other = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().MatchInvoiceAsync(Guid.NewGuid(), MatchRequest(), Metadata("k-inv-other")));
        Assert.Equal(404, other.Status);
    }

    [Fact]
    public async Task The_tolerance_comes_from_the_configured_policy_and_an_unapproved_invoice_cannot_be_matched()
    {
        var (invoicing, id, _) = await ApprovedInvoiceAsync();
        invoicing.Tolerance = new SalesInvoiceMatchTolerancePolicy(0.10m);
        invoicing.Official.Has(648.59m);
        var matched = await invoicing.Service().MatchInvoiceAsync(id, MatchRequest(), Metadata("k-inv-ten"));
        Assert.Equal(("0.10", "0.10"), (matched.Invoice.Match!.Difference, matched.Invoice.Match.Tolerance));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SalesInvoiceMatchTolerancePolicy(-0.01m));

        var draft = await InvoiceWorld.CreateAsync();
        var created = (await draft.Service().CreateInvoiceAsync(draft.Request(), Metadata("k-inv-draft"))).Invoice;
        draft.Official.Has(648.49m);
        var notApproved = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            draft.Service().MatchInvoiceAsync(created.Id, MatchRequest("1"), Metadata("k-inv-state")));
        Assert.Equal(409, notApproved.Status);
    }

    [Theory]
    [InlineData("{\"expectedVersion\":\"3\",\"officialInvoiceNo\":\"GIB1\",\"ettn\":\"0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d\"}", true)]
    [InlineData("{\"expectedVersion\":\"3\",\"officialInvoiceNo\":\"GIB1\",\"ettn\":\"0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d\",\"approveDifference\":true,\"differenceReason\":\"x\"}", true)]
    [InlineData("{\"expectedVersion\":\"0\",\"officialInvoiceNo\":\"GIB1\",\"ettn\":\"0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d\"}", false)]
    [InlineData("{\"expectedVersion\":\"3\",\"officialInvoiceNo\":\" GIB1\",\"ettn\":\"0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d\"}", false)]
    [InlineData("{\"expectedVersion\":\"3\",\"officialInvoiceNo\":\"GIB1\",\"ettn\":\"not-a-guid\"}", false)]
    [InlineData("{\"expectedVersion\":\"3\",\"officialInvoiceNo\":\"GIB1\",\"ettn\":\"0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d\",\"extra\":1}", false)]
    public void The_match_body_reader_is_strict(string json, bool valid)
    {
        if (valid)
        {
            Assert.Equal("3", SalesInvoiceStateJson.ReadMatch(json).ExpectedVersion);
        }
        else
        {
            Assert.ThrowsAny<System.Text.Json.JsonException>(() => SalesInvoiceStateJson.ReadMatch(json));
        }
    }

    [Fact]
    public void The_match_hash_depends_on_every_field_and_ignores_the_ettn_case()
    {
        var id = Guid.NewGuid();
        var baseline = SalesInvoiceMatchHash.Compute(id, MatchRequest());
        Assert.Equal(baseline, SalesInvoiceMatchHash.Compute(id, MatchRequest() with { Ettn = OfficialEttn.ToUpperInvariant() }));
        Assert.NotEqual(baseline, SalesInvoiceMatchHash.Compute(Guid.NewGuid(), MatchRequest()));
        Assert.NotEqual(baseline, SalesInvoiceMatchHash.Compute(id, MatchRequest("4")));
        Assert.NotEqual(baseline, SalesInvoiceMatchHash.Compute(id, MatchRequest(approve: true, reason: "x")));
        Assert.NotEqual(baseline, SalesInvoiceMatchHash.Compute(id, MatchRequest(no: "GIB2026000000009")));
    }

    /// <summary>An in-memory invoice store with the attempt rules of the real one (same key + content + actor replays; else 409).</summary>
    private sealed class FakeInvoiceStore : ISalesInvoiceStore
    {
        public Dictionary<Guid, SalesInvoice> Stored { get; } = [];
        public Dictionary<string, (string Hash, string Actor, Guid Invoice, string Correlation)> Attempts { get; } = [];
        public HashSet<Guid> Invoiced { get; } = [];

        public Dictionary<string, (string Hash, string Actor, Guid Invoice, string Correlation, SalesDraftTerminalRefusal? Refusal)> StateAttempts { get; } = [];
        public int DecideCalls { get; private set; }

        public Task<SalesInvoiceReplay?> FindInvoiceStateReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
            CancellationToken cancellationToken = default)
        {
            if (!StateAttempts.TryGetValue(envelope.IdempotencyKey, out var attempt))
            {
                return Task.FromResult<SalesInvoiceReplay?>(null);
            }

            return attempt.Hash == requestHash && attempt.Actor == envelope.ActorId
                ? Task.FromResult<SalesInvoiceReplay?>(new SalesInvoiceReplay(attempt.Refusal is null ? Stored[attempt.Invoice] : null, attempt.Refusal, attempt.Correlation))
                : throw new SalesDraftStoreException(SalesDraftStoreCodes.SameKeyPayloadMismatch, "same key, other content");
        }

        public Task RecordInvoiceRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName, Guid invoiceId,
            SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken = default)
        {
            StateAttempts[envelope.IdempotencyKey] = (requestHash, envelope.ActorId, invoiceId, envelope.CorrelationId, refusal);
            return Task.CompletedTask;
        }

        public Task<SalesInvoiceStoreResult?> DecideInvoiceAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
            Guid invoiceId, Action<SalesInvoice, DateTimeOffset> mutate, SalesDraftStateAudit audit, CancellationToken cancellationToken = default)
        {
            DecideCalls++;
            if (!Stored.TryGetValue(invoiceId, out var invoice))
            {
                return Task.FromResult<SalesInvoiceStoreResult?>(null);
            }

            mutate(invoice, DateTimeOffset.UnixEpoch);
            StateAttempts[envelope.IdempotencyKey] = (requestHash, envelope.ActorId, invoiceId, envelope.CorrelationId, null);
            return Task.FromResult<SalesInvoiceStoreResult?>(new SalesInvoiceStoreResult(invoice, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId));
        }

        public int MatchCalls { get; private set; }

        public Task<SalesInvoiceStoreResult?> MatchInvoiceAsync(SalesDraftWriteEnvelope envelope, string requestHash, Guid invoiceId,
            Func<SalesInvoice, DateTimeOffset, SalesInvoiceMatchFacts> record, SalesDraftStateAudit audit,
            CancellationToken cancellationToken = default)
        {
            MatchCalls++;
            if (!Stored.TryGetValue(invoiceId, out var invoice))
            {
                return Task.FromResult<SalesInvoiceStoreResult?>(null);
            }

            record(invoice, DateTimeOffset.UnixEpoch);
            StateAttempts[envelope.IdempotencyKey] = (requestHash, envelope.ActorId, invoiceId, envelope.CorrelationId, null);
            return Task.FromResult<SalesInvoiceStoreResult?>(new SalesInvoiceStoreResult(invoice, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId));
        }

        public int FinalizeCalls { get; private set; }

        public int FinanceAttemptCalls { get; private set; }

        public Task<SalesInvoiceStoreResult?> FinalizeInvoiceAsync(SalesDraftWriteEnvelope envelope, string requestHash, Guid invoiceId,
            Action<SalesInvoice, DateTimeOffset> finalize, SalesDraftStateAudit audit, CancellationToken cancellationToken = default)
        {
            FinalizeCalls++;
            if (!Stored.TryGetValue(invoiceId, out var invoice))
            {
                return Task.FromResult<SalesInvoiceStoreResult?>(null);
            }

            finalize(invoice, DateTimeOffset.UnixEpoch);
            StateAttempts[envelope.IdempotencyKey] = (requestHash, envelope.ActorId, invoiceId, envelope.CorrelationId, null);
            return Task.FromResult<SalesInvoiceStoreResult?>(new SalesInvoiceStoreResult(invoice, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId));
        }

        public Task<SalesInvoiceStoreResult?> RecordFinanceAttemptAsync(SalesDraftWriteEnvelope envelope, string? requestHash, string? commandName,
            Guid invoiceId, Action<SalesInvoice, DateTimeOffset> record, SalesDraftStateAudit audit, CancellationToken cancellationToken = default)
        {
            FinanceAttemptCalls++;
            if (!Stored.TryGetValue(invoiceId, out var invoice))
            {
                return Task.FromResult<SalesInvoiceStoreResult?>(null);
            }

            record(invoice, DateTimeOffset.UnixEpoch);
            return Task.FromResult<SalesInvoiceStoreResult?>(new SalesInvoiceStoreResult(invoice, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId));
        }

        public Task<Guid?> FindMatchedInvoiceIdAsync(string officialNo, string ettn, CancellationToken cancellationToken = default) =>
            Task.FromResult(Stored.Values.Where(invoice => invoice.Match is { } match && (match.OfficialNo == officialNo || match.Ettn == ettn))
                .Select(invoice => (Guid?)invoice.Id).FirstOrDefault());

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

        public int OfOrderReads { get; private set; }

        public Task<SalesOrderInvoicePage> ListInvoicesOfOrderAsync(Guid salesOrderId, int limit, CancellationToken cancellationToken = default)
        {
            OfOrderReads++;
            var found = Stored.Values.Where(invoice => invoice.Lines.Any(line => line.Share.SalesOrderId == salesOrderId))
                .OrderByDescending(invoice => invoice.CreatedAtUtc).ToList();
            return Task.FromResult(new SalesOrderInvoicePage([.. found.Take(limit)], found.Count > limit));
        }

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
