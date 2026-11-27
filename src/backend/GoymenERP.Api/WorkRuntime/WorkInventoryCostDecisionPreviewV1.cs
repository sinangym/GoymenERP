using System.Globalization;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.WorkRuntime;

// Authorized human whitelist. Owner amounts/ratios are copied, never calculated; opaque evidence stays server-side.
public sealed record WorkInventoryCostPreviewV1(WorkInventoryCostViewV1 Proposal, DateTimeOffset PreparedRecordedAtUtc)
{
    internal static WorkInventoryCostPreviewV1 FromNative(AccountingInventoryCostDecisionDetailV1 value) =>
        new(WorkInventoryCostViewV1.FromNative(value.Proposal), value.PreparedRecordedAtUtc);
}

public sealed record WorkInventoryCostViewV1(Guid ProposalId,
    Guid CompanyId,
    Guid EventId,
    AccountingInventoryCostKindV1 Kind,
    Guid InvoiceId,
    string InvoiceVersion,
    Guid AdmissionId,
    DateOnly InvoiceDate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OfficialInvoiceNumber,
    WorkInventoryCostComponentV1 Component,
    string Version,
    string Status,
    DateTimeOffset SourceOccurredAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorkInventoryCostPreparationV1? Preparation,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ApprovedBy,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? ApprovedAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? JournalId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? CancellationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AccountingJournalReversalProposalViewV1? ReversalProposal,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? CurrentPostedJournalId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorkInventoryCostCancellationV1? ComponentCancellation,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorkInventoryCostStaleClosureV1? StaleClosure,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorkInventoryCostRenewalV1? RenewedFrom,
    string ExactTlAmount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UnpostedTlAmountText)
{
    internal static WorkInventoryCostViewV1 FromNative(AccountingInventoryCostProposalV1 value) =>
        new(value.ProposalId,
            value.CompanyId,
            value.EventId,
            value.Kind,
            value.InvoiceId,
            value.InvoiceVersion.ToString(CultureInfo.InvariantCulture),
            value.AdmissionId,
            value.InvoiceDate,
            value.OfficialInvoiceNumber,
            WorkInventoryCostComponentV1.FromNative(value.Component),
            value.Version.ToString(CultureInfo.InvariantCulture),
            value.Status,
            value.SourceOccurredAtUtc,
            value.Preparation is { } preparation ? WorkInventoryCostPreparationV1.FromNative(preparation) : null,
            value.ApprovedBy,
            value.ApprovedAtUtc,
            value.JournalId,
            value.CancellationId,
            value.ReversalProposal,
            value.CurrentPostedJournalId,
            value.ComponentCancellation is { } componentCancellation ? WorkInventoryCostCancellationV1.FromNative(componentCancellation) : null,
            value.StaleClosure is { } staleClosure ? WorkInventoryCostStaleClosureV1.FromNative(staleClosure) : null,
            value.RenewedFrom is { } renewedFrom ? WorkInventoryCostRenewalV1.FromNative(renewedFrom) : null,
            value.ExactTlAmount,
            value.UnpostedTlAmountText);
}

public sealed record WorkInventoryCostComponentV1(string ComponentKey,
    Guid ProductId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? InvoiceLineId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? InvoiceLineNumber,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? LocationId,
    AccountingInventoryCostRatioV1 SignedTlAmount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<AccountingSalesPhysicalCostClaimV1>? SalesPhysicalClaims,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<WorkInventoryCostManufacturingShareV1>? AlreadyPostedComponents,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorkInventoryCostRecordedRoundingV1? RecordedRounding,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AccountingInventoryCostRatioV1? UnpostedTlAmount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<WorkInventoryCostPurchaseShareV1>? AlreadyPostedPurchaseComponents,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorkInventoryCostClaimSuccessionV1? ClaimSuccession)
{
    internal static WorkInventoryCostComponentV1 FromNative(AccountingInventoryCostComponentV1 value) =>
        new(value.ComponentKey,
            value.ProductId,
            value.InvoiceLineId,
            value.InvoiceLineNumber,
            value.LocationId,
            value.SignedTlAmount,
            value.SalesPhysicalClaims,
            value.AlreadyPostedComponents?.Select(WorkInventoryCostManufacturingShareV1.FromNative).ToArray(),
            value.RecordedRounding is { } recordedRounding ? WorkInventoryCostRecordedRoundingV1.FromNative(recordedRounding) : null,
            value.UnpostedTlAmount,
            value.AlreadyPostedPurchaseComponents?.Select(WorkInventoryCostPurchaseShareV1.FromNative).ToArray(),
            value.ClaimSuccession is { } claimSuccession ? WorkInventoryCostClaimSuccessionV1.FromNative(claimSuccession) : null);
}

public sealed record WorkInventoryCostPreparationV1(Guid PeriodId,
    DateOnly AccountingDate,
    DateOnly TransactionDate,
    Guid CurrencyId,
    string SignedAmount,
    Guid MoneyProfileId,
    string MoneyProfileVersion,
    int MoneyScale,
    WorkInventoryCostClassificationV1 Classification,
    InvoiceLineCogsAccountV1 CounterAccount,
    Guid AccountPlanPackageId,
    string AccountPlanPackageVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AccountPlanCatalogVersion,
    string AccountPlanSourceVersion,
    Guid PreparedBy,
    DateTimeOffset PreparedAtUtc,
    string Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AccountingInventoryCostRoundingV1? Rounding)
{
    internal static WorkInventoryCostPreparationV1 FromNative(AccountingInventoryCostPreparationV1 value) =>
        new(value.PeriodId,
            value.AccountingDate,
            value.TransactionDate,
            value.CurrencyId,
            value.SignedAmount,
            value.MoneyProfileId,
            value.MoneyProfileVersion.ToString(CultureInfo.InvariantCulture),
            value.MoneyScale,
            WorkInventoryCostClassificationV1.FromNative(value.Classification),
            value.CounterAccount,
            value.AccountPlanPackageId,
            value.AccountPlanPackageVersion.ToString(CultureInfo.InvariantCulture),
            value.AccountPlanCatalogVersion?.ToString(CultureInfo.InvariantCulture),
            value.AccountPlanSourceVersion,
            value.PreparedBy,
            value.PreparedAtUtc,
            value.Reason,
            value.Rounding);
}

public sealed record WorkInventoryCostClassificationV1(InvoiceLineCogsClassificationV1 Classification,
    Guid AccountId,
    string Origin,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ProductVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ClassificationVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? MappingVersion,
    string InvoiceLineRevision)
{
    internal static WorkInventoryCostClassificationV1 FromNative(AccountingInventoryCostClassificationV1 value) =>
        new(value.Classification,
            value.AccountId,
            value.Origin,
            value.ProductVersion,
            value.ClassificationVersion,
            value.MappingVersion?.ToString(CultureInfo.InvariantCulture),
            value.InvoiceLineRevision.ToString(CultureInfo.InvariantCulture));
}

public sealed record WorkInventoryCostManufacturingShareV1(Guid CompanyId,
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
    WorkInventoryCostManufacturingShipmentV1 Shipment)
{
    internal static WorkInventoryCostManufacturingShareV1 FromNative(PostedManufacturingCostShareV1 value) =>
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
            WorkInventoryCostManufacturingShipmentV1.FromNative(value.Shipment));
}

public sealed record WorkInventoryCostManufacturingShipmentV1(Guid ShipmentId,
    Guid MovementId,
    int MovementLineNumber,
    Guid LoadingInstructionLineId,
    Guid ProductId,
    Guid LabelId,
    decimal PhysicalQuantityKg,
    AccountingInventoryCostRatioV1 RootShare,
    AccountingInventoryCostRatioV1 NetRootQuantityKg,
    DateTimeOffset RecordedAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ProvenOriginalSequence,
    bool HasReturns,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ManufacturingAttributionReturnV1>? Returns)
{
    internal static WorkInventoryCostManufacturingShipmentV1 FromNative(ManufacturingAttributionShipmentV1 value) =>
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
            value.Returns);
}

public sealed record WorkInventoryCostRecordedRoundingV1(Guid CurrencyId,
    Guid MoneyProfileId,
    string MoneyProfileVersion,
    int MoneyScale,
    AccountingInventoryCostRoundingV1 Rounding)
{
    internal static WorkInventoryCostRecordedRoundingV1 FromNative(AccountingInventoryCostRecordedRoundingV1 value) =>
        new(value.CurrencyId,
            value.MoneyProfileId,
            value.MoneyProfileVersion.ToString(CultureInfo.InvariantCulture),
            value.MoneyScale,
            value.Rounding);
}

public sealed record WorkInventoryCostPurchaseShareV1(WorkInventoryCostPurchaseApprovalV1 Approval,
    Guid ShipmentId,
    Guid MovementId,
    int MovementLineNumber,
    Guid ProductId,
    decimal PhysicalQuantityKg,
    string SignedAmount,
    AccountingInventoryCostRatioV1 ExactSignedAmount,
    AccountingInventoryCostRatioV1 ExactSignedResidue,
    IReadOnlyList<WorkInventoryCostPurchaseCandidateV1> Candidates)
{
    internal static WorkInventoryCostPurchaseShareV1 FromNative(PostedPurchaseCostShareV1 value) =>
        new(WorkInventoryCostPurchaseApprovalV1.FromNative(value.Approval),
            value.ShipmentId,
            value.MovementId,
            value.MovementLineNumber,
            value.ProductId,
            value.PhysicalQuantityKg,
            value.SignedAmount,
            value.ExactSignedAmount,
            value.ExactSignedResidue,
            value.Candidates.Select(WorkInventoryCostPurchaseCandidateV1.FromNative).ToArray());
}

public sealed record WorkInventoryCostPurchaseApprovalV1(Guid CompanyId,
    Guid SourceEventId,
    Guid InvoiceLineId,
    Guid ProposalId,
    string ProposalVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? JournalId,
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
    WorkInventoryCostRecordedRoundingV1 RecordedRounding)
{
    internal static WorkInventoryCostPurchaseApprovalV1 FromNative(PurchaseFirstCostApprovalV1 value) =>
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
            WorkInventoryCostRecordedRoundingV1.FromNative(value.RecordedRounding));
}

public sealed record WorkInventoryCostPurchaseCandidateV1(Guid CompanyId,
    Guid SourceEventId,
    Guid InvoiceLineId,
    Guid AcceptanceId,
    Guid ReceiptMovementId,
    Guid RootLabelId,
    Guid ProductId,
    AccountingInventoryCostRatioV1 InvoicedRootShare,
    AccountingInventoryCostRatioV1 InvoicedNetQuantityKg,
    AccountingInventoryCostRatioV1 ExactTlAmount,
    WorkInventoryCostPurchaseShipmentV1 Shipment)
{
    internal static WorkInventoryCostPurchaseCandidateV1 FromNative(PurchaseFirstCostCandidateV1 value) =>
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
            WorkInventoryCostPurchaseShipmentV1.FromNative(value.Shipment));
}

public sealed record WorkInventoryCostPurchaseShipmentV1(Guid ShipmentId,
    Guid MovementId,
    int MovementLineNumber,
    Guid LoadingInstructionLineId,
    Guid ProductId,
    Guid LabelId,
    decimal PhysicalQuantityKg,
    AccountingInventoryCostRatioV1 RootShare,
    AccountingInventoryCostRatioV1 NetRootQuantityKg,
    DateTimeOffset RecordedAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ProvenOriginalSequence,
    IReadOnlyList<PurchaseFirstCostReturnV1> Returns)
{
    internal static WorkInventoryCostPurchaseShipmentV1 FromNative(PurchaseFirstCostShipmentV1 value) =>
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
            value.Returns);
}

public sealed record WorkInventoryCostClaimSuccessionV1(Guid PreviousEventId,
    Guid PreviousInvoiceId,
    Guid PreviousInvoiceLineId,
    Guid PreviousAdmissionId,
    string PreviousFinalizedVersion,
    Guid PreviousProposalId,
    string PreviousProposalVersion,
    Guid CancellationId,
    string InvoiceLineRevision,
    IReadOnlyList<WorkInventoryCostClaimGenerationV1> Claims,
    IReadOnlyList<WorkInventoryCostCancellationComponentV1> ReversedComponents,
    AccountingInventoryCostRatioV1 OriginalSourceAmount,
    AccountingInventoryCostRatioV1 KnownBaseAmount,
    AccountingInventoryCostRatioV1 RestoredAmount)
{
    internal static WorkInventoryCostClaimSuccessionV1 FromNative(InventoryCostClaimSuccessionV1 value) =>
        new(value.PreviousEventId,
            value.PreviousInvoiceId,
            value.PreviousInvoiceLineId,
            value.PreviousAdmissionId,
            value.PreviousFinalizedVersion.ToString(CultureInfo.InvariantCulture),
            value.PreviousProposalId,
            value.PreviousProposalVersion.ToString(CultureInfo.InvariantCulture),
            value.CancellationId,
            value.InvoiceLineRevision.ToString(CultureInfo.InvariantCulture),
            value.Claims.Select(WorkInventoryCostClaimGenerationV1.FromNative).ToArray(),
            value.ReversedComponents.Select(WorkInventoryCostCancellationComponentV1.FromNative).ToArray(),
            value.OriginalSourceAmount,
            value.KnownBaseAmount,
            value.RestoredAmount);
}

public sealed record WorkInventoryCostClaimGenerationV1(AccountingSalesPhysicalCostClaimV1 Physical,
    string PreviousGeneration)
{
    internal static WorkInventoryCostClaimGenerationV1 FromNative(InventoryCostClaimGenerationV1 value) =>
        new(value.Physical,
            value.PreviousGeneration.ToString(CultureInfo.InvariantCulture));
}

public sealed record WorkInventoryCostCancellationComponentV1(string EconomicKey,
    string Origin,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? SourceJournalId,
    Guid JournalSourceId,
    string PostingPurpose,
    Guid CurrencyId,
    string JournalTotal,
    string SignedAmount,
    Guid CogsAccountId,
    Guid CounterAccountId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ReversingJournalId)
{
    internal static WorkInventoryCostCancellationComponentV1 FromNative(InventoryCostCancellationComponentV1 value) =>
        new(value.EconomicKey,
            value.Origin,
            value.SourceJournalId,
            value.JournalSourceId,
            value.PostingPurpose,
            value.CurrencyId,
            value.JournalTotal,
            value.SignedAmount,
            value.CogsAccountId,
            value.CounterAccountId,
            value.ReversingJournalId);
}

public sealed record WorkInventoryCostCancellationV1(Guid Id,
    Guid CompanyId,
    Guid InventoryCostProposalId,
    Guid CancellationId,
    string Version,
    string Status,
    string InvoiceLineRevision,
    Guid PeriodId,
    DateOnly AccountingDate,
    DateOnly TransactionDate,
    string Reason,
    Guid PreparedBy,
    DateTimeOffset PreparedAtUtc,
    IReadOnlyList<WorkInventoryCostCancellationComponentV1> Components,
    IReadOnlyList<WorkInventoryCostCancellationProfileV1> MoneyProfiles,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ApprovedBy,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? ApprovedAtUtc)
{
    internal static WorkInventoryCostCancellationV1 FromNative(InventoryCostCancellationV1 value) =>
        new(value.Id,
            value.CompanyId,
            value.InventoryCostProposalId,
            value.CancellationId,
            value.Version.ToString(CultureInfo.InvariantCulture),
            value.Status,
            value.InvoiceLineRevision.ToString(CultureInfo.InvariantCulture),
            value.PeriodId,
            value.AccountingDate,
            value.TransactionDate,
            value.Reason,
            value.PreparedBy,
            value.PreparedAtUtc,
            value.Components.Select(WorkInventoryCostCancellationComponentV1.FromNative).ToArray(),
            value.MoneyProfiles.Select(WorkInventoryCostCancellationProfileV1.FromNative).ToArray(),
            value.ApprovedBy,
            value.ApprovedAtUtc);
}

public sealed record WorkInventoryCostCancellationProfileV1(Guid CurrencyId,
    Guid ProfileId,
    string Version,
    int Scale)
{
    internal static WorkInventoryCostCancellationProfileV1 FromNative(InventoryCostCancellationMoneyProfileV1 value) =>
        new(value.CurrencyId,
            value.ProfileId,
            value.Version.ToString(CultureInfo.InvariantCulture),
            value.Scale);
}

public sealed record WorkInventoryCostStaleClosureV1(Guid EventId,
    string PreparedVersion,
    Guid PreparedBy,
    DateTimeOffset ClosedAtUtc,
    string ReasonCode,
    string ReasonText,
    string CorrelationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CausationId)
{
    internal static WorkInventoryCostStaleClosureV1 FromNative(AccountingCostProposalStaleClosureV1 value) =>
        new(value.EventId,
            value.PreparedVersion.ToString(CultureInfo.InvariantCulture),
            value.PreparedBy,
            value.ClosedAtUtc,
            value.ReasonCode,
            value.ReasonText,
            value.CorrelationId,
            value.CausationId);
}

public sealed record WorkInventoryCostRenewalV1(Guid PreviousProposalId,
    string PreviousClosedVersion,
    Guid ClosureEventId)
{
    internal static WorkInventoryCostRenewalV1 FromNative(AccountingCostProposalRenewalV1 value) =>
        new(value.PreviousProposalId,
            value.PreviousClosedVersion.ToString(CultureInfo.InvariantCulture),
            value.ClosureEventId);
}
