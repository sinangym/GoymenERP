using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

// API-85b (F018 plan §L2; FNS-D57, FNS-D31, FNS-D58): the Finance customer collateral (müşteri teminatı) master.
// One shared collateral store for the received (API-85b) and the given (API-84, later) direction; every record names
// its direction and its source. A collateral is held for one company account (cari hesap) of the server-resolved
// company, in ONE currency (a Settings money currency candidate), as exact decimal text (> 0, never rounded, never
// converted). It moves no money and writes no ledger.
//
// FNS-D57: a holder of the record authority records, changes or releases a collateral in ONE step, effective at once
// (no proposer/approver, no threshold); every change is audited with its old and new values (who, when, type, amount,
// currency). History is append-only: a change or a release is a new revision that supersedes the previous one; a
// release is a reverse record (the previous values with Kind = Release), never a delete.
//
// FNS-D31: the collateral TYPE and its RATE are owned by Settings (a type catalog, the rate per type, 0–100, exact,
// starting at 100%). Finance reads them through IFinanceCollateralTypeCatalogReader and never hard-codes the type list
// or a rate; a cheque or a promissory note is never a collateral type (refused here as well as by Settings). While no
// Settings catalog is registered, the default reader answers Unavailable and recording and the weighted figure fail
// closed. The rate is applied at READ time (type x currency) in the Sales risk port's Collateral figure.
//
// FNS-D58: the documentary risk (evraklı risk) of an account is every customer cheque/note of the Finance portfolio not
// Collected and not ReturnedToCustomer (endorsed or at the bank but uncollected included); an unknown status is
// "cannot be calculated". It is shown SEPARATELY and is not added to the gross risk.
//
// NOT built: a haircut on the rate (OPEN-010 stays open), the given direction's commands (API-84), and a Sales risk port
// field for the documentary risk (a cross-module contract change for the L3 read endpoint).

[JsonConverter(typeof(JsonStringEnumConverter<PartyCollateralDirection>))]
public enum PartyCollateralDirection { Received, Given }

[JsonConverter(typeof(JsonStringEnumConverter<PartyCollateralSource>))]
public enum PartyCollateralSource { FinanceManualEntry }

[JsonConverter(typeof(JsonStringEnumConverter<PartyCollateralKind>))]
public enum PartyCollateralKind { Record, Change, Release }

[JsonConverter(typeof(JsonStringEnumConverter<PartyCollateralStatus>))]
public enum PartyCollateralStatus { Effective, Superseded }

// Available: the amounts are complete. Unavailable: cannot be calculated (ReasonCode says why). Withheld: the caller may
// not see these amounts (the documentary risk follows the customer instrument amount authority).
[JsonConverter(typeof(JsonStringEnumConverter<PartyCollateralFigureState>))]
public enum PartyCollateralFigureState { Available, Unavailable, Withheld }

// A new received collateral. ValidUntil null: open-ended. DocumentReference: the paper's reference (optional).
public sealed record PartyCollateralRecordRequest(Guid PartyId, Guid CompanyAccountId, Guid CollateralTypeId,
    Guid CurrencyId, string Amount, DateOnly ValidFrom, DateOnly? ValidUntil, string? DocumentReference,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? BankInstitutionId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BankSourceVersion = null);

// A change names the collateral and its current revision; the new values replace the old ones as a new revision.
public sealed record PartyCollateralChangeRequest(Guid CollateralId, string ExpectedRevision, Guid CollateralTypeId,
    Guid CurrencyId, string Amount, DateOnly ValidFrom, DateOnly? ValidUntil, string? DocumentReference,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? BankInstitutionId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BankSourceVersion = null);

// A release names the collateral, its current revision and a reason.
public sealed record PartyCollateralReleaseRequest(Guid CollateralId, string ExpectedRevision, string Reason);

// HTTP bodies: the collateral is the route's.
public sealed record PartyCollateralChangeBody(string ExpectedRevision, Guid CollateralTypeId, Guid CurrencyId,
    string Amount, DateOnly ValidFrom, DateOnly? ValidUntil, string? DocumentReference,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? BankInstitutionId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BankSourceVersion = null);

public sealed record PartyCollateralReleaseBody(string ExpectedRevision, string Reason);

// One revision of a collateral. CollateralTypeVersion: the Settings type version the revision was recorded against.
// No actor id is disclosed.
public sealed record PartyCollateralRecordView(
    Guid Id,
    Guid CompanyId,
    Guid CollateralId,
    string Revision,
    PartyCollateralDirection Direction,
    PartyCollateralSource Source,
    Guid PartyId,
    Guid CompanyAccountId,
    PartyCollateralKind Kind,
    Guid CollateralTypeId,
    string CollateralTypeVersion,
    Guid CurrencyId,
    string Amount,
    DateOnly ValidFrom,
    DateOnly? ValidUntil,
    string? DocumentReference,
    string? ReleaseReason,
    PartyCollateralStatus Status,
    DateTimeOffset RecordedAtUtc,
    DateTimeOffset? SupersededAtUtc,
    string Version,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FinanceCollateralBankProofV1? BankInstitution = null);

public sealed record PartyCollateralAmountView(Guid CurrencyId, string Amount);

// Per currency; amounts of different currencies are never added.
public sealed record PartyCollateralFigureView(PartyCollateralFigureState State,
    IReadOnlyList<PartyCollateralAmountView> Amounts, string? ReasonCode);

// The account's held (not released) received collaterals, the weighted collateral (amount x the Settings type rate,
// for the collaterals valid on the read date; the Sales risk port's Collateral figure) and the documentary risk.
public sealed record PartyCollateralAccountView(
    Guid CompanyId,
    Guid CompanyAccountId,
    IReadOnlyList<PartyCollateralRecordView> Held,
    PartyCollateralFigureView WeightedCollateral,
    PartyCollateralFigureView DocumentaryRisk);

public sealed record PartyCollateralHistoryPage(Guid CompanyId, Guid CompanyAccountId,
    IReadOnlyList<PartyCollateralRecordView> Items);

public sealed record PartyCollateralCommandResult(CommandOutcome Outcome, PartyCollateralRecordView? Resource,
    bool IsReplay, string OriginalCorrelationId, IReadOnlyList<ValidationIssue> Issues);

// Settings' active collateral types as given (cheque/note types never listed); an empty list is "no candidates".
public sealed record PartyCollateralTypeCandidate(Guid CollateralTypeId, string Code, string Name, string RatePercent,
    string Version);

public sealed record PartyCollateralTypeCandidates(Guid CompanyId, IReadOnlyList<PartyCollateralTypeCandidate> Items);

public sealed record PartyCollateralCurrencyCandidate(Guid CurrencyId, string Code, string DisplayLabel);

public sealed record PartyCollateralCurrencyCandidates(Guid CompanyId,
    IReadOnlyList<PartyCollateralCurrencyCandidate> Items);

// Every command checks fresh company/action authority, including a replay.
public interface IFinancePartyCollateralMaster
{
    // FNS-D57: record authority; effective at once.
    Task<PartyCollateralCommandResult> RecordAsync(FinanceContext context, CommandMetadata metadata,
        PartyCollateralRecordRequest request, CancellationToken cancellationToken);

    Task<PartyCollateralCommandResult> ChangeAsync(FinanceContext context, CommandMetadata metadata,
        PartyCollateralChangeRequest request, CancellationToken cancellationToken);

    Task<PartyCollateralCommandResult> ReleaseAsync(FinanceContext context, CommandMetadata metadata,
        PartyCollateralReleaseRequest request, CancellationToken cancellationToken);

    // Under the read authority.
    Task<ReadResult<PartyCollateralAccountView>> GetAccountAsync(FinanceContext context, Guid companyAccountId,
        CancellationToken cancellationToken);

    Task<ReadResult<PartyCollateralHistoryPage>> HistoryAsync(FinanceContext context, Guid companyAccountId,
        CancellationToken cancellationToken);

    // Under the record authority.
    Task<ReadResult<PartyCollateralTypeCandidates>> TypeCandidatesAsync(FinanceContext context,
        CancellationToken cancellationToken);

    Task<ReadResult<PartyCollateralCurrencyCandidates>> CurrencyCandidatesAsync(FinanceContext context,
        CancellationToken cancellationToken);
}

// ---- The consumer side of the Settings collateral type catalog (FNS-D31; owner: Settings) ----

[JsonConverter(typeof(JsonStringEnumConverter<FinanceCollateralTypeCatalogState>))]
public enum FinanceCollateralTypeCatalogState { Unavailable, Available }

// One Settings collateral type as of a date. RatePercent: exact decimal text, 0–100 (Finance re-checks the range and
// refuses anything else as unavailable). IsNegotiableInstrument: Settings marks a cheque/note class (FNS-D31(2)); Finance
// refuses such a type even if listed. Version: the type's catalog version (changes when the rate changes).
public sealed record FinanceCollateralTypeCatalogItem(Guid CollateralTypeId, string Code, string Name, bool IsActive,
    string RatePercent, string Version, bool IsNegotiableInstrument);

public sealed record FinanceCollateralTypeCatalogAnswer(FinanceCollateralTypeCatalogState State,
    IReadOnlyList<FinanceCollateralTypeCatalogItem> Items)
{
    public static FinanceCollateralTypeCatalogAnswer NotAvailable { get; } =
        new(FinanceCollateralTypeCatalogState.Unavailable, []);
}

// Implemented by a Settings adapter (the draft ISettingsCollateralTypeCatalogV1). Until one is registered the Finance
// default answers Unavailable: no type is assumed, no rate is assumed.
public interface IFinanceCollateralTypeCatalogReader
{
    Task<FinanceCollateralTypeCatalogAnswer> ListAsync(Guid companyId, DateOnly asOf,
        CancellationToken cancellationToken);
}
