namespace GoymenERP.BuildingBlocks.Work;

/// <summary>Private routing/content identity, not a monetary proof. Full owner facts or a genuine native receipt must match it.</summary>
public sealed record WorkSalesPriceFloorProofV1(Guid CompanyId, Guid DraftId, string DocumentNo, string Version,
    bool Pending, bool Historical, bool PriceDisclosed, IReadOnlyList<WorkSalesPriceFloorLineProofV1> Lines,
    string SourceFactsDigest, string PreviewDigest, string? OriginalKeyHash, string? OriginalPayloadHash);
public sealed record WorkSalesPriceFloorLineProofV1(Guid LineId, int LineNo, string RequestedBy, DateTimeOffset RequestedAtUtc,
    Guid ProductId, string ProductCode, string ProductName, Guid PriceListId, string PriceListName);
