using System.Globalization;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>Actual settlement and a separately approved carrying value; TCMB comparison values are not inputs.</summary>
public static class CashExchangeJournalAmountsV1
{
    // One original journal purpose for the one approved carrying-value fact; a mapping revision
    // must not create another original economic event by renaming its purpose.
    public const string PostingPurpose = "finance.cash-exchange";
    public const string SourceCarrying = "cash-exchange.source-carrying";
    public const string TargetReceipt = "cash-exchange.target-receipt";
    public const string RealizedGain = "cash-exchange.realized-gain";
    public const string RealizedLoss = "cash-exchange.realized-loss";

    public static IReadOnlyList<NormalJournalExactSourceLineV1>? TryCreate(string sourceBookValueTl, string actualTargetTl)
    {
        if (!NormalJournalDraftSourcePreparationV1.TryExactAmount(sourceBookValueTl, out var carrying)
            || !NormalJournalDraftSourcePreparationV1.TryExactAmount(actualTargetTl, out var receipt)
            || !NormalJournalExactSourceAmountsV1.TryDifference(receipt, carrying, out var difference)) return null;
        var lines = new List<NormalJournalExactSourceLineV1>
        {
            new(TargetReceipt, "Debit", Text(receipt)), new(SourceCarrying, "Credit", Text(carrying))
        };
        if (difference > 0) lines.Add(new(RealizedGain, "Credit", Text(difference)));
        if (difference < 0) lines.Add(new(RealizedLoss, "Debit", Text(-difference)));
        return NormalJournalExactSourceAmountsV1.Valid(lines) ? lines : null;
    }

    public static string Text(decimal amount) => amount.ToString("0.############################", CultureInfo.InvariantCulture);
}
