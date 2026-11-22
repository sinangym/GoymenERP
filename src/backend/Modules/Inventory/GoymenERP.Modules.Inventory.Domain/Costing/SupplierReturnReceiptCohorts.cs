namespace GoymenERP.Modules.Inventory.Domain.Costing;

/// <summary>
/// One recognised invoice of a goods receipt. <see cref="InvoicedRootKg"/> is the receipt kilograms that invoice bills; null is
/// the older shape of one complete invoice of the whole receipt. <see cref="CountedReversalIds"/> are the receipt undos the
/// invoice already counted when it was recognised.
/// </summary>
public sealed record SupplierReturnCohortEffect(Guid SourceEventId, ExactCostRatio? InvoicedRootKg, IReadOnlyCollection<Guid> CountedReversalIds);

/// <summary>Kilograms taken from one invoiced source (its event) or, with a null event, from the part still waiting for an invoice.</summary>
public sealed record SupplierReturnCohortSelection(Guid? SourceEventId, ExactCostRatio QuantityKg);

/// <summary>
/// An earlier supplier return or receipt undo of the receipt. <see cref="Selection"/> is the attribution recorded with it (the
/// preparer's choice, K3-B2); null when it recorded none.
/// </summary>
public sealed record SupplierReturnCohortExit(Guid MovementId, Guid? ReversalId, decimal QuantityKg,
    IReadOnlyList<SupplierReturnCohortSelection>? Selection);

/// <summary>
/// What is left of one receipt (TI3): <see cref="OpenKg"/> = received − earlier returns; the pending part and each invoiced
/// source's part add up to it exactly.
/// </summary>
public sealed record SupplierReturnCohortState(ExactCostRatio OpenKg, ExactCostRatio PendingKg,
    IReadOnlyDictionary<Guid, ExactCostRatio> InvoicedKg);

public enum SupplierReturnCohortChoiceOutcome
{
    Chosen = 1,

    /// <summary>K3-B2: both an invoiced and a pending part (or several invoices) are open and nothing was chosen.</summary>
    ChoiceRequired = 2,

    /// <summary>The choice names an unknown source, a negative quantity, or does not add up to the returned kilograms.</summary>
    Mismatch = 3,

    /// <summary>TI3: the choice takes more than what is left of a part; nothing is moved to another part.</summary>
    LimitExceeded = 4,
}

public sealed record SupplierReturnCohortChoice(SupplierReturnCohortChoiceOutcome Outcome,
    IReadOnlyList<SupplierReturnCohortSelection> Selected);

/// <summary>
/// SAT-D433 step 3 / K3-B2 / TI3, pure: the invoiced and invoice-pending parts left of one goods receipt, and the validation of
/// the preparer's explicit choice between them. The limits are the receipt's own kilograms (received minus what earlier returns
/// took from each part); they are not scaled by sales or other stock exits. Nothing here assigns a part on the preparer's behalf
/// when more than one is open, and a history that cannot be attributed exactly is unreadable (null), never estimated.
/// </summary>
public static class SupplierReturnReceiptCohorts
{
    private static readonly ExactCostRatio Zero = ExactCostRatio.Of(0m);

    public static SupplierReturnCohortState? Resolve(decimal receivedKg, IReadOnlyList<SupplierReturnCohortEffect> effects,
        IReadOnlyList<SupplierReturnCohortExit> exits)
    {
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(exits);
        if (receivedKg <= 0m || effects.Any(effect => effect is null || effect.SourceEventId == Guid.Empty || effect.CountedReversalIds is null
                || effect.InvoicedRootKg is { Sign: <= 0 })
            || effects.Select(effect => effect.SourceEventId).Distinct().Count() != effects.Count
            || exits.Any(exit => exit is null || exit.MovementId == Guid.Empty || exit.QuantityKg <= 0m)
            || exits.Select(exit => exit.MovementId).Distinct().Count() != exits.Count
            || effects.Count > 1 && effects.Any(effect => effect.InvoicedRootKg is null))
            return null;

        var received = ExactCostRatio.Of(receivedKg);
        var open = exits.Aggregate(received, (left, exit) => left.Subtract(ExactCostRatio.Of(exit.QuantityKg)));
        if (open.Sign < 0) return null;
        var invoiced = new Dictionary<Guid, ExactCostRatio>();
        var pending = received;
        var attributed = new List<SupplierReturnCohortExit>();
        foreach (var exit in exits)
        {
            if (exit.Selection is not null) { attributed.Add(exit); continue; }
            // Without a recorded attribution an earlier exit is exact only when no invoice exists (it was all pending) or when
            // every invoice was recognised after it and counted it (it reduced the receipt before the invoices were measured).
            if (effects.Count != 0 && (exit.ReversalId is not { } reversal
                    || effects.Any(effect => effect.InvoicedRootKg is null || !effect.CountedReversalIds.Contains(reversal))))
                return null;
            pending = pending.Subtract(ExactCostRatio.Of(exit.QuantityKg));
        }

        foreach (var effect in effects)
        {
            var root = effect.InvoicedRootKg ?? pending;
            invoiced.Add(effect.SourceEventId, root);
            pending = pending.Subtract(root);
        }

        if (pending.Sign < 0) return null;
        foreach (var exit in attributed)
        {
            var total = Zero;
            foreach (var selected in exit.Selection!)
            {
                if (selected is null || selected.QuantityKg.Sign < 0) return null;
                total = total.Add(selected.QuantityKg);
                if (selected.SourceEventId is { } id)
                {
                    if (!invoiced.TryGetValue(id, out var left)) return null;
                    invoiced[id] = left.Subtract(selected.QuantityKg);
                }
                else pending = pending.Subtract(selected.QuantityKg);
            }

            if (total.CompareTo(ExactCostRatio.Of(exit.QuantityKg)) != 0) return null;
        }

        if (pending.Sign < 0 || invoiced.Values.Any(left => left.Sign < 0)
            || invoiced.Values.Aggregate(pending, (sum, left) => sum.Add(left)).CompareTo(open) != 0)
            return null;
        return new SupplierReturnCohortState(open, pending, invoiced);
    }

    /// <summary>
    /// Validates the choice for <paramref name="quantityKg"/> of the receipt. With no explicit choice the only open part is taken
    /// (all pending, or the one invoiced source when nothing is pending); when more than one part is open the preparer must choose.
    /// </summary>
    public static SupplierReturnCohortChoice Choose(SupplierReturnCohortState state, decimal quantityKg,
        IReadOnlyList<SupplierReturnCohortSelection>? explicitSelection)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (quantityKg <= 0m) throw new ArgumentOutOfRangeException(nameof(quantityKg));
        var quantity = ExactCostRatio.Of(quantityKg);
        if (quantity.CompareTo(state.OpenKg) > 0) return new(SupplierReturnCohortChoiceOutcome.LimitExceeded, []);
        IReadOnlyList<SupplierReturnCohortSelection> selected;
        if (explicitSelection is null)
        {
            var openInvoices = state.InvoicedKg.Where(pair => pair.Value.Sign > 0).ToArray();
            if (openInvoices.Length == 0) selected = [new(null, quantity)];
            else if (openInvoices.Length == 1 && state.PendingKg.Sign == 0) selected = [new(openInvoices[0].Key, quantity)];
            else return new(SupplierReturnCohortChoiceOutcome.ChoiceRequired, []);
        }
        else
        {
            if (explicitSelection.Any(item => item is null || item.QuantityKg.Sign < 0
                    || item.SourceEventId is { } id && !state.InvoicedKg.ContainsKey(id))
                || explicitSelection.Select(item => item.SourceEventId).Distinct().Count() != explicitSelection.Count
                || explicitSelection.Aggregate(Zero, (sum, item) => sum.Add(item.QuantityKg)).CompareTo(quantity) != 0)
                return new(SupplierReturnCohortChoiceOutcome.Mismatch, []);
            selected = explicitSelection.Where(item => item.QuantityKg.Sign > 0).ToArray();
        }

        foreach (var item in selected)
        {
            var left = item.SourceEventId is { } id ? state.InvoicedKg[id] : state.PendingKg;
            if (item.QuantityKg.CompareTo(left) > 0) return new(SupplierReturnCohortChoiceOutcome.LimitExceeded, []);
        }

        return new(SupplierReturnCohortChoiceOutcome.Chosen, selected);
    }
}
