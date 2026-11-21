using System.Data;
using System.Globalization;
using GoymenERP.Api.LogisticsRuntime;
using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Real_subcontract_host_composes_sales_service_and_different_physical_product_then_stocks_and_ships_the_whole_instruction()
    {
        await using var run = await StartManufacturingInventoryRunAsync(); var seed = await SeedSubcontractDeliveryHostAsync(run, "SYN-S3-HOST");
        await using var scope = SubcontractHostScope.Create(run, seed.Party, seed.Account);
        var command = await scope.CommandAsync(seed); var metadata = new LogisticsCommandMetadata("s3-real-create", "SENTETIK-s3-create", null, At);
        var created = await scope.Host.CreateAsync(command, metadata);
        Assert.NotEqual(Assert.Single(created.Lines).Source.ProductId, created.Lines[0].Source.SalesServiceProductId);
        Assert.Equal(seed.ServiceProduct, created.Lines[0].Source.SalesServiceProductId);
        Assert.Equal(80m, await run.ScalarAsync<decimal>("SELECT RemainingKg FROM inventory.Allocations WHERE Id=@id", ("@id", command.Lines[0].AllocationId)));
        await scope.Store.MutateInstructionAsync(scope.Envelope("s3-host-issue"), S3Hash("s3-host-issue"), LogisticsCommands.IssueV1, created.InstructionId,
            (value, now) => value.Issue(long.Parse(created.Version, CultureInfo.InvariantCulture), now), new("logistics.loading-instruction.issued.v1", "Issue", "SENTETIK"));
        var issued = await scope.Host.ReadAsync(created.InstructionId);
        var loaded = await scope.Host.LoadAsync(created.InstructionId, command.Lines[0].LineId, new(issued.Version, Guid.NewGuid(), "80", "Measured"),
            new("s3-host-load", "SENTETIK-s3-host-load", null, At));
        Assert.Equal("80", Assert.Single(loaded.Lines).LoadedKg); Assert.NotNull(loaded.RecordedLoadId);
        var weighing = Guid.NewGuid();
        await scope.Store.MutateInstructionAsync(scope.Envelope("s3-host-weigh"), S3Hash("s3-host-weigh"), LogisticsCommands.RecordWeighingV1, created.InstructionId,
            (value, now) => value.RecordWeighing(value.Version, weighing, WeighingDirection.TareFirst, S3Kg(100), S3Kg(180), run.Actor.ToString("D"), now), new("logistics.loading-instruction.weighed.v1", "Weigh", "SENTETIK"));
        var current = await scope.Host.ReadAsync(created.InstructionId); var line = Assert.Single(current.Lines); var movement = Guid.NewGuid();
        var shipped = await scope.Host.ShipAsync(created.InstructionId, new(current.Version, At.AddHours(2), [new(line.LineId, Guid.NewGuid(), movement,
            line.AllocationVersion!, line.LabelVersion!, line.Source.SalesOrderId, line.Source.SalesOrderLineId, line.Source.ManufacturingOrderId,
            line.Source.OutputFactId, line.Source.LabelId)]), new("s3-real-ship", "SENTETIK-s3-ship", null, At));
        Assert.Equal("80", Assert.Single(shipped.Lines).ShippedKg); Assert.Single(scope.Risk.Reads);
        Assert.Equal(0m, await run.ScalarAsync<decimal>("SELECT COALESCE(SUM(QuantityKg),0) FROM inventory.Balances WHERE LabelId=@id", ("@id", seed.Output.LabelId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE Id=@id", ("@id", movement)));
        var replay = await scope.Host.CreateAsync(command, metadata with { CorrelationId = "SENTETIK-later" });
        Assert.True(replay.IsReplay); Assert.Equal(created.Version, replay.Version); Assert.Equal("Draft", replay.Status); Assert.Equal(metadata.CorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations WHERE Id=@id", ("@id", command.Lines[0].AllocationId)));
        // SAT-D378: a REAL Sales invoice claim blocks the REAL correction host, and only a real cancellation releases it.
        var company = new CompanyContext(); company.Set(CompanyA); var audit = new AuditEventCollector();
        await using var salesDb = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(audit, company)).Options, company);
        var salesStore = new SalesStore(salesDb, company, audit, TimeProvider.System, new SalesSubcontractWeighingCommitReadHost());
        var actor = run.Actor.ToString("D"); var day = DateOnly.FromDateTime(At.UtcDateTime);
        var invoiceEnvelope = new SalesDraftWriteEnvelope(actor, SalesPermissionManifestV1.InvoicesWrite, ["SENTETIK"], "SENTETIK-s3-invoice", null, "s3-real-invoice", "Integration");
        var tax = new SalesLineTaxSnapshot(seed.ServiceProduct, day, "Normal", "Service", "Resolved", null, "Record", Guid.NewGuid(), 1,
            "Resolved", 20m, "NotRequired", null, null, "NotApplied", null, "NotRequired", null, null, null, 0, "Normal", false);
        var savedInvoice = await salesStore.StoreSubcontractServiceInvoiceAsync(invoiceEnvelope, S3Hash("s3-real-invoice"), number =>
            SalesSubcontractServiceInvoice.Create(Guid.NewGuid(), CompanyA, number, new(seed.Party, seed.Account, Guid.NewGuid(), Guid.NewGuid(), "1", 2, day),
                new(seed.SalesOrder, created.InstructionId, weighing, 1, long.Parse(shipped.Version, CultureInfo.InvariantCulture), "80"),
                [new(Guid.NewGuid(), seed.SalesLine, 0, seed.ServiceProduct, SettingsUnitIdentitiesV1.Kilogram, 80m, "80", new("1", SettingsUnitIdentitiesV1.Kilogram, null), tax)], 80m, actor, TimeProvider.System.GetUtcNow()));
        var correction = new CorrectSubcontractWeighingV1(shipped.Version, weighing, 1, Guid.NewGuid(), "TareFirst", "100", "179", "SENTETIK doğrulanmış kantar düzeltmesi");
        var refused = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => scope.Host.CorrectWeighingAsync(created.InstructionId, correction, new("s3-correction-blocked", "SENTETIK-correction-blocked", null, At)));
        Assert.Equal(LogisticsSubcontractDeliveryV1.ActiveInvoice, refused.Code); Assert.Equal(409, refused.Status);
        await salesStore.UpdateLifecycleAsync(invoiceEnvelope with { IdempotencyKey = "s3-cancel-invoice" }, S3Hash("s3-cancel-invoice"), SalesSubcontractInvoiceLifecycleCommandsV1.Cancel,
            savedInvoice.Invoice.Id, (value, now) => value.Cancel(value.Version, actor, "SENTETIK önce fatura iptali", now), new("sales.subcontract-service-invoice.cancelled.v1", "Cancel", "SENTETIK önce fatura iptali"));
        var corrected = await scope.Host.CorrectWeighingAsync(created.InstructionId, correction, new("s3-correction-applied", "SENTETIK-correction-applied", null, At));
        Assert.Equal("79", corrected.Weighing!.NetKg); Assert.Equal("80", Assert.Single(corrected.Corrections).PreviousNetKg);
        Assert.Equal("80", Assert.Single(corrected.Lines).ShippedKg);
        Assert.Equal(80m, await run.ScalarAsync<decimal>("SELECT NetKg FROM logistics.VehicleWeighings WHERE InstructionId=@id", ("@id", created.InstructionId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE Id=@id", ("@id", movement)));
    }

    [SqlServerFact]
    public async Task A_failure_after_actual_inventory_reservation_and_logistics_save_rolls_back_both_owners_audit_and_command_attempt()
    {
        await using var run = await StartManufacturingInventoryRunAsync(); var seed = await SeedSubcontractDeliveryHostAsync(run, "SYN-S3-ROLLBACK");
        var before = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        await using var scope = SubcontractHostScope.Create(run, seed.Party, seed.Account, new FailAfterS3LogisticsSave()); var command = await scope.CommandAsync(seed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Host.CreateAsync(command, new("s3-fail", "SENTETIK-s3-fail", null, At)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations WHERE Id=@id", ("@id", command.Lines[0].AllocationId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingInstructions WHERE Id=@id", ("@id", command.InstructionId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingAttempts WHERE InstructionId=@id", ("@id", command.InstructionId)));
        Assert.Equal(before, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
        Assert.Equal(80m, await run.ScalarAsync<decimal>("SELECT SUM(QuantityKg) FROM inventory.Balances WHERE LabelId=@id", ("@id", seed.Output.LabelId)));
    }

    private static string S3Hash(string value) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    private static GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Logistics.Domain.Quantities.LogisticsQuantityRefusals> S3Kg(decimal value) =>
        GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Logistics.Domain.Quantities.LogisticsQuantityRefusals>.Of(value, GoymenERP.BuildingBlocks.Quantities.QuantityUnit.Kilogram);
    private sealed record SubcontractHostSeed(CompletionV2Seed Output, Guid Party, Guid Account, Guid ServiceProduct, Guid SalesOrder, Guid SalesLine);
    private static async Task<SubcontractHostSeed> SeedSubcontractDeliveryHostAsync(Run run, string code, Guid? customer = null, Guid? customerAccount = null)
    {
        var output = await SeedCompletionV2Async(run, code, customer, customerAccount); var company = new CompanyContext(); company.Set(CompanyA);
        await using var manufacturing = run.Store(CompanyA); var plan = (await manufacturing.Plans.FindPlanAsync(output.PlanId))!;
        var party = plan.SalesOrderLink!.CustomerPartyId; var account = plan.SalesOrderLink.CustomerAccountId; var service = Guid.NewGuid(); var order = Guid.NewGuid(); var line = Guid.NewGuid();
        var audit = new AuditEventCollector();
        await using var salesDb = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(audit, company)).Options, company);
        var salesStore = new SalesStore(salesDb, company, audit, TimeProvider.System);
        var currency = Guid.NewGuid(); var moneyProfile = Guid.NewGuid(); var kgProfile = Guid.NewGuid(); var day = DateOnly.FromDateTime(At.UtcDateTime);
        await salesStore.StoreDraftAsync(new(run.Actor.ToString("D"), "SENTETIK-sales", ["SENTETIK"], "SENTETIK-s3-sales", null, code, "Integration"), S3Hash(code), number =>
        {
            var header = new SalesDraftHeader(party, account, currency, day, day.AddDays(10), DueTerms: TestDueTerms.PointDue30,
                BuyerTax: SalesBuyerTax.Create(TaxBuyerSituationsV1.Normal), IsSubcontractService: true);
            var draft = SalesOrderDraft.Create(order, CompanyA, number, header, new(CompanyA, currency, SalesOrderDraft.MoneyOperation, moneyProfile, "1", day, true, day.AddDays(-1), null, 2), At);
            var unit = new UnitProfileSnapshot(CompanyA, SettingsUnitIdentitiesV1.Kilogram, "kg", "weight", kgProfile, "1", true, 3, 3, true, day.AddDays(-1), null);
            var binding = new UnitBindingSnapshot(CompanyA, SettingsUnitIdentitiesV1.Kilogram, kgProfile, "1");
            var product = new ProductSnapshot(service, "1", "SYN-SERVICE", "SENTETIK gerçek hizmet kaydı", day, binding, binding, binding);
            draft.AddLine(draft.Version, new(line, product, unit, "80", 80m, day.AddDays(1), Commercial: new("1", SettingsUnitIdentitiesV1.Kilogram, null), ItemType: SalesOrderDraft.ServiceItemType), At);
            draft.Submit(draft.Version, "SENTETIK-preparer", At); draft.Approve(draft.Version, "SENTETIK-approver", At); return draft;
        });
        await manufacturing.SalesOrderLinks.ChangeSalesOrderLinkAsync(plan.Id, plan.Version, plan.SalesOrderLinkRevision,
            plan.SalesOrderLink with { SalesOrderId = order, SalesOrderLineId = line, LineRevision = 0, ProductId = service }, Metadata(run));
        return new(output, party, account, service, order, line);
    }

    private sealed class SubcontractHostScope(AsyncServiceScope services, LogisticsDbContext context, LogisticsStore store, LogisticsSubcontractDeliveryHost host, S3Risk risk, Guid actor, ICompanyContext company) : IAsyncDisposable
    {
        public LogisticsDbContext Context => context;
        public LogisticsSubcontractDeliveryHost Host => host; public LogisticsStore Store => store; public S3Risk Risk => risk;
        public ICompanyContext Company => company;
        public LogisticsWriteEnvelope Envelope(string key) => new(actor.ToString("D"), LogisticsPermissionManifestV1.InstructionsCreate, ["SYNTHETIC-MANUFACTURING"], key, null, key, "Integration");
        public async Task<CreateSubcontractDeliveryV1> CommandAsync(SubcontractHostSeed seed)
        {
            var page = await host.CandidatesAsync(seed.SalesOrder, seed.SalesLine, 0); var item = Assert.Single(page.Items);
            Assert.Equal(seed.Output.LabelId, item.LabelId);
            return new(Guid.NewGuid(), DateOnly.FromDateTime(At.UtcDateTime), [new(Guid.NewGuid(), item.SalesOrderId, item.SalesOrderLineId, item.SalesOrderVersion,
                item.SalesLineRevision, item.ManufacturingOrderId, item.ManufacturingOrderVersion, item.PlanVersion, item.SalesLinkRevision,
                item.OutputFactId, item.LabelId, item.LabelVersion, Guid.NewGuid(), "80", "SENTETIK gerçek owner kaynakları")]);
        }
        public static SubcontractHostScope Create(Run run, Guid party, Guid account, IInterceptor? failure = null,
            ICurrentUser? currentUser = null, IErpAuthorityRevalidationService? authority = null,
            Func<IInventoryNegativeStockAttemptRecorderV1, IInventoryNegativeStockAttemptRecorderV1>? recorder = null,
            ICompanyContext? companyContext = null)
        {
            var services = run.Factory.Services.CreateAsyncScope(); var provider = services.ServiceProvider;
            var scopedCompany = provider.GetRequiredService<ICompanyContext>(); scopedCompany.Set(CompanyA);
            // Opt-in test context for an actual post-rollback drift; the default remains the real immutable context.
            var company = companyContext ?? scopedCompany; company.Set(CompanyA);
            var collector = provider.GetRequiredService<AuditEventCollector>(); var writer = provider.GetRequiredService<IAuditTransactionWriterV1>();
            var options = new DbContextOptionsBuilder<LogisticsDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString).AddInterceptors(new AuditSaveChangesInterceptor(collector, company));
            if (failure is not null) options.AddInterceptors(failure);
            var db = new LogisticsDbContext(options.Options, company); var store = new LogisticsStore(db, company, collector, TimeProvider.System); var risk = new S3Risk();
            var user = currentUser ?? new ManufacturingHostCurrentUser(run.Actor, CompanyA);
            var permissions = authority ?? new GenerationHostAuthority();
            var actualRecorder = new InventoryNegativeStockAttempts(company, user, permissions,
                provider.GetRequiredService<IServiceScopeFactory>(), provider.GetRequiredService<InventoryDbContext>());
            var refusalServices = new SubcontractRefusalServices(provider, recorder?.Invoke(actualRecorder) ?? actualRecorder);
            // Authority, customer activity and the risk verdict are explicit synthetic inputs; Sales/Mfg/Inventory stores, sessions, locks, audit and transaction are real.
            var host = new LogisticsSubcontractDeliveryHost(db, store, company, user, permissions, collector, writer,
                ManufacturingSettings(run), provider.GetRequiredService<ISalesOrderLineSerializationLockBinderV1>(), provider.GetRequiredService<ISalesOrderLineReferencePortV1>(),
                new S3Customer(party, account), risk, TimeProvider.System, refusalServices);
            return new(services, db, store, host, risk, run.Actor, company);
        }
        public async ValueTask DisposeAsync() { await context.DisposeAsync(); await services.DisposeAsync(); }
    }
    private sealed class SubcontractRefusalServices(IServiceProvider actual, IInventoryNegativeStockAttemptRecorderV1 recorder) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IInventoryNegativeStockAttemptRecorderV1)
            ? recorder : actual.GetService(serviceType);
    }
    private sealed class S3Customer(Guid party, Guid account) : ILogisticsPartyAccountPortV1
    {
        public Task<LogisticsPartyAccountAnswerV1> GetAuthorizedAsync(Guid partyId, Guid companyAccountId, CancellationToken token = default) =>
            Task.FromResult(partyId == party && companyAccountId == account ? new LogisticsPartyAccountAnswerV1(LogisticsPartyAccountOutcomeV1.Found, new(party, account, CompanyA, true, true, "SENTETIK1", "SENTETIK1")) : new(LogisticsPartyAccountOutcomeV1.NotFound, null));
    }
    private sealed class S3Risk : ISalesShipmentRiskGateReadPortV1
    {
        public List<Guid> Reads { get; } = [];
        public Guid? OverLimitOrder { get; set; } public Guid? ThrowingOrder { get; set; }
        public Task<SalesShipmentRiskGateReadV1> GetAsync(Guid orderId, Guid instructionId, CancellationToken token = default)
        { Reads.Add(orderId); if (orderId == ThrowingOrder) throw new InvalidOperationException("SENTETIK risk source outage");
          return Task.FromResult(new SalesShipmentRiskGateReadV1(orderId, instructionId, orderId == OverLimitOrder ? SalesShipmentRiskResultV1.Over : SalesShipmentRiskResultV1.Within,
            orderId == OverLimitOrder ? SalesShipmentRiskHoldReasonV1.OverLimit : null,
            new(orderId, instructionId, SalesShipmentRiskExceptionStatusV1.None, null, null), SalesShipmentRiskGateContractV1.Version)); }
    }
    private sealed class FailAfterS3LogisticsSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SENTETIK failure after both owners saved inside their uncommitted transaction");
    }
}
