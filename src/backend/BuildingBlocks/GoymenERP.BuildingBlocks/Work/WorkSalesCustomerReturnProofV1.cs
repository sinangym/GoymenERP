namespace GoymenERP.BuildingBlocks.Work;

// Routing identity only; genuine native owner/Attempt/Audit facts bind complete commercial amounts and lines.
public sealed record WorkSalesCustomerReturnProofV1(Guid CompanyId, Guid ReturnId, Guid OriginalInvoiceId,
    string OriginalDocumentNo, string CustomerDocumentNo, string PreparedBy, DateTimeOffset PreparedAtUtc,
    string SubmittedBy, DateTimeOffset SubmittedAtUtc, long SelectedExpectedVersion, long CurrentVersion,
    string State, string SourceFactsDigest, string PreviewDigest, string? OriginalDecisionDigest);
