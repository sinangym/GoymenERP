using System.Numerics;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// FIN-S10 (FNS-D44, FNS-D81, SAT-D294): the one deterministic calculator of a customer return's split over the original
/// invoice's installments. The acceptance and the screen's prefill read both use it.
/// <list type="bullet">
/// <item>A return that covers the whole outstanding amount closes every outstanding item; the rest is the advance.</item>
/// <item>A partial return is split by the user's installment selection (FNS-D81: the automatic nearest-due order of
/// FNS-D54 is not applied). Without a selection it is refused when the invoice has more than one outstanding installment
/// (<see cref="FinanceInvoiceAcceptanceService.AllocationRequiredCode"/>); with exactly one outstanding installment the
/// split is unambiguous and that installment is reduced. The nearest-due spill-over stays only as the screen's prefill
/// (FNS-D44), a suggestion the user confirms or changes.</item>
/// <item>A partial return with the user's selection reduces exactly the selected parts (FNS-D44): every selected item is an
/// outstanding item of the invoice, no part exceeds its item's open amount, and the parts total the return amount exactly;
/// otherwise nothing is planned. The selection is never replaced by the prefill.</item>
/// </list>
/// Stored amounts or states that do not read back are refused, not guessed. Pure: no clock, no database, no rounding.
/// </summary>
internal static class FinanceCustomerReturnInstallmentCalculator
{
    /// <summary>The split the acceptance writes.</summary>
    internal static FinanceCustomerReturnPlan Plan(IReadOnlyList<FinanceOpenItemRow> items, string amountText,
        IReadOnlyList<FinanceInvoiceAcceptanceAllocationV2>? allocations)
    {
        if (!FinanceExactDecimalText.TryParsePositive(amountText, out var amount))
        {
            return FinanceCustomerReturnPlan.Refused(new ValidationIssue("amount", AmountInvalidCode));
        }
        if (Read(items) is not { } stored)
        {
            return FinanceCustomerReturnPlan.Refused(
                new ValidationIssue("originalInvoiceId", FinanceInvoiceAcceptanceService.StoredStateNotUnderstoodCode));
        }

        if (amount.CompareTo(stored.OutstandingTotal) >= 0)
        {
            if (allocations is not null)
            {
                return FinanceCustomerReturnPlan.Refused(
                    new ValidationIssue("allocations", FinanceInvoiceAcceptanceService.AllocationNotApplicableCode));
            }
            var closing = stored.Outstanding
                .Select(row => (row.Item, After: default(FinanceExactDecimalText), Reduced: row.Outstanding))
                .ToArray();
            return new FinanceCustomerReturnPlan(null, amount, stored.Total, stored.OutstandingTotal,
                amount.Subtract(stored.OutstandingTotal), closing, FinanceCustomerReturnPlanBasis.WholeOutstanding);
        }

        if (allocations is null)
        {
            // FNS-D81: the user chooses the installment of a partial return; only a single outstanding installment leaves
            // nothing to choose. Several outstanding installments are never split by an automatic order.
            if (stored.Outstanding is not [var only])
            {
                return FinanceCustomerReturnPlan.Refused(
                    new ValidationIssue("allocations", FinanceInvoiceAcceptanceService.AllocationRequiredCode));
            }
            return new FinanceCustomerReturnPlan(null, amount, stored.Total, amount, default,
                [(only.Item, After: only.Outstanding.Subtract(amount), Reduced: amount)],
                FinanceCustomerReturnPlanBasis.SingleOutstanding);
        }

        return Selected(stored, amount, allocations);
    }

    /// <summary>The screen's prefill: every outstanding installment, nearest due first, with the calculator's part.</summary>
    internal static FinanceCustomerReturnPrefill Prefill(IReadOnlyList<FinanceOpenItemRow> items, string amountText)
    {
        if (!FinanceExactDecimalText.TryParsePositive(amountText, out var amount))
        {
            return FinanceCustomerReturnPrefill.Refused(new ValidationIssue("amount", AmountInvalidCode));
        }
        if (Read(items) is not { } stored || NearestDue(stored.Outstanding, amount) is not { } lines)
        {
            return FinanceCustomerReturnPrefill.Refused(
                new ValidationIssue("originalInvoiceId", FinanceInvoiceAcceptanceService.StoredStateNotUnderstoodCode));
        }

        var whole = amount.CompareTo(stored.OutstandingTotal) >= 0;
        return new FinanceCustomerReturnPrefill(null, amount, stored.Total, SelectionAllowed: !whole,
            whole ? stored.OutstandingTotal : amount,
            whole ? amount.Subtract(stored.OutstandingTotal) : default,
            lines);
    }

    private const string AmountInvalidCode = "finance_customer_return_amount_invalid";

    private static FinanceCustomerReturnPlan Selected(FinanceCustomerReturnStored stored, FinanceExactDecimalText amount,
        IReadOnlyList<FinanceInvoiceAcceptanceAllocationV2> allocations)
    {
        var parts = new Dictionary<Guid, FinanceExactDecimalText>();
        var selectedTotal = default(FinanceExactDecimalText);
        foreach (var allocation in allocations)
        {
            if (allocation is null
                || !FinanceExactDecimalText.TryParsePositive(allocation.Amount, out var part)
                || FinerThan(part, amount.Scale)
                || !parts.TryAdd(allocation.OpenItemId, AtScale(part, amount.Scale)))
            {
                return FinanceCustomerReturnPlan.Refused(
                    new ValidationIssue("allocations", FinanceInvoiceAcceptanceService.AllocationInvalidCode));
            }
            selectedTotal = selectedTotal.Add(parts[allocation.OpenItemId]);
        }

        var outstanding = stored.Outstanding.ToDictionary(row => row.Item.Id);
        foreach (var (openItemId, part) in parts)
        {
            if (!outstanding.TryGetValue(openItemId, out var row))
            {
                return FinanceCustomerReturnPlan.Refused(
                    new ValidationIssue("allocations", FinanceInvoiceAcceptanceService.AllocationItemUnknownCode));
            }
            if (part.CompareTo(row.Outstanding) > 0)
            {
                return FinanceCustomerReturnPlan.Refused(
                    new ValidationIssue("allocations", FinanceInvoiceAcceptanceService.AllocationExceedsOpenAmountCode));
            }
        }
        if (selectedTotal.CompareTo(amount) != 0)
        {
            return FinanceCustomerReturnPlan.Refused(
                new ValidationIssue("allocations", FinanceInvoiceAcceptanceService.AllocationTotalMismatchCode));
        }

        // The stored order of the invoice's items, so the written rows do not depend on the order of the selection.
        var reductions = stored.Outstanding
            .Where(row => parts.ContainsKey(row.Item.Id))
            .Select(row => (row.Item, After: row.Outstanding.Subtract(parts[row.Item.Id]), Reduced: parts[row.Item.Id]))
            .ToArray();
        return new FinanceCustomerReturnPlan(null, amount, stored.Total, amount, default, reductions,
            FinanceCustomerReturnPlanBasis.UserSelection);
    }

    /// <summary>
    /// Nearest due first: the open installments ordered by due date, then by id for a deterministic tie; each takes the
    /// smaller of its open amount and what is left of the return. An outstanding item without a due date has no place in
    /// that order and is refused (null).
    /// </summary>
    private static IReadOnlyList<FinanceCustomerReturnInstallmentLine>? NearestDue(
        IReadOnlyList<(FinanceOpenItemRow Item, FinanceExactDecimalText Original, FinanceExactDecimalText Outstanding)> outstanding,
        FinanceExactDecimalText amount)
    {
        if (outstanding.Any(row => row.Item.DueOn is null))
        {
            return null;
        }

        var left = amount;
        var lines = new List<FinanceCustomerReturnInstallmentLine>(outstanding.Count);
        foreach (var row in outstanding.OrderBy(x => x.Item.DueOn!.Value).ThenBy(x => x.Item.Id))
        {
            var part = left.CompareTo(row.Outstanding) < 0 ? left : row.Outstanding;
            left = left.Subtract(part);
            // An installment the return does not reach shows a plain zero ("0").
            lines.Add(new FinanceCustomerReturnInstallmentLine(row.Item, row.Item.DueOn!.Value, row.Original,
                row.Outstanding, part.IsZero ? default : part));
        }
        return lines;
    }

    /// <summary>A part with a non-zero digit below the return amount's own scale (finer than its kuruş).</summary>
    private static bool FinerThan(FinanceExactDecimalText part, int scale) =>
        part.Scale > scale && !(part.Unscaled % BigInteger.Pow(10, part.Scale - scale)).IsZero;

    /// <summary>
    /// The same value written at exactly <paramref name="scale"/>, the return amount's own scale, so the stored reduction
    /// reads like the return amount (only zero digits are dropped or added; <see cref="FinerThan"/> is checked first).
    /// </summary>
    private static FinanceExactDecimalText AtScale(FinanceExactDecimalText part, int scale) =>
        part.Scale >= scale
            ? new FinanceExactDecimalText(part.Unscaled / BigInteger.Pow(10, part.Scale - scale), scale)
            : new FinanceExactDecimalText(part.Unscaled * BigInteger.Pow(10, scale - part.Scale), scale);

    private static FinanceCustomerReturnStored? Read(IReadOnlyList<FinanceOpenItemRow> items)
    {
        var total = default(FinanceExactDecimalText);
        var outstandingTotal = default(FinanceExactDecimalText);
        var outstanding = new List<(FinanceOpenItemRow Item, FinanceExactDecimalText Original, FinanceExactDecimalText Outstanding)>();
        foreach (var item in items)
        {
            if (item.Direction != FinanceOpenItemDirectionCodes.Receivable
                || item.Status is not (FinanceOpenItemStatuses.Open or FinanceOpenItemStatuses.Closed)
                || !FinanceExactDecimalText.TryParsePositive(item.OriginalAmountText, out var original)
                || !FinanceExactDecimalText.TryParseNonNegative(item.OutstandingAmountText, out var left)
                || left.CompareTo(original) > 0
                || (item.Status == FinanceOpenItemStatuses.Closed) != left.IsZero)
            {
                return null;
            }
            total = total.Add(original);
            outstandingTotal = outstandingTotal.Add(left);
            if (!left.IsZero)
            {
                outstanding.Add((item, original, left));
            }
        }
        return new FinanceCustomerReturnStored(total, outstandingTotal, outstanding);
    }

    private sealed record FinanceCustomerReturnStored(
        FinanceExactDecimalText Total,
        FinanceExactDecimalText OutstandingTotal,
        IReadOnlyList<(FinanceOpenItemRow Item, FinanceExactDecimalText Original, FinanceExactDecimalText Outstanding)> Outstanding);
}

/// <summary>How a plan's reductions were chosen; recorded on the audit event.</summary>
internal enum FinanceCustomerReturnPlanBasis
{
    /// <summary>A refused plan.</summary>
    None = 0,
    /// <summary>The return covers the whole outstanding amount: every outstanding item is closed.</summary>
    WholeOutstanding = 1,
    /// <summary>
    /// Historical only: a partial return without a selection split nearest due first (FNS-D54). FNS-D81 replaced it with
    /// the user's selection; the value is kept so audit events written before stay readable. Never produced.
    /// </summary>
    NearestDue = 2,
    /// <summary>A partial return over the user's selection, applied as submitted (FNS-D44, FNS-D81).</summary>
    UserSelection = 3,
    /// <summary>A partial return without a selection over the invoice's only outstanding installment (FNS-D81).</summary>
    SingleOutstanding = 4,
}

/// <summary>
/// The computed split of one return: <see cref="Applied"/> reduced the listed items, <see cref="Advance"/> is the rest;
/// <see cref="InvoiceTotal"/> is the invoice's original total. When <see cref="Issue"/> is set nothing is written.
/// </summary>
internal sealed record FinanceCustomerReturnPlan(
    ValidationIssue? Issue,
    FinanceExactDecimalText Amount,
    FinanceExactDecimalText InvoiceTotal,
    FinanceExactDecimalText Applied,
    FinanceExactDecimalText Advance,
    IReadOnlyList<(FinanceOpenItemRow Item, FinanceExactDecimalText After, FinanceExactDecimalText Reduced)> Reductions,
    FinanceCustomerReturnPlanBasis Basis = FinanceCustomerReturnPlanBasis.None)
{
    public static FinanceCustomerReturnPlan Refused(ValidationIssue issue) =>
        new(issue, default, default, default, default, []);
}

/// <summary>One outstanding installment in nearest-due order and the calculator's part of the return on it.</summary>
internal sealed record FinanceCustomerReturnInstallmentLine(
    FinanceOpenItemRow Item,
    DateOnly DueOn,
    FinanceExactDecimalText Original,
    FinanceExactDecimalText Outstanding,
    FinanceExactDecimalText Prefill);

/// <summary>The prefill of one return amount; when <see cref="Issue"/> is set there is no prefill.</summary>
internal sealed record FinanceCustomerReturnPrefill(
    ValidationIssue? Issue,
    FinanceExactDecimalText Amount,
    FinanceExactDecimalText InvoiceTotal,
    bool SelectionAllowed,
    FinanceExactDecimalText Applied,
    FinanceExactDecimalText Advance,
    IReadOnlyList<FinanceCustomerReturnInstallmentLine> Lines)
{
    public static FinanceCustomerReturnPrefill Refused(ValidationIssue issue) =>
        new(issue, default, default, false, default, default, []);
}
