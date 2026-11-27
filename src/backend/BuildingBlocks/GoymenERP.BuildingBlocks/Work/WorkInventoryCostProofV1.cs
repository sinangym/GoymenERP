namespace GoymenERP.BuildingBlocks.Work;

/// <summary>Routing/header facts only. Full source/cost facts require the genuine owner detail or original receipt.</summary>
public sealed record WorkInventoryCostProofV1(Guid CompanyId, Guid ProposalId, int CostKind, Guid EventId,
    Guid InvoiceId, string? OfficialInvoiceNumber, Guid PeriodId, Guid PreparedBy, DateTimeOffset PreparedRecordedAtUtc,
    string Status, bool SourceCancelled, string SourceFactsDigest, string PreviewDigest);
