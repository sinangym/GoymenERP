using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application.Contracts;

public enum SalesPriceFloorDecisionReadStateV1 { Found, NotFound, Denied, Unavailable }
public sealed record SalesPriceFloorDecisionReadResultV1(SalesPriceFloorDecisionReadStateV1 State, SalesPriceFloorDecisionDetailV1? Detail);
public interface ISalesPriceFloorDecisionReadV1
{
    Task<SalesPriceFloorDecisionReadResultV1> ReadPriceFloorDecisionAsync(Guid draftId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
    Task<SalesPriceFloorOriginalVersionReadResultV1> ReadPriceFloorOriginalVersionAsync(Guid draftId, string expectedVersion,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}
public sealed record SalesPriceFloorOriginalVersionReadResultV1(SalesPriceFloorDecisionReadStateV1 State,
    [property: JsonIgnore] SalesPriceFloorOriginalReceiptV1? Receipt)
{
    [JsonIgnore] public long? CurrentVersion { get; init; }
}

/// <summary>Selected commercial facts only; no general draft, margin, Product or current price-list read.</summary>
public sealed record SalesPriceFloorDecisionDetailV1(Guid CompanyId, Guid DraftId, string DocumentNo, string Version,
    int Revision, string State, string ApprovalState, Guid PartyId, Guid CompanyAccountId, Guid CurrencyId, DateOnly BusinessDate,
    IReadOnlyList<SalesPriceFloorDecisionLineV1> Lines, SalesPriceDisclosure PriceDisclosure, bool ApprovalSupported,
    string SourceFactsDigest, string PreviewDigest)
{
    [JsonIgnore] public SalesPriceFloorDecisionSourceV1? Source { get; init; }
}
public sealed record SalesPriceFloorDecisionLineV1(Guid LineId, int LineNo, Guid ProductId, string ProductVersion,
    string ProductCode, string ProductName, Guid OrderUnitId, string Quantity,
    SalesDraftPriceFloorExceptionV1? FloorException, SalesDraftLinePricingV1? Pricing);

/// <summary>Native owner/store seam. Commercial values never pass through the withheld human projection.</summary>
public interface ISalesPriceFloorDecisionReadStoreV1
{
    Task<SalesPriceFloorDecisionSourceV1?> ReadPriceFloorDecisionSourceAsync(Guid draftId,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default);
    Task<SalesPriceFloorOriginalVersionReadResultV1> ReadPriceFloorOriginalVersionAsync(Guid draftId, long expectedVersion,
        string actorId, Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SalesPriceFloorOriginalVersionReadResultV1(SalesPriceFloorDecisionReadStateV1.Unavailable, null));
}
public sealed record SalesPriceFloorDecisionSourceV1(Guid CompanyId, Guid DraftId, string DocumentNo, long Version,
    int Revision, string State, string ApprovalState, Guid PartyId, Guid CompanyAccountId, Guid CurrencyId, DateOnly BusinessDate,
    IReadOnlyList<SalesPriceFloorDecisionSourceLineV1> Lines);
public sealed record SalesPriceFloorDecisionSourceLineV1(Guid LineId, int LineNo, Guid ProductId, string ProductVersion,
    string ProductCode, string ProductName, Guid OrderUnitId, string Quantity, CommercialDraftFields Commercial);

/// <summary>
/// Private proof of the original command, its two stored disclosure variants and its immutable commercial Audit facts.
/// PayloadHash is the actual stored native hash; trimmed Audit reasons cannot reconstruct the raw request/order/version text.
/// </summary>
public sealed record SalesPriceFloorOriginalReceiptV1(Guid CompanyId, Guid DraftId, string CommandName, string ActorId,
    string IdempotencyKeyHash, string PayloadHash, long ExpectedVersion, long ResultVersion, string OriginalCorrelationId,
    DateTimeOffset RecordedAtUtc, Guid AuditEventId, string AuditIntegrityHash,
    IReadOnlyList<SalesPriceFloorOriginalLineV1> Lines, SalesDraftReplayResult OriginalResult,
    string OriginalResultDigest, string OriginalDecisionDigest)
{
    public string CurrentResponseDigest { get; init; } = "";
}
public sealed record SalesPriceFloorOriginalLineV1(Guid LineId, int LineNo, CommercialDraftFields Before, CommercialDraftFields After);

public static class SalesPriceFloorDecisionDigestsV1
{
    /// <summary>Stored snapshot identity binding, using the native net formatter; this does not calculate a second price policy.</summary>
    public static bool MatchesCommercialSnapshot(CommercialDraftFields commercial)
    {
        if (!SalesOrderDraft.IsBelowPriceFloor(commercial)) return commercial.FloorException is null;
        if (commercial.Reference is not { } reference || commercial.FloorException is not { } value
            || string.IsNullOrWhiteSpace(value.RequestedBy) || value.PriceListId != reference.PriceListId
            || value.PriceListVersionNo != reference.VersionNo || value.PriceUnitId != reference.PriceUnitId
            || value.PriceUnitId != commercial.PriceUnitId || value.UnitPrice != commercial.UnitPrice
            || value.NetUnitPrice != SalesLinePriceCalculator.NetUnitPriceText(commercial)
            || value.FloorUnitPrice != reference.FloorUnitPrice) return false;
        return value.ApprovedBy is null && value.ApprovedAtUtc is null && value.Reason is null
            || !string.IsNullOrWhiteSpace(value.ApprovedBy) && value.ApprovedBy != value.RequestedBy && value.ApprovedAtUtc is not null
                && !string.IsNullOrWhiteSpace(value.Reason) && value.Reason == value.Reason.Trim()
                && value.Reason.Length <= SalesOrderDraft.MaximumRejectionReasonLength;
    }
    public static string SourceFactsDigest(SalesPriceFloorDecisionSourceV1 source) => Hash(source);
    public static string PreviewDigest(SalesPriceFloorDecisionDetailV1 detail) => Hash(detail with { PreviewDigest = "" });
    public static string OriginalResultDigest(SalesDraftReplayResult result) => Hash(result);
    public static string OriginalDecisionDigest(SalesPriceFloorOriginalReceiptV1 receipt) =>
        Hash(receipt with { OriginalDecisionDigest = "", CurrentResponseDigest = "" });
    public static string CurrentResponseDigest(SalesOrderDraftResponse response) =>
        Hash(response with { IsReplay = false, OriginalFloorApprovalReceipt = null });
    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, SalesDraftJson.Options)));
}
