using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

public enum CreditPlanNatureV1 { Installments, SourceMovements }
public enum CreditCashKindV1 { Spot, Revolving, Installment, Overdraft, ForeignCurrencyIndexed, Export, CreditCard }
public enum CreditNonCashKindV1 { Guarantee, LetterOfCredit, Acceptance, Aval }
public enum CreditInstrumentKindV1 { ChequeCollateral, NoteCollateral, Factoring }
public enum CreditChargeTreatmentV1 { IncludedInInstallment, CollectedSeparately }

/// <summary>Values explicitly shown by the source document. The reference is evidence, never a fabricated external ID.</summary>
public sealed record CreditSourceDocumentV1(string Reference, DateOnly DocumentDate, string Revision);

// Appended source fields omit null so existing v1 profiles retain their original JSON and command hashes.
public sealed record CreditCashProfileV1(CreditCashKindV1 Kind, DateOnly? MaturityDate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? IndexCurrencyId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExportCommitmentReference = null);
public sealed record CreditNonCashProfileV1(CreditNonCashKindV1 Kind, string BeneficiaryReference,
    bool IsIndefinite, DateOnly? ExpiryDate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RiskAmount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CreditNonCashCommissionV1? Commission = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RelatedCommercialDocumentReference = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CreditNonCashReturnV1? ReturnEvidence = null);
public sealed record CreditInstrumentProfileV1(CreditInstrumentKindV1 Kind, bool? WithRecourse,
    IReadOnlyList<Guid> InstrumentIds, IReadOnlyList<string> ReceivableDocumentReferences,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? MaturityDate = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CreditInstrumentDeductionsV1? Deductions = null);
public sealed record CreditLeasingProfileV1(string AssetReference, DateOnly LeaseStartDate, DateOnly LeaseEndDate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? ContractDate = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PurchaseOptionAmount = null);

/// <summary>Documented commission only. Period is source text, not an accrual schedule or a rate.</summary>
public sealed record CreditNonCashCommissionV1(string Amount, DateOnly CommissionDate, CreditSourceDocumentV1 SourceDocument,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Period = null);

/// <summary>Return/expiry evidence only; recording it does not release risk, pay money or post a journal.</summary>
public sealed record CreditNonCashReturnV1(DateOnly ReturnedOn, CreditSourceDocumentV1 SourceDocument);

/// <summary>Bank-supplied deductions in the profile's source document and currency; never derived from rates or totals.</summary>
public sealed record CreditInstrumentDeductionsV1(string Interest, string Commission, string Bsmv);

/// <summary>
/// Four explicit source-backed profiles. A plan is either imported installments or source-documented variable movements;
/// no synthetic installment is inserted for a contingent or revolving facility.
/// </summary>
public sealed record CreditAccountProfileV1(Guid LenderPartyId, Guid LenderCompanyPartyAccountId,
    CreditSourceDocumentV1 SourceDocument, CreditPlanNatureV1 PlanNature,
    CreditCashProfileV1? Cash = null, CreditNonCashProfileV1? NonCash = null,
    CreditInstrumentProfileV1? InstrumentAndFactoring = null, CreditLeasingProfileV1? Leasing = null);

/// <summary>Commission/fee/tax amounts supplied by the bank. The explicit treatment alone decides whether it enters the installment sum.</summary>
public sealed record CreditPlanChargeInputV1(string Description, string Amount, CreditChargeTreatmentV1 Treatment,
    string SourceDocumentReference);

public sealed record CreditPlanChargeTotalsV1(string IncludedInInstallment, string CollectedSeparately);

public sealed record CreditPlanToleranceSnapshotV1(Guid ProfileId, long ProfileVersion, int CurrencyScale,
    string SmallestCurrencyUnit);
