namespace GoymenERP.BuildingBlocks.Work;

/// <summary>Private current request identity. Action support and queue membership are distinct: Approved may still be rejected.</summary>
public sealed record WorkSalesRequestProofV1(Guid CompanyId, Guid RequestId, string DocumentNo, Guid PartyId,
    Guid CompanyAccountId, DateOnly BusinessDate, DateTimeOffset CreatedAtUtc, string Version, string State,
    bool ApprovalSupported, bool RejectionSupported, string SourceFactsDigest, string PreviewDigest);
