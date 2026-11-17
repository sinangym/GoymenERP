namespace GoymenERP.Modules.Purchasing.Domain.Orders;

/// <summary>ALS-D112/D115: facts read by the Purchasing owner in the mutation transaction.
/// Unknown invoice history never means that no invoice exists.</summary>
public enum PurchaseOrderInvoiceHistory { Unavailable = 0, None = 1, Exists = 2 }

public sealed record PurchaseSupplierRevisionLine(Guid LineId, PurchaseOrderLinePrice? Price);
public sealed record PurchaseSupplierRevision(int Revision, long OrderVersionAfter, Guid PreviousSupplierPartyId,
    Guid SupplierPartyId, string? PreviousPreparer, PurchaseOrderApproval PreviousApproval, long PreviousApprovalVersion,
    IReadOnlyList<PurchaseSupplierRevisionLine> PreviousLines, string PreparedBy, string Reason, DateTimeOffset RecordedAtUtc);
public sealed record PurchaseSupplierRevisionPriceChange(int Revision, long OrderVersionAfter, Guid LineId,
    PurchaseOrderLinePrice? PreviousPrice, PurchaseOrderLinePrice Price, string RecordedBy, DateTimeOffset RecordedAtUtc);

public sealed partial class PurchaseOrder
{
    public const int SupplierRevisionReasonMaximumLength = 2000;
    public const string SupplierRevisionBlocked = "purchasing_supplier_revision_blocked";
    public const string SupplierRevisionInvoiceUnavailable = "purchasing_supplier_revision_invoice_unavailable";
    public const string SupplierRevisionMoneyUnavailable = "purchasing_supplier_revision_money_unavailable";
    public const string SupplierRevisionNoChange = "purchasing_supplier_revision_no_change";
    public const string SupplierRevisionPriceRequired = "purchasing_supplier_revision_price_required";
    public const string SupplierRevisionNotDraft = "purchasing_supplier_revision_not_draft";
    private readonly List<PurchaseSupplierRevision> supplierRevisions = [];
    private readonly List<PurchaseSupplierRevisionPriceChange> supplierRevisionPrices = [];
    public int SupplierRevision => supplierRevisions.Count;
    public IReadOnlyList<PurchaseSupplierRevision> SupplierRevisions => supplierRevisions.AsReadOnly();
    public IReadOnlyList<PurchaseSupplierRevisionPriceChange> SupplierRevisionPrices => supplierRevisionPrices.AsReadOnly();

    /// <summary>The accepted order stays in history; the same order/line identities and quantities enter a new draft.
    /// Existing prices cannot leak into the new supplier's approval. Receipt history includes reversed receipts.</summary>
    public void ReviseSupplier(long expectedVersion, Guid supplierPartyId, PurchaseOrderInvoiceHistory invoiceHistory,
        string preparedBy, string reason, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(PurchaseOrderStatus.Approved, SupplierRevisionBlocked, "Yalnız onaylı sipariş için cari revizyonu açılabilir.");
        Identity.Require(supplierPartyId, nameof(supplierPartyId));
        var actor = RequireActor(preparedBy, nameof(preparedBy));
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > SupplierRevisionReasonMaximumLength)
            throw new ArgumentException("Cari değişikliği gerekçesi zorunludur ve en fazla 2000 karakter olabilir.", nameof(reason));
        if (supplierPartyId == SupplierPartyId)
            throw new PurchasingRuleException(SupplierRevisionNoChange, "Yeni cari mevcut cariden farklı olmalıdır.");
        if (invoiceHistory is not (PurchaseOrderInvoiceHistory.None or PurchaseOrderInvoiceHistory.Exists))
            throw new PurchasingRuleException(SupplierRevisionInvoiceUnavailable, "Fatura geçmişi doğrulanamadı.");
        if (acceptances.Count != 0 || invoiceHistory == PurchaseOrderInvoiceHistory.Exists || Approval is null || ApprovalVersion is null)
            throw new PurchasingRuleException(SupplierRevisionBlocked, "Kabul veya fatura bulunan ya da onay kanıtı eksik siparişin carisi değiştirilemez.");
        // Revision approval requires explicit replacement prices. Do not strand a legacy order in a draft whose
        // mandatory price entry cannot use an actual currency/profile snapshot, and never invent that snapshot.
        if (Money is null)
            throw new PurchasingRuleException(SupplierRevisionMoneyUnavailable, "Yeni fiyatlar için kayıtlı para birimi ve profil kanıtı bulunamadı.");
        var revision = new PurchaseSupplierRevision(checked(SupplierRevision + 1), checked(Version + 1), SupplierPartyId,
            supplierPartyId, PreparedBy, Approval, ApprovalVersion.Value, Array.AsReadOnly(lines.Select(line => new PurchaseSupplierRevisionLine(line.Id, line.Price)).ToArray()),
            actor, reason.Trim(), now.ToUniversalTime());
        supplierRevisions.Add(revision);
        SupplierPartyId = supplierPartyId;
        PreparedBy = actor;
        Approval = null;
        ApprovalVersion = null;
        Status = PurchaseOrderStatus.Draft;
        foreach (var line in lines) line.ReplaceSupplierRevisionPrice(null);
        Touch(now);
    }

    /// <summary>Price entry for the new supplier uses the existing currency/profile calculator. No stock or receipt is changed.</summary>
    public void SetSupplierRevisionPrice(long expectedVersion, Guid lineId, PurchaseOrderLinePrice price, string recordedBy, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(recordedBy, nameof(recordedBy));
        ArgumentNullException.ThrowIfNull(price);
        if (Status != PurchaseOrderStatus.Draft || SupplierRevision == 0)
            throw new PurchasingRuleException(SupplierRevisionNotDraft, "Fiyat yalnız cari revizyonunun taslağında yeniden girilebilir.");
        var line = lines.SingleOrDefault(item => item.Id == lineId)
            ?? throw new PurchasingRuleException(PurchasingRuleCodes.LineNotFound, "Sipariş satırı bulunamadı.");
        if (line.Kind != PurchaseOrderLineKind.General)
            throw new PurchasingRuleException(SupplierRevisionNotDraft, "Fiyat genel sipariş satırında girilir.");
        RequirePrice(price, line.UnitId, line.Quantity);
        if (line.Price == price)
            throw new PurchasingRuleException(SupplierRevisionNoChange, "Satır fiyatı değişmedi.");
        var change = new PurchaseSupplierRevisionPriceChange(SupplierRevision, checked(Version + 1), line.Id, line.Price, price, actor, now.ToUniversalTime());
        supplierRevisionPrices.Add(change);
        line.ReplaceSupplierRevisionPrice(price);
        Touch(now);
    }

    private void RequireSupplierRevisionPrices()
    {
        if (SupplierRevision > 0 && lines.Any(line => line.Kind == PurchaseOrderLineKind.General && line.Price is null))
            throw new PurchasingRuleException(SupplierRevisionPriceRequired, "Cari revizyonundaki genel satırların fiyatları yeniden girilmelidir.");
    }
}
