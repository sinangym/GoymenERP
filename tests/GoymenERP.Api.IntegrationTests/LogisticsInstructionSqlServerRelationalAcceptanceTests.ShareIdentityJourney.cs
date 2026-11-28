using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    // Actual Logistics shipment/Inventory participant, delivery, dispatch invoice-source and historical cost reader.
    // Seeded stock/allocation and Sales-risk owner ports reuse the existing SENTETIK hosted fixture. This is not
    // actual Sales invoice finalization or Accounting posting acceptance; real SQL execution remains NOT_RUN.
    [SqlServerFact]
    public async Task Share_identity_links_actual_shipment_delivery_invoice_source_and_cost_read_across_replay_and_dispatch_cancellation()
    {
        await using var run = await Run.StartAsync();
        var fixture = await SeedHostedShipmentAsync(run, "SYN-LOJD21-JOURNEY");
        var grant = await GrantRealizationAsync(run, $"SYN-LOJD21-JOURNEY-{Guid.NewGuid():N}");
        await using var host = HostedShipmentHost(run, fixture, new HostedAllocationReads(fixture), grant.Subject);
        using var client = HostedClientFor(host, grant.Subject, CompanyA);
        var request = CreateHostedShipmentRequest(fixture, "1", "identity-journey");
        using var response = await client.SendAsync(request.Message);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var shipped = Assert.IsType<LoadingInstructionResponse>(
            await response.Content.ReadFromJsonAsync<LoadingInstructionResponse>(LogisticsJson.Options));
        var shippedShare = Assert.Single(shipped.Detail.Shares, share => share.ShareId == request.ShipmentId);
        Assert.Equal("Shipped", shippedShare.Kind);

        var company = new CompanyContext(); company.Set(CompanyA);
        await using var inventory = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema)).Options, company);
        var costs = new InventoryShipmentCostReadPort(inventory, company);
        var originalCost = await costs.GetByShipmentAsync(CompanyA, request.ShipmentId);
        Assert.Equal(InventoryShipmentCostReadOutcomeV1.Available, originalCost.Outcome);
        Assert.Equal((CompanyA, request.ShipmentId, request.MovementId),
            (originalCost.CompanyId, originalCost.ShipmentId, originalCost.MovementId));
        var originalLine = Assert.Single(originalCost.Lines);
        Assert.Equal((fixture.InstructionId, fixture.LineId, fixture.AllocationId, fixture.LabelId, fixture.ProductId, 2m),
            (originalLine.LoadingInstructionId, originalLine.LoadingInstructionLineId, originalLine.AllocationId,
                originalLine.LabelId, originalLine.ProductId, originalLine.QuantityKg));

        var deliveryId = Guid.NewGuid(); var noteId = Guid.NewGuid();
        await using (var writer = run.Store(CompanyA))
        {
            var delivered = (await writer.Store.MutateInstructionAsync(run.Envelope("identity-journey-deliver"),
                Hash("identity-journey-deliver"), LogisticsCommands.RecordDeliveryV1, fixture.InstructionId,
                (value, now) => value.MarkDelivered(shipped.Detail.Instruction.Version, deliveryId, fixture.LineId,
                    request.ShipmentId, Kg(2m), now),
                new LogisticsMutationAudit("logistics.loading-instruction.delivery-recorded.v1", "RecordDelivery", "SENTETIK LOJ-D21 teslim")))!.Instruction;
            var delivery = Assert.Single(delivered.Instruction.Shares, share => share.ShareId == deliveryId);
            Assert.Equal(request.ShipmentId, delivery.SourceShipmentId);
            var drafted = (await writer.Store.MutateInstructionAsync(run.Envelope("identity-journey-note"),
                Hash("identity-journey-note"), LogisticsCommands.CreateDispatchNoteV1, fixture.InstructionId,
                (value, now) => value.CreateDispatchNoteDraft(delivered.Instruction.Version, noteId,
                    [request.ShipmentId], run.Actor, now), Drafted))!.Instruction;
            await writer.Store.MutateInstructionAsync(run.Envelope("identity-journey-approve"),
                Hash("identity-journey-approve"), LogisticsCommands.ApproveDispatchNoteV1, fixture.InstructionId,
                (value, now) => value.ApproveDispatchNote(drafted.Instruction.Version, noteId,
                    "SENTETIK-LOJD21-dispatch-approver", now), Approved);
        }
        await using (var reader = run.Store(CompanyA))
        {
            var source = Assert.IsType<StoredApprovedDispatchInvoiceSource>(await reader.Store.FindApprovedDispatchInvoiceSourceAsync(noteId));
            var line = Assert.Single(source.Lines);
            Assert.Equal((request.ShipmentId, fixture.OrderLineId, fixture.ProductId, 2m),
                (line.ShipmentId, line.SourceOrderLineId, line.ProductId, line.Quantity));
            var proof = await new LogisticsInvoicedShipmentProofReadPortV1(company, reader.Store)
                .GetAsync(CompanyA, noteId, line.LineNo, request.ShipmentId);
            Assert.Equal(LogisticsInvoicedShipmentProofOutcomeV1.Found, proof.Outcome);
            Assert.Equal((originalCost.ShipmentId, originalLine.LoadingInstructionId, originalLine.LoadingInstructionLineId),
                (proof.Proof!.ShipmentId, proof.Proof.InstructionId, proof.Proof.InstructionLineId));
        }

        var beforeReplay = await HostedEffectsAsync(run, fixture, request);
        var retry = CreateHostedShipmentRequest(fixture, "1", "identity-journey", request.ShipmentId, request.MovementId);
        using var replay = await client.SendAsync(retry.Message);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal(request.CorrelationId, Assert.Single(replay.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        Assert.Equal(beforeReplay, await HostedEffectsAsync(run, fixture, request));

        await using (var writer = run.Store(CompanyA))
        {
            var current = Assert.IsType<StoredLoadingInstruction>(await writer.Store.FindInstructionAsync(fixture.InstructionId));
            await writer.Store.MutateInstructionAsync(run.Envelope("identity-journey-cancel"),
                Hash("identity-journey-cancel"), LogisticsCommands.CancelDispatchNoteV1, fixture.InstructionId,
                (value, now) => value.CancelDispatchNote(current.Instruction.Version, noteId, run.Actor,
                    "SENTETIK LOJ-D21 dispatch cancellation", now), Cancelled);
            Assert.Null(await writer.Store.FindApprovedDispatchInvoiceSourceAsync(noteId));
        }
        var afterCancellation = await costs.GetByShipmentAsync(CompanyA, request.ShipmentId);
        Assert.Equal(InventoryShipmentCostReadOutcomeV1.Available, afterCancellation.Outcome);
        Assert.Equal((originalCost.ShipmentId, originalCost.MovementId, originalCost.SourceVersion),
            (afterCancellation.ShipmentId, afterCancellation.MovementId, afterCancellation.SourceVersion));
        Assert.Equal(originalLine, Assert.Single(afterCancellation.Lines));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingShares WHERE CompanyId=@company AND ShareId=@share",
            ("@company", CompanyA), ("@share", request.ShipmentId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE CompanyId=@company AND SourceId=@share",
            ("@company", CompanyA), ("@share", request.ShipmentId)));
        Assert.False(inventory.ChangeTracker.HasChanges());
    }
}
