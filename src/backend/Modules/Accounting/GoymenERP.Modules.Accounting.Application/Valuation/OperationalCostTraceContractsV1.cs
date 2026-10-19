namespace GoymenERP.Modules.Accounting.Application.Valuation;

/// <summary>Physical disposition of a caller-proven historical quantity share, not a journal account.</summary>
public enum OperationalCostShareKindV1
{
    OnHand,
    Wip,
    SoldOrConsumed,
}

/// <summary>Inventory's proven ownership classification; a customer material share is not a company asset.</summary>
public enum OperationalCostAssetKindV1
{
    CompanyOwned,
    CustomerOwned,
}

/// <summary>
/// Caller-proven canonical quantity unit and approved unit-profile snapshot. Every quantity in
/// one preview must use this exact identity; this slice has no conversion or default unit.
/// </summary>
public sealed record OperationalCostQuantityBasisV1(
    Guid UnitId,
    Guid ApprovedProfileId,
    string ApprovedProfileVersion);

/// <summary>
/// Technical preview serialization limits, not a money scale, rounding rule or business policy.
/// Larger inputs require a versioned contract change and are refused, never clipped or rounded.
/// </summary>
public static class OperationalCostTraceSerializationLimitsV1
{
    public const int MaxNumericTextLength = 128;
    public const int MaxApprovedMoneyScale = 64;
    public const int MaxShareCount = 4096;
}

/// <summary>
/// Immutable source identity for one operational cost origin. Its stock owner, product snapshot,
/// lot and label are supplied by their owners; this contract does not discover or approve them.
/// </summary>
public sealed record OperationalCostOriginV1(
    Guid CostOriginId,
    Guid CompanyId,
    Guid StockOwnerId,
    Guid ProductId,
    string ProductVersion,
    Guid LotId,
    Guid LabelId,
    Guid CurrencyId,
    string SourceFactKind,
    Guid SourceFactId,
    string SourceFactVersion,
    OperationalCostAssetKindV1 AssetKind);

/// <summary>
/// One externally proven share of an origin. The evidence fact/version must be checked by the
/// Inventory/Manufacturing owner before a future runtime passes it here. A share may have a new
/// lot/label after a physical transformation; it still retains the same cost origin.
/// </summary>
public sealed record OperationalCostShareV1(
    Guid ShareId,
    Guid CostOriginId,
    Guid CompanyId,
    Guid StockOwnerId,
    Guid ProductId,
    string ProductVersion,
    Guid LotId,
    Guid LabelId,
    Guid CurrencyId,
    string EvidenceProducer,
    string EvidenceFactKind,
    Guid EvidenceFactId,
    string EvidenceFactVersion,
    OperationalCostShareKindV1 Kind,
    OperationalCostQuantityBasisV1 QuantityBasis,
    string QuantityText,
    string? CurrentAmountText);

/// <summary>
/// A caller-supplied prior application from complete durable history. This pure slice cannot
/// establish durable exactly-once behavior; a later writer must atomically enforce it.
/// </summary>
public sealed record OperationalCostApplicationV1(Guid AdjustmentId, string Fingerprint);

/// <summary>
/// A source-only preview. ApprovedProfileId/version/scale and SourcePeriodOpen are trusted-owner
/// snapshots, not an authority check here. Null money means unknown, never zero. No source,
/// physical, ledger, journal, outbox or database record is created by this contract.
/// </summary>
public sealed record OperationalCostTraceRequestV1(
    OperationalCostOriginV1? Origin,
    Guid AdjustmentId,
    string AdjustmentSourceKind,
    Guid AdjustmentSourceFactId,
    string AdjustmentSourceVersion,
    DateOnly SourceBusinessDate,
    string? AdjustmentAmountText,
    Guid ApprovedProfileId,
    string ApprovedProfileVersion,
    int? ApprovedScale,
    bool SourcePeriodOpen,
    OperationalCostQuantityBasisV1 QuantityBasis,
    string TotalQuantityText,
    string? CurrentTotalAmountText,
    IReadOnlyList<OperationalCostShareV1>? Shares,
    IReadOnlyList<OperationalCostApplicationV1>? PriorApplications);

public enum OperationalCostTraceStatusV1
{
    Ready,
    Unavailable,
    Conflict,
    RoundingRequired,
    ForwardCorrectionRequired,
}

public static class OperationalCostTraceCodesV1
{
    public const string Ready = "operational_cost_preview_ready";
    public const string Replayed = "operational_cost_preview_replayed";
    public const string InvalidReference = "operational_cost_invalid_reference";
    public const string SourceMismatch = "operational_cost_source_mismatch";
    public const string ShareTotalMismatch = "operational_cost_share_total_mismatch";
    public const string QuantityBasisMismatch = "operational_cost_quantity_basis_mismatch";
    public const string AmountMismatch = "operational_cost_amount_mismatch";
    public const string AmountScaleExceeded = "operational_cost_amount_scale_exceeded";
    public const string NegativeValue = "operational_cost_negative_value";
    public const string AdjustmentConflict = "operational_cost_adjustment_conflict";
    public const string UnknownAmount = "operational_cost_unknown_amount";
    public const string ProfileUnavailable = "operational_cost_profile_unavailable";
    public const string NumericLimitExceeded = "operational_cost_numeric_limit_exceeded";
    public const string CustomerMaterial = "operational_cost_customer_material_not_company_asset";
    public const string RoundingRequired = "operational_cost_rounding_required";
    public const string ForwardCorrectionRequired = "operational_cost_forward_correction_required";
}

public sealed record OperationalCostSharePreviewV1(
    Guid ShareId,
    OperationalCostShareKindV1 Kind,
    string DeltaAmountText,
    string NewAmountText);

/// <summary>A deterministic preview only; Ready and Replayed never authorize a write or posting.</summary>
public sealed record OperationalCostTraceResultV1(
    OperationalCostTraceStatusV1 Status,
    string Code,
    string? Fingerprint,
    bool Replayed,
    string? NewTotalAmountText,
    IReadOnlyList<OperationalCostSharePreviewV1> Shares);
