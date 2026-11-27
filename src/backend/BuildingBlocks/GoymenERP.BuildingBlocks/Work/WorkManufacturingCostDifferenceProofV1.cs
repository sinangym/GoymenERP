namespace GoymenERP.BuildingBlocks.Work;

/// <summary>Private routing identity only. Full cost facts require the owner read or genuine native receipt.</summary>
public sealed record WorkManufacturingCostDifferenceProofV1(Guid CompanyId, Guid ProposalId, Guid ManufacturingOrderId,
    Guid PeriodId, Guid DebitAccountId, Guid CreditAccountId, Guid PreparedBy, DateTimeOffset PreparedAtUtc,
    string Status, string SourceFactsDigest, string PreviewDigest);
