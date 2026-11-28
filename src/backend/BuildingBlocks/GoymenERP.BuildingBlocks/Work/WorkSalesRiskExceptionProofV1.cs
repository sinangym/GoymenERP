namespace GoymenERP.BuildingBlocks.Work;

// Private selection identity; full native facts and genuine command receipts remain mandatory.
public sealed record WorkSalesRiskExceptionProofV1(
    Guid CompanyId, Guid ExceptionId, Guid OrderId, int ExceptionNo,
    string OrderDocumentNo, string Version, string CurrentVersion,
    string RequestedBy, DateTimeOffset RequestedAtUtc,
    bool Pending, bool ApprovalSupported, bool RejectionSupported, bool Historical,
    string SourceFactsDigest, string PreviewDigest,
    string? OriginalDecisionDigest, string? OriginalKeyHash, string? OriginalPayloadHash);
