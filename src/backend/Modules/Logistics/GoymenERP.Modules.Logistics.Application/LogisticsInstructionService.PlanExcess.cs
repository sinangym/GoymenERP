using System.Globalization;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Modules.Logistics.Application;

public sealed partial class LogisticsInstructionService : ILogisticsPlanExcessServiceV1
{
    public async Task<LogisticsPlanExcessPageV1> ListAsync(string purpose, Guid? instructionId, string? cursor, CancellationToken token = default)
    {
        var permission = purpose switch { "approve" => LogisticsPlanExcessV1.ApprovePermission,
            "load" => bindings.RecordPermission, "ship" => bindings.ShipmentPermission, _ => null };
        if (permission is null) throw new LogisticsInstructionServiceException(400, "validation_failed");
        var scope = await RequireAsync(permission, token);
        if (instructionId == Guid.Empty || cursor?.Length > 8192) throw new LogisticsInstructionServiceException(400, "validation_failed");
        var owner = store as ILogisticsPlanExcessStoreV1 ?? throw PlanExcessUnavailable();
        return await GuardedAsync(scope, permission, () => owner.ListPlanExcessAsync(instructionId, cursor, token), token);
    }
    public async Task<LogisticsPlanExcessResultV1> ProposeAsync(Guid instructionId, Guid lineId,
        ProposeLogisticsPlanExcessV1 request, LogisticsCommandMetadata metadata, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var permission = bindings.RecordPermission;
        var scope = await RequireAsync(permission, token);
        if (instructionId == Guid.Empty || lineId == Guid.Empty || request.ExceptionId == Guid.Empty
            || !PositiveVersion(request.ExpectedInstructionVersion) || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > 500 || request.Reason.Any(char.IsControl)) throw new LogisticsInstructionServiceException(400, "validation_failed");
        var owner = store as ILogisticsPlanExcessStoreV1 ?? throw PlanExcessUnavailable();
        var envelope = Envelope(scope, metadata, permission!);
        var hash = LogisticsRequestHash.Compute(LogisticsPlanExcessV1.ProposeCommand, instructionId, new { lineId, request });
        if (await PlanExcessReplayAsync(scope, permission, owner, envelope, hash, LogisticsPlanExcessV1.ProposeCommand, token) is { } replay) return replay;
        var instruction = await GuardedAsync(scope, permission, () => store.FindInstructionAsync(instructionId, token), token)
            ?? throw new LogisticsInstructionServiceException(404, "not_found");
        var line = instruction.Instruction.Lines.SingleOrDefault(line => line.Id == lineId)
            ?? throw new LogisticsInstructionServiceException(404, "not_found");
        _ = await VerifyUnitAsync(scope, permission, line.UnitId, request.Quantity, instruction.EffectiveOn, "planExcess", token);
        return await WritePlanExcessAsync(scope, permission, owner, envelope, hash, LogisticsPlanExcessV1.ProposeCommand,
            ct => owner.ProposePlanExcessAsync(envelope, hash, instructionId, lineId, request, ct), token);
    }
    public async Task<LogisticsPlanExcessResultV1> ApproveAsync(Guid exceptionId, ApproveLogisticsPlanExcessV1 request,
        LogisticsCommandMetadata metadata, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var permission = LogisticsPlanExcessV1.ApprovePermission;
        var scope = await RequireAsync(permission, token);
        if (exceptionId == Guid.Empty || !PositiveVersion(request.ExpectedVersion) || !PositiveVersion(request.ExpectedInstructionVersion))
            throw new LogisticsInstructionServiceException(400, "validation_failed");
        var owner = store as ILogisticsPlanExcessStoreV1 ?? throw PlanExcessUnavailable();
        var envelope = Envelope(scope, metadata, permission);
        var hash = LogisticsRequestHash.Compute(LogisticsPlanExcessV1.ApproveCommand, exceptionId, request);
        return await WritePlanExcessAsync(scope, permission, owner, envelope, hash, LogisticsPlanExcessV1.ApproveCommand,
            async ct => await PlanExcessReplayAsync(scope, permission, owner, envelope, hash, LogisticsPlanExcessV1.ApproveCommand, ct, withinTransaction: true)
                ?? await owner.ApprovePlanExcessAsync(envelope, hash, exceptionId, request, ct), token);
    }
    private async Task<LogisticsPlanExcessResultV1?> PlanExcessReplayAsync(Scope scope, string? permission,
        ILogisticsPlanExcessStoreV1 owner, LogisticsWriteEnvelope envelope, string hash, string command, CancellationToken token, bool withinTransaction = false)
    {
        if (command == LogisticsPlanExcessV1.ApproveCommand && !withinTransaction)
        {
            var transaction = realization ?? throw PlanExcessUnavailable();
            return await transaction.ExecutePlanExcessAsync(scope.CompanyId,
                ct => PlanExcessReplayAsync(scope, permission, owner, envelope, hash, command, ct, withinTransaction: true),
                ct => RequireStillAsync(scope, permission, ct), token);
        }
        try { return await GuardedAsync(scope, permission, () => owner.FindPlanExcessReplayAsync(envelope, hash, command, token), token); }
        catch (LogisticsStoreException error) { throw Store(error); }
    }
    private async Task<LogisticsPlanExcessResultV1> WritePlanExcessAsync(Scope scope, string? permission,
        ILogisticsPlanExcessStoreV1 owner, LogisticsWriteEnvelope envelope, string hash, string command,
        Func<CancellationToken, Task<LogisticsPlanExcessResultV1>> work, CancellationToken token)
    {
        var transaction = realization ?? throw PlanExcessUnavailable();
        try
        {
            return await transaction.ExecutePlanExcessAsync(scope.CompanyId, ct => GuardedAsync(scope, permission, () => work(ct), ct),
                ct => RequireStillAsync(scope, permission, ct), token);
        }
        catch (LogisticsStoreException error)
        {
            await RequireStillAsync(scope, permission, token);
            var won = await PlanExcessReplayAsync(scope, permission, owner, envelope, hash, command, token);
            if (won is not null) return won;
            if (error.Code is LogisticsStoreCodes.ConcurrentChange or LogisticsStoreCodes.SameKeyPayloadMismatch)
                throw new LogisticsInstructionServiceException(409, error.Code);
            throw PlanExcessUnavailable();
        }
        catch (LogisticsInstructionServiceException error) when (error.Status == 409 && !error.IsReplay)
        {
            // The host has confirmed rollback. A fresh own transaction stores only this immutable terminal answer.
            var result = await transaction.ExecutePlanExcessAsync<LogisticsPlanExcessResultV1?>(scope.CompanyId, async ct =>
            {
                var won = await PlanExcessReplayAsync(scope, permission, owner, envelope, hash, command, ct, withinTransaction: true);
                if (won is not null) return won;
                await GuardedAsync(scope, permission, async () =>
                {
                    await store.RecordRefusalAsync(envelope, hash, command, new(error.Status, error.Code, error.Errors), ct);
                    return true;
                }, ct);
                return null;
            }, ct => RequireStillAsync(scope, permission, ct), token);
            if (result is not null) return result;
            throw;
        }
    }
    private static bool PositiveVersion(string text) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        && value > 0 && text == value.ToString(CultureInfo.InvariantCulture);
    private static LogisticsInstructionServiceException PlanExcessUnavailable() => new(503, LogisticsPlanExcessV1.Unavailable);
}
