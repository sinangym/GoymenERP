using System.Text.Json.Serialization;

namespace GoymenERP.BuildingBlocks.Work;

/// <summary>WP8 purpose read over the owner's own record. No price, cost or permission override.
/// Unlike the pending queue, the immutable maker metadata remains readable after a decision so an
/// authorized retry can reach the owner's idempotency receipt. Version belongs to the owner:
/// mutable records use their real version; an append-only submission uses its immutable identity.</summary>
public interface IWorkApprovalDecisionSourceV1 : IWorkInboxSourceV1
{
    Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId,
        CancellationToken cancellationToken);
}

/// <summary>Optional owner selection of an original native proposal version for an immutable command retry.
/// Other sources retain their existing read contract; this is not a current-version substitution.</summary>
public interface IWorkApprovalDecisionVersionSourceV1 : IWorkApprovalDecisionSourceV1
{
    Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionVersionAsync(WorkInboxSourceContextV1 context, Guid itemId,
        string expectedProposalVersion, CancellationToken cancellationToken);
}

public sealed record WorkApprovalDecisionSnapshotV1(Guid CompanyId, WorkInboxItemV1 Item,
    string SourceVersion, bool Pending,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkManufacturingPlanReapprovalOptionsV1? ManufacturingPlanReapproval = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkInventoryTransferLossOptionsV1? InventoryTransferLoss = null,
    [property: JsonIgnore] WorkInventoryTransferLossProofV1? InventoryTransferLossProof = null,
    [property: JsonIgnore] WorkCounterpartyTaxIdentityChangePreviewV1? CounterpartyTaxIdentityChange = null,
    [property: JsonIgnore] WorkCounterpartyLegalNameChangePreviewV1? CounterpartyLegalNameChange = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorkLogisticsPlanExcessOptionsV1? LogisticsPlanExcess = null,
    [property: JsonIgnore] WorkLogisticsPlanExcessProofV1? LogisticsPlanExcessProof = null,
    [property: JsonIgnore] WorkInventoryTransferOverageProofV1? InventoryTransferOverageProof = null,
    [property: JsonIgnore] WorkLogisticsWeighingDifferenceProofV1? LogisticsWeighingDifferenceProof = null,
    [property: JsonIgnore] WorkSupplierReturnCostExceptionProofV1? SupplierReturnCostExceptionProof = null,
    [property: JsonIgnore] WorkInventoryStockCountProofV1? InventoryStockCountProof = null,
    [property: JsonIgnore] WorkBankAccountProofV1? BankAccountProof = null,
    [property: JsonIgnore] WorkInventoryStockScrapProofV1? InventoryStockScrapProof = null,
    [property: JsonIgnore] WorkLogisticsDispatchNoteProofV1? LogisticsDispatchNoteProof = null,
    [property: JsonIgnore] WorkLogisticsCustomerReturnProofV1? LogisticsCustomerReturnProof = null,
    [property: JsonIgnore] WorkBankAccountIbanChangeProofV1? BankAccountIbanChangeProof = null,
    [property: JsonIgnore] WorkProductPackageStandardProofV1? ProductPackageStandardProof = null,
    [property: JsonIgnore] WorkProductPriceListProofV1? ProductPriceListProof = null,
    [property: JsonIgnore] WorkPartyCreditLimitProofV1? PartyCreditLimitProof = null,
    [property: JsonIgnore] WorkOfficialInvoiceImportProofV1? OfficialInvoiceImportProof = null,
    [property: JsonIgnore] WorkOfficialSalesInvoiceProofV1? OfficialSalesInvoiceProof = null,
    [property: JsonIgnore] WorkManualBankPaymentReversalProofV1? ManualBankPaymentReversalProof = null,
    [property: JsonIgnore] WorkManualReceiptReversalProofV1? ManualReceiptReversalProof = null,
    [property: JsonIgnore] WorkExpensePaymentProofV1? ExpensePaymentProof = null,
    [property: JsonIgnore] WorkBankCreditLimitProofV1? BankCreditLimitProof = null,
    [property: JsonIgnore] WorkOpeningBalanceProofV1? OpeningBalanceProof = null,
    [property: JsonIgnore] WorkCreditMovementProofV1? CreditMovementProof = null,
    [property: JsonIgnore] WorkCashExchangeBookValueProofV1? CashExchangeBookValueProof = null,
    [property: JsonIgnore] WorkNormalJournalDraftProofV1? NormalJournalDraftProof = null,
    [property: JsonIgnore] WorkAccountingPeriodOpeningProofV1? AccountingPeriodOpeningProof = null,
    [property: JsonIgnore] WorkAccountingPeriodReopeningProofV1? AccountingPeriodReopeningProof = null,
    [property: JsonIgnore] WorkJournalReversalProofV1? JournalReversalProof = null)
{
    // Body property keeps existing constructor callers unchanged. Human package rows stay in the owner read.
    [JsonIgnore] public WorkApprovedSourceCatalogProofV1? ApprovedSourceCatalogProof { get; init; }

    // Routing identity and immutable native proof remain separate from the human account-plan preview.
    [JsonIgnore] public WorkPostingAccountMappingProofV1? PostingAccountMappingProof { get; init; }

    [JsonIgnore] public WorkManufacturingCostDifferenceProofV1? ManufacturingCostDifferenceProof { get; init; }
    [JsonIgnore] public WorkInvoiceLineCogsProofV1? InvoiceLineCogsProof { get; init; }
    [JsonIgnore] public WorkInventoryCostProofV1? InventoryCostProof { get; init; }
    [JsonIgnore] public WorkInventoryCostCancellationProofV1? InventoryCostCancellationProof { get; init; }
    [JsonIgnore] public WorkSourceVoucherProofV1? SourceVoucherProof { get; init; }
    [JsonIgnore] public WorkOrganizationCompanyTaxIdProofV1? OrganizationCompanyTaxIdProof { get; init; }
    [JsonIgnore] public WorkSalesRequestProofV1? SalesRequestProof { get; init; }
    [JsonIgnore] public WorkSalesQuoteConversionProofV1? SalesQuoteConversionProof { get; init; }
    [JsonIgnore] public WorkSalesPriceFloorProofV1? SalesPriceFloorProof { get; init; }
    [JsonIgnore] public WorkSalesDiscountProofV1? SalesDiscountProof { get; init; }
    [JsonIgnore] public WorkSalesOverDeliveryRequestProofV1? SalesOverDeliveryRequestProof { get; init; }
    [JsonIgnore] public WorkIdentityAccessGrantProofV1? IdentityAccessGrantProof { get; init; }
}

/// <summary>Private mapping identity; a genuine original receipt binds complete rows and prepared plan facts.</summary>
public sealed record WorkPostingAccountMappingProofV1(Guid CompanyId, long MappingVersion, Guid PreparedBy,
    DateTimeOffset PreparedAtUtc, string Status, long Version, long ActiveMappingVersion,
    string SourceFactsDigest, string PreviewDigest);

/// <summary>Private catalog identity; full package facts require the owner's read or genuine original receipt.</summary>
public sealed record WorkApprovedSourceCatalogProofV1(Guid CompanyId, Guid PackageId, Guid PreparedBy,
    DateTimeOffset PreparedAtUtc, string Status, long PackageVersion, long ActiveCatalogVersion,
    string SourceFactsDigest, string PreviewDigest);

/// <summary>Private journal reversal identity. Full native proposal and reason stay in the authorized purpose read.</summary>
public sealed record WorkJournalReversalProofV1(Guid CompanyId, Guid ProposalId, Guid SourceJournalId,
    Guid PeriodId, Guid ProposedBy, DateTimeOffset ProposedAtUtc, string Status, int Version,
    Guid? CancellationSourceActorId, string SourceFactsDigest, string PreviewDigest);

/// <summary>Private owner evidence; a later close does not replace an original opening approval result.</summary>
public sealed record WorkAccountingPeriodOpeningProofV1(Guid CompanyId, Guid PeriodId, Guid PreparedBy,
    DateTimeOffset PreparedAtUtc, DateOnly StartsOn, DateOnly EndsOn, string Status, long Version,
    string SourceFactsDigest, string PreviewDigest);

/// <summary>Private reopening evidence. ClosedPeriodVersion is retained; PeriodVersion is the current owner projection.</summary>
public sealed record WorkAccountingPeriodReopeningProofV1(Guid CompanyId, Guid ProposalId, Guid PeriodId,
    Guid ClosureId, Guid PreparedBy, DateTimeOffset PreparedAtUtc, DateOnly StartsOn, DateOnly EndsOn,
    long Version, long ClosedPeriodVersion, long PeriodVersion, string PeriodStatus, bool Approved,
    bool Pending, bool LaterPeriodClosed, string SourceFactsDigest, string PreviewDigest);

/// <summary>Private normal journal identity; full lines and amounts belong to the authorized human purpose GET.</summary>
public sealed record WorkNormalJournalDraftProofV1(Guid CompanyId, Guid DraftId, Guid PreparedBy,
    DateTimeOffset PreparedAtUtc, string Status, long Version, string SourceFactsDigest, string PreviewDigest);

/// <summary>Scalar native source bindings only; full money facts stay in the authorized human purpose GET.</summary>
public sealed record WorkCreditMovementProofV1(Guid CompanyId, Guid MovementId, string ProposedBy,
    DateTimeOffset ProposedAtUtc, string Status, string SourceFactsDigest, string PreviewDigest);
public sealed record WorkCashExchangeBookValueProofV1(Guid CompanyId, Guid ProposalId, Guid TransferId, string PreparedBy,
    DateTimeOffset PreparedAtUtc, string Status, string Version, string TransferSourceHash, string ProposalFactsDigest, string PreviewDigest);

/// <summary>Scalar owner identity only; full bank limit and opening facts stay in the authorized purpose GET.</summary>
public sealed record WorkBankCreditLimitProofV1(Guid CompanyId, Guid LimitId, string ProposedBy,
    DateTimeOffset ProposedAtUtc, string Status, string SourceFactsDigest, string PreviewDigest);
public sealed record WorkOpeningBalanceProofV1(Guid CompanyId, Guid OpeningBalanceId, Guid? ReversalId,
    string OpeningProposedBy, string ProposedBy, DateTimeOffset ProposedAtUtc, string Status, string Version,
    string OpeningFactsDigest, string? ReversalFactsDigest, string PreviewDigest);

/// <summary>Private owner identity and draft binding; full expense facts belong only to the authorized human GET.</summary>
public sealed record WorkExpensePaymentProofV1(Guid CompanyId, Guid PaymentId, string PreparedBy,
    DateTimeOffset PreparedAtUtc, string Status, string DraftFactsDigest, string PreviewDigest);

/// <summary>Scalar native proposal/recorded-source binding only; full money facts belong to the authorized human GET.</summary>
public sealed record WorkManualBankPaymentReversalProofV1(Guid CompanyId, Guid ProposalId, Guid PaymentId,
    string ProposedBy, DateTimeOffset ProposedAtUtc, string Status, string RecordedFactsDigest, string PreviewDigest);
public sealed record WorkManualReceiptReversalProofV1(Guid CompanyId, Guid ProposalId, Guid OriginalSourceId,
    string ProposedBy, DateTimeOffset ProposedAtUtc, string Status, string RecordedFactsDigest, string PreviewDigest);

/// <summary>Scalar owner binding only. Full monetary/document facts are released only by authorized human GET.</summary>
public sealed record WorkOfficialSalesInvoiceProofV1(Guid CompanyId, Guid InvoiceId, string OfficialNo,
    Guid CustomerPartyId, Guid CurrencyId, Guid PdfDocumentId, string RecordedBy, DateTimeOffset RecordedAtUtc,
    string Status, string RecordedFactsDigest, string PreviewDigest);

/// <summary>Price-free immutable import header; full canonical rows stay in the authorized human GET only.</summary>
public sealed record WorkOfficialInvoiceImportProofV1(Guid CompanyId, Guid BatchId, string BatchVersion,
    string BatchChecksum, string TemplateVersion, int RowCount, string UploadedBy, DateTimeOffset UploadedAtUtc,
    string? ApprovedBy, DateTimeOffset? ApprovedAtUtc, string PreviewDigest);

/// <summary>Existing Finance approval-only disclosure, released only by the authorized human purpose GET.</summary>
public sealed record WorkPartyCreditLimitPreviewV1(Guid PartyId, Guid CompanyAccountId, string Kind,
    Guid? CurrencyId, string? Amount, string BaseRevision, string CurrentRevision, string ProposedBy, DateTimeOffset ProposedAtUtc);
public sealed record WorkPartyCreditLimitProofV1(Guid CompanyId, Guid ProposalId, WorkPartyCreditLimitPreviewV1 Proposal);

/// <summary>Price-free owner identity/version/maker binding. Full price facts are released only by authorized human GET.</summary>
public sealed record WorkProductPriceListProofV1(Guid CompanyId, Guid PriceListId, Guid VersionId, int VersionNo,
    string HeaderVersion, string CreatedBy, DateTimeOffset CreatedAtUtc, IReadOnlyList<string> Preparers);
public sealed record WorkProductPriceListPreviewV1(Guid PriceListId, string Name, string CurrencyCode, string Channel,
    string VatBasis, Guid VersionId, int VersionNo, string ValidFrom, string? ValidThrough,
    IReadOnlyList<WorkProductPriceListLineV1> Lines, WorkProductPriceListScopeV1 Scope);
public sealed record WorkProductPriceListScopeV1(string Kind, Guid? PartyId, Guid? CompanyAccountId);
public sealed record WorkProductPriceListLineV1(Guid LineId, int LineNo, Guid ProductId, string? ProductCode, string? ProductName,
    Guid PriceUnitId, string UnitPrice, string? FloorUnitPrice, string LowerQuantity, string? UpperQuantity);

/// <summary>Immutable, price-free package table facts, disclosed only by the authorized human purpose GET.</summary>
public sealed record WorkProductPackageStandardPreviewV1(string SourceKind, string? SourceLabel, string? FileName,
    string Reason, string PreparedBy, DateTimeOffset PreparedAtUtc, int EntryCount, int CustomerEntryCount,
    IReadOnlyList<WorkProductPackageStandardEntryV1> Entries);
public sealed record WorkProductPackageStandardEntryV1(Guid ProductId, Guid? CustomerId, int PiecesPerPackage);
public sealed record WorkProductPackageStandardProofV1(Guid CompanyId, Guid VersionId, string Version,
    WorkProductPackageStandardPreviewV1 Table);

/// <summary>Finance-only source binding. Raw facts are never serialized in an inbox, generic snapshot, journal or AI tool.</summary>
public sealed record WorkBankAccountIbanChangeProofV1(Guid CompanyId, Guid AccountId, string ProposedBy,
    DateTimeOffset ProposedAtUtc, string ProposalVersion, string FactsDigest, WorkBankAccountIbanChangeFactsV1? HumanFacts);
public sealed record WorkBankAccountIbanChangeFactsV1(string? CurrentIban, string ProposedIban, string Reason);

/// <summary>Recorded physical-return facts for the authorized human only; no price or current master lookup.</summary>
public sealed record WorkLogisticsCustomerReturnPreviewV1(Guid LoadingInstructionId, Guid LoadingInstructionLineId,
    Guid SourceDeliveryId, Guid SourceShipmentId, Guid SourceOrderId, Guid SourceOrderLineId, Guid UnitId,
    string QuantityUnit, string Quantity, string SourceDeliveredQuantity, string Reason, string RequestedBy,
    DateTimeOffset OccurredAtUtc, DateTimeOffset RequestedAtUtc, string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorkCustomerReturnWeighingV1? Weighing = null);
public sealed record WorkCustomerReturnWeighingV1(Guid WeighingId, string Direction, string FirstWeighingKg,
    string SecondWeighingKg, string NetKg, string RecordedBy, DateTimeOffset RecordedAtUtc);
/// <summary>Owner-bound identity, distinct from the immutable native command receipt.</summary>
public sealed record WorkLogisticsCustomerReturnProofV1(Guid CompanyId, Guid CustomerReturnId, WorkLogisticsCustomerReturnPreviewV1 Return);

/// <summary>Immutable, price-free native count evidence, exposed only by the authorized human purpose GET.</summary>
public sealed record WorkInventoryStockCountPreviewV1(Guid LabelId, string LabelVersion,
    string OwnerKind, Guid OwnerId, string CustodianKind, Guid CustodianId, Guid LocationId, Guid ProductId,
    string Block, string BookKg, string CountedKg, string DifferenceKg, DateTimeOffset CountedAtUtc,
    string Reason, string RecordedBy, DateTimeOffset RecordedAtUtc, int? BookPieceCount, int? CountedPieceCount);
/// <summary>Owner-only logical-state proof; neither a live label version nor an Inbox/AI payload.</summary>
public sealed record WorkInventoryStockCountProofV1(Guid CompanyId, Guid CountId,
    WorkInventoryStockCountPreviewV1 Count, string Status);

/// <summary>Immutable, price-free native scrap evidence, exposed only by the authorized human purpose GET.</summary>
public sealed record WorkInventoryStockScrapPreviewV1(Guid LabelId, string LabelVersion,
    string OwnerKind, Guid OwnerId, string CustodianKind, Guid CustodianId, Guid LocationId, Guid ProductId,
    string Block, string BookKg, string ScrapKg, string RemainingKg, DateTimeOffset ScrappedAtUtc,
    string Reason, string RecordedBy, DateTimeOffset RecordedAtUtc, int? BookPieceCount, int? ScrapPieceCount);
/// <summary>Owner-only logical-state proof; neither a live label version nor an Inbox/AI payload.</summary>
public sealed record WorkInventoryStockScrapProofV1(Guid CompanyId, Guid ScrapId,
    WorkInventoryStockScrapPreviewV1 Scrap, string Status);

/// <summary>Approval-purpose identities from the current plan revision, never an implicit user selection.</summary>
public sealed record WorkManufacturingPlanReapprovalOptionsV1(IReadOnlyList<Guid> ParentRollIds);

/// <summary>The current native request, not a transfer version or a decision timestamp.</summary>
public sealed record WorkInventoryTransferLossOptionsV1(int RequestNumber, DateTimeOffset RequestedAtUtc);
/// <summary>Owner-only routing/history proof. Never serialized into the purpose preview.</summary>
public sealed record WorkInventoryTransferLossProofV1(Guid TransferId, Guid LineId,
    IReadOnlyList<WorkInventoryTransferLossRequestProofV1> Requests);
public sealed record WorkInventoryTransferLossRequestProofV1(int RequestNumber, DateTimeOffset RequestedAtUtc,
    string PreparedBy, string Status);

/// <summary>Exact immutable proposal for the authorized human decision GET only. Never Inbox/AI metadata.</summary>
public sealed record WorkCounterpartyTaxIdentityChangePreviewV1(Guid PartyId, Guid ChangeRequestId,
    string CurrentTaxIdentityNumber, string ProposedTaxIdentityNumber, string ProposedKind, string Reason);
/// <summary>Native full names (up to 250), separate from the generic bounded Work caption/reference.</summary>
public sealed record WorkCounterpartyLegalNameChangePreviewV1(Guid PartyId, Guid ChangeRequestId,
    string CurrentLegalName, string ProposedLegalName, string Reason);

/// <summary>Current native instruction version for the human's frozen approval request; not a POST default.</summary>
public sealed record WorkLogisticsPlanExcessOptionsV1(string InstructionVersion);
/// <summary>Exact immutable proposal for the authorized human decision GET only. Never Inbox/AI metadata.</summary>
public sealed record WorkLogisticsPlanExcessPreviewV1(string InstructionVersion, Guid InstructionId, Guid LineId,
    string Quantity, string Reason, string ProposedBy, DateTimeOffset ProposedAtUtc);
/// <summary>Owner-only immutable proposal binding for validating the original native receipt.</summary>
public sealed record WorkLogisticsPlanExcessProofV1(Guid CompanyId, Guid ExceptionId, Guid InstructionId, Guid LineId,
    string Quantity, string Reason, string ProposedBy, DateTimeOffset ProposedAtUtc);

/// <summary>Native numbered excess request; exposed only by the authorized human purpose GET.</summary>
public sealed record WorkInventoryTransferOveragePreviewV1(int RequestNumber, DateTimeOffset RequestedAtUtc,
    Guid TransferId, Guid LineId, string Kilograms, int? PieceCount, string Reason, string RequestedBy, string ReceivedBy);
/// <summary>Retained owner history for exact native replay, never Inbox/AI metadata.</summary>
public sealed record WorkInventoryTransferOverageProofV1(Guid TransferId, Guid LineId, string ReceivedBy,
    IReadOnlyList<WorkInventoryTransferOverageRequestProofV1> Requests);
public sealed record WorkInventoryTransferOverageRequestProofV1(int RequestNumber, DateTimeOffset RequestedAtUtc,
    string PreparedBy, string Status, string Kilograms, int? PieceCount, string Reason);

/// <summary>Immutable owner evaluation, for receipt binding only; never serialized as Inbox/AI context.</summary>
public sealed record WorkLogisticsWeighingDifferenceProofV1(Guid CompanyId, Guid RecordId, Guid DispatchNoteId,
    Guid InstructionId, decimal TheoreticalKg, decimal ScaleKg, decimal DifferenceKg, string BandLevel,
    decimal? WarningBandPercent, decimal? ApprovalBandPercent, decimal? BlockBandPercent, decimal TotalExcessKg,
    decimal? ApprovalRatePercent, string Decision, IReadOnlyList<WorkLogisticsWeighingDifferenceProductProofV1> Products,
    IReadOnlyList<string> EnteredBy, bool EnteredByComplete, string RecordedBy, DateTimeOffset RecordedAtUtc,
    string? KilogramAllocationJson);
public sealed record WorkLogisticsWeighingDifferenceProductProofV1(Guid ProductId, decimal NoteKg, decimal ScaleKg,
    decimal DifferenceKg, decimal RemainingPhysicalKg, decimal PhysicallyCoveredKg, decimal ExcessKg, int? TargetLineNo);

/// <summary>Price-free retained native return facts. Never Inbox/AI/journal wire context.</summary>
public sealed record WorkSupplierReturnCostExceptionProofV1(Guid CompanyId, Guid ReturnId, Guid ReturnLineId,
    Guid PurchaseOrderId, Guid PurchaseOrderLineId, Guid GoodsAcceptanceId, Guid InventoryMovementId,
    string SourceBlock, string QuantityKg, string CostBasis, bool ReplacementExpected, string Reason,
    DateTimeOffset OccurredAtUtc, string RequestedBy, DateTimeOffset RequestedAtUtc,
    string? RecordedExpectedPurchaseOrderVersion, string? CurrentPurchaseOrderVersion, string? UndoQuantity);

/// <summary>Safe native account view and immutable proposer identity. No card/IBAN/account-number data.</summary>
public sealed record WorkBankAccountProofV1(Guid CompanyId, Guid AccountId, Guid CurrencyId, string DisplayName,
    bool IsActive, bool IsUsableForReceipt, string ProposedBy, DateTimeOffset ProposedAtUtc);


/// <summary>Full price-free native dispatch document for the authorized human GET only; never Inbox/AI content.</summary>
public sealed record WorkLogisticsDispatchNotePreviewV1(Guid InstructionId, Guid DispatchNoteId, Guid CustomerPartyId,
    Guid CompanyAccountId, DateOnly EffectiveOn, string Status, string? Number, string? EDispatchNumber,
    string CreatedBy, DateTimeOffset CreatedAtUtc, IReadOnlyList<WorkLogisticsDispatchNoteLinePreviewV1> Lines);
public sealed record WorkLogisticsDispatchNoteLinePreviewV1(int LineNo, Guid ShipmentId, Guid InstructionLineId,
    Guid SourceOrderId, Guid SourceOrderLineId, Guid ProductId, Guid UnitId, string Quantity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? QuantityKg = null);
/// <summary>Owner-only company/version binding, separate from mutable status and from the original command receipt.</summary>
public sealed record WorkLogisticsDispatchNoteProofV1(Guid CompanyId, string InstructionVersion,
    WorkLogisticsDispatchNotePreviewV1 Note);
