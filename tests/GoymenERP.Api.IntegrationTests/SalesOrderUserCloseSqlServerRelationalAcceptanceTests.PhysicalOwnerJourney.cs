using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.SalesRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesOrderUserCloseSqlServerRelationalAcceptanceTests
{
    // Actual owner linkage, not full financial/physical E2E acceptance. Initial stock/label/allocation, product/unit/money/tax
    // snapshots, credit-limit eligibility/Settings inputs and official-import facts are SENTETIK fixture inputs.
    // Native Cari creates the customer; actual Sales risk/line/over-delivery and Inventory allocation reads stay registered.
    // Shipment uses the host's real Logistics + enlisted Inventory transaction. Actual dispatch/invoice claims/cost/source
    // readers feed closure. AdmitAndAllocateClosureInvoice explicitly seeds a recorded receipt + MoneyEvent; this does NOT
    // prove receipt/bank/official-document workflows. No fake empty Inventory/Manufacturing/Purchasing obligation ports.
    [SqlServerFact]
    public async Task Actual_shipment_inventory_and_posted_dispatch_identity_feed_finalized_invoice_settlement_and_native_order_close()
    {
        await using var run = await Run.StartAsync(readySeam: true, configure: services =>
        {
            // Retain only the inherited SENTETIK tolerance ladder; restore actual closure owner composition.
            services.RemoveAll<ISalesOrderOwnerAxesSourceV1>();
            services.AddScoped<ISalesOrderOwnerAxesSourceV1, SalesOrderOwnerAxesHostV1>();
        });
        var (party, account) = await CreatePhysicalClosureCustomer(run);
        var draft = await CreatePhysicalClosureOrder(run, party, account);
        await SetPhysicalClosureLimit(run, draft);
        var physical = await SeedPhysicalClosureStockAndInstruction(run, draft);
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            ErpPermissions.LogisticsShipmentsRecord, ErpPermissions.InventoryPhysicalAllocationWrite,
            SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersClose);
        using var client = run.Client(subject, CompanyA);
        var shipmentId = Guid.NewGuid(); var movementId = Guid.NewGuid();
        using (var request = new HttpRequestMessage(HttpMethod.Post,
            $"{LogisticsEndpoints.InstructionsPath}/{physical.InstructionId:D}/lines/{physical.LineId:D}/shipments"))
        {
            request.Content = JsonContent.Create(new RecordShipmentRequest(physical.Version.ToString(CultureInfo.InvariantCulture),
                shipmentId, physical.AllocationId, "1", "1", movementId, "6", T0.AddMinutes(10)));
            request.Headers.Add("Idempotency-Key", "physical-closure-shipment");
            request.Headers.Add("X-Correlation-Id", "SENTETIK-physical-closure-shipment");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.Equal((int)StockAllocationStatus.Consumed, await run.ScalarAsync<int>(
            "SELECT Status FROM inventory.Allocations WHERE CompanyId=@company AND Id=@id", ("@company", CompanyA), ("@id", physical.AllocationId)));
        Assert.Equal(0m, await run.ScalarAsync<decimal>(
            "SELECT RemainingKg FROM inventory.Allocations WHERE CompanyId=@company AND Id=@id", ("@company", CompanyA), ("@id", physical.AllocationId)));
        var noteId = await DeliverAndApprovePhysicalClosureDispatch(run, physical, shipmentId);
        SalesInvoiceableShipmentV1 source;
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            Assert.IsType<SalesOrderOwnerAxesHostV1>(scope.ServiceProvider.GetRequiredService<ISalesOrderOwnerAxesSourceV1>());
            Assert.IsType<SalesShipmentRiskGateReadPort>(scope.ServiceProvider.GetRequiredService<ISalesShipmentRiskGateReadPortV1>());
            Assert.IsType<SalesOrderLineReferencePort>(scope.ServiceProvider.GetRequiredService<ISalesOrderLineReferencePortV1>());
            var reader = scope.ServiceProvider.GetRequiredService<ISalesInvoiceableShipmentReadPortV1>();
            var answer = await reader.ReadInvoiceableShipmentsAsync(new(account, 100, [draft.Id]));
            Assert.Equal(SalesDispatchReadOutcomeV1.Found, answer.Outcome); Assert.False(answer.HasMore);
            source = Assert.Single(answer.Shares);
            Assert.Equal((CompanyA, noteId, shipmentId, draft.Id, Assert.Single(draft.Lines).Id, Product, KgUnit, 6m),
                (answer.CompanyId, source.DispatchNoteId, source.ShipmentId, source.SalesOrderId, source.SalesOrderLineId,
                    source.ProductId, source.UnitId, source.Quantity));
            var proof = await scope.ServiceProvider.GetRequiredService<ILogisticsInvoicedShipmentProofReadPortV1>()
                .GetAsync(CompanyA, noteId, source.NoteLineNo, shipmentId);
            Assert.Equal(LogisticsInvoicedShipmentProofOutcomeV1.Found, proof.Outcome);
            Assert.Equal((physical.InstructionId, physical.LineId), (proof.Proof!.InstructionId, proof.Proof.InstructionLineId));
            var costs = await scope.ServiceProvider.GetRequiredService<IInventoryShipmentCostReadPortV1>()
                .GetByShipmentAsync(CompanyA, shipmentId);
            Assert.Equal(InventoryShipmentCostReadOutcomeV1.Available, costs.Outcome);
            Assert.Equal((CompanyA, shipmentId, movementId), (costs.CompanyId, costs.ShipmentId, costs.MovementId));
            var cost = Assert.Single(costs.Lines);
            Assert.Equal((physical.InstructionId, physical.LineId, physical.AllocationId, physical.LabelId, Product, 6m),
                (cost.LoadingInstructionId, cost.LoadingInstructionLineId, cost.AllocationId, cost.LabelId, cost.ProductId, cost.QuantityKg));
        }
        var invoice = await FinalizePhysicalClosureInvoice(run, draft, source);
        await AdmitAndAllocateClosureInvoice(run, invoice, leaveSecondOpen: false);
        await PrepareClosurePurchasing(run, draft, createRequest: false);
        var status = await run.JsonAsync(client, $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/status");
        foreach (var axis in new[] { "realization", "source", "invoicing", "financialClosure" })
            Assert.Equal(SalesDraftStatusAxisStatesV1.Completed, status.GetProperty(axis).GetProperty("state").GetString());
        Assert.True(status.GetProperty("readyToClose").GetBoolean());
        var expected = draft.Version.ToString(CultureInfo.InvariantCulture);
        var beforeClose = await run.EffectsAsync(draft.Id);
        string originalCorrelation;
        using (var command = Close($"{SalesEndpoints.DraftsPath}/{draft.Id:D}/close", "physical-closure-close", expected))
        {
            originalCorrelation = Assert.Single(command.Headers.GetValues("X-Correlation-Id"));
            using var closed = await client.SendAsync(command);
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        }
        var afterClose = await run.EffectsAsync(draft.Id);
        using (var replay = await client.SendAsync(Close($"{SalesEndpoints.DraftsPath}/{draft.Id:D}/close", "physical-closure-close", expected)))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(originalCorrelation, Assert.Single(replay.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }
        Assert.Equal(afterClose, await run.EffectsAsync(draft.Id));
        Assert.Equal(beforeClose with { SalesAttempts = beforeClose.SalesAttempts + 1, AuditEvents = beforeClose.AuditEvents + 1,
            UserClosures = beforeClose.UserClosures + 1, DraftVersion = draft.Version + 1 }, afterClose);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftUserClosures WHERE CompanyId=@company AND DraftId=@id",
            ("@company", CompanyA), ("@id", draft.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId=@company AND EntityId=@id AND EventType=N'sales.order-draft.closed.v1' "
            + "AND PermissionCode=N'sales.orders.close' AND EntityVersionBefore=@before AND EntityVersionAfter=@after",
            ("@company", CompanyA), ("@id", draft.Id.ToString("D")), ("@before", draft.Version), ("@after", draft.Version + 1)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE CompanyId=@company AND SourceId=@share AND Id=@movement",
            ("@company", CompanyA), ("@share", shipmentId), ("@movement", movementId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.InvoiceFinalizations WHERE InvoiceId=@id AND FinanceStatus=2",
            ("@id", invoice.Id)));
    }

    private static async Task<(Guid Party, Guid Account)> CreatePhysicalClosureCustomer(Run run)
    {
        // Same native Cari creation as SalesDispatchSqlServerRelationalAcceptanceTests.Run.CustomerAsync.
        using var client = run.Factory.CreateClientFor(TestUsers.GroupAdmin, CompanyA);
        using var response = await client.PostAsJsonAsync("/api/v1/counterparties",
            new CreateCounterparty(PartyKind.LegalEntity, "SENTETIK physical closure customer", null, PartyRole.Customer));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var account = Assert.Single(created.GetProperty("companyAccounts").EnumerateArray());
        Assert.Equal(CompanyA, account.GetProperty("companyId").GetGuid());
        return (created.GetProperty("id").GetGuid(), account.GetProperty("id").GetGuid());
    }

    private static SalesLineTaxSnapshot PhysicalClosureTax() => new(Product, Day, "Normal", "Steel", "Resolved", null,
        "Record", Guid.NewGuid(), 1, "Resolved", 20m, "NotRequired", null, null, "NotApplied", null,
        "NotRequired", null, null, null, 0, "Normal", false);

    private static async Task<SalesOrderDraft> CreatePhysicalClosureOrder(Run run, Guid party, Guid account)
    {
        // Native priced-order pattern from SecondShipmentRiskOwners; retained Settings/Product/tax snapshots are inputs.
        await using var scope = run.SalesStore(CompanyA);
        var stored = await scope.Store.StoreDraftAsync(run.SalesEnvelope("physical-order-create"), Hash("physical-order-create"), number =>
        {
            var order = SalesOrderDraft.Create(Guid.NewGuid(), CompanyA, number,
                new(party, account, Currency, Day, Day.AddDays(10), DueTerms: TestDueTerms.PointDue30),
                new(CompanyA, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2), T0);
            var unit = new UnitProfileSnapshot(CompanyA, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
            var binding = new UnitBindingSnapshot(CompanyA, KgUnit, KgProfile, "4");
            var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
            order.AddLine(order.Version, new(Guid.NewGuid(), product, unit, "6", 6m, Day.AddDays(3), Commercial:
                SalesOrderDraft.WithDiscountLimitCheck(new("10", KgUnit, null), SalesDiscountLimit.Undefined, run.Actor, T0),
                Tax: PhysicalClosureTax()), T0);
            return order;
        });
        var line = Assert.Single(stored.Draft.Lines);
        var priced = await scope.Store.ReviseDraftCommercialAsync(run.SalesEnvelope("physical-order-price", "SENTETIK-price-checker"),
            Hash("physical-order-price"), SalesDraftCommands.ApproveDiscountExceptionsV1, stored.Draft.Id, (order, now) =>
            { order.ApproveDiscountExceptions(order.Version, [new(line.Id, "SENTETIK fiyat")], "SENTETIK-price-checker", now); return null; },
            new("sales.order-draft.discount-exceptions-approved.v1", "ApproveDiscountExceptions", "SENTETIK"), _ => Task.CompletedTask,
            default, SalesRiskCheckSqlServerRelationalAcceptanceTests.SecondShipmentReplayProjection);
        var submitted = await scope.Store.UpdateDraftStateAsync(run.SalesEnvelope("physical-order-submit"), Hash("physical-order-submit"),
            SalesDraftCommands.SubmitV1, stored.Draft.Id, (value, now) => value.Submit(priced!.Draft.Version, run.Actor, now), Submitted);
        return (await scope.Store.UpdateDraftStateAsync(run.SalesEnvelope("physical-order-approve", "SENTETIK-order-checker"),
            Hash("physical-order-approve"), SalesDraftCommands.ApproveV1, stored.Draft.Id,
            (value, now) => value.Approve(submitted!.Draft.Version, "SENTETIK-order-checker", now), Approved))!.Draft;
    }

    private static async Task SetPhysicalClosureLimit(Run run, SalesOrderDraft draft)
    {
        var company = new CompanyContext(); company.Set(CompanyA); var audit = new AuditEventCollector();
        await using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(audit, company)).Options, company);
        var inputs = new PhysicalClosureLimitInputs(draft, run.Actor);
        var owner = new FinancePartyCreditLimitService(db, company, new ClosureOwnerUser(run.Actor), inputs, inputs, inputs, audit,
            new(new("SENTETIK.limit.propose", [], "Web"), new("SENTETIK.limit.approve", [], "Web")), TimeProvider.System);
        Assert.Equal(CommandOutcome.Succeeded, (await owner.SetAsync(new(CompanyA, run.Actor, run.Correlation, null),
            new("physical-closure-limit"), new(draft.Header.PartyId, draft.Header.CompanyAccountId, Currency, "800.00", "0"), default)).Outcome);
    }

    private sealed class PhysicalClosureLimitInputs(SalesOrderDraft draft, string actor)
        : IFinancePermissionPort, IFinancePartyAccountPort, ISettingsMoneyCandidateReader
    {
        // Eligibility/permission and the money candidate are explicit SENTETIK inputs, not a native grant workflow test.
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? id, CancellationToken token) =>
            Task.FromResult(context.CompanyId == CompanyA && context.ActorId == actor ? PermissionDecision.Allowed : PermissionDecision.Denied);
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid party, Guid account, CancellationToken token) =>
            Task.FromResult(context.CompanyId == CompanyA && party == draft.Header.PartyId && account == draft.Header.CompanyAccountId
                ? new ReadResult<PartyAccountEligibility>(ReadOutcome.Found, new(party, account, CompanyA, true, IsPartyActive: true))
                : new(ReadOutcome.NotFound, null));
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<MoneyCurrencyCandidateV1>>([new(Currency, "TRY", "Sentetik para")]);
    }

    private sealed record PhysicalClosureStock(Guid InstructionId, Guid LineId, long Version, Guid AllocationId, Guid LabelId);

    private static async Task<PhysicalClosureStock> SeedPhysicalClosureStockAndInstruction(Run run, SalesOrderDraft draft)
    {
        var line = Assert.Single(draft.Lines); var allocation = Guid.NewGuid(); var label = Guid.NewGuid(); var lot = Guid.NewGuid();
        var location = await run.ScalarAsync<Guid>("SELECT TOP (1) Id FROM organization.Locations WHERE CompanyId=@company ORDER BY Id", ("@company", CompanyA));
        var company = new CompanyContext(); company.Set(CompanyA);
        await using (var db = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema)).Options, company))
        {
            db.Lots.Add(new LotRow { Id = lot, CompanyId = CompanyA, ProductId = Product, Version = 1, CreatedAtUtc = T0, CreatedBy = run.Actor });
            db.Labels.Add(new LabelRow { Id = label, CompanyId = CompanyA, Code = "SENTETIK-PHYSICAL-CLOSURE", ProductId = Product,
                LotId = lot, RequiredDocumentsJson = "[]", Version = 1, CreatedAtUtc = T0, UpdatedAtUtc = T0 });
            db.Balances.Add(new BalanceRow { Id = Guid.NewGuid(), CompanyId = CompanyA, OwnerKind = (int)StockPartyKind.Company,
                OwnerId = CompanyA, CustodianKind = (int)StockPartyKind.Company, CustodianId = CompanyA, LocationId = location,
                LabelId = label, ProductId = Product, Block = (int)BlockState.Free, QuantityKg = 10m });
            db.Allocations.Add(new AllocationRow { Id = allocation, CompanyId = CompanyA, OwnerKind = (int)StockPartyKind.Company,
                OwnerId = CompanyA, CustodianKind = (int)StockPartyKind.Company, CustodianId = CompanyA, LocationId = location,
                LabelId = label, Block = (int)BlockState.Free, DemandKind = InventoryAllocationDemandKindsV1.SalesOrderLine,
                DemandId = draft.Id, DemandLineId = line.Id, DemandProductId = Product, DemandProductVersion = "7",
                Reason = "SENTETIK initial stock allocation", AllocatedKg = 6m, RemainingKg = 6m,
                Status = (int)StockAllocationStatus.Active, Version = 1, CreatedAtUtc = T0, UpdatedAtUtc = T0 });
            await db.SaveChangesAsync();
        }
        await using var scope = run.LogisticsStore(CompanyA); var instructionLine = Guid.NewGuid();
        var stored = (await scope.Store.StoreInstructionAsync(run.LogisticsEnvelope("physical-instruction"), Hash("physical-instruction"), Day, () =>
        {
            var instruction = LoadingInstruction.Create(Guid.NewGuid(), CompanyA, draft.Header.PartyId, draft.Header.CompanyAccountId, T0);
            instruction.AddLine(instruction.Version, instructionLine, new(draft.Id, line.Id, draft.Revision), Product, KgUnit, allocation, Kg(6m), T0);
            return instruction;
        })).Instruction;
        var issued = (await scope.Store.MutateInstructionAsync(run.LogisticsEnvelope("physical-issue"), Hash("physical-issue"),
            LogisticsCommands.IssueV1, stored.Instruction.Id, (value, now) => value.Issue(stored.Instruction.Version, now),
            new("logistics.loading-instruction.issued.v1", "Issue", "SENTETIK")))!.Instruction;
        var loaded = (await scope.Store.MutateInstructionAsync(run.LogisticsEnvelope("physical-load"), Hash("physical-load"),
            LogisticsCommands.RecordLoadV1, stored.Instruction.Id,
            (value, now) => value.RecordLoad(issued.Instruction.Version, Guid.NewGuid(), instructionLine, Kg(6m), MeasurementKind.Measured, now),
            new("logistics.loading-instruction.load-recorded.v1", "RecordLoad", "SENTETIK")))!.Instruction;
        return new(stored.Instruction.Id, instructionLine, loaded.Instruction.Version, allocation, label);
    }

    private static async Task<Guid> DeliverAndApprovePhysicalClosureDispatch(Run run, PhysicalClosureStock physical, Guid shipment)
    {
        var note = Guid.NewGuid(); long version;
        await using (var scope = run.LogisticsStore(CompanyA))
        {
            var current = Assert.IsType<StoredLoadingInstruction>(await scope.Store.FindInstructionAsync(physical.InstructionId));
            var delivered = (await scope.Store.MutateInstructionAsync(run.LogisticsEnvelope("physical-deliver"), Hash("physical-deliver"),
                LogisticsCommands.RecordDeliveryV1, physical.InstructionId,
                (value, now) => value.MarkDelivered(current.Instruction.Version, Guid.NewGuid(), physical.LineId, shipment, Kg(6m), now),
                new("logistics.loading-instruction.delivery-recorded.v1", "RecordDelivery", "SENTETIK")))!.Instruction;
            var drafted = (await scope.Store.MutateInstructionAsync(run.LogisticsEnvelope("physical-note"), Hash("physical-note"),
                LogisticsCommands.CreateDispatchNoteV1, physical.InstructionId,
                (value, now) => value.CreateDispatchNoteDraft(delivered.Instruction.Version, note, [shipment], run.Actor, now),
                new("logistics.loading-instruction.dispatch-note-drafted.v1", "CreateDispatchNote", "SENTETIK")))!.Instruction;
            version = drafted.Instruction.Version;
        }
        await using var ownerScope = run.Factory.Services.CreateAsyncScope();
        ownerScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        var owner = (ILogisticsDispatchNoteApprovalStoreV1)ownerScope.ServiceProvider.GetRequiredService<GoymenERP.Modules.Logistics.Infrastructure.Persistence.LogisticsStore>();
        var envelope = run.LogisticsEnvelope("physical-note-approve") with { ActorId = "SENTETIK-dispatch-checker" };
        Assert.NotNull(await owner.ApproveDispatchNoteOwnedAsync(envelope, Hash("physical-note-approve"), physical.InstructionId, note, version,
            new("logistics.loading-instruction.dispatch-note-approved.v1", "ApproveDispatchNote", "SENTETIK"), _ => Task.CompletedTask));
        return note;
    }

    private static async Task<SalesInvoice> FinalizePhysicalClosureInvoice(Run run, SalesOrderDraft draft, SalesInvoiceableShipmentV1 source)
    {
        await using var db = run.SalesContext(CompanyA);
        var company = new CompanyContext(); company.Set(CompanyA);
        var invoices = (ISalesInvoiceStore)new SalesStore(db, company, new AuditEventCollector(), TimeProvider.System);
        var id = Guid.NewGuid();
        var line = Assert.Single(draft.Lines);
        var tax = new SalesLineTaxSnapshot(Product, Day, "Normal", "Steel", "Resolved", null, "Record", Guid.NewGuid(), 1,
            "Resolved", 20m, "NotRequired", null, null, "NotApplied", null, "NotRequired", null, null, null, 0, "Normal", false);
        var terms = SalesDueTerms.Create([new(1, SalesDueBasis.OrderDate, 30, "50"), new(2, SalesDueBasis.OrderDate, 60, "50")]);
        var share = new SalesInvoiceShareInput(Guid.NewGuid(), source.ShipmentId, source.DispatchNoteId, source.SystemNumber, source.NoteLineNo, Day,
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

}
