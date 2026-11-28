using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchaseServiceOrderStoreV1
{
    internal static async Task StageMixedServicePriceRevisionAsync(PurchasingDbContext database, IAuditEventCollector audit,
        IAuditTransactionWriterV1 writer, PurchasingStore.MixedServicePriceExecution owner, PurchaseServiceOrderState before,
        PurchaseServiceOrderState after, PurchasingWriteEnvelope envelope, string reason, CancellationToken token)
    {
        owner.RequireParticipant(database, audit, writer);
        PurchaseServiceOrder.Validate(before); PurchaseServiceOrder.Validate(after);
        var changes = after.Lines.Where(line => before.Lines.Single(x => x.LineId == line.LineId).UnitPrice != line.UnitPrice)
            .ToDictionary(x => x.LineId, x => x.UnitPrice ?? throw new InvalidOperationException("Native price revision has no null setter."));
        var native = PurchaseServiceOrder.RevisePrices(before, before.Version, changes, envelope.ActorId.Trim(), reason, after.UpdatedAtUtc);
        if (!PurchaseMixedOrderApprovalFactsV1.SameChild(native, after)) throw new InvalidOperationException("Not the exact native child price revision.");
        var snapshot = await StageStateAsync(database, before.CompanyId, before.OrderId, before, after, token, owner.Fence);
        owner.RequireParticipant(database, audit, writer);
        audit.Enqueue(new AuditEventDraft(before.CompanyId,
            PurchaseServiceOrderCommandsV1.RevisePrices.Replace(".v1", ".recorded.v1", StringComparison.Ordinal),
            "Purchasing.ServiceOrder", before.OrderId.ToString("D"), PurchaseServiceOrderCommandsV1.RevisePrices,
            before.Version, after.Version, AuditActorTypes.User, envelope.ActorId.Trim(), envelope.PermissionCode, envelope.RoleCodes,
            JsonSerializer.Serialize(before, Json), snapshot, reason, null, AuditAiInvolvement.None, null, envelope.SourceChannel,
            envelope.ClientIpAddress, envelope.UserAgent, envelope.CorrelationId.Trim(), envelope.CausationId, envelope.IdempotencyKey));
    }
}
