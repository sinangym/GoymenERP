using System.Data;
using System.Globalization;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

public sealed partial class LogisticsStore : ILogisticsPlanExcessMutationStoreV1
{
    private async Task ValidatePlanExcessSnapshotAsync(IReadOnlyList<LoadingInstructionLineRow> lines,
        IReadOnlyList<LoadingShareRow> shares, CancellationToken token)
    {
        var ids = lines.Select(line => line.InstructionId).Distinct().ToArray();
        var approvals = await dbContext.PlanExcessExceptions.AsNoTracking().Where(row => ids.Contains(row.InstructionId) && row.Status == 2).ToArrayAsync(token);
        var uses = await dbContext.PlanExcessUsages.AsNoTracking().Where(row => ids.Contains(row.InstructionId)).ToArrayAsync(token);
        foreach (var line in lines)
        {
            var own = approvals.Where(row => row.InstructionId == line.InstructionId && row.LineId == line.LineId).ToArray();
            if (own.Sum(row => row.Quantity) != line.ApprovedPlanExcess) throw new InvalidOperationException("Approved exception capacity lacks its actual ledger.");
            var usage = uses.Where(use => use.InstructionId == line.InstructionId && use.LineId == line.LineId).ToArray();
            if (usage.Where(use => use.Kind == 1).Sum(use => use.Quantity) != Math.Max(0m, line.Loaded - line.Instructed)
                || usage.Where(use => use.Kind == 2).Sum(use => use.Quantity) != Math.Max(0m, line.Shipped - line.Instructed))
                throw new InvalidOperationException("Actual plan excess usage does not reconcile its physical shares.");
            foreach (var use in usage)
                if (!own.Any(row => row.ExceptionId == use.ExceptionId) || !shares.Any(share => share.InstructionId == use.InstructionId
                    && share.LineId == use.LineId && share.ShareId == use.ShareId && share.Kind == use.Kind && share.Quantity >= use.Quantity))
                    throw new InvalidOperationException("Plan excess usage lacks its actual approved exception and physical share.");
            foreach (var row in own)
            {
                var loaded = usage.Where(use => use.ExceptionId == row.ExceptionId && use.Kind == 1).Sum(use => use.Quantity);
                var shipped = usage.Where(use => use.ExceptionId == row.ExceptionId && use.Kind == 2).Sum(use => use.Quantity);
                if (loaded > row.Quantity || shipped > loaded) throw new InvalidOperationException("Plan excess usage exceeds its independent approved cap.");
            }
        }
    }
    public Task<LogisticsStoreResult?> MutateWithPlanExcessAsync(LogisticsWriteEnvelope envelope, string hash, string command,
        Guid instructionId, Action<LoadingInstruction, DateTimeOffset> mutate, LogisticsMutationAudit audit,
        LogisticsOrderLineAggregateLoadLimit? loadLimit, LogisticsShipmentRiskGateFactsV1? risk,
        IReadOnlyDictionary<Guid, IReadOnlyList<LogisticsPlanExcessUseV1>?> selections, CancellationToken token) =>
        MutateInstructionCoreAsync(envelope, hash, command, instructionId, mutate, audit, loadLimit, risk, token, planExcessUses: selections);

    private async Task AppendPlanExcessUsesAsync(Guid companyId, LoadingInstruction instruction, IReadOnlyList<LoadingInstructionLineRow> prior,
        IReadOnlyList<LoadingShare> shares, IReadOnlyDictionary<Guid, IReadOnlyList<LogisticsPlanExcessUseV1>?>? selections,
        string actor, string correlation, DateTimeOffset now, CancellationToken token)
    {
        if (selections is not null && selections.Any(pair => pair.Value is { Count: > 0 }
            && !shares.Any(share => share.LineId == pair.Key && share.Kind is LoadingShareKind.Loaded or LoadingShareKind.Shipped)))
            throw new LogisticsInstructionServiceException(422, "validation_failed");
        foreach (var share in shares.Where(share => share.Kind is LoadingShareKind.Loaded or LoadingShareKind.Shipped))
        {
            var old = prior.Single(line => line.LineId == share.LineId);
            var before = share.Kind == LoadingShareKind.Loaded ? old.Loaded : old.Shipped;
            var required = Math.Max(0m, before + share.Quantity.Value - old.Instructed) - Math.Max(0m, before - old.Instructed);
            var uses = selections?.GetValueOrDefault(share.LineId);
            if (required == 0m && uses is null or { Count: 0 }) continue;
            if (dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                throw new LogisticsInstructionServiceException(503, LogisticsPlanExcessV1.Unavailable);
            if (uses is null || uses.Count is < 1 or > 500 || uses.Any(use => use is null)
                || uses.Select(use => use.ExceptionId).Distinct().Count() != uses.Count)
                throw new LogisticsInstructionServiceException(409, LogisticsPlanExcessV1.ApprovalRequired);
            decimal total = 0m;
            foreach (var use in uses)
            {
                if (use.ExceptionId == Guid.Empty || !long.TryParse(use.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
                    || version != 2 || use.ExpectedVersion != "2" || !decimal.TryParse(use.Quantity, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
                    || amount <= 0m || amount >= ColumnBound || old.UnitIsKilogram && decimal.Round(amount, 3) != amount
                    || !old.UnitIsKilogram && decimal.Truncate(amount) != amount)
                    throw new LogisticsInstructionServiceException(422, "validation_failed");
                var exception = await dbContext.PlanExcessExceptions.AsNoTracking().SingleOrDefaultAsync(row => row.CompanyId == companyId
                    && row.ExceptionId == use.ExceptionId && row.InstructionId == instruction.Id && row.LineId == share.LineId, token);
                if (exception is not { Status: 2, Version: 2 } || exception.ApprovedBy is null || exception.ApprovedAtUtc is null)
                    throw new LogisticsInstructionServiceException(409, LogisticsPlanExcessV1.ApprovalRequired);
                var loaded = await dbContext.PlanExcessUsages.Where(row => row.ExceptionId == use.ExceptionId && row.Kind == 1)
                    .SumAsync(row => (decimal?)row.Quantity, token) ?? 0m;
                var shipped = await dbContext.PlanExcessUsages.Where(row => row.ExceptionId == use.ExceptionId && row.Kind == 2)
                    .SumAsync(row => (decimal?)row.Quantity, token) ?? 0m;
                var available = share.Kind == LoadingShareKind.Loaded ? exception.Quantity - loaded : loaded - shipped;
                if (amount > available) throw new LogisticsInstructionServiceException(409, LogisticsPlanExcessV1.Conflict);
                total = checked(total + amount);
                dbContext.PlanExcessUsages.Add(new() { CompanyId = companyId, ExceptionId = exception.ExceptionId,
                    InstructionId = instruction.Id, LineId = share.LineId, ShareId = share.ShareId,
                    Kind = share.Kind == LoadingShareKind.Loaded ? 1 : 2, Quantity = amount,
                    ActorId = actor, CorrelationId = correlation, RecordedAtUtc = now });
            }
            if (total != required) throw new LogisticsInstructionServiceException(409, LogisticsPlanExcessV1.Conflict);
        }
    }
}
