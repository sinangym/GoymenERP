namespace GoymenERP.BuildingBlocks.Work;

// Routing/quantity identity only. Full owner facts or a genuine native Audit/attempt receipt remain mandatory.
public sealed record WorkSalesOverDeliveryRequestProofV1(Guid CompanyId, Guid RequestId, Guid OrderId, int RequestNo, int SubjectRevision,
    Guid LineId, string RequestedBy, DateTimeOffset RequestedAtUtc, string OrderDocumentNo, string Ordered, string Quantity,
    string RequestedTotal, string RequestStatus, long RequestVersion, string SourceFactsDigest, string PreviewDigest);
