using System.Text.Json;
using GoymenERP.Modules.Purchasing.Domain.Orders;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>Rebuilds each supplier revision through the same domain rules, without replacing the original creator or price facts.</summary>
internal sealed record PurchaseSupplierRevisionHistory(Guid CompanyId, Guid OrderId,
    PurchaseSupplierRevision[] Revisions, PurchaseSupplierRevisionPriceChange[] Prices, long? CurrentApprovalVersion)
{
    internal static PurchaseSupplierRevisionHistory? Read(PurchaseOrderRow row)
    {
        if (row.SupplierRevisionHistoryJson is null)
        {
            if (row.CurrentPreparedBy is not null) throw new InvalidOperationException("Cari revizyonunun geçmişi eksik.");
            return null;
        }
        PurchaseSupplierRevisionHistory history;
        try
        {
            history = JsonSerializer.Deserialize<PurchaseSupplierRevisionHistory>(row.SupplierRevisionHistoryJson)
                ?? throw new InvalidOperationException("Cari revizyon geçmişi boş.");
        }
        catch (JsonException exception) { throw new InvalidOperationException("Cari revizyon geçmişi bozuk.", exception); }
        if (history.CompanyId != row.CompanyId || history.OrderId != row.Id || history.Revisions is not { Length: > 0 }
            || history.Prices is null || string.IsNullOrWhiteSpace(row.CurrentPreparedBy)
            || history.Revisions.Where((item, index) => item.Revision != index + 1 || item.PreviousApprovalVersion <= 0
                || item.PreviousApprovalVersion >= item.OrderVersionAfter || item.PreviousLines is null).Any()
            || history.Revisions[^1].SupplierPartyId != row.SupplierPartyId
            || history.Revisions[^1].PreparedBy != row.CurrentPreparedBy
            || (history.CurrentApprovalVersion is null) != (row.ApprovedBy is null))
            throw new InvalidOperationException("Cari revizyon geçmişi siparişle uyuşmuyor.");
        return history;
    }

    internal static string Write(PurchaseOrder order) => JsonSerializer.Serialize(new PurchaseSupplierRevisionHistory(
        order.CompanyId, order.Id, order.SupplierRevisions.ToArray(), order.SupplierRevisionPrices.ToArray(), order.ApprovalVersion));

    internal PurchaseOrderLinePrice? OriginalPrice(Guid lineId)
    {
        var candidates = Revisions[0].PreviousLines.Where(line => line.LineId == lineId).ToArray();
        return candidates.Length == 1 ? candidates[0].Price : throw new InvalidOperationException("İlk cari revizyonunda satır geçmişi eksik veya mükerrer.");
    }

    internal SortedDictionary<long, Action<PurchaseOrder>> ReplayActions(PurchaseOrderRow row)
    {
        var actions = new SortedDictionary<long, Action<PurchaseOrder>>();
        foreach (var revision in Revisions)
        {
            Add(revision.PreviousApprovalVersion, order => order.Approve(order.Version,
                revision.PreviousApproval.ApprovedBy, revision.PreviousApproval.ApprovedAtUtc));
            Add(revision.OrderVersionAfter, order =>
            {
                if (order.SupplierPartyId != revision.PreviousSupplierPartyId || order.PreparedBy != revision.PreviousPreparer
                    || order.Lines.Count != revision.PreviousLines.Count
                    || order.Lines.Any(line => revision.PreviousLines.Count(old => old.LineId == line.Id && old.Price == line.Price) != 1))
                    throw new InvalidOperationException("Cari revizyonunun önceki görüntüsü yeniden kurulan siparişle uyuşmuyor.");
                order.ReviseSupplier(order.Version, revision.SupplierPartyId, PurchaseOrderInvoiceHistory.None,
                    revision.PreparedBy, revision.Reason, revision.RecordedAtUtc);
                if (order.SupplierRevision != revision.Revision) throw new InvalidOperationException("Cari revizyon numarası ardışık değil.");
            });
        }
        foreach (var price in Prices)
            Add(price.OrderVersionAfter, order =>
            {
                if (order.SupplierRevision != price.Revision || order.Lines.Single(line => line.Id == price.LineId).Price != price.PreviousPrice)
                    throw new InvalidOperationException("Cari revizyon fiyat geçmişi ardışık değil.");
                order.SetSupplierRevisionPrice(order.Version, price.LineId, price.Price, price.RecordedBy, price.RecordedAtUtc);
            });
        if (CurrentApprovalVersion is { } approvedVersion)
            Add(approvedVersion, order => order.Approve(order.Version, row.ApprovedBy!,
                row.ApprovedAtUtc ?? throw new InvalidOperationException("Güncel onay zamanı eksik.")));
        return actions;

        void Add(long version, Action<PurchaseOrder> action)
        {
            if (version < 1 || version > row.Version || !actions.TryAdd(version, action))
                throw new InvalidOperationException("Cari revizyon geçmişi mükerrer veya geçersiz sürüm taşıyor.");
        }
    }
}

/// <summary>ALS-D115: immutable invoice-owner binding, even before finance admission. Invoice matching must take the same order line locks.</summary>
internal sealed class PurchaseInvoiceOrderBindingRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid InvoiceLineId { get; set; }
    public Guid OrderId { get; set; }
    public Guid OrderLineId { get; set; }
    public long OrderVersion { get; set; }
    public Guid SupplierPartyId { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}
