using System.Text.Json;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;

namespace GoymenERP.Logistics.UnitTests;

public sealed partial class LogisticsInstructionServiceTests
{
    [Fact]
    public async Task Dispatch_approval_private_receipt_retains_original_version_after_later_native_cancellation()
    {
        var (world, id, _, shipment, _) = await ShippedWorld();
        var draft = DispatchNote("6", shipment);
        await world.Service().CreateDispatchNoteAsync(id, draft, Metadata("dispatch-receipt-draft"));
        var first = await world.Approver().ApproveDispatchNoteAsync(id, draft.DispatchNoteId, new("7"), Metadata("dispatch-receipt"));
        Assert.True(first.CommandReceiptProven);
        var receipt = Assert.IsType<LogisticsDispatchNoteApprovalReceiptV1>(first.DispatchNoteApprovalReceipt);
        Assert.Equal((id, draft.DispatchNoteId, 7L, 8L), (receipt.InstructionId, receipt.DispatchNoteId, receipt.ExpectedVersion, receipt.ApprovedVersion));
        await world.Service().CancelDispatchNoteAsync(id, draft.DispatchNoteId, new("8", "Düzeltme"), Metadata("dispatch-later-cancel"));
        var replay = await world.Approver().ApproveDispatchNoteAsync(id, draft.DispatchNoteId, new("7"), Metadata("dispatch-receipt"));
        Assert.True(replay.IsReplay); Assert.True(replay.CommandReceiptProven); Assert.Equal(receipt, replay.DispatchNoteApprovalReceipt);
        Assert.Equal("Cancelled", replay.Detail.DispatchNotes[0].Status); Assert.Equal("9", replay.Detail.Instruction.Version);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(replay, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(new[] { "detail", "isReplay", "originalCorrelationId" }, json.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Single(world.Store.Transitions, t => t.Command == LogisticsCommands.ApproveDispatchNoteV1);
    }

    [Fact]
    public async Task Dispatch_approval_failed_save_uses_one_bounded_winner_read_not_a_mutation_retry_or_durable_refusal()
    {
        var (world, id, _, shipment, _) = await ShippedWorld();
        var draft = DispatchNote("6", shipment);
        await world.Service().CreateDispatchNoteAsync(id, draft, Metadata("dispatch-race-draft"));
        var mutationCalls = 0;
        world.Store.MutateFault = command =>
        {
            if (command != LogisticsCommands.ApproveDispatchNoteV1) return null;
            mutationCalls++;
            return new LogisticsStoreException(LogisticsStoreCodes.ConcurrentChange, "SENTETIK save race");
        };
        var reads = world.Store.Replays;
        var error = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Approver()
            .ApproveDispatchNoteAsync(id, draft.DispatchNoteId, new("7"), Metadata("dispatch-race")));
        Assert.Equal((409, LogisticsProblemCodesV1.ConcurrentChange), (error.Status, error.Code));
        Assert.Equal(1, mutationCalls); Assert.Equal(2, world.Store.Replays - reads); Assert.Empty(world.Store.Refusals);
    }

    private sealed partial class FakeStore : ILogisticsDispatchNoteApprovalStoreV1
    {
        private readonly Dictionary<string, LogisticsDispatchNoteApprovalReceiptV1> dispatchReceipts = [];
        public async Task<LogisticsReplay?> FindDispatchNoteApprovalReplayAsync(LogisticsWriteEnvelope envelope, string hash,
            Guid instructionId, Guid dispatchNoteId, long expectedVersion, Func<CancellationToken, Task> fresh, CancellationToken token = default)
        {
            await fresh(token);
            var replay = await FindReplayAsync(envelope, hash, LogisticsCommands.ApproveDispatchNoteV1, token);
            await fresh(token);
            return replay is null ? null : replay with { DispatchNoteApprovalReceipt = dispatchReceipts.GetValueOrDefault(envelope.IdempotencyKey) };
        }
        public async Task<LogisticsStoreResult?> ApproveDispatchNoteOwnedAsync(LogisticsWriteEnvelope envelope, string hash,
            Guid instructionId, Guid dispatchNoteId, long expectedVersion, LogisticsMutationAudit audit,
            Func<CancellationToken, Task> fresh, CancellationToken token = default)
        {
            await fresh(token);
            var saved = await MutateInstructionAsync(envelope, hash, LogisticsCommands.ApproveDispatchNoteV1, instructionId,
                (item, now) => item.ApproveDispatchNote(expectedVersion, dispatchNoteId, envelope.ActorId, now), audit, token);
            if (saved is null) return null;
            if (saved.Outcome == LogisticsWriteOutcome.Recorded)
            {
                var note = saved.Instruction.Instruction.DispatchNotes.Single(n => n.DispatchNoteId == dispatchNoteId);
                dispatchReceipts.Add(envelope.IdempotencyKey, new(saved.Instruction.Instruction.CompanyId, LogisticsCommands.ApproveDispatchNoteV1,
                    instructionId, dispatchNoteId, expectedVersion, expectedVersion + 1, envelope.ActorId, note.ApprovedAtUtc!.Value,
                    saved.OriginalCorrelationId, hash));
            }
            await fresh(token);
            return saved with { DispatchNoteApprovalReceipt = dispatchReceipts.GetValueOrDefault(envelope.IdempotencyKey) };
        }
        public async Task RecordDispatchNoteApprovalRefusalAsync(LogisticsWriteEnvelope envelope, string hash,
            LogisticsTerminalRefusal refusal, Func<CancellationToken, Task> fresh, CancellationToken token = default)
        {
            await fresh(token);
            await RecordRefusalAsync(envelope, hash, LogisticsCommands.ApproveDispatchNoteV1, refusal, token);
            await fresh(token);
        }
    }
}
