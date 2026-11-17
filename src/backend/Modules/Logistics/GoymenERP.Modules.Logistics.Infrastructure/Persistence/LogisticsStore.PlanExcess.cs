using System.Data;
using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

public sealed partial class LogisticsStore : ILogisticsPlanExcessStoreV1
{
    public async Task<LogisticsPlanExcessPageV1> ListPlanExcessAsync(Guid? instructionId, string? cursor, CancellationToken token)
    {
        var companyId = RequireResolvedCompany();
        Guid? after = null;
        if (cursor is not null)
        {
            var bits = cursor.Split('/');
            if (bits.Length != 3 || bits[0] != companyId.ToString("N") || bits[1] != (instructionId?.ToString("N") ?? "all")
                || !Guid.TryParseExact(bits[2], "N", out var id)) throw new LogisticsInstructionServiceException(400, "invalid_cursor");
            after = id;
        }
        var query = dbContext.PlanExcessExceptions.AsNoTracking().Where(row => row.CompanyId == companyId);
        if (instructionId is { } instruction) query = query.Where(row => row.InstructionId == instruction);
        if (after is { } last) query = query.Where(row => row.ExceptionId.CompareTo(last) > 0);
        var rows = await query.OrderBy(row => row.ExceptionId).Take(51).ToArrayAsync(token);
        var items = new List<LogisticsPlanExcessViewV1>();
        foreach (var row in rows.Take(50)) items.Add(await PlanExcessViewAsync(row, token));
        return new(items, rows.Length > 50 ? $"{companyId:N}/{instructionId?.ToString("N") ?? "all"}/{rows[49].ExceptionId:N}" : null);
    }
    public async Task<LogisticsPlanExcessResultV1?> FindPlanExcessReplayAsync(LogisticsWriteEnvelope envelope, string hash,
        string command, CancellationToken token)
    {
        RequireEnvelope(envelope); RequireHash(hash); RequireResolvedCompany();
        if (command is not (LogisticsPlanExcessV1.ProposeCommand or LogisticsPlanExcessV1.ApproveCommand)) throw new ArgumentException(nameof(command));
        var row = await FindAttemptAsync(command, IdempotencyKeyHash(envelope.IdempotencyKey), token);
        if (row is null) return null;
        RequireSameContent(row, envelope.ActorId.Trim(), hash);
        if (row.Outcome == AttemptOutcomeRefused)
        {
            var refusal = JsonSerializer.Deserialize<LogisticsTerminalRefusal>(row.RefusalJson ?? "null", Json)
                ?? throw new InvalidOperationException("Invalid original plan exception refusal.");
            throw new LogisticsInstructionServiceException(refusal.Status, refusal.Code, refusal.Errors, true, row.CorrelationId);
        }
        var result = JsonSerializer.Deserialize<LogisticsPlanExcessResultV1>(row.ResultJson ?? "null", Json);
        if (result is null || result.Exception.CompanyId != companyContext.CompanyId || result.Exception.ExceptionId == Guid.Empty
            || result.OriginalCorrelationId != row.CorrelationId) throw new InvalidOperationException("Invalid original plan exception result.");
        return result with { IsReplay = true };
    }
    public async Task<LogisticsPlanExcessResultV1> ProposePlanExcessAsync(LogisticsWriteEnvelope envelope, string hash,
        Guid instructionId, Guid lineId, ProposeLogisticsPlanExcessV1 request, CancellationToken token)
    {
        RequirePlanExcessTransaction(); RequireEnvelope(envelope); var companyId = RequireResolvedCompany();
        if (envelope.PermissionCode != LogisticsPermissionManifestV1.LoadsRecord) throw new LogisticsInstructionServiceException(403, "forbidden");
        if (await FindPlanExcessReplayAsync(envelope, hash, LogisticsPlanExcessV1.ProposeCommand, token) is { } replay) return replay;
        var instruction = await dbContext.Instructions.SingleOrDefaultAsync(row => row.CompanyId == companyId && row.Id == instructionId, token)
            ?? throw new LogisticsInstructionServiceException(404, "not_found");
        var line = await dbContext.Lines.SingleOrDefaultAsync(row => row.CompanyId == companyId && row.InstructionId == instructionId && row.LineId == lineId, token)
            ?? throw new LogisticsInstructionServiceException(404, "not_found");
        if (instruction.Status != (int)LoadingInstructionStatus.Issued || instruction.Version.ToString(CultureInfo.InvariantCulture) != request.ExpectedInstructionVersion
            || line.IsBlocked) throw new LogisticsInstructionServiceException(409, LogisticsPlanExcessV1.Conflict);
        var amount = decimal.Parse(request.Quantity, CultureInfo.InvariantCulture);
        if (amount <= 0 || amount >= ColumnBound || line.UnitIsKilogram && decimal.Round(amount, 3) != amount
            || !line.UnitIsKilogram && decimal.Truncate(amount) != amount) throw new LogisticsInstructionServiceException(422, "validation_failed");
        if (await dbContext.PlanExcessExceptions.AnyAsync(row => row.ExceptionId == request.ExceptionId, token))
            throw new LogisticsInstructionServiceException(409, LogisticsPlanExcessV1.Conflict);
        var row = new LogisticsPlanExcessRow { CompanyId = companyId, ExceptionId = request.ExceptionId,
            InstructionId = instructionId, LineId = lineId, Quantity = amount, Status = 1, Version = 1,
            Reason = request.Reason.Trim(), ProposedBy = envelope.ActorId, ProposedAtUtc = timeProvider.GetUtcNow() };
        dbContext.PlanExcessExceptions.Add(row);
        var view = await PlanExcessViewAsync(row, token);
        return await SavePlanExcessAsync(envelope, hash, LogisticsPlanExcessV1.ProposeCommand, instruction, view, token);
    }
    public async Task<LogisticsPlanExcessResultV1> ApprovePlanExcessAsync(LogisticsWriteEnvelope envelope, string hash,
        Guid exceptionId, ApproveLogisticsPlanExcessV1 request, CancellationToken token)
    {
        RequirePlanExcessTransaction(); RequireEnvelope(envelope); var companyId = RequireResolvedCompany();
        if (envelope.PermissionCode != LogisticsPlanExcessV1.ApprovePermission) throw new LogisticsInstructionServiceException(403, "forbidden");
        if (await FindPlanExcessReplayAsync(envelope, hash, LogisticsPlanExcessV1.ApproveCommand, token) is { } replay) return replay;
        var row = await dbContext.PlanExcessExceptions.SingleOrDefaultAsync(row => row.CompanyId == companyId && row.ExceptionId == exceptionId, token)
            ?? throw new LogisticsInstructionServiceException(404, "not_found");
        var instruction = await dbContext.Instructions.SingleAsync(item => item.CompanyId == companyId && item.Id == row.InstructionId, token);
        var line = await dbContext.Lines.SingleAsync(item => item.CompanyId == companyId && item.InstructionId == row.InstructionId && item.LineId == row.LineId, token);
        if (request.ExpectedVersion != "1" || row.Status != 1 || row.Version != 1 || instruction.Status != (int)LoadingInstructionStatus.Issued
            || instruction.Version.ToString(CultureInfo.InvariantCulture) != request.ExpectedInstructionVersion || line.IsBlocked)
            throw new LogisticsInstructionServiceException(409, LogisticsPlanExcessV1.Conflict);
        line.ApprovedPlanExcess = RequireFits(checked(line.ApprovedPlanExcess + row.Quantity));
        row.Status = 2; row.Version = 2; row.ApprovedBy = envelope.ActorId; row.ApprovedAtUtc = timeProvider.GetUtcNow();
        var view = await PlanExcessViewAsync(row, token);
        return await SavePlanExcessAsync(envelope, hash, LogisticsPlanExcessV1.ApproveCommand, instruction, view, token);
    }
    private async Task<LogisticsPlanExcessResultV1> SavePlanExcessAsync(LogisticsWriteEnvelope envelope, string hash, string command,
        LoadingInstructionRow instruction, LogisticsPlanExcessViewV1 view, CancellationToken token)
    {
        var result = new LogisticsPlanExcessResultV1(view, false, envelope.CorrelationId);
        var attempt = Attempt(view.CompanyId, command, IdempotencyKeyHash(envelope.IdempotencyKey), hash, envelope.ActorId,
            envelope, instruction.Id, timeProvider.GetUtcNow());
        attempt.ResultJson = JsonSerializer.Serialize(result, Json); dbContext.Attempts.Add(attempt);
        Audit(envelope, view.CompanyId, instruction.Id, command, command == LogisticsPlanExcessV1.ProposeCommand ? "ProposePlanExcess" : "ApprovePlanExcess",
            instruction.Version, instruction.Version, "{}", JsonSerializer.Serialize(view, Json), view.Reason);
        await SaveAsync(token); return result;
    }
    private async Task<LogisticsPlanExcessViewV1> PlanExcessViewAsync(LogisticsPlanExcessRow row, CancellationToken token)
    {
        var line = await dbContext.Lines.AsNoTracking().SingleAsync(line => line.CompanyId == row.CompanyId
            && line.InstructionId == row.InstructionId && line.LineId == row.LineId, token);
        var instruction = await dbContext.Instructions.AsNoTracking().SingleAsync(item => item.CompanyId == row.CompanyId && item.Id == row.InstructionId, token);
        var loaded = await dbContext.PlanExcessUsages.Where(use => use.ExceptionId == row.ExceptionId && use.Kind == 1).SumAsync(use => (decimal?)use.Quantity, token) ?? 0m;
        var shipped = await dbContext.PlanExcessUsages.Where(use => use.ExceptionId == row.ExceptionId && use.Kind == 2).SumAsync(use => (decimal?)use.Quantity, token) ?? 0m;
        if (loaded < 0m || loaded > row.Quantity || shipped < 0m || shipped > loaded)
            throw new LogisticsInstructionServiceException(503, LogisticsPlanExcessV1.Unavailable);
        return new(row.CompanyId, row.ExceptionId, row.InstructionId, row.LineId, line.SourceOrderId, line.SourceOrderLineId, line.UnitId,
            row.Version.ToString(CultureInfo.InvariantCulture), instruction.Version.ToString(CultureInfo.InvariantCulture), row.Status == 1 ? "Pending" : "Approved",
            line.Instructed.ToString(CultureInfo.InvariantCulture), row.Quantity.ToString(CultureInfo.InvariantCulture), loaded.ToString(CultureInfo.InvariantCulture),
            shipped.ToString(CultureInfo.InvariantCulture), row.Reason, row.ProposedBy, row.ProposedAtUtc, row.ApprovedBy, row.ApprovedAtUtc,
            (row.Status == 2 ? row.Quantity - loaded : 0m).ToString(CultureInfo.InvariantCulture),
            (row.Status == 2 ? loaded - shipped : 0m).ToString(CultureInfo.InvariantCulture));
    }
    private void RequirePlanExcessTransaction()
    {
        if (dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new LogisticsInstructionServiceException(503, LogisticsPlanExcessV1.Unavailable);
    }
}
