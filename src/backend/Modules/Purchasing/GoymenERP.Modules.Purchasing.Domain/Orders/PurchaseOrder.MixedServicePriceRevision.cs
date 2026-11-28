using System.Text.Json.Serialization;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Modules.Purchasing.Domain.Orders;

public sealed record PurchaseMixedServicePriceRevision(long ExpectedVersion, long OrderVersionAfter, string PreviousPreparer,
    PurchaseOrderApproval PreviousApproval, long PreviousApprovalVersion, string PreparedBy, string Reason, DateTimeOffset RecordedAtUtc, string Kind = PurchaseOrder.ServicePriceRevisionKind);

public sealed partial class PurchaseOrder
{
    public const string ServicePriceRevisionKind = "purchasing.purchase-order.service-prices-revise.v1";
    private readonly List<PurchaseMixedServicePriceRevision> servicePriceRevisions = [];
    [JsonIgnore] public IReadOnlyList<PurchaseMixedServicePriceRevision> ServicePriceRevisions => servicePriceRevisions.AsReadOnly();

    /// <summary>ALS-D9/D18 and U-02: explicit parent revision with the native changed-child price transition.
    /// No supplier clear, acceptance reversal, quantity change or independent child approval is performed.</summary>
    public IReadOnlyList<PurchaseServiceOrderState> ReviseSelectedServicePrices(long expectedVersion, Guid companyAccountId,
        IReadOnlyList<PurchaseServiceOrderState> current, IReadOnlyList<PurchaseOrderServiceSelection> selections,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, decimal>> changes, string actor, string reason, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(PurchaseOrderStatus.Approved, PurchasingRuleCodes.OrderNotApproved, "Fiyat revizyonu onaylÄ± sipariÅŸte aÃ§Ä±lÄ±r.");
        var preparedBy = RequireActor(actor, nameof(actor));
        if (Approval is null || ApprovalVersion is null || PreparedBy is null || serviceMemberships.Count == 0
            || current.Count != serviceMemberships.Count || selections.Count != current.Count || changes.Count == 0
            || current.Any(x => x is null) || selections.Any(x => x is null)
            || current.Select(x => x.OrderId).Distinct().Count() != current.Count
            || selections.Select(x => x.ServiceOrderId).Distinct().Count() != selections.Count
            || changes.Keys.Any(id => current.All(x => x.OrderId != id)))
            throw new ArgumentException("Fiyat revizyonu gerÃ§ek ve tam baÄŸlÄ± hizmet seÃ§imini gerektirir.");
        var candidates = new List<PurchaseServiceOrderState>();
        foreach (var value in current)
        {
            PurchaseServiceOrder.Validate(value);
            var selected = selections.SingleOrDefault(x => x.ServiceOrderId == value.OrderId);
            var member = serviceMemberships.SingleOrDefault(x => x.ServiceOrderId == value.OrderId);
            if (member is null || selected is null || value.CompanyId != CompanyId || value.SupplierPartyId != SupplierPartyId
                || value.CompanyAccountId != companyAccountId || value.Money.CurrencyId != Money?.CurrencyId
                || value.Status != PurchaseServiceOrderStatus.Approved || selected.ExpectedVersion != value.Version
                || selected.LineIds is null || selected.LineIds.Count != value.Lines.Count
                || selected.LineIds.Distinct().Count() != selected.LineIds.Count
                || !selected.LineIds.ToHashSet().SetEquals(value.Lines.Select(x => x.LineId)))
                throw new ArgumentException("BaÄŸlÄ± hizmetin gÃ¼ncel sahibi, sÃ¼rÃ¼mÃ¼ veya satÄ±r seÃ§imi uyuÅŸmuyor.");
            // Native validator/zero/scale/acceptance preservation are reused verbatim, before touching the parent.
            candidates.Add(changes.TryGetValue(value.OrderId, out var prices)
                ? PurchaseServiceOrder.RevisePrices(value, value.Version, prices, preparedBy, reason, now) : value);
        }
        var revision = new PurchaseMixedServicePriceRevision(expectedVersion, checked(expectedVersion + 1), PreparedBy,
            Approval, ApprovalVersion.Value, preparedBy, reason.Trim(), now.ToUniversalTime());
        PreparedBy = preparedBy; Approval = null; ApprovalVersion = null; Status = PurchaseOrderStatus.Draft;
        Touch(now); servicePriceRevisions.Add(revision);
        return candidates.AsReadOnly();
    }

    // Persistence-only after genuine native Attempt/Audit and exact child revisions were verified by the owner.
    internal void RestoreRecordedServicePriceRevision(PurchaseMixedServicePriceRevision revision)
    {
        RequireVersion(revision.ExpectedVersion);
        if (revision.Kind != ServicePriceRevisionKind || Status != PurchaseOrderStatus.Approved || serviceMemberships.Count == 0 || PreparedBy != revision.PreviousPreparer
            || Approval != revision.PreviousApproval || ApprovalVersion != revision.PreviousApprovalVersion
            || revision.OrderVersionAfter != checked(Version + 1) || revision.RecordedAtUtc.Offset != TimeSpan.Zero
            || string.IsNullOrWhiteSpace(revision.PreparedBy) || string.IsNullOrWhiteSpace(revision.Reason)
            || revision.Reason.Length > 1000 || revision.Reason != revision.Reason.Trim())
            throw new InvalidOperationException("Recorded mixed service price revision is not the exact native transition.");
        PreparedBy = revision.PreparedBy; Approval = null; ApprovalVersion = null; Status = PurchaseOrderStatus.Draft;
        Version = revision.OrderVersionAfter; UpdatedAtUtc = revision.RecordedAtUtc; servicePriceRevisions.Add(revision);
    }
}
