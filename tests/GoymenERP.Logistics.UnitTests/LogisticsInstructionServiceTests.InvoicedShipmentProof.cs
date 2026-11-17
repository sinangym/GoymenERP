using GoymenERP.Modules.Logistics.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Logistics.UnitTests;

public sealed partial class LogisticsInstructionServiceTests
{
    [Fact]
    public async Task Invoiced_shipment_proof_joins_the_actual_whole_note_line_to_the_loading_line_without_writes()
    {
        var (world, instructionId, noteId, shipmentId) = await InvoiceProofWorld();
        var transitions = world.Store.Transitions.Count;
        var reader = new LogisticsInvoicedShipmentProofReadPortV1(world.Company, world.Store);
        var result = await reader.GetAsync(Company, noteId, 1, shipmentId);
        Assert.Equal(LogisticsInvoicedShipmentProofOutcomeV1.Found, result.Outcome);
        var proof = Assert.IsType<LogisticsInvoicedShipmentProofV1>(result.Proof);
        var stored = (await world.Store.FindInstructionAsync(instructionId))!.Instruction;
        var actual = Assert.Single(stored.Shares, item => item.ShareId == shipmentId);
        Assert.Equal((Company, noteId, 1, shipmentId, instructionId, actual.LineId, Order, OrderLine, Product, KgUnit, 3m, "8"),
            (proof.CompanyId, proof.DispatchNoteId, proof.DispatchNoteLineNo, proof.ShipmentId, proof.InstructionId,
                proof.InstructionLineId, proof.SalesOrderId, proof.SalesOrderLineId, proof.ProductId, proof.UnitId,
                proof.DispatchQuantity, proof.SourceVersion));
        Assert.Equal(transitions, world.Store.Transitions.Count);
        Assert.DoesNotContain(typeof(LogisticsInvoicedShipmentProofV1).GetProperties(),
            property => property.Name.Contains("Cost") || property.Name.Contains("Price") || property.Name.Contains("Amount"));
    }

    [Fact]
    public async Task Invoiced_shipment_proof_does_not_turn_another_line_or_foreign_company_into_a_valid_source()
    {
        var (world, _, noteId, shipmentId) = await InvoiceProofWorld();
        var reader = new LogisticsInvoicedShipmentProofReadPortV1(world.Company, world.Store);
        var wrongLine = await reader.GetAsync(Company, noteId, 2, shipmentId);
        Assert.Equal(LogisticsInvoicedShipmentProofOutcomeV1.NotFound, wrongLine.Outcome);
        Assert.Null(wrongLine.Proof);
        var calls = world.Store.InvoiceSourceReads;
        var foreign = await reader.GetAsync(Guid.NewGuid(), noteId, 1, shipmentId);
        Assert.Equal(LogisticsInvoicedShipmentProofOutcomeV1.Unavailable, foreign.Outcome);
        Assert.Null(foreign.Proof);
        Assert.Equal(calls, world.Store.InvoiceSourceReads);
    }

    [Fact]
    public async Task Invoiced_shipment_proof_rejects_company_change_during_an_owner_read()
    {
        var (world, _, noteId, shipmentId) = await InvoiceProofWorld();
        world.Store.AfterInvoiceSourceRead = () => world.Company.Set(Guid.NewGuid());
        var result = await new LogisticsInvoicedShipmentProofReadPortV1(world.Company, world.Store)
            .GetAsync(Company, noteId, 1, shipmentId);
        Assert.Equal(LogisticsInvoicedShipmentProofOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.Proof);
    }

    [Fact]
    public async Task Invoiced_shipment_proof_refuses_a_contradictory_historical_order_revision()
    {
        var (world, _, noteId, shipmentId) = await InvoiceProofWorld();
        world.Store.InvoiceSourceRevisionOverride = 999;
        var result = await new LogisticsInvoicedShipmentProofReadPortV1(world.Company, world.Store)
            .GetAsync(Company, noteId, 1, shipmentId);
        Assert.Equal(LogisticsInvoicedShipmentProofOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.Proof);
    }

    [Fact]
    public async Task A_cancelled_note_is_not_an_active_invoiced_shipment_cost_source()
    {
        var (world, instructionId, noteId, shipmentId) = await InvoiceProofWorld();
        await world.Service().CancelDispatchNoteAsync(instructionId, noteId,
            new CancelDispatchNoteRequest("8", "SENTETIK iptal"), Metadata("invoice-proof-cancel"));
        var result = await new LogisticsInvoicedShipmentProofReadPortV1(world.Company, world.Store)
            .GetAsync(Company, noteId, 1, shipmentId);
        Assert.Equal(LogisticsInvoicedShipmentProofOutcomeV1.NotFound, result.Outcome);
        Assert.Null(result.Proof);
    }

    [Fact]
    public async Task Invoiced_shipment_proof_propagates_caller_cancellation_without_a_source_read()
    {
        var world = new World();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new LogisticsInvoicedShipmentProofReadPortV1(world.Company, world.Store)
                .GetAsync(Company, Guid.NewGuid(), 1, Guid.NewGuid(), cancellation.Token));
        Assert.Equal(0, world.Store.InvoiceSourceReads);
    }

    private static async Task<(World World, Guid InstructionId, Guid NoteId, Guid ShipmentId)> InvoiceProofWorld()
    {
        var (world, instructionId, _, firstShipment, secondShipment) = await ShippedWorld();
        var draft = DispatchNote("6", firstShipment, secondShipment);
        await world.Service().CreateDispatchNoteAsync(instructionId, draft, Metadata("invoice-proof-create"));
        await world.Service().ApproveDispatchNoteAsync(instructionId, draft.DispatchNoteId,
            new ApproveDispatchNoteRequest("7"), Metadata("invoice-proof-approve"));
        return (world, instructionId, draft.DispatchNoteId, firstShipment);
    }
}
