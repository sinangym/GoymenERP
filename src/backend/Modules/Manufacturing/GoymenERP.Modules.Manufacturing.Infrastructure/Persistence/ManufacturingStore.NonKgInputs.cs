using System.Text.Json;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

public sealed partial class ManufacturingStore : IManufacturingNonKgInputStoreV1
{
    public async Task<ManufacturingNonKgInputHistoryV1?> ReadNonKgInputsAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var company = RequireResolvedCompany();
        var order = await dbContext.Orders.AsNoTracking().SingleOrDefaultAsync(row => row.CompanyId == company && row.Id == orderId, cancellationToken);
        if (order is null) return null;
        var facts = await dbContext.NonKgInputs.AsNoTracking().Where(row => row.CompanyId == company && row.OrderId == orderId)
            .OrderBy(row => row.Revision).Select(row => new ManufacturingNonKgInputConsumptionV1(row.FactId, row.Revision,
                (ManufacturingNonKgInputKindV1)row.Kind, row.Item, (ManufacturingNonKgInputUnitV1)row.Unit, row.Quantity,
                row.OccurredAtUtc, row.ReplacesFactId, row.Reason)).ToArrayAsync(cancellationToken);
        var result = new ManufacturingNonKgInputHistoryV1(company, orderId, order.Version, facts.LastOrDefault()?.Revision ?? 0, facts);
        result.RequireValid(); return result;
    }

    public async Task<ManufacturingNonKgInputHistoryV1> RecordNonKgInputAsync(Guid orderId, long expectedVersion, long expectedRevision,
        ManufacturingNonKgInputConsumptionV1 fact, ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        RequireMetadata(metadata); var company = RequireResolvedCompany(); fact.RequireValid();
        var row = await dbContext.Orders.SingleOrDefaultAsync(item => item.CompanyId == company && item.Id == orderId, cancellationToken)
            ?? throw Refused(ManufacturingStoreCodes.OrderNotFound, "Bu şirketin böyle bir emri yok.");
        if (row.Version != expectedVersion) throw new ManufacturingVersionConflictException(expectedVersion, row.Version);
        if (row.Status != (int)OrderStatus.Open) throw new ManufacturingRuleException(ManufacturingRuleCodes.OrderClosed, "Gerçek sarf yalnız açık emre kaydedilir.");
        var history = (await ReadNonKgInputsAsync(orderId, cancellationToken))!;
        if (history.Revision != expectedRevision || fact.Revision != expectedRevision + 1)
            throw Refused(ManufacturingStoreCodes.ConcurrentChange, "Gerçek sarf kayıtları değişti; yeniden okuyun.");
        if (await dbContext.NonKgInputs.AnyAsync(item => item.CompanyId == company && item.FactId == fact.FactId, cancellationToken))
            throw Refused(ManufacturingStoreCodes.FactIdConflict, "Bu olgu kimliği önceden kaydedildi.");
        if (fact.ReplacesFactId is { } replaces)
        {
            var original = history.Facts.SingleOrDefault(item => item.FactId == replaces);
            if (original is null || original.Kind != fact.Kind || original.Unit != fact.Unit
                || history.Facts.Any(item => item.ReplacesFactId == replaces))
                throw Refused(ManufacturingStoreCodes.ConcurrentChange, "Düzeltilen sarf kaydı güncel değil.");
        }
        var now = timeProvider.GetUtcNow();
        dbContext.NonKgInputs.Add(new NonKgInputRow { CompanyId = company, OrderId = orderId, FactId = fact.FactId,
            Revision = fact.Revision, Kind = (int)fact.Kind, Item = fact.Item, Unit = (int)fact.Unit, Quantity = fact.Quantity,
            OccurredAtUtc = fact.OccurredAtUtc, ReplacesFactId = fact.ReplacesFactId, Reason = fact.Reason,
            RecordedAtUtc = now, RecordedBy = metadata.ActorId.Trim(), CorrelationId = metadata.CorrelationId.Trim() });
        // Own revision serializes consumption facts; the existing parent Version token fences a competing close/cancel.
        // Explicit modification also preserves that fence when the clock returns the same timestamp.
        row.UpdatedAtUtc = now; dbContext.Entry(row).Property(item => item.UpdatedAtUtc).IsModified = true;
        var result = history with { Revision = fact.Revision, Facts = [.. history.Facts, fact] };
        Audit(metadata, company, "Manufacturing.NonKgInput", fact.FactId, "manufacturing.order.non-kg-input-recorded.v1",
            "Record", expectedRevision, fact.Revision, "{}", JsonSerializer.Serialize(result, Json), fact.Reason);
        Complete(metadata, company, result); await SaveAsync(cancellationToken); return result;
    }
}
