using System.Data;
using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Api.IntegrationTests;

// Real SQL fact sources, NOT_RUN. The stock label and lineage are created by the actual shared output host.
public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Completion_V2_borrowed_reader_observes_uncommitted_real_link_revision_and_disposal_preserves_caller_transaction()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var seed = await SeedCompletionV2Async(run, "SYN-COMPLETION-V2-TX");
        var beforeAudits = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        ManufacturingSubcontractCompletionProofV2 initial;
        await using (var db = run.Context(CompanyA))
        {
            var company = new CompanyContext(); company.Set(CompanyA);
            var owner = new ManufacturingSubcontractCompletionReadPortV2(db, company, TimeProvider.System);
            initial = (await owner.GetAsync(CompanyA, seed.OrderId)).Value!;
            Assert.Equal(seed.OrderVersion, initial.OrderVersion); Assert.False(initial.IsCompleted);
            var output = Assert.Single(initial.Outputs);
            Assert.Equal((seed.OutputId, seed.LabelId, "80"), (output.OutputFactId, output.LabelId, output.RemainingKg));
        }
        await using var writer = run.Store(CompanyA);
        await using var tx = await writer.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var plan = (await writer.Plans.FindPlanAsync(seed.PlanId))!;
        var movedLink = plan.SalesOrderLink! with { SalesOrderLineId = OtherSalesLine, LineRevision = 4 };
        var moved = await writer.SalesOrderLinks.ChangeSalesOrderLinkAsync(plan.Id, plan.Version, plan.SalesOrderLinkRevision, movedLink, Metadata(run));
        var companyContext = new MutableReopeningCompany(CompanyA);
        var session = await ManufacturingSubcontractCompletionReadSessionV2.EnlistAsync(writer.Context.Database.GetDbConnection(), tx.GetDbTransaction(), companyContext);
        var current = await session.GetAsync(CompanyA, seed.OrderId);
        Assert.Equal(ManufacturingSubcontractCompletionOutcomeV1.Available, current.Outcome);
        Assert.Equal((moved.Version, moved.SalesOrderLinkRevision, OtherSalesLine, 4),
            (current.Value!.PlanVersion, current.Value.SalesLinkRevision, current.Value.SalesOrderLineId, current.Value.SalesLineRevision));
        Assert.NotEqual(initial.SourceVersion, current.Value.SourceVersion);
        var page = await session.ReadBySalesLineAsync(CompanyA, LinkedSalesOrder, OtherSalesLine, 0);
        Assert.Equal(ManufacturingSubcontractCompletionOutcomeV1.Available, page.Outcome); Assert.False(page.HasNext);
        Assert.Equal(seed.OrderId, Assert.Single(page.Items).ManufacturingOrderId);
        Assert.Empty((await session.ReadBySalesLineAsync(CompanyA, LinkedSalesOrder, LinkedSalesLine, 0)).Items);
        Assert.Empty((await session.ReadBySalesLineAsync(CompanyB, LinkedSalesOrder, OtherSalesLine, 0)).Items);
        companyContext.Set(CompanyB);
        Assert.Null((await session.GetAsync(CompanyA, seed.OrderId)).Value);
        companyContext.Set(CompanyA);
        Assert.Equal(current.Value.SourceVersion, (await session.GetAsync(CompanyA, seed.OrderId)).Value!.SourceVersion);
        await session.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.GetAsync(CompanyA, seed.OrderId));
        Assert.Same(tx, writer.Context.Database.CurrentTransaction);
        await using (var command = writer.Context.Database.GetDbConnection().CreateCommand())
        {
            command.Transaction = tx.GetDbTransaction(); command.CommandText = "SELECT 1";
            Assert.Equal(1, (int)(await command.ExecuteScalarAsync())!);
        }
        await tx.RollbackAsync();
        await using var fresh = run.Context(CompanyA);
        var restoredCompany = new CompanyContext(); restoredCompany.Set(CompanyA);
        var restored = await new ManufacturingSubcontractCompletionReadPortV2(fresh, restoredCompany, TimeProvider.System).GetAsync(CompanyA, seed.OrderId);
        Assert.Equal(initial.SourceVersion, restored.Value!.SourceVersion);
        Assert.Equal(beforeAudits, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
    }

    [SqlServerFact]
    public async Task Completion_V2_reader_refuses_weak_foreign_or_ended_transactions_without_claiming_empty_source()
    {
        await using var run = await StartManufacturingInventoryRunAsync(); var seed = await SeedCompletionV2Async(run, "SYN-COMPLETION-V2-HANDLES");
        await using var db = run.Context(CompanyA); var company = new CompanyContext(); company.Set(CompanyA);
        await using (var weak = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => ManufacturingSubcontractCompletionReadSessionV2.EnlistAsync(db.Database.GetDbConnection(), weak.GetDbTransaction(), company));
            var rejected = await new ManufacturingSubcontractCompletionReadPortV2(db, company, TimeProvider.System).GetAsync(CompanyA, seed.OrderId);
            Assert.Equal(ManufacturingSubcontractCompletionOutcomeV1.Unavailable, rejected.Outcome); Assert.Null(rejected.Value);
            Assert.Same(weak, db.Database.CurrentTransaction); await weak.RollbackAsync();
        }
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await using (var otherConnection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await otherConnection.OpenAsync(); await using var foreignTx = await otherConnection.BeginTransactionAsync(IsolationLevel.Serializable);
            await Assert.ThrowsAsync<InvalidOperationException>(() => ManufacturingSubcontractCompletionReadSessionV2.EnlistAsync(db.Database.GetDbConnection(), foreignTx, company));
            await foreignTx.RollbackAsync();
        }
        await using var owner = await ManufacturingSubcontractCompletionReadSessionV2.EnlistAsync(db.Database.GetDbConnection(), tx.GetDbTransaction(), company);
        Assert.NotNull((await owner.GetAsync(CompanyA, seed.OrderId)).Value);
        Assert.Null((await owner.GetAsync(CompanyB, seed.OrderId)).Value);
        await tx.RollbackAsync();
        var ended = await owner.GetAsync(CompanyA, seed.OrderId);
        Assert.Equal(ManufacturingSubcontractCompletionOutcomeV1.Unavailable, ended.Outcome); Assert.Null(ended.Value);
        Assert.Equal(ManufacturingSubcontractCompletionOutcomeV1.Unavailable,
            (await owner.ReadBySalesLineAsync(CompanyA, LinkedSalesOrder, LinkedSalesLine, 0)).Outcome);
        await using var otherCompanyDb = run.Context(CompanyB); var otherCompany = new CompanyContext(); otherCompany.Set(CompanyB);
        Assert.Equal(ManufacturingSubcontractCompletionOutcomeV1.NotFound,
            (await new ManufacturingSubcontractCompletionReadPortV2(otherCompanyDb, otherCompany, TimeProvider.System).GetAsync(CompanyB, seed.OrderId)).Outcome);
    }

    [SqlServerFact]
    public async Task Completion_V2_net_positive_candidates_follow_actual_output_reversals_and_process_loss_has_no_label()
    {
        await using var run = await StartManufacturingInventoryRunAsync(); var seed = await SeedCompletionV2Async(run, "SYN-COMPLETION-V2-NET");
        var company = new CompanyContext(); company.Set(CompanyA);
        await using var inventory = InventoryHostContext(run, CompanyA);
        var dispositions = new InventoryLabelDispositionReadPort(inventory, company);
        await using var host = ReturnOutputHostScope.Create(run, new CorrectionHostAuthority(), new ManufacturingOutputAdmission(), labelDispositions: dispositions);
        var command = new ManufacturingInventoryOutputCorrectionPortCommandV1(seed.OrderId, seed.OrderVersion, seed.OutputId,
            Guid.NewGuid(), seed.LabelId, seed.LabelVersion, Guid.NewGuid(), new Kilograms(10m), "SENTETIK miktar düzeltmesi", seed.OccurredAtUtc.AddMinutes(6));
        var metadata = ManufacturingHostMetadata(run, "completion-v2-correction") with { PermissionCode = ErpPermissions.ManufacturingFactsCorrect };
        var result = await host.Host.CorrectOutputAsync(command, metadata);
        await using var db = run.Context(CompanyA); var owner = new ManufacturingSubcontractCompletionReadPortV2(db, company, TimeProvider.System);
        var revised = await owner.GetAsync(CompanyA, seed.OrderId);
        Assert.Equal(result.OrderVersion, revised.Value!.OrderVersion);
        Assert.Equal((seed.OutputId, seed.LabelId, "70"), (revised.Value.Outputs[0].OutputFactId, revised.Value.Outputs[0].LabelId, revised.Value.Outputs[0].RemainingKg));
        Assert.Single(revised.Value.Outputs);
        var second = command with { ExpectedOrderVersion = result.OrderVersion, ReversalId = Guid.NewGuid(), ExpectedLabelVersion = result.LabelVersion,
            MovementId = Guid.NewGuid(), Quantity = new Kilograms(70m), OccurredAtUtc = command.OccurredAtUtc.AddMinutes(1) };
        await host.Host.CorrectOutputAsync(second, ManufacturingHostMetadata(run, "completion-v2-complete-correction") with { PermissionCode = ErpPermissions.ManufacturingFactsCorrect });
        var complete = await owner.GetAsync(CompanyA, seed.OrderId);
        Assert.Equal(ManufacturingSubcontractCompletionOutcomeV1.Available, complete.Outcome); Assert.Empty(complete.Value!.Outputs);
        Assert.NotEqual(revised.Value.SourceVersion, complete.Value.SourceVersion);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Facts WHERE OrderId=@order AND OutputClass=5", ("@order", seed.OrderId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(DISTINCT OutputId) FROM manufacturing.OutputLineageEdges WHERE OrderId=@order", ("@order", seed.OrderId)));
    }

    private static async Task<CompletionV2Seed> SeedCompletionV2Async(Run run, string code, Guid? customerParty = null, Guid? customerAccount = null)
    {
        var customer = customerParty ?? Guid.NewGuid(); var account = customerAccount ?? Guid.NewGuid();
        var seed = await PrepareIssuedOrderAsync(run, code, 100m, StockPartyKind.Counterparty, customer, StockPartyKind.Company, CompanyA,
            new(customer, account, "SENTETIK-party-v1", "SENTETIK-account-v1"));
        ManufacturingInventoryOutputResult output;
        var declaration = OutputCommand(seed, seed.OrderVersion, seed.ParentLabelVersion, 80m) with { OutputLine = PlanOutputLineReference.ClassLine };
        await using (var host = ReturnOutputHostScope.Create(run, new CorrectionHostAuthority(), new ManufacturingOutputAdmission()))
            output = await host.Host.DeclareOutputAsync(declaration, ManufacturingHostMetadata(run, "completion-v2-output"));
        await using var store = run.Store(CompanyA); var order = (await store.Orders.FindOrderAsync(seed.OrderId))!;
        var plan = (await store.Plans.FindPlanAsync(order.PlanId))!;
        await store.SalesOrderLinks.ChangeSalesOrderLinkAsync(plan.Id, plan.Version, 0,
            SalesLink(run, LinkedSalesLine) with { CustomerPartyId = customer, CustomerAccountId = account }, Metadata(run));
        await store.Orders.DeclareOutputAsync(order.Id, output.OrderVersion, Guid.NewGuid(), seed.ParentLabelId, OutputClass.ProcessLoss,
            new Kilograms(20m), seed.OccurredAtUtc.AddMinutes(3), Metadata(run));
        return new(order.Id, plan.Id, declaration.OutputId, declaration.LabelId, output.LabelVersion, output.OrderVersion + 1, seed.OccurredAtUtc);
    }
    private sealed record CompletionV2Seed(Guid OrderId, Guid PlanId, Guid OutputId, Guid LabelId, long LabelVersion, long OrderVersion, DateTimeOffset OccurredAtUtc);
}
