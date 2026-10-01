using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Logistics.UnitTests;

public sealed partial class LogisticsInstructionServiceTests
{
    [Fact]
    public async Task Approved_dispatch_note_is_read_as_price_free_invoice_source_without_a_write()
    {
        var (world, instructionId, _, firstShipment, secondShipment) = await ShippedWorld();
        var draft = DispatchNote("6", firstShipment, secondShipment);
        await world.Service().CreateDispatchNoteAsync(instructionId, draft, Metadata("invoice-source-draft"));

        var notApproved = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().GetApprovedDispatchInvoiceSourceAsync(draft.DispatchNoteId));
        Assert.Equal((404, "not_found"), (notApproved.Status, notApproved.Code));

        await world.Service().ApproveDispatchNoteAsync(instructionId, draft.DispatchNoteId,
            new ApproveDispatchNoteRequest("7"), Metadata("invoice-source-approve"));
        var authorityCalls = world.Authority.Calls;
        var transitions = world.Store.Transitions.Count;

        var source = await world.Service().GetApprovedDispatchInvoiceSourceAsync(draft.DispatchNoteId);

        Assert.Equal((Company, draft.DispatchNoteId, instructionId, 8L, Party, Account, "IRS-000001", (string?)null),
            (source.CompanyId, source.DispatchNoteId, source.InstructionId, source.InstructionVersion,
                source.CustomerPartyId, source.CompanyAccountId, source.SystemNumber, source.EDispatchNumber));
        Assert.Equal([(Order, OrderLine, firstShipment, Product, KgUnit, "3"),
                      (Order, OrderLine, secondShipment, Product, KgUnit, "2.5")],
            source.Lines.Select(line => (line.SourceOrderId, line.SourceOrderLineId, line.ShipmentId,
                line.ProductId, line.UnitId, line.ShippedQuantity)).ToArray());
        Assert.NotEqual(default, source.ApprovedAtUtc);
        Assert.Equal(authorityCalls + 2, world.Authority.Calls);
        Assert.Equal(ReadPermission, world.Authority.LastPermission);
        Assert.Equal(transitions, world.Store.Transitions.Count);

        var headerProperties = typeof(ApprovedDispatchInvoiceSourceV1).GetProperties().Select(property => property.Name).ToArray();
        Assert.DoesNotContain("SourceOrderId", headerProperties);
        Assert.DoesNotContain(headerProperties, IsMoneyOrConsumptionMember);
        Assert.DoesNotContain(typeof(ApprovedDispatchInvoiceSourceLineV1).GetProperties(), property => IsMoneyOrConsumptionMember(property.Name));
    }

    [Fact]
    public async Task Cancelled_dispatch_note_is_no_longer_an_invoice_source()
    {
        var (world, instructionId, _, firstShipment, _) = await ShippedWorld();
        var draft = DispatchNote("6", firstShipment);
        await world.Service().CreateDispatchNoteAsync(instructionId, draft, Metadata("invoice-source-create"));
        await world.Service().ApproveDispatchNoteAsync(instructionId, draft.DispatchNoteId,
            new ApproveDispatchNoteRequest("7"), Metadata("invoice-source-approve"));
        await world.Service().CancelDispatchNoteAsync(instructionId, draft.DispatchNoteId,
            new CancelDispatchNoteRequest("8", "Yanlış araç"), Metadata("invoice-source-cancel"));

        var cancelled = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().GetApprovedDispatchInvoiceSourceAsync(draft.DispatchNoteId));

        Assert.Equal((404, "not_found"), (cancelled.Status, cancelled.Code));
    }

    [Fact]
    public async Task Authority_revoked_after_owner_read_suppresses_the_invoice_source()
    {
        var (world, instructionId, _, firstShipment, _) = await ShippedWorld();
        var draft = DispatchNote("6", firstShipment);
        await world.Service().CreateDispatchNoteAsync(instructionId, draft, Metadata("invoice-source-create"));
        await world.Service().ApproveDispatchNoteAsync(instructionId, draft.DispatchNoteId,
            new ApproveDispatchNoteRequest("7"), Metadata("invoice-source-approve"));
        world.Authority.DenyOnCall = world.Authority.Calls + 2;

        var denied = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().GetApprovedDispatchInvoiceSourceAsync(draft.DispatchNoteId));

        Assert.Equal((403, "forbidden"), (denied.Status, denied.Code));
        Assert.Equal(1, world.Store.InvoiceSourceReads);
    }

    private static bool IsMoneyOrConsumptionMember(string name) =>
        name.Contains("Price", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Amount", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Tax", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Remaining", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Consumed", StringComparison.OrdinalIgnoreCase);
}
