using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

public sealed partial class LogisticsStore
{
    /// <summary>Actual same-transaction approval and original instruction identity; no Inventory tables are read.</summary>
    public async Task<(LogisticsPlanExcessViewV1 Exception, decimal Reserved)> ReadPlanExcessReservationCapacityAsync(
        Guid instructionId, Guid lineId, Guid exceptionId, CancellationToken token)
    {
        RequirePlanExcessTransaction(); var company = RequireResolvedCompany();
        var row = await dbContext.PlanExcessExceptions.AsNoTracking().SingleOrDefaultAsync(row => row.CompanyId == company
            && row.ExceptionId == exceptionId && row.InstructionId == instructionId && row.LineId == lineId, token)
            ?? throw new LogisticsInstructionServiceException(404, "not_found");
        if (row.Status != 2 || row.Version != 2 || row.ApprovedBy is null || row.ApprovedAtUtc is null)
            throw new LogisticsInstructionServiceException(409, LogisticsPlanExcessV1.ApprovalRequired);
        var reserved = await dbContext.PlanExcessReservations.Where(item => item.ExceptionId == exceptionId)
            .SumAsync(item => (decimal?)item.Quantity, token) ?? 0m;
        if (reserved < 0 || reserved > row.Quantity) throw new LogisticsInstructionServiceException(503, LogisticsPlanExcessV1.Unavailable);
        return (await PlanExcessViewAsync(row, token), reserved);
    }
    public async Task<IReadOnlyList<LogisticsPlanExcessReservationV1>> ReadPlanExcessReservationsAsync(Guid instructionId, CancellationToken token)
    {
        var company = RequireResolvedCompany();
        var rows = await dbContext.PlanExcessReservations.AsNoTracking().Where(row => row.CompanyId == company && row.InstructionId == instructionId)
            .OrderBy(row => row.AllocationId).ToArrayAsync(token);
        return rows.Select(row => new LogisticsPlanExcessReservationV1(row.CompanyId, row.ExceptionId, row.InstructionId,
            row.LineId, row.AllocationId, row.LabelId, row.Quantity.ToString("0.###", CultureInfo.InvariantCulture),
            row.OriginalAllocationVersion, row.OriginalLabelVersion)).ToArray();
    }
    /// <summary>After the real Inventory reservation succeeds in this borrowed transaction, pin its actual owner reply.</summary>
    public async Task RecordPlanExcessReservationAsync(LogisticsWriteEnvelope envelope, string hash,
        LogisticsPlanExcessReservationV1 proof, CancellationToken token)
    {
        RequirePlanExcessTransaction(); RequireEnvelope(envelope); RequireHash(hash); var company = RequireResolvedCompany();
        if (envelope.PermissionCode != LogisticsPermissionManifestV1.LoadsRecord || proof.CompanyId != company || proof.AllocationId == Guid.Empty
            || proof.LabelId == Guid.Empty || proof.OriginalAllocationVersion != "1" || !long.TryParse(proof.OriginalLabelVersion,
                NumberStyles.None, CultureInfo.InvariantCulture, out var labelVersion) || labelVersion < 1)
            throw new LogisticsInstructionServiceException(503, LogisticsPlanExcessV1.Unavailable);
        var (approved, reserved) = await ReadPlanExcessReservationCapacityAsync(proof.InstructionId, proof.LineId, proof.ExceptionId, token);
        var amount = decimal.Parse(proof.QuantityKg, CultureInfo.InvariantCulture);
        if (amount <= 0 || reserved + amount > decimal.Parse(approved.Quantity, CultureInfo.InvariantCulture))
            throw new LogisticsInstructionServiceException(409, LogisticsPlanExcessV1.Conflict);
        var header = await dbContext.Instructions.SingleAsync(row => row.Id == proof.InstructionId, token);
        var line = await dbContext.Lines.SingleAsync(row => row.InstructionId == proof.InstructionId && row.LineId == proof.LineId, token);
        if (header.ShipmentKind != (int)LoadingInstructionShipmentKind.ContractProcessingDelivery || header.Status != (int)LoadingInstructionStatus.Issued
            || line.IsBlocked || !line.UnitIsKilogram || line.AllocationId == proof.AllocationId)
            throw new LogisticsInstructionServiceException(409, LogisticsPlanExcessV1.Conflict);
        var now = timeProvider.GetUtcNow();
        dbContext.PlanExcessReservations.Add(new() { CompanyId = company, ExceptionId = proof.ExceptionId,
            InstructionId = proof.InstructionId, LineId = proof.LineId, AllocationId = proof.AllocationId, LabelId = proof.LabelId,
            Quantity = amount, OriginalAllocationVersion = proof.OriginalAllocationVersion, OriginalLabelVersion = proof.OriginalLabelVersion,
            ActorId = envelope.ActorId, CorrelationId = envelope.CorrelationId, RecordedAtUtc = now });
        dbContext.Attempts.Add(Attempt(company, LogisticsPlanExcessV1.ReserveCommand, IdempotencyKeyHash(envelope.IdempotencyKey), hash,
            envelope.ActorId, envelope, proof.InstructionId, now));
        Audit(envelope, company, header.Id, LogisticsPlanExcessV1.ReserveCommand, "ReservePlanExcess", header.Version, header.Version,
            "{}", JsonSerializer.Serialize(new { approval = approved, actualReservation = proof }, Json),
            "Onaylı plan üstü pay için ayrı gerçek stok tahsisi");
        await SaveAsync(token);
    }
}
