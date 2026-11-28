namespace GoymenERP.BuildingBlocks.Work;

// Scalar routing identity only; the native owner and immutable receipt bind complete monetary facts.
public sealed record WorkSalesContractDiscountProofV1(
    Guid CompanyId, Guid ContractId, int ContractNo,
    string Version, string CurrentVersion, string CreatedBy, DateTimeOffset CreatedAtUtc,
    bool Pending, bool Historical, bool PriceDisclosed, bool LimitDefined, int RuleCount,
    string SourceFactsDigest, string PreviewDigest,
    string? OriginalDecisionDigest, string? OriginalKeyHash, string? OriginalPayloadHash);
