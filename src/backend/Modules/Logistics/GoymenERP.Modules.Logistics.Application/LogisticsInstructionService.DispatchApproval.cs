using System.Globalization;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain;

namespace GoymenERP.Modules.Logistics.Application;

public sealed partial class LogisticsInstructionService
{
    private async Task<LoadingInstructionResponse> ApproveDispatchNoteGuardedAsync(Guid instructionId, Guid dispatchNoteId,
        ApproveDispatchNoteRequest request, LogisticsCommandMetadata metadata, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var permission = bindings.DispatchNoteApprovePermission;
        var scope = await RequireAsync(permission, token);
        if (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expected) || expected < 1)
            throw new LogisticsInstructionServiceException(400, "validation_failed", [new("expectedVersion", "validation_failed")]);
        if (store is not ILogisticsDispatchNoteApprovalStoreV1 guarded)
            throw new LogisticsInstructionServiceException(503, "service_unavailable");
        var hash = LogisticsRequestHash.Compute(LogisticsCommands.ApproveDispatchNoteV1, instructionId, new { dispatchNoteId, request });
        var envelope = Envelope(scope, metadata, permission!);
        Task Fence(CancellationToken ct) => RequireStillAsync(scope, permission, ct);
        async Task<LogisticsReplay?> Replay()
        {
            try { return await guarded.FindDispatchNoteApprovalReplayAsync(envelope, hash, instructionId, dispatchNoteId, expected, Fence, token); }
            catch (LogisticsStoreException e) { throw Store(e); }
        }
        async Task<LoadingInstructionResponse> Refusal(LogisticsTerminalRefusal refusal)
        {
            try { await guarded.RecordDispatchNoteApprovalRefusalAsync(envelope, hash, refusal, Fence, token); }
            catch (LogisticsStoreException e) when (e.Code == LogisticsStoreCodes.ConcurrentChange)
            {
                if (await Replay() is { } winner) return Replayed(winner);
                throw Store(e);
            }
            catch (LogisticsStoreException e) { throw Store(e); }
            throw new LogisticsInstructionServiceException(refusal.Status, refusal.Code, refusal.Errors);
        }
        try
        {
            if (await Replay() is { } replay) return Replayed(replay);
            if (instructionId == Guid.Empty) throw new LogisticsInstructionServiceException(404, "not_found");
            var result = await guarded.ApproveDispatchNoteOwnedAsync(envelope, hash, instructionId, dispatchNoteId, expected,
                new("logistics.loading-instruction.dispatch-note-approved.v1", "ApproveDispatchNote", "İrsaliye onaylandı ve numaralandı"), Fence, token);
            return result is null ? throw new LogisticsInstructionServiceException(404, "not_found")
                : Response(result.Instruction, result.Outcome == LogisticsWriteOutcome.Replayed, result.OriginalCorrelationId,
                    result.DispatchNoteApprovalReceipt);
        }
        catch (LogisticsVersionConflictException)
        {
            return await Refusal(new(409, LogisticsProblemCodesV1.VersionConflict, [new("expectedVersion", "version_conflict")]));
        }
        catch (LogisticsRuleException e) when (e.Code == LogisticsRuleCodes.InstructionNotIssued)
        {
            return await Refusal(new(409, LogisticsProblemCodesV1.InstructionState, [new("status", e.Code)]));
        }
        catch (LogisticsRuleException e) when (e.Code == LogisticsRuleCodes.DispatchNoteState)
        {
            return await Refusal(new(409, LogisticsProblemCodesV1.DispatchNoteState, [new("status", e.Code)]));
        }
        catch (LogisticsRuleException e) when (e.Code == LogisticsRuleCodes.DispatchNoteSelfApproval)
        {
            return await Refusal(new(409, LogisticsProblemCodesV1.DispatchNoteSelfApproval, [new("dispatchNoteId", e.Code)]));
        }
        catch (LogisticsRuleException e) when (e.Code == LogisticsRuleCodes.DispatchNoteNotFound)
        {
            throw new LogisticsInstructionServiceException(404, "not_found");
        }
        catch (LogisticsRuleException e)
        {
            throw new LogisticsInstructionServiceException(422, "validation_failed", [new("instruction", e.Code)]);
        }
        catch (ArgumentException) { throw new LogisticsInstructionServiceException(400, "validation_failed"); }
        catch (LogisticsStoreException e) when (e.Code == LogisticsStoreCodes.ConcurrentChange)
        {
            if (await Replay() is { } winner) return Replayed(winner);
            throw Store(e); // One bounded winner read; never retry the mutation or persist a transient 409.
        }
        catch (LogisticsStoreException e) { throw Store(e); }
    }
}
