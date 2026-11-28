namespace GoymenERP.BuildingBlocks.Work;

/// <summary>Private selected conversion request identity. Quote approval and request rejection have distinct version axes.</summary>
public sealed record WorkSalesQuoteConversionProofV1(Guid CompanyId, Guid RequestId, Guid QuoteId, int RequestNo,
    int SubjectRevision, string RequestVersion, string QuoteVersion, int QuoteRevision, string QuoteDocumentNo,
    string RequestedBy, DateTimeOffset RequestedAtUtc, string RequestStatus, string QuoteState,
    bool ApprovalSupported, bool RejectionSupported, string SourceFactsDigest, string PreviewDigest);
