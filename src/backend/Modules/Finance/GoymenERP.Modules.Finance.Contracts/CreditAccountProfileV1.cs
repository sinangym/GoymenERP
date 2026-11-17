using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

public enum CreditPlanNatureV1 { Installments, SourceMovements }
public enum CreditCashKindV1 { Spot, Revolving, Installment, Overdraft, ForeignCurrencyIndexed, Export, CreditCard }
public enum CreditNonCashKindV1 { Guarantee, LetterOfCredit, Acceptance, Aval }
public enum CreditInstrumentKindV1 { ChequeCollateral, NoteCollateral, Factoring }
public enum CreditChargeTreatmentV1 { IncludedInInstallment, CollectedSeparately }

/// <summary>Values explicitly shown by the source document. The reference is evidence, never a fabricated external ID.</summary>
public sealed record CreditSourceDocumentV1(string Reference, DateOnly DocumentDate, string Revision);

public sealed record CreditCashProfileV1(CreditCashKindV1 Kind, DateOnly? MaturityDate);
public sealed record CreditNonCashProfileV1(CreditNonCashKindV1 Kind, string BeneficiaryReference,
    bool IsIndefinite, DateOnly? ExpiryDate);
public sealed record CreditInstrumentProfileV1(CreditInstrumentKindV1 Kind, bool? WithRecourse,
    IReadOnlyList<Guid> InstrumentIds, IReadOnlyList<string> ReceivableDocumentReferences);
public sealed record CreditLeasingProfileV1(string AssetReference, DateOnly LeaseStartDate, DateOnly LeaseEndDate);

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
