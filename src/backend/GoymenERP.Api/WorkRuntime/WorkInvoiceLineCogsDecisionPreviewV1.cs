using System.Globalization;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.WorkRuntime;

// Authorized human transport only. Native JSON/hash, decimals, ratio text and private receipt are unchanged.
public sealed record WorkInvoiceLineCogsPreviewV1(WorkInvoiceLineCogsViewV1 Proposal, WorkInvoiceLineCogsSourceV1 Source,
    string CurrentLineVersion, string SourceFactsDigest, string PreviewDigest)
{
    internal static WorkInvoiceLineCogsPreviewV1 FromNative(InvoiceLineCogsDecisionDetailV1 detail) =>
        new(WorkInvoiceLineCogsViewV1.FromNative(detail.Proposal), WorkInvoiceLineCogsSourceV1.FromNative(detail.Source),
            detail.CurrentLineVersion.ToString(CultureInfo.InvariantCulture), detail.SourceFactsDigest, detail.PreviewDigest);
}

public sealed record WorkInvoiceLineCogsViewV1(Guid ProposalId,
    Guid CompanyId,
    Guid InvoiceId,
    Guid InvoiceLineId,
    string FinalizedVersion,
    string Version,
    string ExpectedLineVersion,
    string Status,
    InvoiceLineCogsClassificationV1 Classification,
    WorkInvoiceLineCogsAccountPlanV1 AccountPlan,
    Guid PeriodId,
    DateOnly TransactionDate,
    DateOnly AccountingDate,
    DateOnly? ValueDate,
    Guid PreparedBy,
    DateTimeOffset PreparedAtUtc,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAtUtc,
    string Reason,
    WorkInvoiceLineCogsPostedEvidenceV1? PostedEvidence,
    Guid? ReversingCorrectionJournalId,
    Guid? ReplacementCorrectionJournalId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<WorkInvoiceLineCogsAttributedEvidenceV1>? AttributedEvidence = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<InvoiceLineCogsAttributedCorrectionV1>? AttributedCorrections = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<WorkInvoiceLineCogsPurchaseAttributedEvidenceV1>? PurchaseAttributedEvidence = null)
{
    internal static WorkInvoiceLineCogsViewV1 FromNative(InvoiceLineCogsCorrectionViewV1 value) =>
        new(value.ProposalId,
            value.CompanyId,
            value.InvoiceId,
            value.InvoiceLineId,
            value.FinalizedVersion.ToString(CultureInfo.InvariantCulture),
            value.Version.ToString(CultureInfo.InvariantCulture),
            value.ExpectedLineVersion.ToString(CultureInfo.InvariantCulture),
            value.Status,
            value.Classification,
            WorkInvoiceLineCogsAccountPlanV1.FromNative(value.AccountPlan),
            value.PeriodId,
            value.TransactionDate,
            value.AccountingDate,
            value.ValueDate,
            value.PreparedBy,
            value.PreparedAtUtc,
            value.ApprovedBy,
            value.ApprovedAtUtc,
            value.Reason,
            value.PostedEvidence is { } postedEvidence ? WorkInvoiceLineCogsPostedEvidenceV1.FromNative(postedEvidence) : null,
            value.ReversingCorrectionJournalId,
            value.ReplacementCorrectionJournalId,
            value.AttributedEvidence?.Select(WorkInvoiceLineCogsAttributedEvidenceV1.FromNative).ToArray(),
            value.AttributedCorrections,
            value.PurchaseAttributedEvidence?.Select(WorkInvoiceLineCogsPurchaseAttributedEvidenceV1.FromNative).ToArray());
}

public sealed record WorkInvoiceLineCogsAccountPlanV1(Guid PackageId,
    string PackageVersion,
    string? CatalogVersion,
    string SourceVersion,
    string SourceSha256,
    InvoiceLineCogsAccountV1 Account)
{
    internal static WorkInvoiceLineCogsAccountPlanV1 FromNative(InvoiceLineCogsAccountPlanV1 value) =>
        new(value.PackageId,
            value.PackageVersion.ToString(CultureInfo.InvariantCulture),
            value.CatalogVersion?.ToString(CultureInfo.InvariantCulture),
            value.SourceVersion,
            value.SourceSha256,
            value.Account);
}

public sealed record WorkInvoiceLineCogsSourceV1(Guid CompanyId,
    Guid InvoiceId,
    Guid InvoiceLineId,
    Guid FinalizationAdmissionId,
    string FinalizedVersion,
    Guid ProductId,
    Guid ShipmentId,
    Guid DispatchNoteId,
    int DispatchNoteLineNo,
    int InvoiceLineNo,
    DateOnly InvoiceDate,
    string OfficialInvoiceNumber,
    Guid Ettn,
    string Fingerprint)
{
    internal static WorkInvoiceLineCogsSourceV1 FromNative(FinalizedInvoiceLineReferenceV1 value) =>
        new(value.CompanyId,
            value.InvoiceId,
            value.InvoiceLineId,
            value.FinalizationAdmissionId,
            value.FinalizedVersion.ToString(CultureInfo.InvariantCulture),
            value.ProductId,
            value.ShipmentId,
            value.DispatchNoteId,
            value.DispatchNoteLineNo,
            value.InvoiceLineNo,
            value.InvoiceDate,
            value.OfficialInvoiceNumber,
            value.Ettn,
            value.Fingerprint);
}

public sealed record WorkInvoiceLineCogsPostedEvidenceV1(Guid CompanyId,
    Guid InvoiceId,
    Guid InvoiceLineId,
    string FinalizedVersion,
    Guid JournalId,
    int CogsJournalLineNumber,
    int CounterJournalLineNumber,
    Guid CogsAccountId,
    Guid CounterAccountId,
    Guid CurrencyId,
    string Amount,
    string SourceFingerprint)
{
    internal static WorkInvoiceLineCogsPostedEvidenceV1 FromNative(InvoiceLineCogsPostedEvidenceV1 value) =>
        new(value.CompanyId,
            value.InvoiceId,
            value.InvoiceLineId,
            value.FinalizedVersion.ToString(CultureInfo.InvariantCulture),
            value.JournalId,
            value.CogsJournalLineNumber,
            value.CounterJournalLineNumber,
            value.CogsAccountId,
            value.CounterAccountId,
            value.CurrencyId,
            value.Amount,
            value.SourceFingerprint);
}

public sealed record WorkInvoiceLineCogsAttributedEvidenceV1(WorkInvoiceLineCogsManufacturingShareV1 Source,
    Guid CurrentJournalId,
    Guid CurrentCogsAccountId,
    Guid CounterAccountId,
    string CurrentJournalTotal)
{
    internal static WorkInvoiceLineCogsAttributedEvidenceV1 FromNative(InvoiceLineCogsAttributedEvidenceV1 value) =>
        new(WorkInvoiceLineCogsManufacturingShareV1.FromNative(value.Source),
            value.CurrentJournalId,
            value.CurrentCogsAccountId,
            value.CounterAccountId,
            value.CurrentJournalTotal);
}

public sealed record WorkInvoiceLineCogsManufacturingShareV1(Guid CompanyId,
    Guid SourceEventId,
    Guid OutputId,
    Guid GenerationId,
    string GenerationVersion,
    Guid ReceiptMovementId,
    Guid JournalId,
    Guid CurrencyId,
    Guid CogsAccountId,
    Guid CounterAccountId,
    string JournalTotal,
    string SignedAmount,
    AccountingInventoryCostRatioV1 ExactSignedAmount,
    string SignedRoundingResidue,
    Guid MoneyProfileId,
    string MoneyProfileVersion,
    int MoneyScale,
    DateOnly ProfileAccountingDate,
    WorkInvoiceLineCogsManufacturingShipmentV1 Shipment,
    string SourcePayloadSha256,
    string LineageSha256,
    string HistorySha256,
    string Fingerprint)
{
    internal static WorkInvoiceLineCogsManufacturingShareV1 FromNative(PostedManufacturingCostShareV1 value) =>
        new(value.CompanyId,
            value.SourceEventId,
            value.OutputId,
            value.GenerationId,
            value.GenerationVersion,
            value.ReceiptMovementId,
            value.JournalId,
            value.CurrencyId,
            value.CogsAccountId,
            value.CounterAccountId,
            value.JournalTotal,
            value.SignedAmount,
            value.ExactSignedAmount,
            value.SignedRoundingResidue,
            value.MoneyProfileId,
            value.MoneyProfileVersion.ToString(CultureInfo.InvariantCulture),
            value.MoneyScale,
            value.ProfileAccountingDate,
            WorkInvoiceLineCogsManufacturingShipmentV1.FromNative(value.Shipment),
            value.SourcePayloadSha256,
            value.LineageSha256,
            value.HistorySha256,
            value.Fingerprint);
}

public sealed record WorkInvoiceLineCogsManufacturingShipmentV1(Guid ShipmentId,
    Guid MovementId,
    int MovementLineNumber,
    Guid LoadingInstructionLineId,
    Guid ProductId,
    Guid LabelId,
    decimal PhysicalQuantityKg,
    AccountingInventoryCostRatioV1 RootShare,
    AccountingInventoryCostRatioV1 NetRootQuantityKg,
    DateTimeOffset RecordedAtUtc,
    string? ProvenOriginalSequence,
    bool HasReturns,
    string PhysicalProofJson,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ManufacturingAttributionReturnV1>? Returns = null)
{
    internal static WorkInvoiceLineCogsManufacturingShipmentV1 FromNative(ManufacturingAttributionShipmentV1 value) =>
        new(value.ShipmentId,
            value.MovementId,
            value.MovementLineNumber,
            value.LoadingInstructionLineId,
            value.ProductId,
            value.LabelId,
            value.PhysicalQuantityKg,
            value.RootShare,
            value.NetRootQuantityKg,
            value.RecordedAtUtc,
            value.ProvenOriginalSequence?.ToString(CultureInfo.InvariantCulture),
            value.HasReturns,
            value.PhysicalProofJson,
            value.Returns);
}

public sealed record WorkInvoiceLineCogsPurchaseAttributedEvidenceV1(WorkInvoiceLineCogsPurchaseShareV1 Source,
    Guid? CurrentJournalId,
    Guid CurrentCogsAccountId,
    Guid CounterAccountId,
    string CurrentJournalTotal)
{
    internal static WorkInvoiceLineCogsPurchaseAttributedEvidenceV1 FromNative(InvoiceLineCogsPurchaseAttributedEvidenceV1 value) =>
        new(WorkInvoiceLineCogsPurchaseShareV1.FromNative(value.Source),
            value.CurrentJournalId,
            value.CurrentCogsAccountId,
            value.CounterAccountId,
            value.CurrentJournalTotal);
}

public sealed record WorkInvoiceLineCogsPurchaseShareV1(WorkInvoiceLineCogsPurchaseApprovalV1 Approval,
    Guid ShipmentId,
    Guid MovementId,
    int MovementLineNumber,
    Guid ProductId,
    decimal PhysicalQuantityKg,
    string SignedAmount,
    AccountingInventoryCostRatioV1 ExactSignedAmount,
    AccountingInventoryCostRatioV1 ExactSignedResidue,
    IReadOnlyList<WorkInvoiceLineCogsPurchaseCandidateV1> Candidates,
    string Fingerprint)
{
    internal static WorkInvoiceLineCogsPurchaseShareV1 FromNative(PostedPurchaseCostShareV1 value) =>
        new(WorkInvoiceLineCogsPurchaseApprovalV1.FromNative(value.Approval),
            value.ShipmentId,
            value.MovementId,
            value.MovementLineNumber,
            value.ProductId,
            value.PhysicalQuantityKg,
            value.SignedAmount,
            value.ExactSignedAmount,
            value.ExactSignedResidue,
            value.Candidates.Select(WorkInvoiceLineCogsPurchaseCandidateV1.FromNative).ToArray(),
            value.Fingerprint);
}

public sealed record WorkInvoiceLineCogsPurchaseApprovalV1(Guid CompanyId,
    Guid SourceEventId,
    Guid InvoiceLineId,
    Guid ProposalId,
    string ProposalVersion,
    Guid? JournalId,
    string Status,
    string SignedAmount,
    Guid CurrencyId,
    Guid MoneyProfileId,
    string MoneyProfileVersion,
    int MoneyScale,
    Guid CogsAccountId,
    Guid CounterAccountId,
    DateOnly AccountingDate,
    Guid PreparedBy,
    Guid ApprovedBy,
    DateTimeOffset ApprovedAtUtc,
    WorkInvoiceLineCogsRecordedRoundingV1 RecordedRounding,
    string SourceProofSha256,
    string Fingerprint)
{
    internal static WorkInvoiceLineCogsPurchaseApprovalV1 FromNative(PurchaseFirstCostApprovalV1 value) =>
        new(value.CompanyId,
            value.SourceEventId,
            value.InvoiceLineId,
            value.ProposalId,
            value.ProposalVersion.ToString(CultureInfo.InvariantCulture),
            value.JournalId,
            value.Status,
            value.SignedAmount,
            value.CurrencyId,
            value.MoneyProfileId,
            value.MoneyProfileVersion.ToString(CultureInfo.InvariantCulture),
            value.MoneyScale,
            value.CogsAccountId,
            value.CounterAccountId,
            value.AccountingDate,
            value.PreparedBy,
            value.ApprovedBy,
            value.ApprovedAtUtc,
            WorkInvoiceLineCogsRecordedRoundingV1.FromNative(value.RecordedRounding),
            value.SourceProofSha256,
            value.Fingerprint);
}

public sealed record WorkInvoiceLineCogsRecordedRoundingV1(Guid CurrencyId,
    Guid MoneyProfileId,
    string MoneyProfileVersion,
    int MoneyScale,
    AccountingInventoryCostRoundingV1 Rounding)
{
    internal static WorkInvoiceLineCogsRecordedRoundingV1 FromNative(AccountingInventoryCostRecordedRoundingV1 value) =>
        new(value.CurrencyId,
            value.MoneyProfileId,
            value.MoneyProfileVersion.ToString(CultureInfo.InvariantCulture),
            value.MoneyScale,
            value.Rounding);
}

public sealed record WorkInvoiceLineCogsPurchaseCandidateV1(Guid CompanyId,
    Guid SourceEventId,
    Guid InvoiceLineId,
    Guid AcceptanceId,
    Guid ReceiptMovementId,
    Guid RootLabelId,
    Guid ProductId,
    AccountingInventoryCostRatioV1 InvoicedRootShare,
    AccountingInventoryCostRatioV1 InvoicedNetQuantityKg,
    AccountingInventoryCostRatioV1 ExactTlAmount,
    WorkInvoiceLineCogsPurchaseShipmentV1 Shipment,
    string SourceProofSha256,
    string PhysicalSourceSha256,
    string Fingerprint)
{
    internal static WorkInvoiceLineCogsPurchaseCandidateV1 FromNative(PurchaseFirstCostCandidateV1 value) =>
        new(value.CompanyId,
            value.SourceEventId,
            value.InvoiceLineId,
            value.AcceptanceId,
            value.ReceiptMovementId,
            value.RootLabelId,
            value.ProductId,
            value.InvoicedRootShare,
            value.InvoicedNetQuantityKg,
            value.ExactTlAmount,
            WorkInvoiceLineCogsPurchaseShipmentV1.FromNative(value.Shipment),
            value.SourceProofSha256,
            value.PhysicalSourceSha256,
            value.Fingerprint);
}

public sealed record WorkInvoiceLineCogsPurchaseShipmentV1(Guid ShipmentId,
    Guid MovementId,
    int MovementLineNumber,
    Guid LoadingInstructionLineId,
    Guid ProductId,
    Guid LabelId,
    decimal PhysicalQuantityKg,
    AccountingInventoryCostRatioV1 RootShare,
    AccountingInventoryCostRatioV1 NetRootQuantityKg,
    DateTimeOffset RecordedAtUtc,
    string? ProvenOriginalSequence,
    IReadOnlyList<PurchaseFirstCostReturnV1> Returns,
    string PhysicalProofJson)
{
    internal static WorkInvoiceLineCogsPurchaseShipmentV1 FromNative(PurchaseFirstCostShipmentV1 value) =>
        new(value.ShipmentId,
            value.MovementId,
            value.MovementLineNumber,
            value.LoadingInstructionLineId,
            value.ProductId,
            value.LabelId,
            value.PhysicalQuantityKg,
            value.RootShare,
            value.NetRootQuantityKg,
            value.RecordedAtUtc,
            value.ProvenOriginalSequence?.ToString(CultureInfo.InvariantCulture),
            value.Returns,
            value.PhysicalProofJson);
}
