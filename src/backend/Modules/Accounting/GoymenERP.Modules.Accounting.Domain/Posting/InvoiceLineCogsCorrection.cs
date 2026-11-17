using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Periods;

namespace GoymenERP.Modules.Accounting.Domain.Posting;

/// <summary>SAT-D422/423: two balanced component corrections preserve the original journal and every commercial fact.</summary>
public static partial class InvoiceLineCogsCorrection
{
    public static (Journal Reversing, Journal Replacement) Create(Journal source, decimal frozenAmount,
        Guid previousCogsAccountId, Guid counterAccountId, Guid newCogsAccountId, AccountingPeriod period,
        PostingDates dates, Guid reversingId, Guid replacementId, Guid reversingCorrectionId, Guid replacementCorrectionId)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(period);
        if (frozenAmount <= 0 || previousCogsAccountId == counterAccountId || newCogsAccountId == counterAccountId
            || newCogsAccountId == previousCogsAccountId || source.CompanyId != period.CompanyId
            || source.Lines.Count != 2 || source.Lines[0].Account.AccountId != previousCogsAccountId
            || source.Lines[0].Debit != frozenAmount || source.Lines[0].Credit != 0
            || source.Lines[1].Account.AccountId != counterAccountId || source.Lines[1].Credit != frozenAmount || source.Lines[1].Debit != 0)
            throw new AccountingDomainException(AccountingErrorCodes.InvalidReference, "A line correction needs one exact posted component and another actual COGS account.");
        var previous = new AccountRef(source.CompanyId, previousCogsAccountId);
        var counter = new AccountRef(source.CompanyId, counterAccountId);
        var next = new AccountRef(source.CompanyId, newCogsAccountId);
        var ledger = SourcePostingLedger.FromStored(source.CompanyId, [source], [], [], []);
        var reversing = ledger.Correct("accounting.invoice-line-cogs", reversingId.ToString("D"), reversingCorrectionId,
            source.Id, reversingId, "invoice-line-cogs-correction/v1", dates,
            [new(counter, frozenAmount, 0), new(previous, 0, frozenAmount)], period).Journal!;
        var replacement = ledger.Correct("accounting.invoice-line-cogs", replacementId.ToString("D"), replacementCorrectionId,
            source.Id, replacementId, "invoice-line-cogs-correction/v1", dates,
            [new(next, frozenAmount, 0), new(counter, 0, frozenAmount)], period).Journal!;
        return (reversing, replacement);
    }
}
