namespace GoymenERP.BuildingBlocks.Work;

/// <summary>Private discount routing identity; scalar facts are not a monetary approval certificate.</summary>
public sealed record WorkSalesDiscountProofV1(Guid CompanyId, Guid DraftId, string DocumentNo, string Version,
    bool Pending, bool Historical, bool PriceDisclosed, IReadOnlyList<WorkSalesDiscountLineProofV1> Lines,
    string SourceFactsDigest, string PreviewDigest, string? OriginalKeyHash, string? OriginalPayloadHash);
public sealed record WorkSalesDiscountLineProofV1(Guid LineId, int LineNo, string CheckedBy, DateTimeOffset CheckedAtUtc,
    Guid ProductId, string ProductCode, string ProductName, Guid? PriceListId, string? PriceListName, bool LimitDefined);
