using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// F-020 through the service: cancelling a loading instruction is its own registered name, version-fenced, idempotent, refuses a
/// shipped instruction as a recorded terminal 409 and an already cancelled one as a state 409, and writes one transition.
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class LogisticsInstructionServiceTests
{
    private const string InstructionCancelPermission = LogisticsPermissionManifestV1.InstructionsCancel;

    [Fact]
    public async Task Cancelling_an_instruction_is_fenced_by_its_own_name_validated_and_a_missing_grant_writes_nothing()
    {
        var world = new World();
        var created = await world.Service().CreateInstructionAsync(Request(quantity: "10"), Metadata("k-create"));
        var id = created.Detail.Instruction.Id;
        world.Authority.DeniedPermission = InstructionCancelPermission;

        var denied = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CancelInstructionAsync(id, new CancelInstructionRequest("2", "Müşteri vazgeçti"), Metadata("k-denied")));
        Assert.Equal((403, "forbidden"), (denied.Status, denied.Code));
        Assert.DoesNotContain(world.Store.Transitions, transition => transition.Command == LogisticsCommands.CancelInstructionV1);

        world.Authority.DeniedPermission = null;
        foreach (var reason in new[] { "", "   ", " padded ", new string('x', 501) })
        {
            var invalid = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
                world.Service().CancelInstructionAsync(id, new CancelInstructionRequest("2", reason), Metadata($"k-bad-{reason.Length}")));
            Assert.Equal((400, "validation_failed", "reason"), (invalid.Status, invalid.Code, invalid.Errors[0].Field));
        }

        Assert.Equal(LoadingInstructionStatus.Draft, world.Store.Instruction(id).Status);
        var cancelled = await world.Service().CancelInstructionAsync(id, new CancelInstructionRequest("2", "Müşteri vazgeçti"), Metadata("k-cancel"));
        Assert.Equal(InstructionCancelPermission, world.Authority.LastPermission);
        Assert.Equal("3", cancelled.Detail.Instruction.Version);
        Assert.Equal(LoadingInstructionStatus.Cancelled, world.Store.Instruction(id).Status);
        Assert.Single(world.Store.Transitions, transition => transition.Command == LogisticsCommands.CancelInstructionV1);
    }

    [Fact]
    public async Task Cancelling_replays_on_the_same_key_refuses_a_second_cancel_a_stale_version_and_another_companys_instruction()
    {
        var world = new World();
        var created = await world.Service().CreateInstructionAsync(Request(quantity: "10"), Metadata("k-create"));
        var id = created.Detail.Instruction.Id;
        var request = new CancelInstructionRequest("2", "Müşteri vazgeçti");
        await world.Service().CancelInstructionAsync(id, request, Metadata("k-cancel"));

        var replay = await world.Service().CancelInstructionAsync(id, request, Metadata("k-cancel"));
        Assert.True(replay.IsReplay);
        Assert.Equal(1, world.Store.Transitions.Count(transition => transition.Command == LogisticsCommands.CancelInstructionV1));

        // The same key with another payload is the idempotency mismatch, never a second cancel.
        var mismatch = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CancelInstructionAsync(id, new CancelInstructionRequest("2", "Başka gerekçe"), Metadata("k-cancel")));
        Assert.Equal(409, mismatch.Status);

        // A new key on a cancelled instruction: a state refusal; the cancel marks stay.
        var again = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CancelInstructionAsync(id, new CancelInstructionRequest("3", "Tekrar"), Metadata("k-cancel-2")));
        Assert.Equal((409, LogisticsProblemCodesV1.InstructionState), (again.Status, again.Code));
        Assert.Equal(1, world.Store.Transitions.Count(transition => transition.Command == LogisticsCommands.CancelInstructionV1));

        var unknown = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CancelInstructionAsync(Guid.NewGuid(), new CancelInstructionRequest("1", "Yok"), Metadata("k-unknown")));
        Assert.Equal((404, "not_found"), (unknown.Status, unknown.Code));
    }

    [Fact]
    public async Task A_shipped_instruction_cannot_be_cancelled_and_an_issued_one_with_a_load_can()
    {
        var (shippedWorld, shippedId, _, _, _) = await ShippedWorld();
        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            shippedWorld.Service().CancelInstructionAsync(shippedId, new CancelInstructionRequest("6", "Geç kaldı"), Metadata("k-shipped")));
        Assert.Equal((409, LogisticsProblemCodesV1.InstructionShipped), (refusal.Status, refusal.Code));
        Assert.Equal(LoadingInstructionStatus.Issued, shippedWorld.Store.Instruction(shippedId).Status);

        var world = new World();
        var created = await world.Service().CreateInstructionAsync(Request(quantity: "10"), Metadata("k-create"));
        var id = created.Detail.Instruction.Id;
        var line = created.Detail.Lines[0].LineId;
        await world.Service().IssueInstructionAsync(id, new IssueLoadingInstructionRequest("2"), Metadata("k-issue"));
        await world.Service().RecordLoadAsync(id, line, new RecordLoadRequest("3", Guid.NewGuid(), "8", "Measured"), Metadata("k-load"));
        await world.Service().CancelInstructionAsync(id, new CancelInstructionRequest("4", "Araç gelmedi"), Metadata("k-cancel"));
        Assert.Equal(LoadingInstructionStatus.Cancelled, world.Store.Instruction(id).Status);
    }
}
