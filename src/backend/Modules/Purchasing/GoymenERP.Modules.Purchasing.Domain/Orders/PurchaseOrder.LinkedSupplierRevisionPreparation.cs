using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Modules.Purchasing.Domain.Orders;

public sealed partial class PurchaseOrder
{
    /// <summary>Pure unpriced child candidates, NOT an authorized owner read, mutation, approval or durable receipt.
    /// ALS-D115 clears the previous supplier's prices. The real owner must supply actual current children and
    /// invoice/reference facts under one transaction; unavailable prerequisites never become empty history.
    /// The existing public ReviseSupplier remains closed for linked orders until that complete participant exists.</summary>
    public IReadOnlyList<PurchaseServiceOrderState> PrepareLinkedSupplierRevision(long expectedVersion, Guid currentCompanyAccountId,
        IReadOnlyList<PurchaseServiceOrderState> currentChildren, IReadOnlyList<PurchaseOrderServiceSelection> selections,
        Guid targetSupplierPartyId, Guid targetCompanyAccountId, string targetPartyVersion, string targetAccountVersion,
        PurchaseOrderInvoiceHistory invoiceHistory, string preparedBy, string reason, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(PurchaseOrderStatus.Approved, SupplierRevisionBlocked, "Yalnız onaylı sipariş için cari revizyonu hazırlanabilir.");
        var actor = RequireActor(preparedBy, nameof(preparedBy));
        Identity.Require(targetSupplierPartyId, nameof(targetSupplierPartyId)); Identity.Require(targetCompanyAccountId, nameof(targetCompanyAccountId));
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > SupplierRevisionReasonMaximumLength)
            throw new ArgumentException("Cari değişikliği gerekçesi zorunludur ve en fazla 2000 karakter olabilir.", nameof(reason));
        if (targetSupplierPartyId == SupplierPartyId) throw new PurchasingRuleException(SupplierRevisionNoChange, "Cari değişmedi.");
        if (invoiceHistory is not (PurchaseOrderInvoiceHistory.None or PurchaseOrderInvoiceHistory.Exists))
            throw new PurchasingRuleException(SupplierRevisionInvoiceUnavailable, "Fatura geçmişi doğrulanamadı.");
        if (invoiceHistory == PurchaseOrderInvoiceHistory.Exists || acceptances.Count != 0 || Approval is null || ApprovalVersion is null)
            throw new PurchasingRuleException(SupplierRevisionBlocked, "Kabul/fatura varsa cari revizyonu hazırlanamaz.");
        if (Money is null) throw new PurchasingRuleException(SupplierRevisionMoneyUnavailable, "Para profili eksik.");
        ArgumentNullException.ThrowIfNull(currentChildren); ArgumentNullException.ThrowIfNull(selections);
        if (serviceMemberships.Count == 0 || currentChildren.Count != serviceMemberships.Count || selections.Count != serviceMemberships.Count
            || currentChildren.Any(x => x is null) || selections.Any(x => x is null)
            || currentChildren.Select(x => x.OrderId).Distinct().Count() != currentChildren.Count
            || selections.Select(x => x.ServiceOrderId).Distinct().Count() != selections.Count)
            throw new ArgumentException("Bütün güncel hizmet kimlikleri açıkça seçilmelidir.");
        var candidates = new List<PurchaseServiceOrderState>();
        foreach (var member in serviceMemberships)
        {
            var child = currentChildren.SingleOrDefault(x => x.OrderId == member.ServiceOrderId)
                ?? throw new ArgumentException("Gerçek güncel hizmet kaydı eksik.");
            var selection = selections.SingleOrDefault(x => x.ServiceOrderId == member.ServiceOrderId)
                ?? throw new ArgumentException("Güncel hizmet seçimi eksik.");
            PurchaseServiceOrder.Validate(child);
            if (member.CompanyId != CompanyId || member.PurchaseOrderId != Id || child.CompanyId != CompanyId
                || child.SupplierPartyId != SupplierPartyId || child.CompanyAccountId != currentCompanyAccountId || child.Money.CurrencyId != Money.CurrencyId
                || child.Version != selection.ExpectedVersion || child.Version == long.MaxValue
                || child.Status is not (PurchaseServiceOrderStatus.Approved or PurchaseServiceOrderStatus.Draft)
                || child.Acceptances.Count != 0 || child.Lines.Any(x => x.AcceptedQuantity != 0)
                || selection.LineIds is not { Count: > 0 } || selection.LineIds.Any(x => x == Guid.Empty)
                || selection.LineIds.Distinct().Count() != selection.LineIds.Count
                || !selection.LineIds.ToHashSet().SetEquals(child.Lines.Select(x => x.LineId)))
                throw new PurchasingRuleException(SupplierRevisionBlocked, "Güncel hizmet kapsamı, sürümü veya kabul durumu uyuşmuyor.");
            var candidate = PurchaseServiceOrder.PrepareSupplierRevision(child, child.Version, targetSupplierPartyId,
                targetCompanyAccountId, targetPartyVersion, targetAccountVersion, actor, reason.Trim(), now.ToUniversalTime(), invoiceHistory);
            candidates.Add(candidate);
        }
        return candidates.AsReadOnly();
    }
    public IReadOnlyList<PurchaseServiceOrderState> ReviseSupplierWithServiceOrders(long expectedVersion, Guid currentAccount,
        IReadOnlyList<PurchaseServiceOrderState> original, IReadOnlyList<PurchaseServiceOrderState> preparedCandidates,
        IReadOnlyList<PurchaseOrderServiceSelection> selections, Guid targetParty, Guid targetAccount,
        PurchaseOrderInvoiceHistory invoiceHistory, string actor, string reason, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(preparedCandidates); ArgumentNullException.ThrowIfNull(selections);
        if (preparedCandidates.Count != original.Count) throw new ArgumentException("Bütün native adaylar gerekli.");
        var result = new List<PurchaseServiceOrderState>();
        foreach (var child in original)
        {
            var candidate = preparedCandidates.Single(x => x.OrderId == child.OrderId);
            var expected = PrepareLinkedSupplierRevision(expectedVersion, currentAccount, original, selections, targetParty,
                targetAccount, candidate.PartyVersion, candidate.AccountVersion, invoiceHistory, actor, reason, at)
                .Single(x => x.OrderId == child.OrderId);
            if ((candidate with { UpdatedAtUtc = expected.UpdatedAtUtc, Lines = expected.Lines, Materials = expected.Materials, ExpectedOutputs = expected.ExpectedOutputs,
                Acceptances = expected.Acceptances }) != expected || !candidate.Lines.SequenceEqual(expected.Lines)
                || !candidate.Materials.SequenceEqual(expected.Materials) || !candidate.ExpectedOutputs.SequenceEqual(expected.ExpectedOutputs)
                || !candidate.Acceptances.SequenceEqual(expected.Acceptances)) throw new ArgumentException("Native cari revizyonu adayı uyuşmuyor.");
            result.Add(expected);
        }
        ReviseSupplierCore(expectedVersion, targetParty, invoiceHistory, actor, reason, at);
        return result.AsReadOnly();
    }

    internal void RestoreRecordedLinkedSupplierRevision(PurchaseSupplierRevision revision)
    {
        RequireVersion(revision.OrderVersionAfter - 1);
        if (Status != PurchaseOrderStatus.Approved || serviceMemberships.Count == 0 || revision.Revision != SupplierRevision + 1
            || revision.PreviousSupplierPartyId != SupplierPartyId || revision.PreviousPreparer != PreparedBy || Approval != revision.PreviousApproval
            || ApprovalVersion != revision.PreviousApprovalVersion || revision.PreviousLines.Count != lines.Count
            || lines.Any(x => revision.PreviousLines.Count(p => p.LineId == x.Id && p.Price == x.Price) != 1))
            throw new InvalidOperationException("Doğrulanmış bağlı cari revizyonu geçmişle uyuşmuyor.");
        supplierRevisions.Add(revision); SupplierPartyId = revision.SupplierPartyId; PreparedBy = revision.PreparedBy;
        Approval = null; ApprovalVersion = null; Status = PurchaseOrderStatus.Draft;
        foreach (var line in lines) line.ReplaceSupplierRevisionPrice(null);
        Version = revision.OrderVersionAfter; UpdatedAtUtc = revision.RecordedAtUtc;
    }

}
