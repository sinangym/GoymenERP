using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

// F018 plan §L3 (FNS-D27 (2), FNS-D31 (4), FNS-D58): the read behind the Finance panel of the Cari card. Finance owns the
// credit limit and the collateral (SAT-D51); the Cari card only shows this answer and sends edits to the Finance
// endpoints. One company account (cari hesap) of the server-resolved company; nothing is written (no attempt, audit
// or version effect).
//
// Every figure is per currency as exact decimal text (never converted, never rounded) and is computed here, by the same
// backend calculators the Sales risk port and the collateral master use — never on the screen:
// - CreditLimit: the account's effective (approved) limit; NotSet when none (never zero, never unlimited).
// - OpenReceivables: the Sales risk port's open receivables input (open receivable items less unallocated payments).
// - HeldCollateral ("mevcut teminat"): the held received collaterals in force on the read date, at their amount.
// - WeightedCollateral ("sayılan teminat"): the same at the Settings rate of their type (FNS-D31 (3)).
// - CollateralUtilization ("teminat doluluğu"): the open receivables taken from the weighted collateral — per currency
//   the smaller of the two. When one currency has collateral left over while another has receivables uncovered, the
//   answer would need a cross-currency rule that is not decided: Unavailable (NEEDS-SINAN), never a guess.
// - DocumentaryRisk ("evraklı risk", FNS-D58): uncollected customer cheques/notes; a breakdown, never added to a risk.
//
// The gross risk ("riskteki tutar") and the limit headroom are NOT here: they are the Sales risk calculator's answer
// (F018 §L3). A figure the caller may not read is Withheld (no amount disclosed); one Finance cannot read exactly is
// Unavailable ("hesaplanamadı") — never a partial sum.

[JsonConverter(typeof(JsonStringEnumConverter<PartyCreditPanelFigureState>))]
public enum PartyCreditPanelFigureState { Available, Unavailable, NotSet, Withheld }

public sealed record PartyCreditPanelAmountView(Guid CurrencyId, string Amount);

// Available: Amounts is complete (an empty list is a recorded zero). Otherwise Amounts is empty and ReasonCode says why.
public sealed record PartyCreditPanelFigureView(
    PartyCreditPanelFigureState State,
    IReadOnlyList<PartyCreditPanelAmountView> Amounts,
    string? ReasonCode);

public sealed record PartyCreditPanelView(
    Guid CompanyId,
    Guid PartyId,
    Guid CompanyAccountId,
    DateOnly AsOf,
    PartyCreditPanelFigureView CreditLimit,
    PartyCreditPanelFigureView OpenReceivables,
    PartyCreditPanelFigureView HeldCollateral,
    PartyCreditPanelFigureView WeightedCollateral,
    PartyCreditPanelFigureView CollateralUtilization,
    PartyCreditPanelFigureView DocumentaryRisk);

// Under the credit limit read or the collateral read authority (either opens the panel; a figure under the other one
// is Withheld). The party must be an account of the server-resolved company the caller can see.
public interface IFinancePartyCreditPanelReader
{
    Task<ReadResult<PartyCreditPanelView>> GetAsync(FinanceContext context, Guid partyId, Guid companyAccountId,
        CancellationToken cancellationToken);
}
