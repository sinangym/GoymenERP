namespace GoymenERP.BuildingBlocks.Work;

/// <summary>Private proposal routing only. Full frozen cost facts require the owner or genuine original receipt.</summary>
public sealed record WorkInvoiceLineCogsProofV1(Guid CompanyId, Guid ProposalId, Guid InvoiceId, Guid InvoiceLineId,
    long FinalizedVersion, long ExpectedLineVersion, Guid PreparedBy, DateTimeOffset PreparedAtUtc,
    string Status, string SourceFactsDigest, string PreviewDigest);
