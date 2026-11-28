using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingStore
{
    internal sealed record MixedSupplierHistory(
        IReadOnlyDictionary<long, PurchaseMixedOrderApprovalReceiptV1> Approvals,
        IReadOnlyDictionary<long, PurchaseMixedSupplierRevisionReceiptV1> Revisions);

    private async Task<MixedSupplierHistory?> ReadMixedSupplierHistoryAsync(PurchaseOrderRow row,
        bool hasMembers, Func<CancellationToken, Task>? fence, CancellationToken token)
    {
        var history = PurchaseSupplierRevisionHistory.Read(row);
        if (!hasMembers || history is null) return null;
        var approvals = new Dictionary<long, PurchaseMixedOrderApprovalReceiptV1>();
        var revisions = new Dictionary<long, PurchaseMixedSupplierRevisionReceiptV1>();
        foreach (var change in history.Revisions)
        {
            var receipt = await ReadMixedSupplierVersionAsync(row.Id, change.OrderVersionAfter, fence, token,
                change.OrderVersionAfter == long.MaxValue ? long.MaxValue : change.OrderVersionAfter + 1);
            if (receipt.ResultVersion > row.Version || !MixedJsonEqual(System.Text.Json.JsonSerializer.Serialize(receipt.NativeRevision, Json),
                System.Text.Json.JsonSerializer.Serialize(change, Json))) throw MixedSupplierUnavailable();
            revisions.Add(change.OrderVersionAfter, receipt);
            var approved = await ReadMixedApprovalVersionAsync(row.Id, change.PreviousApprovalVersion, fence, token, change.OrderVersionAfter);
            if (!approvals.TryAdd(approved.ResultVersion, approved)) throw MixedSupplierUnavailable();
        }
        if (history.CurrentApprovalVersion is { } current)
        {
            var approved = await ReadMixedApprovalVersionAsync(row.Id, current, fence, token,
                current == long.MaxValue ? long.MaxValue : current + 1);
            if (approved.ActorId != row.ApprovedBy || approved.RecordedAtUtc != row.ApprovedAtUtc) throw MixedSupplierUnavailable();
            approvals.Add(current, approved);
        }
        if (revisions.Values.OrderBy(x => x.ResultVersion).Zip(revisions.Values.OrderBy(x => x.ResultVersion).Skip(1),
            (left, right) => left.CompanyAccountId != right.PreviousCompanyAccountId).Any(x => x)) throw MixedSupplierUnavailable();
        return new(approvals, revisions);
    }

    private static void ApplyMixedSupplierHistory(PurchaseOrderRow row, PurchaseSupplierRevisionHistory native,
        SortedDictionary<long, Action<PurchaseOrder>> actions, MixedSupplierHistory? verified)
    {
        if (verified is null || verified.Revisions.Count != native.Revisions.Length
            || verified.Approvals.Count != native.Revisions.Length + (native.CurrentApprovalVersion is null ? 0 : 1))
            throw new InvalidOperationException(PurchaseOrder.ServiceMembershipUnavailable);
        foreach (var change in native.Revisions)
        {
            if (!verified.Revisions.TryGetValue(change.OrderVersionAfter, out var receipt)
                || receipt.CompanyId != row.CompanyId || receipt.OrderId != row.Id
                || !MixedJsonEqual(System.Text.Json.JsonSerializer.Serialize(receipt.NativeRevision, Json), System.Text.Json.JsonSerializer.Serialize(change, Json)))
                throw new InvalidOperationException(PurchaseOrder.ServiceMembershipUnavailable);
            actions[change.OrderVersionAfter] = order => order.RestoreRecordedLinkedSupplierRevision(receipt.NativeRevision);
        }
        foreach (var approved in verified.Approvals.Values)
        {
            if (!actions.ContainsKey(approved.ResultVersion) || approved.CompanyId != row.CompanyId || approved.OrderId != row.Id)
                throw new InvalidOperationException(PurchaseOrder.ServiceMembershipUnavailable);
            actions[approved.ResultVersion] = order => order.RestoreRecordedServiceApproval(approved.ExpectedVersion,
                approved.ResultVersion, approved.ActorId, approved.RecordedAtUtc);
        }
    }
}
