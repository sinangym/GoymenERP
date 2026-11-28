using System.Text.Json;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchaseServiceOrderStoreV1
{
    // No standalone command, attempt, transaction or number is created by this parent-owned participant.
    internal static async Task StageMixedApprovalAsync(PurchasingDbContext database, IAuditEventCollector audit,
        IAuditTransactionWriterV1 auditWriter, PurchasingStore.MixedApprovalExecution owner,
        PurchaseServiceOrderState before, PurchaseServiceOrderState after, PurchasingWriteEnvelope envelope, CancellationToken token)
    {
        owner.RequireParticipant(database, audit, auditWriter);
        PurchaseServiceOrder.Validate(before); PurchaseServiceOrder.Validate(after);
        if (before.CompanyId != after.CompanyId || before.OrderId != after.OrderId || after.Version != checked(before.Version + 1)
            || before.Status != PurchaseServiceOrderStatus.Draft || after.Status != PurchaseServiceOrderStatus.Approved
            || after.ApprovedBy != envelope.ActorId.Trim()) throw new InvalidOperationException("Invalid native mixed child transition.");
        var snapshot = await StageStateAsync(database, before.CompanyId, before.OrderId, before, after, token, owner.Fence);
        owner.RequireParticipant(database, audit, auditWriter);
        audit.Enqueue(new AuditEventDraft(before.CompanyId, PurchaseServiceOrderCommandsV1.Approve.Replace(".v1", ".recorded.v1", StringComparison.Ordinal),
            "Purchasing.ServiceOrder", before.OrderId.ToString("D"), PurchaseServiceOrderCommandsV1.Approve, before.Version,
            after.Version, AuditActorTypes.User, envelope.ActorId.Trim(), envelope.PermissionCode, envelope.RoleCodes,
            JsonSerializer.Serialize(before, Json), snapshot, "Satınalma siparişi onaylandı", null, AuditAiInvolvement.None,
            null, envelope.SourceChannel, envelope.ClientIpAddress, envelope.UserAgent, envelope.CorrelationId.Trim(), envelope.CausationId, envelope.IdempotencyKey));
    }

    private static async Task<string> StageStateAsync(PurchasingDbContext database, Guid scope, Guid orderId,
        PurchaseServiceOrderState? before, PurchaseServiceOrderState after, CancellationToken token,
        Func<CancellationToken, Task>? fence)
    {
        string snapshot;
        var parent = await PurchasingStore.ReadMixedOwnerAsync(() => database.ServiceOrders.SingleOrDefaultAsync(x => x.OrderId == orderId, token), fence, token);
        if (parent is null) database.ServiceOrders.Add(new PurchaseServiceOrderRow() { CompanyId = scope, OrderId = orderId, Version = after.Version });
        else { if (parent.Version != before!.Version) throw new DbUpdateConcurrencyException(); parent.Version = after.Version; }
        snapshot = JsonSerializer.Serialize(after, Json);
        database.ServiceOrderRevisions.Add(new PurchaseServiceOrderRevisionRow() { CompanyId = scope, OrderId = orderId, Version = after.Version, SnapshotJson = snapshot });
        foreach (var acceptance in after.Acceptances.Where(x => before is null || before.Acceptances.All(old => old.AcceptanceId != x.AcceptanceId)))
        {
            var acceptedLine = after.Lines.Single(x => x.LineId == acceptance.LineId);
            database.ServiceAcceptances.Add(new PurchaseServiceAcceptanceRow() { CompanyId = scope, OrderId = orderId, LineId = acceptance.LineId,
                AcceptanceId = acceptance.AcceptanceId, Quantity = acceptance.Quantity, OrderVersionAfter = acceptance.OrderVersionAfter,
                SupplierPartyId = after.SupplierPartyId, CompanyAccountId = after.CompanyAccountId, CurrencyId = after.Money.CurrencyId,
                ServiceTypeId = acceptedLine.ServiceTypeId, UnitId = acceptedLine.UnitId });
        }
        return snapshot;
    }
}
