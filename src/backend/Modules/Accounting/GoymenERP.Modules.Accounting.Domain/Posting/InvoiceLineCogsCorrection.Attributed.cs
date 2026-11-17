using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Periods;

namespace GoymenERP.Modules.Accounting.Domain.Posting;

/// <summary>
/// An Accounting-owned immutable attribution component, not a proportion supplied by a correction request.
/// The persistence owner proves the stored attribution and its unique economic claim before calling the factory.
/// The actual aggregate journal and both original account identities remain part of this proof.
/// </summary>
public sealed record AttributedPostedJournalComponent(Guid CompanyId, Guid AttributionSourceEventId, Guid OutputId,
    Guid MovementId, int MovementLineNumber, Guid JournalId, Guid JournalSourceId, string PostingPurpose,
    Guid CurrencyId, decimal JournalTotal, decimal SignedAmount, Guid CogsAccountId, Guid CounterAccountId,
    string AttributionFingerprint);

public static partial class InvoiceLineCogsCorrection
{
    /// <summary>
    /// Corrects only the explicitly attributed component of an existing aggregate. The ordinary Create method's
    /// exact two-line/full-amount rule is unchanged. This method creates only the authorized correction pair,
    /// never a second initial COGS journal or a replacement of the aggregate journal.
    /// </summary>
    public static (Journal Reversing, Journal Replacement) CreateAttributed(Journal source,
        AttributedPostedJournalComponent component, Guid newCogsAccountId, AccountingPeriod period,
        PostingDates dates, Guid reversingId, Guid replacementId, Guid reversingCorrectionId, Guid replacementCorrectionId)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(component); ArgumentNullException.ThrowIfNull(period);
        var amount = Math.Abs(component.SignedAmount);
        var positive = component.SignedAmount > 0;
        var cogs = positive ? 0 : 1; var counter = positive ? 1 : 0;
        if (component.CompanyId != source.CompanyId || source.CompanyId != period.CompanyId
            || component.AttributionSourceEventId == Guid.Empty || component.OutputId == Guid.Empty
            || component.MovementId == Guid.Empty || component.MovementLineNumber < 1
            || component.JournalId != source.Id || component.JournalSourceId != source.Source.SourceId
            || component.PostingPurpose != source.Source.PostingPurpose || component.CurrencyId != source.CurrencyId
            || component.AttributionFingerprint is not { Length: 64 } || !component.AttributionFingerprint.All(Uri.IsHexDigit)
            || amount <= 0 || component.JournalTotal < amount || source.Lines.Count != 2
            || source.Lines[0].Debit != component.JournalTotal || source.Lines[0].Credit != 0
            || source.Lines[1].Credit != component.JournalTotal || source.Lines[1].Debit != 0
            || source.Lines[cogs].Account.AccountId != component.CogsAccountId
            || source.Lines[counter].Account.AccountId != component.CounterAccountId
            || component.CogsAccountId == component.CounterAccountId || newCogsAccountId == Guid.Empty
            || newCogsAccountId == component.CogsAccountId || newCogsAccountId == component.CounterAccountId)
            throw new AccountingDomainException(AccountingErrorCodes.InvalidReference,
                "An attributed correction needs the exact stored component and its actual posted aggregate accounts.");
        var previous = new AccountRef(source.CompanyId, component.CogsAccountId);
        var offset = new AccountRef(source.CompanyId, component.CounterAccountId);
        var next = new AccountRef(source.CompanyId, newCogsAccountId);
        var ledger = SourcePostingLedger.FromStored(source.CompanyId, [source], [], [], []);
        JournalLine[] reverse = positive ? [new(offset, amount, 0), new(previous, 0, amount)]
            : [new(previous, amount, 0), new(offset, 0, amount)];
        JournalLine[] replacement = positive ? [new(next, amount, 0), new(offset, 0, amount)]
            : [new(offset, amount, 0), new(next, 0, amount)];
        var reversing = ledger.Correct("accounting.invoice-line-cogs", reversingId.ToString("D"), reversingCorrectionId,
            source.Id, reversingId, "invoice-line-cogs-attributed-correction/v1", dates, reverse, period).Journal!;
        var corrected = ledger.Correct("accounting.invoice-line-cogs", replacementId.ToString("D"), replacementCorrectionId,
            source.Id, replacementId, "invoice-line-cogs-attributed-correction/v1", dates, replacement, period).Journal!;
        return (reversing, corrected);
    }
}
