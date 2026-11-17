using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Transfers;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    /// <summary>Actual Inventory cost and transfer owners; existing outgoing-document proof and
    /// permission fixtures are synthetic. No public grant or Manufacturing HTTP acceptance is claimed.</summary>
    [SqlServerFact]
    public async Task A_recognized_generation_waits_for_real_dispatch_cancellation_before_its_cost_can_reverse()
    {
        await using var run = await Run.StartAsync(); var fixture = await RecognizedTransferOutputAsync(run);
        var plan = await PlanCostOutputTransferAsync(run, fixture.LabelId, fixture.SourceLocation, fixture.TargetLocation);
        var dispatch = new DispatchInventoryTransferV1("1", ManufacturingAt.AddHours(3), Guid.NewGuid(), "1");
        await using (var scope = run.Store(CompanyA)) await scope.Store.DispatchAsync(plan.TransferId, dispatch,
            EffectiveOutgoing(plan, dispatch), InventoryTransferSourceScaleSetting.Off, Metadata(run), Authorized);
        var before = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        var denied = await Assert.ThrowsAsync<InventoryManufacturingCostGenerationException>(() => ReverseTransferGenerationAsync(run, fixture));
        Assert.Equal((409, "inventory_manufacturing_cost_transfer_reversal_required"), (denied.Status, denied.Code));
        Assert.Equal(before, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.ManufacturingCostGenerationHistory"));
        await using (var scope = run.Store(CompanyA)) await scope.Store.CancelDispatchAsync(plan.TransferId,
            new("2", ManufacturingAt.AddHours(4), "Sentetik gercek transfer iptali"), null, null, Metadata(run), Authorized);
        var reversed = await ReverseTransferGenerationAsync(run, fixture);
        Assert.Equal(InventoryManufacturingCostGenerationStateV1.Reversed, reversed.State);
        Assert.Equal(-100m, Assert.Single(reversed.Outputs).RemainingTlValue);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferDispatchCancellations"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.ManufacturingCostGenerationHistory"));
    }

    [SqlServerFact]
    public async Task An_accepted_recognized_output_transfer_returns_only_through_its_real_linked_reverse_dispatch_and_acceptance()
    {
        await using var run = await Run.StartAsync(); var fixture = await RecognizedTransferOutputAsync(run);
        var plan = await PlanCostOutputTransferAsync(run, fixture.LabelId, fixture.SourceLocation, fixture.TargetLocation);
        var dispatch = new DispatchInventoryTransferV1("1", ManufacturingAt.AddHours(3), Guid.NewGuid(), "1");
        await using (var scope = run.Store(CompanyA)) await scope.Store.DispatchAsync(plan.TransferId, dispatch,
            EffectiveOutgoing(plan, dispatch), InventoryTransferSourceScaleSetting.Off, Metadata(run), Authorized);
        var accept = await RecordIncomingAcceptanceDocumentAsync(run, plan.TransferId, plan.Lines[0].LineId, ManufacturingAt.AddHours(4));
        await using (var scope = run.Store(CompanyA)) await scope.Store.AcceptLineAsync(plan.TransferId, plan.Lines[0].LineId, accept, Metadata(run), Authorized);
        Assert.Equal(409, (await Assert.ThrowsAsync<InventoryManufacturingCostGenerationException>(() => ReverseTransferGenerationAsync(run, fixture))).Status);
        var reverseId = Guid.NewGuid(); var reverseLine = Guid.NewGuid();
        await using (var scope = run.Store(CompanyA)) await scope.Store.ReverseAsync(plan.TransferId,
            new(reverseId, ManufacturingAt.AddHours(5), "Sentetik gercek bagli ters transfer",
                [new(reverseLine, plan.Lines[0].LineId, await CostOutputVersionAsync(run, fixture.LabelId), "10")]), Metadata(run), Authorized);
        // A plan is inert; creating it does not pretend that the stock was returned.
        Assert.Equal(409, (await Assert.ThrowsAsync<InventoryManufacturingCostGenerationException>(() => ReverseTransferGenerationAsync(run, fixture))).Status);
        var returnDispatch = new DispatchInventoryTransferV1("1", ManufacturingAt.AddHours(6), Guid.NewGuid(), "1");
        await using (var scope = run.Store(CompanyA)) await scope.Store.DispatchAsync(reverseId, returnDispatch,
            EffectiveOutgoing(reverseId, returnDispatch, reverseLine), InventoryTransferSourceScaleSetting.Off, Metadata(run), Authorized);
        Assert.Equal(409, (await Assert.ThrowsAsync<InventoryManufacturingCostGenerationException>(() => ReverseTransferGenerationAsync(run, fixture))).Status);
        var returnAccept = await RecordIncomingAcceptanceDocumentAsync(run, reverseId, reverseLine, ManufacturingAt.AddHours(7));
        await using (var scope = run.Store(CompanyA)) await scope.Store.AcceptLineAsync(reverseId, reverseLine, returnAccept, Metadata(run), Authorized);
        Assert.Equal(InventoryManufacturingCostGenerationStateV1.Reversed, (await ReverseTransferGenerationAsync(run, fixture)).State);
        Assert.Equal((int)InventoryTransferStatus.Accepted, await run.ScalarAsync<int>("SELECT Status FROM inventory.Transfers WHERE Id=@id", ("@id", plan.TransferId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferReversals"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferDispatchCancellations"));
    }

    [SqlServerFact]
    public async Task The_cost_transfer_epoch_is_append_only_and_a_later_refusal_rolls_back_without_replacing_its_proof()
    {
        await using var run = await Run.StartAsync(); var fixture = await RecognizedTransferOutputAsync(run);
        var json = await run.ScalarAsync<string>("SELECT ResultJson FROM inventory.MovementCostRecognitions WHERE GenerationId=@id", ("@id", fixture.Generation.GenerationId));
        Assert.Contains("\"transferScope\":", json, StringComparison.Ordinal);
        var plan = await PlanCostOutputTransferAsync(run, fixture.LabelId, fixture.SourceLocation, fixture.TargetLocation);
        var dispatch = new DispatchInventoryTransferV1("1", ManufacturingAt.AddHours(3), Guid.NewGuid(), "1");
        await using (var scope = run.Store(CompanyA)) await scope.Store.DispatchAsync(plan.TransferId, dispatch,
            EffectiveOutgoing(plan, dispatch), InventoryTransferSourceScaleSetting.Off, Metadata(run), Authorized);
        await Assert.ThrowsAsync<InventoryManufacturingCostGenerationException>(() => ReverseTransferGenerationAsync(run, fixture));
        Assert.Equal(json, await run.ScalarAsync<string>("SELECT ResultJson FROM inventory.MovementCostRecognitions WHERE GenerationId=@id", ("@id", fixture.Generation.GenerationId)));
        await using var final = run.Context(CompanyA);
        Assert.Equal((int)InventoryManufacturingCostGenerationStateV1.Recognized,
            (await final.ManufacturingCostGenerations.SingleAsync()).State);
        Assert.Equal(1, await final.ManufacturingCostGenerationHistory.CountAsync());
    }

    [SqlServerFact]
    public async Task An_unrelated_ordinary_transfer_back_to_the_same_location_is_not_the_original_transfers_undo_proof()
    {
        await using var run = await Run.StartAsync(); var fixture = await RecognizedTransferOutputAsync(run);
        await Move(fixture.SourceLocation, fixture.TargetLocation, 3);
        await Move(fixture.TargetLocation, fixture.SourceLocation, 5);
        await using (var current = run.Context(CompanyA))
            Assert.Equal(10m, await current.Balances.Where(x => x.LabelId == fixture.LabelId && x.LocationId == fixture.SourceLocation).SumAsync(x => x.QuantityKg));
        var error = await Assert.ThrowsAsync<InventoryManufacturingCostGenerationException>(() => ReverseTransferGenerationAsync(run, fixture));
        Assert.Equal((409, "inventory_manufacturing_cost_transfer_reversal_required"), (error.Status, error.Code));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferReversals"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.ManufacturingCostGenerationHistory"));

        async Task Move(Guid from, Guid to, int hour)
        {
            var plan = await PlanCostOutputTransferAsync(run, fixture.LabelId, from, to);
            var dispatch = new DispatchInventoryTransferV1("1", ManufacturingAt.AddHours(hour), Guid.NewGuid(), "1");
            await using (var scope = run.Store(CompanyA)) await scope.Store.DispatchAsync(plan.TransferId, dispatch,
                EffectiveOutgoing(plan, dispatch), InventoryTransferSourceScaleSetting.Off, Metadata(run), Authorized);
            var accept = await RecordIncomingAcceptanceDocumentAsync(run, plan.TransferId, plan.Lines[0].LineId, ManufacturingAt.AddHours(hour + 1));
            await using var receiving = run.Store(CompanyA); await receiving.Store.AcceptLineAsync(plan.TransferId, plan.Lines[0].LineId, accept, Metadata(run), Authorized);
        }
    }

    private sealed record TransferGenerationFixture(Guid OrderId, Guid LabelId, Guid SourceLocation, Guid TargetLocation,
        InventoryManufacturingCostGenerationResultV1 Generation);
    private static async Task<TransferGenerationFixture> RecognizedTransferOutputAsync(Run run)
    {
        var location = await run.LocationOfAsync(CompanyA); var target = await TransferLocationAsync(run, CompanyA, 1);
        var parent = await CostedLabelAsync(run, location, 10m, 2m, 1m); var order = Guid.NewGuid();
        var issue = await CommitIssueAsync(run, IssueRequest(order, parent.Id, "2", "10"), ManufacturingMetadata(run, "transfer-cost-issue"));
        var output = OutputRequest(order, parent.Id, issue.ParentLabelVersion, location, "10");
        await CommitOutputAsync(run, output, ManufacturingMetadata(run, "transfer-cost-output"));
        var command = new InventoryManufacturingCostGenerationCommandV1(CompanyA, order, Guid.NewGuid(), "0", "8",
            InventoryManufacturingCostGenerationStateV1.Recognized, "SENTETIK-transfer-cost", "SENTETIK-calculator-v1", "{}",
            [new(output.OutputId, output.MovementId, output.LabelId, 10m, 10m, null, 100m)],
            "Sentetik gercek maliyet tanima", ManufacturingAt.AddHours(1));
        return new(order, output.LabelId, location, target, await StageCostGenerationAsync(run, command,
            ManufacturingMetadata(run, "transfer-cost-recognize"), true));
    }
    private static async Task<string> CostOutputVersionAsync(Run run, Guid label)
    {
        await using var context = run.Context(CompanyA);
        return (await context.Labels.Where(x => x.Id == label).Select(x => x.Version).SingleAsync()).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    private static async Task<PlanInventoryTransferV1> PlanCostOutputTransferAsync(Run run, Guid label, Guid source, Guid target)
    {
        var plan = new PlanInventoryTransferV1(Guid.NewGuid(), source, "Company", CompanyA, target, "Company", CompanyA,
            ManufacturingAt.AddHours(2), [new(Guid.NewGuid(), label, await CostOutputVersionAsync(run, label), "10")]);
        await using var scope = run.Store(CompanyA); await scope.Store.PlanAsync(plan, Metadata(run), Authorized); return plan;
    }
    private static async Task<InventoryManufacturingCostGenerationResultV1> ReverseTransferGenerationAsync(Run run, TransferGenerationFixture fixture)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString); await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        await using var scope = run.Factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        await using var owner = await InventoryManufacturingCostGenerationParticipant.EnlistAsync(connection, transaction, CompanyA,
            scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
        var result = await owner.ReverseAsync(new(CompanyA, fixture.OrderId, fixture.Generation.GenerationId, fixture.Generation.GenerationVersion,
            "8", "Sentetik kontrollu maliyet geri acma", ManufacturingAt.AddHours(8)), ManufacturingMetadata(run, "transfer-cost-reverse"));
        await transaction.CommitAsync(); return result;
    }
}
