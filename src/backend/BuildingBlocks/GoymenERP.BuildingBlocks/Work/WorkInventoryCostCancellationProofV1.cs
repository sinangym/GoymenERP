namespace GoymenERP.BuildingBlocks.Work;
/// <summary>Private scalar binding only; immutable human components belong to the purpose read.</summary>
public sealed record WorkInventoryCostCancellationProofV1(Guid CompanyId, Guid InventoryCostProposalId,
    Guid ComponentCancellationId, Guid SourceCancellationId, long ProposalVersion, long PreparedCancellationVersion,
    Guid PeriodId, Guid PreparedBy, DateTimeOffset PreparedAtUtc, DateTimeOffset PreparedRecordedAtUtc,
    string Status, string SourceFactsDigest, string PreviewDigest);
