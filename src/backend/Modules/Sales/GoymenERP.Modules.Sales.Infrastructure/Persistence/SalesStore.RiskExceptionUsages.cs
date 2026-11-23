using System.Text.Json;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.RiskExceptions;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// LOJ-D19, SAT-D285: the usage-mirror half of the risk exception store. A Logistics definite-shipment event that names a
/// single-shipment risk exception is stored once in <c>sales.ShipmentRiskExceptionUsages</c> (append-only) with one audit event,
/// in one save. The exception row itself is never touched (TR_ShipmentRiskExceptions_Guard keeps a decided exception immutable):
/// a "dropped" usage is applied when the exception is read, so it then answers lapsed to the Logistics gate and a new request is
/// allowed. The primary key (company, shipment, kind) and UX_ShipmentRiskExceptionUsages_Exception make a racing second delivery
/// the save's concurrent change; its retry reads the winner. Sales writes only its own tables (SAT-D209).
/// </summary>
public sealed partial class SalesStore : ISalesRiskExceptionUsageStore
{
    public async Task<SalesRiskExceptionUsageOutcome> RecordRiskExceptionUsageAsync(SalesRiskExceptionUsageV1 usage,
        CancellationToken cancellationToken = default)
    {
        RequireUsage(usage);
        var companyId = RequireResolvedCompany();

        // Tenant-filtered: only the ambient company's exceptions are found.
        var exception = usage.ExceptionId is { } exceptionId
            ? await dbContext.ShipmentRiskExceptions.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == exceptionId, cancellationToken)
            : await dbContext.ShipmentRiskExceptions.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.OrderId == usage.OrderId && candidate.ExceptionNo == usage.ExceptionNo,
                    cancellationToken);
        if (exception is null || exception.OrderId != usage.OrderId
            || (usage.ExceptionNo is { } exceptionNo && exception.ExceptionNo != exceptionNo))
        {
            return SalesRiskExceptionUsageOutcome.ExceptionNotFound;
        }

        if (exception.LoadingInstructionId != usage.LoadingInstructionId)
        {
            return SalesRiskExceptionUsageOutcome.InstructionMismatch;
        }

        if (exception.State != (int)SalesShipmentRiskExceptionState.Approved)
        {
            return SalesRiskExceptionUsageOutcome.NotApproved;
        }

        // SAT-D285 (1): one exception, one shipment. Every earlier usage of this exception or of this shipment must name both.
        var kind = (int)usage.Kind;
        var related = await dbContext.ShipmentRiskExceptionUsages.AsNoTracking()
            .Where(row => row.ExceptionId == exception.Id || row.ShipmentId == usage.ShipmentId)
            .ToListAsync(cancellationToken);
        if (related.Any(row => row.ExceptionId != exception.Id || row.ShipmentId != usage.ShipmentId))
        {
            return SalesRiskExceptionUsageOutcome.ConflictingShipment;
        }

        if (related.Any(row => row.Kind == kind))
        {
            return SalesRiskExceptionUsageOutcome.AlreadyRecorded;
        }

        var now = timeProvider.GetUtcNow();
        var row = new ShipmentRiskExceptionUsageRow
        {
            CompanyId = companyId,
            ShipmentId = usage.ShipmentId,
            Kind = kind,
            ExceptionId = exception.Id,
            OrderId = exception.OrderId,
            ExceptionNo = exception.ExceptionNo,
            LoadingInstructionId = exception.LoadingInstructionId,
            SourceName = usage.SourceName.Trim(),
            SourceEventId = usage.SourceEventId,
            OccurredAtUtc = usage.OccurredAtUtc,
            RecordedAtUtc = now,
            CorrelationId = usage.CorrelationId.Trim(),
            CausationId = string.IsNullOrWhiteSpace(usage.CausationId) ? null : usage.CausationId.Trim(),
        };
        dbContext.ShipmentRiskExceptionUsages.Add(row);
        EnqueueRiskExceptionUsageAudit(companyId, usage.Kind, exception, row);
        try
        {
            await SaveAsync(cancellationToken);
        }
        catch
        {
            // SAT-S10: the Work inbox contract (IWorkInboxConsumerV1) asks a failing handler to reset its own scoped state before
            // it throws. The dispatcher hands the company's next event to the same scope, so neither the unsaved usage nor its
            // audit row may stay tracked behind any failure (SaveAsync already clears behind a lost race).
            dbContext.ChangeTracker.Clear();
            throw;
        }

        return SalesRiskExceptionUsageOutcome.Recorded;
    }

    /// <summary>
    /// Restores exception rows with their mirrored drops applied (SAT-D285 (2)): a dropped exception is no longer valid on any day.
    /// One extra tenant-filtered query per read, only over the rows read.
    /// </summary>
    private async Task<IReadOnlyList<SalesShipmentRiskException>> RestoreRiskExceptionsAsync(IReadOnlyList<ShipmentRiskExceptionRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(row => row.Id).ToArray();
        const int dropped = (int)SalesRiskExceptionUsageKind.Dropped;
        var drops = await dbContext.ShipmentRiskExceptionUsages.AsNoTracking()
            .Where(usage => usage.Kind == dropped && ids.Contains(usage.ExceptionId))
            .Select(usage => new { usage.ExceptionId, usage.ShipmentId })
            .ToListAsync(cancellationToken);
        // UX_ShipmentRiskExceptionUsages_Exception: at most one drop per exception.
        var dropByException = drops.ToDictionary(drop => drop.ExceptionId, drop => drop.ShipmentId);
        var items = new List<SalesShipmentRiskException>(rows.Count);
        foreach (var row in rows)
        {
            var item = SalesRiskExceptionRows.Restore(row);
            if (dropByException.TryGetValue(row.Id, out var shipmentId))
            {
                item.ApplyShipmentDropped(shipmentId);
            }

            items.Add(item);
        }

        return items;
    }

    private void EnqueueRiskExceptionUsageAudit(Guid companyId, SalesRiskExceptionUsageKind kind, ShipmentRiskExceptionRow exception,
        ShipmentRiskExceptionUsageRow usage)
    {
        var (eventType, operation, reason) = kind == SalesRiskExceptionUsageKind.Consumed
            ? (SalesLogisticsShipmentInboxV1.ConsumedAuditEventType, "MirrorRiskExceptionConsumed",
                "Tek sevk risk istisnası bir kesin sevkte kullanıldı (Lojistik olayı)")
            : (SalesLogisticsShipmentInboxV1.DroppedAuditEventType, "MirrorRiskExceptionDropped",
                "Risk istisnasını kullanan sevk iptal edildi; istisna düştü (Lojistik olayı)");
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            eventType,
            RiskExceptionEntityType,
            exception.Id.ToString("D"),
            operation,
            EntityVersionBefore: exception.Version,
            EntityVersionAfter: exception.Version,
            AuditActorTypes.Integration,
            usage.SourceName,
            SalesLogisticsShipmentInboxV1.AuditPermissionCode,
            [],
            "{}",
            JsonSerializer.Serialize(new
            {
                exception.Id,
                exception.OrderId,
                exception.ExceptionNo,
                exception.LoadingInstructionId,
                kind = kind.ToString(),
                usage.ShipmentId,
                usage.SourceEventId,
                usage.OccurredAtUtc,
            }, Json),
            reason,
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            SourceChannels.Integration,
            ClientIpAddress: null,
            UserAgent: null,
            usage.CorrelationId,
            usage.CausationId,
            $"{usage.SourceName}:{usage.SourceEventId:D}"));
    }

    private static void RequireUsage(SalesRiskExceptionUsageV1 usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        if (!Enum.IsDefined(usage.Kind) || usage.ShipmentId == Guid.Empty || usage.OrderId == Guid.Empty
            || usage.LoadingInstructionId == Guid.Empty || usage.SourceEventId == Guid.Empty
            || (usage.ExceptionNo is null && usage.ExceptionId is null) || usage.ExceptionNo is < 1 || usage.ExceptionId == Guid.Empty)
        {
            throw new ArgumentException("Risk istisnası kullanımı tür, sevk, sipariş, talimat, olay ve istisna kimliklerini taşır.", nameof(usage));
        }

        if (string.IsNullOrWhiteSpace(usage.SourceName)
            || usage.SourceName.Trim().Length > SalesDbContext.ShipmentRiskExceptionUsageSourceNameLength
            || string.IsNullOrWhiteSpace(usage.CorrelationId) || usage.CorrelationId.Trim().Length > MaximumUsageTraceLength
            || usage.CausationId?.Trim().Length > MaximumUsageTraceLength)
        {
            throw new ArgumentException("Risk istisnası kullanımı kaynak adı ve correlation taşır; izler sınırı aşmaz.", nameof(usage));
        }
    }

    /// <summary>The correlation and causation column bound of the usage table (as every Sales trace column).</summary>
    private const int MaximumUsageTraceLength = 128;
}
