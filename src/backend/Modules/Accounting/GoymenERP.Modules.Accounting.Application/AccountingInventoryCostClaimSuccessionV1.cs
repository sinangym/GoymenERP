namespace GoymenERP.Modules.Accounting.Application;

/// <summary>An append-only ownership generation for the same actual physical issue; never a new stock issue.</summary>
public sealed record InventoryCostClaimGenerationV1(AccountingSalesPhysicalCostClaimV1 Physical,
    long PreviousGeneration, string PreviousClaimSha256);

/// <summary>
/// The preceding invoice's exact source and completed cancellation remain immutable. Captured cost fingerprints
/// delimit the old ownership generation: later actual deltas belong only to the current generation.
/// RestoredAmount goes through the existing initial-COGS preparation and different-checker approval.
/// </summary>
public sealed record InventoryCostClaimSuccessionV1(Guid PreviousEventId, Guid PreviousInvoiceId,
    Guid PreviousInvoiceLineId, Guid PreviousAdmissionId, long PreviousFinalizedVersion,
    Guid PreviousProposalId, long PreviousProposalVersion, string PreviousSourceSha256,
    Guid CancellationId, long InvoiceLineRevision,
    IReadOnlyList<InventoryCostClaimGenerationV1> Claims,
    IReadOnlyList<InventoryCostCancellationComponentV1> ReversedComponents,
    IReadOnlyList<string> CapturedCostFingerprints,
    AccountingInventoryCostRatioV1 OriginalSourceAmount, AccountingInventoryCostRatioV1 KnownBaseAmount,
    AccountingInventoryCostRatioV1 RestoredAmount);
