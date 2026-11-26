using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// F2 (SATIS_RISK_SEVK_KAPISI_VE_FASON_FATURA_PLANI_v1 §F2) through the service over SYNTHETIC owners: the write and the price read
/// are measured first; the shape is checked before any owner is asked; another company's or an unknown order is a 404; a normal
/// order, a missing weighing (LOJ-D12), a weighing of another order or a shared weighing without theoretical kg (SAT-D376) are 409
/// <c>invoice_not_ready</c> and write nothing; an unavailable, throwing or inconsistent Logistics answer is a 503; a foreign-currency
/// order and a non-kg line wait together (open points), never guessed. SENTETIK.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task A_subcontract_service_invoice_without_the_invoice_write_is_forbidden_before_any_owner_is_asked()
    {
        var invoicing = await SubcontractInvoicing.CreateAsync();
        invoicing.World.Authority.Denied.Add(InvoiceWrite);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateSubcontractServiceInvoiceAsync(invoicing.Request(), Metadata("k-fason-inv-denied")));

        Assert.Equal(403, refused.Status);
        Assert.Empty(invoicing.Weighings.Asked);
        Assert.Empty(invoicing.Invoices.Stored);
    }

    [Fact]
    public async Task A_subcontract_service_invoice_request_without_its_three_identities_is_a_validation_error()
    {
        var invoicing = await SubcontractInvoicing.CreateAsync();

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => invoicing.Service().CreateSubcontractServiceInvoiceAsync(
            new CreateSalesSubcontractServiceInvoiceRequest(Guid.Empty, Guid.Empty, Guid.Empty), Metadata("k-fason-inv-shape")));

        Assert.Equal((400, "validation_failed"), Status(refused));
        Assert.Equal(["salesOrderId", "loadingInstructionId", "weighingId"], refused.Errors.Select(error => error.Field));
        Assert.Empty(invoicing.Weighings.Asked);
    }

    [Fact]
    public async Task An_unknown_order_is_not_found_and_no_weighing_is_read_for_it()
    {
        var invoicing = await SubcontractInvoicing.CreateAsync();

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => invoicing.Service().CreateSubcontractServiceInvoiceAsync(
            invoicing.Request() with { SalesOrderId = Guid.NewGuid() }, Metadata("k-fason-inv-404")));

        Assert.Equal((404, "not_found"), Status(refused));
        Assert.Empty(invoicing.Weighings.Asked);
        Assert.Empty(invoicing.Invoices.Stored);
    }

    [Fact]
    public async Task A_normal_order_is_not_billed_as_a_subcontract_service_and_no_weighing_is_read()
    {
        var invoicing = await SubcontractInvoicing.CreateAsync();
        var normal = (await invoicing.World.Service().CreateDraftAsync(RequestWithQuantity("5", null), Metadata("k-fason-inv-normal"))).Detail;

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => invoicing.Service().CreateSubcontractServiceInvoiceAsync(
            invoicing.Request() with { SalesOrderId = normal.Draft.Id }, Metadata("k-fason-inv-not-fason")));

        Assert.Equal((409, SalesInvoiceProblemCodesV1.InvoiceNotReady), Status(refused));
        Assert.Equal(("salesOrderId", SalesSubcontractServiceInvoiceReasonCodesV1.NotSubcontractOrder),
            (Assert.Single(refused.Errors).Field, refused.Errors[0].Code));
        Assert.Empty(invoicing.Weighings.Asked);
    }

    [Fact]
    public async Task LOJ_D12_no_delivery_weighing_means_no_invoice_and_nothing_is_recorded_under_the_key()
    {
        var invoicing = await SubcontractInvoicing.CreateAsync();
        invoicing.Weighings.Status = SalesSubcontractDeliveryWeighingStatusV1.NoSource;

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateSubcontractServiceInvoiceAsync(invoicing.Request(), Metadata("k-fason-inv-no-weighing")));

        Assert.Equal((409, SalesInvoiceProblemCodesV1.InvoiceNotReady), Status(refused));
        Assert.Equal(("weighingId", SalesSubcontractServiceInvoiceReasonCodesV1.WeighingMissing),
            (Assert.Single(refused.Errors).Field, refused.Errors[0].Code));
        Assert.Equal([(Company, invoicing.InstructionId, invoicing.WeighingId)], invoicing.Weighings.Asked);
        Assert.Empty(invoicing.Invoices.Stored);
        Assert.Empty(invoicing.Invoices.Attempts);
    }

    [Theory]
    [InlineData("unavailable", "service_unavailable")]
    [InlineData("throws", "service_unavailable")]
    [InlineData("missing-port", "service_unavailable")]
    [InlineData("other-company", SalesDraftProblemCodesV1.ReferenceProviderInvalid)]
    [InlineData("other-weighing", SalesDraftProblemCodesV1.ReferenceProviderInvalid)]
    [InlineData("no-value", SalesDraftProblemCodesV1.ReferenceProviderInvalid)]
    [InlineData("zero-kg", SalesDraftProblemCodesV1.ReferenceProviderInvalid)]
    public async Task An_unavailable_or_inconsistent_delivery_weighing_answer_is_a_503_never_a_guessed_quantity(string fault, string code)
    {
        var invoicing = await SubcontractInvoicing.CreateAsync();
        switch (fault)
        {
            case "unavailable":
                invoicing.Weighings.Status = SalesSubcontractDeliveryWeighingStatusV1.Unavailable;
                break;
            case "throws":
                invoicing.Weighings.Throws = true;
                break;
            case "missing-port":
                invoicing.NoWeighings = true;
                break;
            case "other-company":
                invoicing.Weighings.AnswerCompany = Guid.NewGuid();
                break;
            case "other-weighing":
                invoicing.Weighings.AnswerWeighing = Guid.NewGuid();
                break;
            case "no-value":
                invoicing.Weighings.Value = null;
                break;
            case "zero-kg":
                invoicing.Weighings.Value = invoicing.Weighings.Value! with { NetKg = "0.000" };
                break;
        }

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateSubcontractServiceInvoiceAsync(invoicing.Request(), Metadata($"k-fason-inv-{fault}")));

        Assert.Equal((503, code), Status(refused));
        Assert.Empty(invoicing.Invoices.Stored);
    }

    [Fact]
    public async Task A_weighing_of_another_order_is_a_mismatch_and_a_shared_weighing_without_theoretical_kg_waits_and_writes_nothing()
    {
        var invoicing = await SubcontractInvoicing.CreateAsync();
        var served = invoicing.Weighings.Value!.Lines[0];
        invoicing.Weighings.Value = invoicing.Weighings.Value with { Lines = [served with { SourceOrderId = Guid.NewGuid() }] };

        var mismatch = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateSubcontractServiceInvoiceAsync(invoicing.Request(), Metadata("k-fason-inv-mismatch")));
        Assert.Equal(("loadingInstructionId", SalesSubcontractServiceInvoiceReasonCodesV1.WeighingOrderMismatch),
            (Assert.Single(mismatch.Errors).Field, mismatch.Errors[0].Code));

        // SAT-D376 (fail closed): the instruction also served another order's line, and a line has no theoretical kg to weigh
        // the split by; nothing is split by guess.
        var other = served with { SourceOrderId = Guid.NewGuid(), SourceOrderLineId = Guid.NewGuid(), TheoreticalKg = "400.000" };
        invoicing.Weighings.Value = invoicing.Weighings.Value with { Lines = [served, other] };
        var shared = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateSubcontractServiceInvoiceAsync(invoicing.Request(), Metadata("k-fason-inv-shared")));
        Assert.Equal((409, SalesInvoiceProblemCodesV1.InvoiceNotReady), Status(shared));
        Assert.Equal(("loadingInstructionId", SalesSubcontractServiceInvoiceReasonCodesV1.TheoreticalKgUnavailable),
            (Assert.Single(shared.Errors).Field, shared.Errors[0].Code));
        Assert.Empty(invoicing.Invoices.Stored);
    }

    [Fact]
    public async Task A_shared_weighing_with_theoretical_kg_is_split_and_no_longer_waits_for_a_share_policy()
    {
        // SAT-D376/D402: with every line's theoretical kg the split is computed; the synthetic order then waits only for its own
        // currency and unit (open in this synthetic world), never for the retired share-policy reason.
        var invoicing = await SubcontractInvoicing.CreateAsync();
        var served = invoicing.Weighings.Value!.Lines[0] with { TheoreticalKg = "800.000" };
        var other = served with { SourceOrderId = Guid.NewGuid(), SourceOrderLineId = Guid.NewGuid(), TheoreticalKg = "400.000" };
        invoicing.Weighings.Value = invoicing.Weighings.Value with { Lines = [served, other] };

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateSubcontractServiceInvoiceAsync(invoicing.Request(), Metadata("k-fason-inv-split")));

        Assert.Equal((409, SalesInvoiceProblemCodesV1.InvoiceNotReady), Status(refused));
        Assert.DoesNotContain(refused.Errors, error => error.Code is SalesSubcontractServiceInvoiceReasonCodesV1.SharePolicyPending
            or SalesSubcontractServiceInvoiceReasonCodesV1.TheoreticalKgUnavailable);
        Assert.Contains(refused.Errors, error =>
            error is { Field: "lines[0]", Code: SalesSubcontractServiceInvoiceReasonCodesV1.LineNotKilogram });
        Assert.Empty(invoicing.Invoices.Stored);
    }

    [Fact]
    public async Task A_foreign_currency_draft_no_longer_waits_for_rate_day_policy_but_non_kilogram_service_still_waits()
    {
        // SAT-D380 fixes the rate day at official matching. Native draft pricing no longer waits for that decided policy;
        // a service line in an unsupported unit still cannot be billed by guessing a conversion.
        var invoicing = await SubcontractInvoicing.CreateAsync();

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateSubcontractServiceInvoiceAsync(invoicing.Request(), Metadata("k-fason-inv-pending")));

        Assert.Equal((409, SalesInvoiceProblemCodesV1.InvoiceNotReady), Status(refused));
        Assert.NotEqual(SettingsCurrencyIdentitiesV1.TurkishLira, Currency);
        Assert.DoesNotContain(refused.Errors, error => error.Code == SalesSubcontractServiceInvoiceReasonCodesV1.CurrencyPending);
        Assert.Contains(refused.Errors, error =>
            error is { Field: "lines[0]", Code: SalesSubcontractServiceInvoiceReasonCodesV1.LineNotKilogram });
        Assert.Empty(invoicing.Invoices.Stored);
        Assert.Empty(invoicing.Invoices.Attempts);
        Assert.Equal(InvoiceWrite, invoicing.World.Authority.Measured[0]);
        Assert.Contains(InvoicePriceRead, invoicing.World.Authority.Measured);
    }

    [Fact]
    public async Task The_invoice_write_lost_while_the_weighing_is_read_writes_nothing()
    {
        var invoicing = await SubcontractInvoicing.CreateAsync();
        invoicing.Weighings.OnRead = () => invoicing.World.Authority.Denied.Add(InvoiceWrite);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateSubcontractServiceInvoiceAsync(invoicing.Request(), Metadata("k-fason-inv-lost")));

        Assert.Equal(403, refused.Status);
        Assert.Single(invoicing.Weighings.Asked);
        Assert.Empty(invoicing.Invoices.Stored);
    }

    [Fact]
    public async Task Without_the_subcontract_invoice_store_the_service_is_unavailable_and_nothing_is_read()
    {
        var invoicing = await SubcontractInvoicing.CreateAsync();
        invoicing.NoStore = true;

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateSubcontractServiceInvoiceAsync(invoicing.Request(), Metadata("k-fason-inv-no-store")));

        Assert.Equal((503, "service_unavailable"), Status(refused));
        Assert.Empty(invoicing.Weighings.Asked);
        Assert.Equal((503, "service_unavailable"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().GetSubcontractServiceInvoiceAsync(Guid.NewGuid()))));
    }

    [Fact]
    public async Task A_stored_invoice_under_the_same_key_is_replayed_before_any_owner_is_asked()
    {
        var invoicing = await SubcontractInvoicing.CreateAsync();
        var stored = SalesSubcontractServiceInvoice.Create(Guid.NewGuid(), Company, "SFH-2026-000001",
            new SalesSubcontractServiceInvoiceHeader(Party, Account, Currency, Guid.NewGuid(), "1", 2, Day),
            new SalesSubcontractServiceInvoiceSource(invoicing.Order.Draft.Id, invoicing.InstructionId, invoicing.WeighingId, 2, 4, "10"),
            [new SalesSubcontractServiceLineInput(Guid.NewGuid(), invoicing.Order.Lines[0].LineId, 0, Product, KgUnit, 10m, "10",
                new GoymenERP.Modules.Sales.Domain.Drafts.CommercialDraftFields("2.5", KgUnit, null), SubcontractTax())],
            10m, "SENTETIK-preparer", DateTimeOffset.UnixEpoch);
        invoicing.Invoices.Seed("k-fason-inv-replay", SalesSubcontractServiceInvoiceRequestHash.Compute(invoicing.Request()), stored);

        var replay = await invoicing.Service().CreateSubcontractServiceInvoiceAsync(invoicing.Request(),
            Metadata("k-fason-inv-replay", "another-correlation"));

        Assert.True(replay.IsReplay);
        Assert.Equal((stored.Id, "SFH-2026-000001", "10", "synthetic-correlation"),
            (replay.Invoice.Id, replay.Invoice.DocumentNo, replay.Invoice.NetKg, replay.OriginalCorrelationId));
        Assert.Empty(invoicing.Weighings.Asked);

        var otherContent = await Assert.ThrowsAsync<SalesDraftServiceException>(() => invoicing.Service().CreateSubcontractServiceInvoiceAsync(
            invoicing.Request() with { WeighingId = Guid.NewGuid() }, Metadata("k-fason-inv-replay")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch), Status(otherContent));
    }

    private static GoymenERP.Modules.Sales.Domain.Drafts.SalesLineTaxSnapshot SubcontractTax() =>
        new(Product, Day, "Normal", "Service", "Resolved", null, "Record", Guid.NewGuid(), 1, "Resolved", 20m, "NotRequired", null, null,
            "NotApplied", null, "NotRequired", null, null, null, 0, "Normal", false);

    /// <summary>An approved, customer-accepted subcontract order (one priced kg line) and a synthetic delivery weighing of it.</summary>
    private sealed class SubcontractInvoicing
    {
        private SubcontractInvoicing(World world, SalesOrderDraftDetailView order)
        {
            World = world;
            Order = order;
            Weighings = new FakeDeliveryWeighings(new SalesSubcontractDeliveryWeighingV1("1234.560", 2, 4,
                [new SalesSubcontractDeliveryWeighingLineV1(order.Draft.Id, order.Lines[0].LineId, Product, KgUnit)]));
        }

        public World World { get; }
        public SalesOrderDraftDetailView Order { get; }
        public Guid InstructionId { get; } = Guid.NewGuid();
        public Guid WeighingId { get; } = Guid.NewGuid();
        public FakeDeliveryWeighings Weighings { get; }
        public FakeSubcontractInvoiceStore Invoices { get; } = new();
        public bool NoStore { get; set; }
        public bool NoWeighings { get; set; }

        public static async Task<SubcontractInvoicing> CreateAsync()
        {
            var world = new World { ItemTypes = new FakeItemTypes() };
            var created = (await world.Service().CreateDraftAsync(Subcontract("2.50"), Metadata("k-fason-inv-order"))).Detail;
            var submitted = await world.Service().SubmitDraftAsync(created.Draft.Id, Command(created.Draft.Version),
                Metadata("k-fason-inv-submit"));
            var approved = (await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(created.Draft.Id,
                Command(submitted.Detail.Draft.Version), Metadata("k-fason-inv-approve")))).Detail;
            // Q9 / SAT-D114: the customer accepted the approved revision, so the invoicing gate is open.
            await world.Service().RecordCustomerAcceptanceAsync(approved.Draft.Id, Command(approved.Draft.Version),
                Metadata("k-fason-inv-accept"));
            world.Authority.Measured.Clear();
            return new SubcontractInvoicing(world, approved);
        }

        public CreateSalesSubcontractServiceInvoiceRequest Request() => new(Order.Draft.Id, InstructionId, WeighingId);

        public SalesInvoiceService Service()
        {
            var company = new CompanyContext();
            company.Set(Company);
            Weighings.Instruction = InstructionId;
            Weighings.Weighing = WeighingId;
            return new SalesInvoiceService(company, World.User, World.Authority, new FixedCorrelation(), new FakeInvoiceStore(), World.Store,
                World.Settings, World.Clock, null, new FakeExchangeRates(ExchangeRateResolutionStatesV1.Resolved, "34.5678"), null, null, null,
                null, NoStore ? null : Invoices, NoWeighings ? null : Weighings);
        }
    }

    private sealed class FakeDeliveryWeighings(SalesSubcontractDeliveryWeighingV1 value) : ISalesSubcontractDeliveryWeighingReadPortV1
    {
        public SalesSubcontractDeliveryWeighingV1? Value { get; set; } = value;
        public SalesSubcontractDeliveryWeighingStatusV1 Status { get; set; } = SalesSubcontractDeliveryWeighingStatusV1.Available;
        public bool Throws { get; set; }
        public Guid? AnswerCompany { get; set; }
        public Guid? AnswerWeighing { get; set; }
        public Guid Instruction { get; set; }
        public Guid Weighing { get; set; }
        public Action? OnRead { get; set; }
        public List<(Guid Company, Guid Instruction, Guid Weighing)> Asked { get; } = [];

        public Task<SalesSubcontractDeliveryWeighingReadV1> ReadAsync(Guid companyId, Guid loadingInstructionId, Guid weighingId,
            CancellationToken cancellationToken = default)
        {
            Asked.Add((companyId, loadingInstructionId, weighingId));
            OnRead?.Invoke();
            if (Throws)
            {
                throw new InvalidOperationException("SENTETIK owner fault");
            }

            return Task.FromResult(new SalesSubcontractDeliveryWeighingReadV1(Status, AnswerCompany ?? companyId, loadingInstructionId,
                AnswerWeighing ?? weighingId, Status == SalesSubcontractDeliveryWeighingStatusV1.Available ? Value : null));
        }
    }

    private sealed class FakeSubcontractInvoiceStore : ISalesSubcontractServiceInvoiceStore
    {
        public Dictionary<Guid, SalesSubcontractServiceInvoice> Stored { get; } = [];
        public Dictionary<string, (string Hash, string Actor, Guid Invoice, string Correlation)> Attempts { get; } = [];

        public void Seed(string key, string hash, SalesSubcontractServiceInvoice invoice)
        {
            Stored[invoice.Id] = invoice;
            Attempts[key] = (hash, Actor, invoice.Id, "synthetic-correlation");
        }

        public Task<SalesSubcontractServiceInvoiceStoreResult> StoreSubcontractServiceInvoiceAsync(SalesDraftWriteEnvelope envelope,
            string requestHash, Func<string, SalesSubcontractServiceInvoice> build, CancellationToken cancellationToken = default)
        {
            if (Replay(envelope, requestHash) is { } replay)
            {
                return Task.FromResult(replay);
            }

            var invoice = build($"SFH-2026-{Stored.Count + 1:D6}");
            if (Stored.Values.Any(existing => existing.Source.SalesOrderId == invoice.Source.SalesOrderId
                && existing.Source.LoadingInstructionId == invoice.Source.LoadingInstructionId
                && existing.Source.WeighingId == invoice.Source.WeighingId))
            {
                throw new SalesDraftStoreException(SalesDraftStoreCodes.ConcurrentChange, "SENTETIK unique key");
            }

            Stored[invoice.Id] = invoice;
            Attempts[envelope.IdempotencyKey] = (requestHash, envelope.ActorId, invoice.Id, envelope.CorrelationId);
            return Task.FromResult(new SalesSubcontractServiceInvoiceStoreResult(invoice, SalesDraftWriteOutcome.Recorded,
                envelope.CorrelationId));
        }

        public Task<SalesSubcontractServiceInvoiceStoreResult?> FindSubcontractServiceInvoiceReplayAsync(SalesDraftWriteEnvelope envelope,
            string requestHash, CancellationToken cancellationToken = default) => Task.FromResult(Replay(envelope, requestHash));

        public Task<SalesSubcontractServiceInvoice?> FindSubcontractServiceInvoiceAsync(Guid invoiceId,
            CancellationToken cancellationToken = default) => Task.FromResult(Stored.GetValueOrDefault(invoiceId));

        public Task<SalesSubcontractServiceInvoice?> FindSubcontractServiceInvoiceByWeighingAsync(Guid salesOrderId,
            Guid loadingInstructionId, Guid weighingId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Stored.Values.FirstOrDefault(invoice => invoice.Source.SalesOrderId == salesOrderId
                && invoice.Source.LoadingInstructionId == loadingInstructionId && invoice.Source.WeighingId == weighingId));

        private SalesSubcontractServiceInvoiceStoreResult? Replay(SalesDraftWriteEnvelope envelope, string requestHash)
        {
            if (!Attempts.TryGetValue(envelope.IdempotencyKey, out var attempt))
            {
                return null;
            }

            return attempt.Hash == requestHash && attempt.Actor == envelope.ActorId
                ? new SalesSubcontractServiceInvoiceStoreResult(Stored[attempt.Invoice], SalesDraftWriteOutcome.Replayed, attempt.Correlation)
                : throw new SalesDraftStoreException(SalesDraftStoreCodes.SameKeyPayloadMismatch, "same key, other content");
        }
    }
}
