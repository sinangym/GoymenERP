using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Periods;

namespace GoymenERP.Modules.Accounting.Domain.Posting;

/// <summary>An owner-proven economic share, not a percentage or a new account chosen by the cancellation request.</summary>
public sealed record PostedCogsCancellationComponent(Guid CompanyId, Guid JournalId, Guid JournalSourceId,
    string PostingPurpose, Guid CurrencyId, decimal JournalTotal, decimal SignedAmount,
    Guid CogsAccountId, Guid CounterAccountId, string EconomicKey, string SourceFingerprint);

public static class InvoiceLineCogsCancellation
{
    public const string RuleVersion = "invoice-line-cogs-component-cancellation/v1";

    /// <summary>Answers only this actual share. JournalKind.Reversal's whole-journal semantics are not reused.</summary>
    public static Journal ReverseComponent(Journal original, PostedCogsCancellationComponent component,
        AccountingPeriod period, PostingDates dates, Guid reversalId)
    {
        ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(period);
        var value = Math.Abs(component.SignedAmount);
        var positive = component.SignedAmount > 0;
        if (component.CompanyId != original.CompanyId || period.CompanyId != original.CompanyId
            || component.JournalId != original.Id || component.JournalSourceId != original.Source.SourceId
            || component.PostingPurpose != original.Source.PostingPurpose || component.CurrencyId != original.CurrencyId
            || original.Kind is not (JournalKind.Original or JournalKind.Correction)
            || component.EconomicKey is not { Length: 64 } || !component.EconomicKey.All(Uri.IsHexDigit)
            || component.SourceFingerprint is not { Length: 64 } || !component.SourceFingerprint.All(Uri.IsHexDigit)
            || value <= 0 || value > component.JournalTotal || original.Lines.Count != 2
            || original.Lines[0].Debit != component.JournalTotal || original.Lines[0].Credit != 0
            || original.Lines[1].Credit != component.JournalTotal || original.Lines[1].Debit != 0
            || original.Lines[positive ? 0 : 1].Account.AccountId != component.CogsAccountId
            || original.Lines[positive ? 1 : 0].Account.AccountId != component.CounterAccountId
            || component.CogsAccountId == component.CounterAccountId)
            throw new AccountingDomainException(AccountingErrorCodes.InvalidReference,
                "Cancellation requires the exact posted economic share and its current accounts.");
        var cogs = new AccountRef(original.CompanyId, component.CogsAccountId);
        var counter = new AccountRef(original.CompanyId, component.CounterAccountId);
        JournalLine[] lines = positive ? [new(counter, value, 0), new(cogs, 0, value)]
            : [new(cogs, value, 0), new(counter, 0, value)];
        return Journal.Post(reversalId, JournalKind.Correction, original.Source, RuleVersion,
            original.CurrencyId, dates, lines, period, original.Id);
    }
}
