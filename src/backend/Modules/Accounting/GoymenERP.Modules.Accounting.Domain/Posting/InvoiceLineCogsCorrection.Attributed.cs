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

/// <summary>A real purchase proposal and source invoice line, never a fabricated manufacturing output.</summary>
public sealed record PurchaseAttributedPostedJournalComponent(Guid CompanyId, Guid SourceEventId, Guid InvoiceLineId,
    Guid ProposalId, Guid MovementId, int MovementLineNumber, Guid JournalId, string PostingPurpose,
    Guid CurrencyId, decimal JournalTotal, decimal Amount, Guid CogsAccountId, Guid CounterAccountId,
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
        if (component.AttributionSourceEventId == Guid.Empty || component.OutputId == Guid.Empty)
            throw InvalidComponent();
        return CreateProvedComponent(source, component.CompanyId, component.MovementId, component.MovementLineNumber,
            component.JournalId, component.JournalSourceId, component.PostingPurpose, component.CurrencyId,
            component.JournalTotal, component.SignedAmount, component.CogsAccountId, component.CounterAccountId,
            component.AttributionFingerprint, newCogsAccountId, period, dates, reversingId, replacementId,
            reversingCorrectionId, replacementCorrectionId);
    }

    public static (Journal Reversing, Journal Replacement) CreatePurchaseAttributed(Journal source,
        PurchaseAttributedPostedJournalComponent component, Guid newCogsAccountId, AccountingPeriod period,
        PostingDates dates, Guid reversingId, Guid replacementId, Guid reversingCorrectionId, Guid replacementCorrectionId)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(component); ArgumentNullException.ThrowIfNull(period);
        if (component.SourceEventId == Guid.Empty || component.InvoiceLineId == Guid.Empty || component.ProposalId == Guid.Empty
            || component.Amount <= 0 || component.PostingPurpose != "purchase-invoice-cost-difference")
            throw InvalidComponent();
        return CreateProvedComponent(source, component.CompanyId, component.MovementId, component.MovementLineNumber,
            component.JournalId, component.ProposalId, component.PostingPurpose, component.CurrencyId,
            component.JournalTotal, component.Amount, component.CogsAccountId, component.CounterAccountId,
            component.AttributionFingerprint, newCogsAccountId, period, dates, reversingId, replacementId,
            reversingCorrectionId, replacementCorrectionId);
    }

    private static (Journal Reversing, Journal Replacement) CreateProvedComponent(Journal source,
        Guid companyId, Guid movementId, int movementLineNumber, Guid journalId, Guid journalSourceId,
        string postingPurpose, Guid currencyId, decimal journalTotal, decimal signedAmount,
        Guid cogsAccountId, Guid counterAccountId, string fingerprint, Guid newCogsAccountId, AccountingPeriod period,
        PostingDates dates, Guid reversingId, Guid replacementId, Guid reversingCorrectionId, Guid replacementCorrectionId)
    {
        var amount = Math.Abs(signedAmount);
        var positive = signedAmount > 0;
        var cogs = positive ? 0 : 1; var counter = positive ? 1 : 0;
        if (companyId != source.CompanyId || source.CompanyId != period.CompanyId
            || movementId == Guid.Empty || movementLineNumber < 1 || journalId != source.Id || journalSourceId != source.Source.SourceId
            || postingPurpose != source.Source.PostingPurpose || currencyId != source.CurrencyId
            || fingerprint is not { Length: 64 } || !fingerprint.All(Uri.IsHexDigit)
            || amount <= 0 || journalTotal < amount || source.Lines.Count != 2
            || source.Lines[0].Debit != journalTotal || source.Lines[0].Credit != 0
            || source.Lines[1].Credit != journalTotal || source.Lines[1].Debit != 0
            || source.Lines[cogs].Account.AccountId != cogsAccountId || source.Lines[counter].Account.AccountId != counterAccountId
            || cogsAccountId == counterAccountId || newCogsAccountId == Guid.Empty
            || newCogsAccountId == cogsAccountId || newCogsAccountId == counterAccountId) throw InvalidComponent();
        var previous = new AccountRef(source.CompanyId, cogsAccountId);
        var offset = new AccountRef(source.CompanyId, counterAccountId);
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

    private static AccountingDomainException InvalidComponent() => new(AccountingErrorCodes.InvalidReference,
        "An attributed correction needs the exact stored component and its actual posted aggregate accounts.");
}
