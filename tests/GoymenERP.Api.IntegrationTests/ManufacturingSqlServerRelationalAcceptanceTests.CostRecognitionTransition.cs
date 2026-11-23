using System.Data;
using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Domain.Transfers;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_transit_pending_close_recognizes_the_same_financial_input_after_real_transfer_acceptance_without_rewriting_the_old_attempt_or_adding_a_second_effect()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var sourceLocation = await InventoryHostLocationOfAsync(run, CompanyA);
        var targetLocation = await run.ScalarAsync<Guid>(
            "SELECT TOP (1) Id FROM organization.Locations WHERE CompanyId=@company AND Id<>@source ORDER BY Id",
            ("@company", CompanyA), ("@source", sourceLocation));
        var parent = InventoryHostLabel(CompanyA, "SYN-COST-TRANSITION-PARENT");
        await using (var stock = GenerationTransitScope.Create(run))
        {
            await stock.Store.SaveLabelAsync(parent, InventoryHostMetadata(run));
            var acceptance = Guid.NewGuid();
            var receipt = InventoryHostMovement(CompanyA, StockMovementKind.Receipt,
                new StockSourceReference(InventoryGoodsReceiptContractV1.SourceKind, acceptance),
                InventoryHostIn(InventoryHostPosition(CompanyA, sourceLocation, parent, BlockState.Free), 100m));
            // Explicit synthetic receipt price; the real store writes immutable receipt/issue stamps and pools.
            stock.Store.UseReceiptCost(receipt.Id, new(CompanyA, acceptance, Guid.NewGuid(), Guid.NewGuid(), null, null, 2m, null,
                new(InventoryIssueCostCurrencyStatusV1.FunctionalCurrency, null, null, null, "synthetic-entry-v1", null),
                "synthetic-cost-v1", "{}", null, null));
            await stock.Store.RecordMovementAsync(receipt, InventoryHostMetadata(run));
        }

        ReturnOutputSeed seed;
        await using (var issue = ManufacturingHostScope.Create(run, CompanyA, new ManufacturingHostAuthority(GrantedManufacturingScope())))
        {
            var command = await SeedManufacturingOrderAsync(run, issue.Store, parent.Id, expectedLabelVersion: 2, quantity: 100m);
            var issued = await issue.Host.IssueAsync(command, ManufacturingHostMetadata(run, "transition-issue"));
            seed = new(command.OrderId, parent.Id, issued.OrderVersion, issued.ParentLabelVersion,
                StockPartyKind.Company, CompanyA, StockPartyKind.Company, CompanyA, sourceLocation, command.OccurredAtUtc);
        }
        var output = OutputCommand(seed, seed.OrderVersion, seed.ParentLabelVersion, 100m);
        long outputOrderVersion;
        string outputLabelVersion;
        await using (var declaring = ReturnOutputHostScope.Create(run, new ManufacturingHostAuthority(GrantedManufacturingScope()), new ManufacturingOutputAdmission()))
        {
            var declared = await declaring.Host.DeclareOutputAsync(output, ManufacturingHostMetadata(run, "transition-output"));
            outputOrderVersion = declared.OrderVersion;
            outputLabelVersion = declared.LabelVersion.ToString(CultureInfo.InvariantCulture);
        }
        var lineId = Guid.NewGuid();
        var plan = new PlanInventoryTransferV1(Guid.NewGuid(), sourceLocation, "Company", CompanyA,
            targetLocation, "Company", CompanyA, At.AddMinutes(10), [new(lineId, output.LabelId, outputLabelVersion, "100")]);
        await using (var stock = GenerationTransitScope.Create(run))
            await stock.Store.PlanAsync(plan, GenerationTransferMetadata(run), static _ => Task.CompletedTask);
        var dispatch = new DispatchInventoryTransferV1("1", At.AddMinutes(11), Guid.NewGuid(), "1");
        await using (var stock = GenerationTransitScope.Create(run))
            await stock.Store.DispatchAsync(plan.TransferId, dispatch,
                new(CompanyA, plan.TransferId, dispatch.OutgoingDocumentId, 1, [lineId]),
                InventoryTransferSourceScaleSetting.Off, GenerationTransferMetadata(run), static _ => Task.CompletedTask);

        ManufacturingCostGenerationV1 pending;
        await using (var closing = GenerationHostScope.Create(run, availableCost: true))
        {
            await using var transaction = await closing.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await closing.Store.CloseOrderAsync(seed.OrderId, outputOrderVersion, run.Actor, At.AddHours(1), closing.Metadata);
            await closing.Host.StageClosedAsync(seed.OrderId, closing.Metadata, CancellationToken.None);
            pending = Assert.Single(await closing.Store.ReadCostGenerationsAsync(seed.OrderId));
            Assert.Equal(ManufacturingCostGenerationStateV1.Pending, pending.State);
            // A no-money observation preserves transfer admission, but its Serializable source
            // read still blocks a real concurrent label mutation until the owner transaction ends.
            var blocked = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
                "SET LOCK_TIMEOUT 200; UPDATE inventory.Labels SET Version=Version+1 WHERE CompanyId=@company AND Id=@label; SELECT 1;",
                ("@company", CompanyA), ("@label", output.LabelId)));
            Assert.Equal(1222, blocked.Number);
            await transaction.CommitAsync();
        }
        Assert.Equal(long.Parse(outputLabelVersion, CultureInfo.InvariantCulture) + 1,
            await run.ScalarAsync<long>("SELECT Version FROM inventory.Labels WHERE CompanyId=@company AND Id=@label",
                ("@company", CompanyA), ("@label", output.LabelId)));
        var originalAttempt = await run.ScalarAsync<string>(
            "SELECT COALESCE((SELECT * FROM inventory.Commands WHERE TargetId=@generation AND CommandName=N'inventory.manufacturing-cost.stage.v1' FOR JSON PATH, INCLUDE_NULL_VALUES), N'[]')",
            ("@generation", pending.GenerationId));
        var oldKeyHash = await run.ScalarAsync<string>(
            "SELECT IdempotencyKeyHash FROM inventory.Commands WHERE TargetId=@generation AND CommandName=N'inventory.manufacturing-cost.stage.v1'",
            ("@generation", pending.GenerationId));

        await using (var receiving = GenerationTransitScope.Create(run))
        {
            var transfer = (await ((IInventoryTransferReaderV1)receiving.Store).ReadAsync(plan.TransferId))!;
            var receivedAt = At.AddHours(2);
            var document = new CreateInventoryTransferReceiptDocumentV1(Guid.NewGuid(), plan.TransferId, transfer.Version, receivedAt,
                [new(Guid.NewGuid(), lineId, "100")]);
            var created = await receiving.Documents.CreateAsync(document, GenerationTransferMetadata(run));
            var effective = await receiving.Documents.FinalizeAsync(document.DocumentId,
                new(created.Document.Version, transfer.Version, "Synthetic transfer receipt checked"), GenerationTransferMetadata(run));
            var accepted = await receiving.Store.AcceptLineAsync(plan.TransferId, lineId,
                new(transfer.Version, receivedAt, new(InventoryTransferReceiptDocumentV1.Kind, document.DocumentId,
                    Assert.Single(effective.Document.Lines).DocumentLineId), effective.Document.Version),
                GenerationTransferMetadata(run), static _ => Task.CompletedTask);
            Assert.Equal("Accepted", accepted.Transfer.Status);
        }

        // Another caller prepared this real command before monetary recognition committed.
        // Executing it afterwards must observe the recognized cost's preserved write fence.
        var beforeRecognitionVersion = await run.ScalarAsync<long>(
            "SELECT Version FROM inventory.Labels WHERE CompanyId=@company AND Id=@label",
            ("@company", CompanyA), ("@label", output.LabelId));
        var stalePlan = new PlanInventoryTransferV1(Guid.NewGuid(), targetLocation, "Company", CompanyA,
            sourceLocation, "Company", CompanyA, At.AddHours(3),
            [new(Guid.NewGuid(), output.LabelId, beforeRecognitionVersion.ToString(CultureInfo.InvariantCulture), "100")]);
        await using (var preparing = GenerationTransitScope.Create(run))
            await preparing.Store.PlanAsync(stalePlan, GenerationTransferMetadata(run), static _ => Task.CompletedTask);
        ManufacturingCostGenerationV1 recognized;
        await using (var recognizing = GenerationHostScope.Create(run, availableCost: true))
            recognized = await recognizing.Host.RecognizeAsync(seed.OrderId, pending.ClosedOrderVersion, pending.Revision,
                "Synthetic recognition after actual transit completion", recognizing.Metadata, CancellationToken.None);
        Assert.Equal(ManufacturingCostGenerationStateV1.Recognized, recognized.State);
        Assert.Equal(pending.InputFingerprint, recognized.InputFingerprint);
        Assert.Equal(pending.GenerationId, recognized.GenerationId);
        Assert.Equal(pending.Revision + 1, recognized.Revision);
        var movementsBeforeStaleDispatch = await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE CompanyId=@company", ("@company", CompanyA));
        await using (var stale = GenerationTransitScope.Create(run))
        {
            var staleDispatch = new DispatchInventoryTransferV1("1", At.AddHours(3).AddMinutes(1), Guid.NewGuid(), "1");
            var refused = await Assert.ThrowsAsync<InventoryTransferException>(() => stale.Store.DispatchAsync(
                stalePlan.TransferId, staleDispatch,
                new(CompanyA, stalePlan.TransferId, staleDispatch.OutgoingDocumentId, 1, [stalePlan.Lines.Single().LineId]),
                InventoryTransferSourceScaleSetting.Off, GenerationTransferMetadata(run), static _ => Task.CompletedTask));
            Assert.Equal((409, InventoryTransferCodesV1.LabelVersionConflict), (refused.Status, refused.Code));
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferDispatches WHERE TransferId=@id", ("@id", stalePlan.TransferId)));
        Assert.Equal(movementsBeforeStaleDispatch, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE CompanyId=@company", ("@company", CompanyA)));
        await using (var retry = GenerationHostScope.Create(run, availableCost: true))
        {
            var unchanged = await retry.Host.RecognizeAsync(seed.OrderId, recognized.ClosedOrderVersion, recognized.Revision,
                "Synthetic repeated observation of the same recognition", retry.Metadata, CancellationToken.None);
            Assert.Equal(recognized, unchanged);
        }
        Assert.Equal(originalAttempt, await run.ScalarAsync<string>(
            "SELECT COALESCE((SELECT * FROM inventory.Commands WHERE TargetId=@generation AND IdempotencyKeyHash=@key AND CommandName=N'inventory.manufacturing-cost.stage.v1' FOR JSON PATH, INCLUDE_NULL_VALUES), N'[]')",
            ("@generation", pending.GenerationId), ("@key", oldKeyHash)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands WHERE TargetId=@id AND CommandName=N'inventory.manufacturing-cost.stage.v1'", ("@id", pending.GenerationId)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.ManufacturingCostGenerationHistory WHERE GenerationId=@id", ("@id", pending.GenerationId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.MovementCostRecognitions WHERE GenerationId=@id", ("@id", pending.GenerationId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages WHERE AggregateId=@id", ("@id", pending.GenerationId)));
        Assert.Equal(100m, await run.ScalarAsync<decimal>("SELECT QuantityKg FROM inventory.AverageCostLedger WHERE CompanyId=@company AND ProductId=@product AND LocationId=@location",
            ("@company", CompanyA), ("@product", InventoryHostProduct), ("@location", targetLocation)));
        Assert.Equal(2m, await run.ScalarAsync<decimal>("SELECT AverageCostPerKg FROM inventory.AverageCostLedger WHERE CompanyId=@company AND ProductId=@product AND LocationId=@location",
            ("@company", CompanyA), ("@product", InventoryHostProduct), ("@location", targetLocation)));
    }

    private static InventoryCommandMetadata GenerationTransferMetadata(Run run) => InventoryHostMetadata(run)
        with { PermissionCode = InventoryPermissionManifestV1.StockTransferWrite };

    private sealed class GenerationTransitScope(AsyncServiceScope services, InventoryDbContext context,
        InventoryStore store, InventoryTransferReceiptDocuments documents) : IAsyncDisposable
    {
        public InventoryStore Store => store;
        public InventoryTransferReceiptDocuments Documents => documents;
        public static GenerationTransitScope Create(Run run)
        {
            var services = run.Factory.Services.CreateAsyncScope();
            var provider = services.ServiceProvider;
            var company = provider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
            var actor = new ManufacturingHostCurrentUser(run.Actor, CompanyA);
            var collector = provider.GetRequiredService<AuditEventCollector>();
            var writer = provider.GetRequiredService<IAuditTransactionWriterV1>();
            var context = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
                .UseSqlServer(run.Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options, company);
            var scale = new InventoryKilogramScale(new ApprovedSettingsPrerequisiteProvider(company, actor));
            var store = new InventoryStore(context, company, collector, scale, TimeProvider.System);
            var documents = new InventoryTransferReceiptDocuments(context, company, actor, new GenerationHostAuthority(),
                store, collector, writer, scale, TimeProvider.System);
            return new(services, context, store, documents);
        }
        public async ValueTask DisposeAsync() { await context.DisposeAsync(); await services.DisposeAsync(); }
    }
}
