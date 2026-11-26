using System.Globalization;
using System.Net;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.SalesRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Requests;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesOrderUserCloseSqlServerRelationalAcceptanceTests
{
    // Bounded composition evidence, NOT full downstream acceptance: Sales draft/shipment/finalization, invoice claims,
    // Finance admission/allocations/read, Purchasing request/read, status and close route are real SQL owner paths.
    // Posted dispatch/inventory proofs and absence of Inventory/Manufacturing obligations are explicit SENTETIK seams.
    // The recorded receipt seed, tax/official-source snapshot and the inherited Settings ladder are SENTETIK too.
    [SqlServerFact]
    public Task Finalized_invoice_and_all_settled_installments_enable_native_close_with_real_owner_axes_on_sql() =>
        VerifyActualAxes(unsettledInstallment: false, openSourceRequest: false);

    [SqlServerFact]
    public Task One_unsettled_installment_blocks_native_close_even_when_invoice_and_source_axes_are_complete_on_sql() =>
        VerifyActualAxes(unsettledInstallment: true, openSourceRequest: false);

    [SqlServerFact]
    public Task A_real_unsourced_sales_request_share_blocks_native_close_after_the_whole_invoice_settles_on_sql() =>
        VerifyActualAxes(unsettledInstallment: false, openSourceRequest: true);

    [SqlServerFact]
    public async Task Actual_host_without_posted_dispatch_proof_never_reports_complete_source_or_ready_on_sql()
    {
        await using var run = await Run.StartAsync(readySeam: true, configure: services =>
        {
            services.RemoveAll<ISalesOrderOwnerAxesSourceV1>();
            services.AddScoped<ISalesOrderOwnerAxesSourceV1, SalesOrderOwnerAxesHostV1>();
        });
        var draft = await run.ApprovedDraftAsync(CompanyA, "actual-missing-proof", "6");
        await run.ShippedInFullAsync(CompanyA, draft, "actual-missing-proof");
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersClose);
        using var client = run.Client(subject, CompanyA);
        var status = await run.JsonAsync(client, $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/status");
        Assert.Contains(status.GetProperty("source").GetProperty("state").GetString(),
            new[] { SalesDraftStatusAxisStatesV1.Partial, SalesDraftStatusAxisStatesV1.Unreadable });
        Assert.False(status.GetProperty("readyToClose").GetBoolean());
        using var refused = await client.SendAsync(Close($"{SalesEndpoints.DraftsPath}/{draft.Id:D}/close", "missing-proof-close", "4"));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftUserClosures WHERE DraftId=@id", ("@id", draft.Id)));
    }

    private static async Task VerifyActualAxes(bool unsettledInstallment, bool openSourceRequest)
    {
        var downstream = new SyntheticClosureDownstream();
        await using var run = await Run.StartAsync(readySeam: true, configure: downstream.Configure);
        var draft = await run.ApprovedDraftAsync(CompanyA, "actual-axes", "6");
        await run.ShippedInFullAsync(CompanyA, draft, "actual-axes");
        downstream.Draft = draft;
        var invoice = await FinalizeClosureInvoice(run, downstream);
        await AdmitAndAllocateClosureInvoice(run, invoice, unsettledInstallment);
        await PrepareClosurePurchasing(run, draft, openSourceRequest);
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersClose);
        using var client = run.Client(subject, CompanyA);
        var status = await run.JsonAsync(client, $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/status");
        Assert.Equal(SalesDraftStatusAxisStatesV1.Completed, status.GetProperty("realization").GetProperty("state").GetString());
        Assert.Equal(SalesDraftStatusAxisStatesV1.Completed, status.GetProperty("invoicing").GetProperty("state").GetString());
        Assert.Equal(openSourceRequest ? SalesDraftStatusAxisStatesV1.Partial : SalesDraftStatusAxisStatesV1.Completed,
            status.GetProperty("source").GetProperty("state").GetString());
        Assert.Equal(unsettledInstallment ? SalesDraftStatusAxisStatesV1.Partial : SalesDraftStatusAxisStatesV1.Completed,
            status.GetProperty("financialClosure").GetProperty("state").GetString());
        var ready = !unsettledInstallment && !openSourceRequest;
        Assert.Equal(ready, status.GetProperty("readyToClose").GetBoolean());
        using var closed = await client.SendAsync(Close($"{SalesEndpoints.DraftsPath}/{draft.Id:D}/close", "actual-axes-close", "4"));
        Assert.Equal(ready ? HttpStatusCode.OK : HttpStatusCode.Conflict, closed.StatusCode);
        Assert.Equal(ready ? 1 : 0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftUserClosures WHERE DraftId=@id", ("@id", draft.Id)));
        Assert.Equal(ready ? 5L : 4L, await run.ScalarAsync<long>("SELECT Version FROM sales.Drafts WHERE Id=@id", ("@id", draft.Id)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId=@id", ("@id", invoice.Id)));
        // Neither the composition nor the native close creates another allocation, payment, or invoice.
        Assert.Equal(unsettledInstallment ? 1 : 2,
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Allocations WHERE OpenItemId IN (SELECT Id FROM finance.OpenItems WHERE SourceId=@id)", ("@id", invoice.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.InvoiceFinalizations WHERE InvoiceId=@id AND FinalizedVersion=4 AND FinanceStatus=2", ("@id", invoice.Id)));
    }

    private static async Task<SalesInvoice> FinalizeClosureInvoice(Run run, SyntheticClosureDownstream downstream)
    {
        await using var db = run.SalesContext(CompanyA);
        var company = new CompanyContext(); company.Set(CompanyA);
        var invoices = (ISalesInvoiceStore)new SalesStore(db, company, new AuditEventCollector(), TimeProvider.System);
        var id = Guid.NewGuid();
        var draft = downstream.Draft;
        var line = Assert.Single(draft.Lines);
        var tax = new SalesLineTaxSnapshot(Product, Day, "Normal", "Steel", "Resolved", null, "Record", Guid.NewGuid(), 1,
            "Resolved", 20m, "NotRequired", null, null, "NotApplied", null, "NotRequired", null, null, null, 0, "Normal", false);
        var terms = SalesDueTerms.Create([new(1, SalesDueBasis.OrderDate, 30, "50"), new(2, SalesDueBasis.OrderDate, 60, "50")]);
        var share = new SalesInvoiceShareInput(Guid.NewGuid(), downstream.Shipment, downstream.Note, "SENTETIK-NOTE", 1, Day,
            draft.Id, line.Id, draft.Revision, Product, KgUnit, 6m, "6", new CommercialDraftFields("10", KgUnit, null), tax);
        await invoices.StoreInvoiceAsync(run.SalesEnvelope("axes-invoice-create"), Hash("axes-invoice-create"), number =>
            SalesInvoice.Create(id, CompanyA, number, new(draft.Header.PartyId, draft.Header.CompanyAccountId, Currency,
                MoneyProfile, "3", 2, Day, null), [share], SalesDueDateCalculator.Compute(terms, Day, Day).DueDates,
                terms, run.Actor, T0));
        await invoices.DecideInvoiceAsync(run.SalesEnvelope("axes-invoice-submit"), Hash("axes-invoice-submit"), SalesInvoiceCommands.SubmitV1,
            id, (value, now) => value.Submit(1, run.Actor, now), new("sales.invoice.submitted.v1", "Submit", "SENTETIK"));
        var approved = await invoices.DecideInvoiceAsync(run.SalesEnvelope("axes-invoice-approve", actor: "SENTETIK-invoice-checker"),
            Hash("axes-invoice-approve"), SalesInvoiceCommands.ApproveV1, id,
            (value, now) => value.Approve(2, "SENTETIK-invoice-checker", now), new("sales.invoice.approved.v1", "Approve", "SENTETIK"));
        var frozen = approved!.Invoice;
        var official = new SalesOfficialInvoiceFacts("SENTETIK-AXES", Guid.NewGuid().ToString("D"), draft.Header.PartyId, Currency,
            frozen.Totals.Payable.Amount, frozen.Totals.Payable.Amount, frozen.Totals.Vat.Amount, frozen.Totals.Withholding.Amount,
            Day, new(CompanyA, Guid.NewGuid(), "approved-import", "SENTETIK-v1", T0, null, null, null, Guid.NewGuid(), new string('a', 64), false));
        await invoices.MatchInvoiceAsync(run.SalesEnvelope("axes-invoice-match", actor: "SENTETIK-matcher"), Hash("axes-invoice-match"), id,
            (value, now) => value.RecordMatch(3, "SENTETIK-matcher", official, 0.05m, false, null, now),
            new("sales.invoice.matched.v1", "Match", "SENTETIK"));
        var finalized = await invoices.FinalizeInvoiceAsync(run.SalesEnvelope("axes-invoice-final"), Hash("axes-invoice-final"), id,
            (value, now) => value.Finalize(3, run.Actor, Guid.NewGuid, now), new("sales.invoice.finalized.v1", "Finalize", "SENTETIK"));
        return finalized!.Invoice;
    }

    private static async Task AdmitAndAllocateClosureInvoice(Run run, SalesInvoice invoice, bool leaveSecondOpen)
    {
        var company = new CompanyContext(); company.Set(CompanyA);
        var collector = new AuditEventCollector();
        await using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options, company);
        await db.Database.MigrateAsync();
        var user = new ClosureOwnerUser(run.Actor);
        var context = new FinanceContext(CompanyA, run.Actor, run.Correlation, null);
        var final = invoice.Finalization!;
        var request = new FinanceInvoiceOpenItemAdmissionV1(final.AdmissionId, FinanceInvoiceProducerV1.Sales, invoice.Id,
            final.FinalizedVersion.ToString(CultureInfo.InvariantCulture), invoice.Header.PartyId, invoice.Header.CompanyAccountId,
            invoice.Header.CurrencyId, null, final.Plan.Select(x => new FinanceInvoiceInstallmentV1(x.OpenItemId, x.InstallmentId,
                x.SequenceNo, x.DueDate, x.Amount.ToString(CultureInfo.InvariantCulture), null)).ToArray());
        var admission = new FinanceInvoiceOpenItemAdmissionService(db, company, user, new ClosureParty(invoice), new ClosureAdmissionAuthority(),
            collector, new("SENTETIK-finalize", ["SENTETIK"], "Web"), TimeProvider.System);
        Assert.Equal(CommandOutcome.Succeeded, (await admission.AdmitAsync(context, new("axes-finance-admit"), request, default)).Outcome);
        db.ChangeTracker.Clear();
        // Explicit recorded-receipt SEED, not evidence of the receipt workflow/provider/bank acceptance.
        var source = Guid.NewGuid(); var treasury = Guid.NewGuid(); var at = TimeProvider.System.GetUtcNow();
        var total = invoice.Totals.Payable.Amount.ToString(CultureInfo.InvariantCulture);
        db.ReceiptDrafts.Add(new() { Id = source, CompanyId = CompanyA, Version = 1, PartyId = invoice.Header.PartyId,
            CompanyAccountId = invoice.Header.CompanyAccountId, CurrencyId = Currency, TreasuryAccountId = treasury,
            AmountText = total, ProfileId = "SENTETIK", ProfileVersion = "1", OccurredOn = Day,
            Stage = ReceiptStages.Recorded, RecordedAtUtc = at, RecordedBy = run.Actor, CreatedAtUtc = at, CreatedBy = run.Actor,
            UpdatedAtUtc = at, UpdatedBy = run.Actor });
        db.MoneyEvents.Add(new() { Id = Guid.NewGuid(), CompanyId = CompanyA, SourceId = source, Kind = MoneyEventKinds.ManualBankReceiptRecorded,
            PartyId = invoice.Header.PartyId, CompanyAccountId = invoice.Header.CompanyAccountId, CurrencyId = Currency,
            TreasuryAccountId = treasury, AmountText = total, ProfileId = "SENTETIK", ProfileVersion = "1", OccurredOn = Day,
            RecordedAtUtc = at, RecordedBy = run.Actor, CorrelationId = run.Correlation });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var allocator = new FinanceAllocationService(db, company, user, new ClosureFinancePermissions(), collector,
            new("SENTETIK-allocation", ["SENTETIK"], "Web"), TimeProvider.System);
        foreach (var item in request.Installments.Take(leaveSecondOpen ? 1 : 2))
        {
            Assert.Equal(CommandOutcome.Succeeded, (await allocator.CreateAsync(context, new($"axes-allocate-{item.Sequence}"),
                new(Guid.NewGuid(), source, "1", item.OpenItemId, "1", item.Amount, "SENTETIK"), default)).Outcome);
            db.ChangeTracker.Clear();
        }
        await using var sales = run.SalesContext(CompanyA);
        var invoices = (ISalesInvoiceStore)new SalesStore(sales, company, new AuditEventCollector(), TimeProvider.System);
        await invoices.RecordFinanceAttemptAsync(run.SalesEnvelope("axes-finance-offer"), null, null, invoice.Id,
            (value, now) => value.RecordFinanceAttempt(true, "admitted", now), new("sales.invoice.finance-offered.v1", "FinanceOffer", "SENTETIK"));
    }

    private static async Task PrepareClosurePurchasing(Run run, SalesOrderDraft draft, bool createRequest)
    {
        var company = new CompanyContext(); company.Set(CompanyA);
        var collector = new AuditEventCollector();
        await using var db = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options, company);
        await db.Database.MigrateAsync();
        if (!createRequest) return;
        var request = PurchaseRequest.Create(Guid.NewGuid(), CompanyA, run.Actor, T0);
        request.AddLine(request.Version, Guid.NewGuid(), Product, KgUnit,
            GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Purchasing.Domain.Quantities.PurchasingQuantityRefusals>
                .Of(6m, GoymenERP.BuildingBlocks.Quantities.QuantityUnit.Kilogram), T0,
            PurchaseRequestLineSource.Of(PurchaseRequestSourceKind.Sales, draft.Id, draft.Lines[0].Id));
        await new PurchasingStore(db, company, collector, TimeProvider.System).StoreRequestAsync(
            new(run.Actor, "SENTETIK-purchasing", ["SENTETIK"], run.Correlation, null, "axes-source-request", "Web", null, null),
            Hash("axes-source-request"), Day, () => request, request.Lines.ToDictionary(x => x.Id, _ => "7"));
    }

    private sealed class ClosureOwnerUser(string actor) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA };
        public bool CanAccessCompany(Guid id) => AllowedCompanyIds.Contains(id);
    }
    private sealed class ClosureParty(SalesInvoice invoice) : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId, Guid companyAccountId,
            CancellationToken cancellationToken) => Task.FromResult(new ReadResult<PartyAccountEligibility>(ReadOutcome.Found,
                new(invoice.Header.PartyId, invoice.Header.CompanyAccountId, CompanyA, true, true)));
    }
    private sealed class ClosureAdmissionAuthority : IFinanceInvoiceOpenItemAdmissionAuthorityV1
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceInvoiceProducerV1 producer, Guid invoiceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }
    private sealed class ClosureFinancePermissions : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }

    private sealed class SyntheticClosureDownstream : ISalesInvoiceableShipmentReadPortV1,
        ILogisticsInvoicedShipmentProofReadPortV1, IInventoryShipmentCostReadPortV1, ISalesOrderAllocationReadPortV2,
        IManufacturingSalesOrderSupplyReadPortV1
    {
        public SalesOrderDraft Draft { get; set; } = null!;
        public Guid Shipment { get; } = Guid.NewGuid();
        public Guid Note { get; } = Guid.NewGuid();
        private readonly Guid instruction = Guid.NewGuid(), instructionLine = Guid.NewGuid(), movement = Guid.NewGuid(),
            allocation = Guid.NewGuid(), lot = Guid.NewGuid(), warehouse = Guid.NewGuid();
        public void Configure(IServiceCollection services)
        {
            services.RemoveAll<ISalesOrderOwnerAxesSourceV1>();
            services.AddScoped<ISalesOrderOwnerAxesSourceV1, SalesOrderOwnerAxesHostV1>();
            services.RemoveAll<ISalesInvoiceableShipmentReadPortV1>(); services.AddSingleton<ISalesInvoiceableShipmentReadPortV1>(this);
            services.RemoveAll<ILogisticsInvoicedShipmentProofReadPortV1>(); services.AddSingleton<ILogisticsInvoicedShipmentProofReadPortV1>(this);
            services.RemoveAll<IInventoryShipmentCostReadPortV1>(); services.AddSingleton<IInventoryShipmentCostReadPortV1>(this);
            services.RemoveAll<ISalesOrderAllocationReadPortV2>(); services.AddSingleton<ISalesOrderAllocationReadPortV2>(this);
            services.RemoveAll<IManufacturingSalesOrderSupplyReadPortV1>(); services.AddSingleton<IManufacturingSalesOrderSupplyReadPortV1>(this);
            // These three owner readers remain REAL SQL implementations. No synthetic complete axes or Finance answer.
            services.RemoveAll<ISalesFinalizedShipmentClaimReadPortV1>();
            services.AddScoped<ISalesFinalizedShipmentClaimReadPortV1, SalesFinalizedShipmentClaimReadPortV1>();
            services.RemoveAll<ISalesOrderInvoicedQuantityReadPortV1>();
            services.AddScoped<ISalesOrderInvoicedQuantityReadPortV1, SalesOrderInvoicedQuantityReadPortV1>();
            services.RemoveAll<IFinanceSalesInvoiceSettlementPortV1>();
            services.AddScoped<IFinanceSalesInvoiceSettlementPortV1, FinanceSalesInvoiceSettlementReaderV1>();
        }
        public Task<SalesInvoiceableShipmentAnswerV1> ReadInvoiceableShipmentsAsync(SalesInvoiceableShipmentQueryV1 query, CancellationToken cancellationToken)
        {
            var share = new SalesInvoiceableShipmentV1(Note, "SENTETIK-NOTE", null, T0, Draft.Header.PartyId,
                Draft.Header.CompanyAccountId, 1, Shipment, T0, Draft.Id, Draft.Lines[0].Id, Product, KgUnit, 6m);
            return Task.FromResult(new SalesInvoiceableShipmentAnswerV1(SalesDispatchReadOutcomeV1.Found, CompanyA, [share], false, SalesInvoicedStateSourceV1.NoSource));
        }
        public Task<LogisticsInvoicedShipmentProofReadV1> GetAsync(Guid companyId, Guid dispatchNoteId, int dispatchNoteLineNo,
            Guid shipmentId, CancellationToken cancellationToken) => Task.FromResult(new LogisticsInvoicedShipmentProofReadV1(
                LogisticsInvoicedShipmentProofOutcomeV1.Found, new(CompanyA, Note, 1, Shipment, instruction, instructionLine,
                    Draft.Id, Draft.Lines[0].Id, Draft.Revision, Product, KgUnit, 6m, "SENTETIK-proof-v1")));
        public Task<InventoryShipmentCostReadV1> GetAsync(Guid companyId, Guid shipmentId, Guid movementId, CancellationToken cancellationToken) =>
            GetByShipmentAsync(companyId, shipmentId, cancellationToken);
        public Task<InventoryShipmentCostReadV1> GetByShipmentAsync(Guid companyId, Guid shipmentId, CancellationToken cancellationToken)
        {
            var cost = new InventoryMovementCostStampV1(InventoryMovementCostStampStatusV1.Unavailable, Guid.Empty, null, null, null, "SENTETIK", "missing_cost");
            var line = new InventoryShipmentCostLineV1(1, instruction, instructionLine, allocation, lot, "SENTETIK", Product, "7",
                StockPartyKind.Company, CompanyA, StockPartyKind.Company, CompanyA, warehouse, BlockState.Free, 6m, cost);
            return Task.FromResult(new InventoryShipmentCostReadV1(InventoryShipmentCostReadOutcomeV1.Available, CompanyA, Shipment,
                movement, [line], "SENTETIK-physical-v1"));
        }
        public Task<SalesOrderAllocationReadAnswerV2> ReadOrderAllocationsAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult(new SalesOrderAllocationReadAnswerV2(CompanyA, Draft.Id, []));
        public Task<IReadOnlyList<ManufacturingSalesOrderSupplyItemV1>> ListOpenForSalesOrderAsync(Guid companyId, Guid salesOrderId,
            IReadOnlyList<Guid>? salesOrderLineIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ManufacturingSalesOrderSupplyItemV1>>([]);
    }
}
