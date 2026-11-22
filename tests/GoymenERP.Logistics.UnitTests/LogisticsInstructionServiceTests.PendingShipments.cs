using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// AI-D35 (Y-A1): the owner's paged shipment-state read — the same permission, validation and store call as the instruction
/// list, each row with its shipment state from the stored lines. Draft and issued-not-shipped read NotShipped, a cancelled
/// instruction is only on the Cancelled page, a status is required and exact, and a lost grant refuses before the store.
/// SENTETIK.
/// </summary>
public sealed partial class LogisticsInstructionServiceTests
{
    [Fact]
    public async Task The_shipment_state_read_pages_by_exact_status_and_reads_unshipped_draft_and_issued_instructions_as_not_shipped()
    {
        var world = new World();
        var draft = (await world.Service().CreateInstructionAsync(Request(), Metadata("k-draft"))).Detail.Instruction.Id;
        var issuedId = (await world.Service().CreateInstructionAsync(Request(), Metadata("k-issued"))).Detail.Instruction.Id;
        await world.Service().IssueInstructionAsync(issuedId, new IssueLoadingInstructionRequest("2"), Metadata("k-issue"));
        var cancelledId = (await world.Service().CreateInstructionAsync(Request(), Metadata("k-cancelled"))).Detail.Instruction.Id;
        await world.Service().CancelInstructionAsync(cancelledId, new CancelInstructionRequest("2", "SENTETIK vazgeçti"), Metadata("k-cancel"));

        var drafts = await world.Service().GetPendingShipmentPageAsync(new LogisticsPendingShipmentPageQueryV1("Draft"));
        var issued = await world.Service().GetPendingShipmentPageAsync(new LogisticsPendingShipmentPageQueryV1("Issued"));
        var cancelled = await world.Service().GetPendingShipmentPageAsync(new LogisticsPendingShipmentPageQueryV1("Cancelled"));

        var draftRow = Assert.Single(drafts.Items);
        Assert.Equal((draft, "Draft", LogisticsShipmentStatesV1.NotShipped, (int?)0), (draftRow.InstructionId, draftRow.Status, draftRow.ShipmentState, draftRow.LinesFullyShipped));
        var issuedRow = Assert.Single(issued.Items);
        Assert.Equal((issuedId, "Issued", LogisticsShipmentStatesV1.NotShipped), (issuedRow.InstructionId, issuedRow.Status, issuedRow.ShipmentState));
        Assert.Equal(cancelledId, Assert.Single(cancelled.Items).InstructionId);
        Assert.All(drafts.Items.Concat(issued.Items).Concat(cancelled.Items), row => Assert.Equal(1, row.LineCount));
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("Shipped")]
    [InlineData("")]
    public async Task The_shipment_state_read_refuses_a_status_that_is_not_an_exact_name(string status)
    {
        var world = new World();

        var bad = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(
            () => world.Service().GetPendingShipmentPageAsync(new LogisticsPendingShipmentPageQueryV1(status)));

        Assert.Equal((400, "validation_failed"), (bad.Status, bad.Code));
    }

    [Fact]
    public async Task The_shipment_state_read_refuses_without_the_read_grant()
    {
        var world = new World { Authority = { Granted = false } };

        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(
            () => world.Service().GetPendingShipmentPageAsync(new LogisticsPendingShipmentPageQueryV1("Issued")));

        Assert.Equal(403, refusal.Status);
    }
}
