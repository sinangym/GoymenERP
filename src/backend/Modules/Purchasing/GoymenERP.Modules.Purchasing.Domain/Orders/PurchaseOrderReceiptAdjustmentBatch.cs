using GoymenERP.Modules.Purchasing.Domain.Quantities;

namespace GoymenERP.Modules.Purchasing.Domain.Orders;

public sealed record PurchaseReceiptAdjustmentInput(Guid AdjustmentId, Guid AcceptanceId, Quantity UndoQuantity,
    bool IsSupplierReturn, bool ReplacementExpected, Guid? NewAcceptanceId = null, Quantity? NewAcceptedQuantity = null);

public sealed record PurchaseReceiptAdjustmentGroupChange(Guid GeneralLineId, Quantity NetOpenedQuantity,
    Quantity AllocatableQuantity, bool Revived);

public sealed record PurchaseReceiptAdjustmentBatchChange(IReadOnlyList<PurchaseReceiptAdjustment> Adjustments,
    IReadOnlyList<PurchaseReceiptAdjustmentGroupChange> Supplies);

public sealed partial class PurchaseOrder
{
    /// <summary>A bounded command payload, not a quantity, business tolerance or automatic batch split.</summary>
    public const int MaximumReceiptCorrectionsPerCommand = 500;

    /// <summary>
    /// One version and one net decision per general line. Every original share, final line total and identity is
    /// validated BEFORE mutation. In particular, an early undo cannot revive a closed line if a later replacement
    /// consumes the same quantity. No temporary single-correction state is exposed to Inventory.
    /// </summary>
    public PurchaseReceiptAdjustmentBatchChange AdjustReceipts(long expectedVersion,
        IReadOnlyList<PurchaseReceiptAdjustmentInput> changes, string actor, string? reason, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        _ = checked(Version + 1);
        ArgumentNullException.ThrowIfNull(changes);
        var recordedActor = RequireActor(actor, nameof(actor));
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > MaximumReasonLength)
            throw new PurchasingRuleException(PurchasingRuleCodes.ReceiptAdjustmentReasonInvalid,
                "Kabul düzeltmesi geçerli ve sınır içinde bir gerekçe ister.");
        if (Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.Cancelled))
            throw new PurchasingRuleException(PurchasingRuleCodes.OrderNotApproved,
                "Kabul düzeltmesi yalnız onay geçmişi olan siparişe yazılır.");
        if (changes.Count is < 1 or > MaximumReceiptCorrectionsPerCommand || changes.Any(item => item is null))
            throw InvalidCorrection();

        var ids = new HashSet<Guid>();
        var replacementIds = new HashSet<Guid>();
        var undoneByAcceptance = new Dictionary<Guid, Quantity>();
        var entries = new List<ReceiptBatchEntry>(changes.Count);
        foreach (var change in changes)
        {
            Identity.Require(change.AdjustmentId, nameof(change.AdjustmentId));
            Identity.Require(change.AcceptanceId, nameof(change.AcceptanceId));
            if (!ids.Add(change.AdjustmentId) || receiptAdjustments.Any(item => item.AdjustmentId == change.AdjustmentId))
                throw new PurchasingRuleException(PurchasingRuleCodes.ReceiptAdjustmentDuplicate,
                    "Aynı kabul düzeltmesi ikinci kez kaydedilemez.");
            var original = OriginalAcceptance(change.AcceptanceId);
            var line = FindLine(original.LineId);
            var replacement = change.NewAcceptedQuantity ?? Quantity.Zero(original.Quantity.Unit);
            if (change.UndoQuantity.Unit != original.Quantity.Unit || replacement.Unit != original.Quantity.Unit)
                throw InvalidCorrection();
            var undone = undoneByAcceptance.GetValueOrDefault(change.AcceptanceId, Quantity.Zero(original.Quantity.Unit))
                .Add(change.UndoQuantity);
            if (change.UndoQuantity.IsZero || undone.IsGreaterThan(original.Quantity.Subtract(AlreadyUndone(change.AcceptanceId))))
                throw new PurchasingRuleException(PurchasingRuleCodes.ReceiptAdjustmentExceedsAcceptance,
                    "Geri alınan pay özgün kabulün henüz geri alınmamış miktarını aşamaz.");
            undoneByAcceptance[change.AcceptanceId] = undone;
            if ((change.NewAcceptanceId is null) != replacement.IsZero || change.NewAcceptanceId == Guid.Empty
                || (change.IsSupplierReturn && (!replacement.IsZero || change.NewAcceptanceId is not null)))
                throw InvalidCorrection();
            if (change.NewAcceptanceId is { } replacementId
                && (!replacementIds.Add(replacementId) || acceptances.Any(item => item.AcceptanceId == replacementId)))
                throw new PurchasingRuleException(PurchasingRuleCodes.DuplicateAcceptanceId,
                    "Düzeltmenin yeni kabul kimliği daha önce kullanılmış.");
            var general = line.Kind == PurchaseOrderLineKind.General ? line : FindLine(line.ParentLineId!.Value);
            entries.Add(new(change, line, general.Id, replacement, change.IsSupplierReturn && !change.ReplacementExpected));
        }

        var totals = new List<ReceiptLineTotal>();
        foreach (var byLine in entries.GroupBy(item => item.Line.Id))
        {
            var line = byLine.First().Line;
            var zero = Quantity.Zero(line.Quantity.Unit);
            var undo = byLine.Aggregate(zero, (sum, item) => sum.Add(item.Input.UndoQuantity));
            var replacement = byLine.Aggregate(zero, (sum, item) => sum.Add(item.Replacement));
            var returned = byLine.Where(item => item.DefinitiveReturn).Aggregate(line.DefinitiveReturnQuantity,
                (sum, item) => sum.Add(item.Input.UndoQuantity));
            var accepted = line.AcceptedQuantity.Subtract(undo).Add(replacement);
            if (accepted.Add(returned).Add(line.CancelledQuantity).Add(line.ClosedQuantity).IsGreaterThan(line.CommittedQuantity))
                throw new PurchasingRuleException(PurchasingRuleCodes.AcceptanceExceedsRemaining,
                    "Yeni kabulün net artışı kalan taahhüdü aşamaz.");
            totals.Add(new(line, accepted, returned));
        }

        var groups = entries.GroupBy(item => item.GeneralLineId).Select(byGeneral =>
        {
            var general = FindLine(byGeneral.Key);
            var members = new[] { general }.Concat(TechnicalSlicesOf(general.Id)).ToArray();
            var net = byGeneral.Sum(item => (item.DefinitiveReturn ? 0m : item.Input.UndoQuantity.Value) - item.Replacement.Value);
            return new ReceiptGroupPlan(general, members, AllocatableGroupQuantity(general, members),
                net > 0, net > 0 && (Status == PurchaseOrderStatus.Cancelled || general.IsClosed));
        }).ToArray();

        // Nothing above changes the aggregate. Apply each physical line's FINAL totals exactly once.
        foreach (var total in totals) total.Line.ApplyReceiptAdjustmentTotals(total.Accepted, total.Returned);
        if (Status == PurchaseOrderStatus.Cancelled && groups.Any(group => group.NetPositive))
            Status = PurchaseOrderStatus.Approved;
        foreach (var group in groups.Where(group => group.NetPositive && group.General.IsClosed))
            foreach (var member in group.Members) member.ReviveReceiptClosure();
        foreach (var entry in entries.Where(item => item.Input.NewAcceptanceId is not null))
            acceptances.Add(new(entry.Input.NewAcceptanceId!.Value, entry.Line.Id, entry.Replacement, now.ToUniversalTime()));
        Touch(now);

        var supplies = groups.Select(group =>
        {
            var after = AllocatableGroupQuantity(group.General, group.Members);
            var opened = after.IsGreaterThan(group.Before) ? after.Subtract(group.Before) : Quantity.Zero(after.Unit);
            return new PurchaseReceiptAdjustmentGroupChange(group.General.Id, opened, after, group.Revived);
        }).ToArray();
        var remainingOpened = supplies.ToDictionary(item => item.GeneralLineId, item => item.NetOpenedQuantity);
        var results = new List<PurchaseReceiptAdjustment>(entries.Count);
        foreach (var entry in entries)
        {
            var remaining = remainingOpened[entry.GeneralLineId];
            var contribution = entry.DefinitiveReturn ? 0m : Math.Max(0m, entry.Input.UndoQuantity.Value - entry.Replacement.Value);
            var opened = Quantity.Of(Math.Min(remaining.Value, contribution), remaining.Unit);
            remainingOpened[entry.GeneralLineId] = remaining.Subtract(opened);
            var supply = supplies.Single(item => item.GeneralLineId == entry.GeneralLineId);
            var recorded = new PurchaseReceiptAdjustment(entry.Input.AdjustmentId, entry.Input.AcceptanceId, entry.Line.Id,
                entry.Input.UndoQuantity, entry.Input.IsSupplierReturn, entry.Input.ReplacementExpected, entry.Input.NewAcceptanceId,
                entry.Replacement, recordedActor, reason, now.ToUniversalTime(), Version, supply.Revived && !opened.IsZero, opened);
            results.Add(recorded);
        }
        receiptAdjustments.AddRange(results);
        return new(results.AsReadOnly(), Array.AsReadOnly(supplies));
    }

    private static PurchasingRuleException InvalidCorrection() => new(PurchasingRuleCodes.ReceiptAdjustmentInvalid,
        "Kabul düzeltmesi özgün kabul biriminde, tutarlı miktar ve yeni kabul kimliğiyle verilmelidir.");
    private sealed record ReceiptBatchEntry(PurchaseReceiptAdjustmentInput Input, PurchaseOrderLine Line,
        Guid GeneralLineId, Quantity Replacement, bool DefinitiveReturn);
    private sealed record ReceiptLineTotal(PurchaseOrderLine Line, Quantity Accepted, Quantity Returned);
    private sealed record ReceiptGroupPlan(PurchaseOrderLine General, PurchaseOrderLine[] Members, Quantity Before,
        bool NetPositive, bool Revived);
}
