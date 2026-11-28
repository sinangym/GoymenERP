using System.Text.Json;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingStore
{
    // Serialization prerequisite only: no legacy command admits these rows or hashes their membership.
    internal static IReadOnlyList<PurchaseOrderServiceMembershipRow> ServiceMembershipRows(PurchaseOrder order)
    {
        if (order.ServiceMemberships.Count != 0 && order.Status != PurchaseOrderStatus.Draft)
            throw new InvalidOperationException(PurchaseOrder.ServiceMembershipUnavailable);
        return order.ServiceMembershipBatches.SelectMany(batch => batch.Members.Select((member, i) =>
            new PurchaseOrderServiceMembershipRow
            {
                CompanyId = member.CompanyId, OrderId = member.PurchaseOrderId, ServiceOrderId = member.ServiceOrderId,
                SupplierPartyId = member.SupplierPartyId, CompanyAccountId = member.CompanyAccountId,
                CurrencyId = member.CurrencyId, PreparedVersion = member.PreparedVersion,
                OrderVersionAfter = batch.OrderVersionAfter, RecordedAtUtc = batch.RecordedAtUtc,
                MemberNo = i + 1, LineIdsJson = JsonSerializer.Serialize(member.LineIds)
            })).ToArray();
    }

    internal static PurchaseOrderServiceMembership ServiceMembershipOf(PurchaseOrderServiceMembershipRow row)
    {
        if (row.CompanyId == Guid.Empty || row.OrderId == Guid.Empty || row.ServiceOrderId == Guid.Empty
            || row.SupplierPartyId == Guid.Empty || row.CompanyAccountId == Guid.Empty || row.CurrencyId == Guid.Empty
            || row.PreparedVersion <= 0 || row.OrderVersionAfter <= 1 || row.MemberNo <= 0
            || row.RecordedAtUtc.Offset != TimeSpan.Zero)
            throw new InvalidOperationException("Stored membership identity or timeline is invalid.");
        Guid[] ids;
        try { ids = JsonSerializer.Deserialize<Guid[]>(row.LineIdsJson) ?? []; }
        catch (JsonException ex) { throw new InvalidOperationException("Stored membership line identities are invalid.", ex); }
        if (ids.Length == 0 || ids.Any(x => x == Guid.Empty) || ids.Distinct().Count() != ids.Length
            || JsonSerializer.Serialize(ids) != row.LineIdsJson)
            throw new InvalidOperationException("Stored membership lines must be a canonical unique nonempty list.");
        return new(row.CompanyId, row.OrderId, row.SupplierPartyId, row.CompanyAccountId, row.CurrencyId,
            row.ServiceOrderId, row.PreparedVersion, Array.AsReadOnly(ids));
    }

    internal static PurchaseServiceOrderState PreparedServiceMembership(PurchaseOrderServiceMembershipRow member,
        PurchaseServiceOrderRevisionRow revision)
    {
        var binding = ServiceMembershipOf(member);
        var state = JsonSerializer.Deserialize<PurchaseServiceOrderState>(revision.SnapshotJson, PurchaseServiceOrderStoreV1.Json)
            ?? throw new InvalidOperationException("Prepared service revision is missing.");
        PurchaseServiceOrder.Validate(state);
        if (revision.CompanyId != member.CompanyId || revision.OrderId != member.ServiceOrderId || revision.Version != member.PreparedVersion
            || state.CompanyId != member.CompanyId || state.OrderId != member.ServiceOrderId || state.Version != member.PreparedVersion
            || state.Status != PurchaseServiceOrderStatus.Draft || state.SupplierPartyId != member.SupplierPartyId
            || state.CompanyAccountId != member.CompanyAccountId || state.Money.CurrencyId != member.CurrencyId
            || !binding.LineIds.ToHashSet().SetEquals(state.Lines.Select(x => x.LineId))
            || string.IsNullOrWhiteSpace(state.DocumentNumber) || state.DocumentNumber.Length > 64)
            throw new InvalidOperationException("Stored service revision does not prove the selected complete native Draft.");
        return state;
    }

    private async Task<(List<PurchaseOrderServiceMembershipRow> Members, List<PurchaseServiceOrderRevisionRow> Prepared)>
        ReadServiceMembershipsAsync(Guid[] parentIds, CancellationToken cancellationToken)
    {
        var members = await dbContext.OrderServiceMemberships.AsNoTracking()
            .Where(x => parentIds.Contains(x.OrderId)).ToListAsync(cancellationToken);
        if (members.Count == 0) return (members, []);
        // Exact FK revisions only: one batch query, neither current child state nor an unbounded history scan.
        var prepared = await (from member in dbContext.OrderServiceMemberships.AsNoTracking()
            join revision in dbContext.ServiceOrderRevisions.AsNoTracking()
                on new { member.CompanyId, OrderId = member.ServiceOrderId, Version = member.PreparedVersion }
                equals new { revision.CompanyId, revision.OrderId, revision.Version }
            where parentIds.Contains(member.OrderId)
            select revision).ToListAsync(cancellationToken);
        return (members, prepared);
    }

    private static Queue<(PurchaseOrderServiceMembershipBatch Batch, IReadOnlyList<PurchaseServiceOrderState> Prepared)>
        MembershipTimeline(PurchaseOrderRow parent, IReadOnlyList<PurchaseOrderServiceMembershipRow>? members,
            IReadOnlyList<PurchaseServiceOrderRevisionRow>? prepared)
    {
        if (members is not { Count: > 0 }) return new();
        if (parent.Status != (int)PurchaseOrderStatus.Draft)
            throw new InvalidOperationException(PurchaseOrder.ServiceMembershipUnavailable);
        if (members.Any(x => x.CompanyId != parent.CompanyId || x.OrderId != parent.Id)
            || members.Select(x => x.ServiceOrderId).Distinct().Count() != members.Count)
            throw new InvalidOperationException("Stored membership belongs to another parent or repeats a child.");
        var result = new Queue<(PurchaseOrderServiceMembershipBatch, IReadOnlyList<PurchaseServiceOrderState>)>();
        foreach (var group in members.GroupBy(x => x.OrderVersionAfter).OrderBy(x => x.Key))
        {
            var rows = group.OrderBy(x => x.MemberNo).ToArray();
            if (rows.Select((x, i) => x.MemberNo != i + 1).Any(x => x)
                || rows.Any(x => x.RecordedAtUtc != rows[0].RecordedAtUtc))
                throw new InvalidOperationException("Stored membership batch is incomplete.");
            var states = rows.Select(member =>
            {
                var revisions = (prepared ?? []).Where(x => x.CompanyId == member.CompanyId
                    && x.OrderId == member.ServiceOrderId && x.Version == member.PreparedVersion).ToArray();
                if (revisions.Length != 1) throw new InvalidOperationException("Exact prepared service revision is missing or repeated.");
                return PreparedServiceMembership(member, revisions[0]);
            }).ToArray();
            result.Enqueue((new(group.Key, rows[0].RecordedAtUtc, rows.Select(ServiceMembershipOf).ToArray()), states));
        }
        return result;
    }
}
