using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application.Contracts;

public enum SalesDiscountDecisionReadStateV1 { Found, NotFound, Denied, Unavailable }
public sealed record SalesDiscountDecisionReadResultV1(SalesDiscountDecisionReadStateV1 State, SalesDiscountDecisionDetailV1? Detail);
public interface ISalesDiscountDecisionReadV1
{
    Task<SalesDiscountDecisionReadResultV1> ReadDiscountDecisionAsync(Guid draftId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
    Task<SalesDiscountOriginalVersionReadResultV1> ReadDiscountOriginalVersionAsync(Guid draftId, string expectedVersion,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}
public sealed record SalesDiscountOriginalVersionReadResultV1(SalesDiscountDecisionReadStateV1 State,
    [property: JsonIgnore] SalesDiscountOriginalReceiptV1? Receipt)
{
    [JsonIgnore] public long? CurrentVersion { get; init; }
}

/// <summary>Selected commercial facts only; no general draft, margin, Product or current price-list read.</summary>
public sealed record SalesDiscountDecisionDetailV1(Guid CompanyId, Guid DraftId, string DocumentNo, string Version,
    int Revision, string State, string ApprovalState, Guid PartyId, Guid CompanyAccountId, Guid CurrencyId, DateOnly BusinessDate, bool IsSubcontractService,
    IReadOnlyList<SalesDiscountDecisionLineV1> Lines, SalesPriceDisclosure PriceDisclosure, bool ApprovalSupported,
    string SourceFactsDigest, string PreviewDigest)
{
    [JsonIgnore] public SalesDiscountDecisionSourceV1? Source { get; init; }
}
public sealed record SalesDiscountDecisionLineV1(Guid LineId, int LineNo, Guid ProductId, string ProductVersion,
    string ProductCode, string ProductName, Guid OrderUnitId, string Quantity,
    SalesDraftDiscountLimitCheckV1? DiscountCheck, SalesDraftLinePricingV1? Pricing);

/// <summary>Native owner/store seam. Commercial values never pass through the withheld human projection.</summary>
public interface ISalesDiscountDecisionReadStoreV1
{
    Task<SalesDiscountDecisionSourceV1?> ReadDiscountDecisionSourceAsync(Guid draftId,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default);
    Task<SalesDiscountOriginalVersionReadResultV1> ReadDiscountOriginalVersionAsync(Guid draftId, long expectedVersion,
        string actorId, Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SalesDiscountOriginalVersionReadResultV1(SalesDiscountDecisionReadStateV1.Unavailable, null));
}
public sealed record SalesDiscountDecisionSourceV1(Guid CompanyId, Guid DraftId, string DocumentNo, long Version,
    int Revision, string State, string ApprovalState, Guid PartyId, Guid CompanyAccountId, Guid CurrencyId, DateOnly BusinessDate, bool IsSubcontractService,
    IReadOnlyList<SalesDiscountDecisionSourceLineV1> Lines);
public sealed record SalesDiscountDecisionSourceLineV1(Guid LineId, int LineNo, Guid ProductId, string ProductVersion,
    string ProductCode, string ProductName, Guid OrderUnitId, string Quantity, CommercialDraftFields Commercial);

/// <summary>
/// Private proof of the original command, its two stored disclosure variants and its immutable commercial Audit facts.
/// PayloadHash is the actual stored native hash; trimmed Audit reasons cannot reconstruct the raw request/order/version text.
/// </summary>
public sealed record SalesDiscountOriginalReceiptV1(Guid CompanyId, Guid DraftId, string CommandName, string ActorId,
    string IdempotencyKeyHash, string PayloadHash, long ExpectedVersion, long ResultVersion, string OriginalCorrelationId,
    DateTimeOffset RecordedAtUtc, Guid AuditEventId, string AuditIntegrityHash,
    IReadOnlyList<SalesDiscountOriginalLineV1> Lines, SalesDraftReplayResult OriginalResult,
    string OriginalResultDigest, string OriginalDecisionDigest)
{
    public string CurrentResponseDigest { get; init; } = "";
}
public sealed record SalesDiscountOriginalLineV1(Guid LineId, int LineNo, CommercialDraftFields Before, CommercialDraftFields After);

public static class SalesDiscountDecisionDigestsV1
{
    /// <summary>Stored snapshot identity binding, using the native net formatter; this does not calculate a second price policy.</summary>
    public static bool MatchesCommercialSnapshot(CommercialDraftFields commercial)
    {
        if (commercial.DiscountCheck is not { } check) return true; // Native legacy unchecked facts remain unchecked.
        try
        {
            var limit = check.LimitPercent is null ? SalesDiscountLimit.Undefined
                : check.LimitProfileId is { } profile && check.LimitProfileVersion is { } version
                    ? SalesDiscountLimit.Defined(SalesLinePriceCalculator.Parse(check.LimitPercent), profile, version)
                    : throw new ArgumentException("İskonto limit profili eksik.");
            if (check.LimitPercent is null && (check.LimitProfileId is not null || check.LimitProfileVersion is not null)) return false;
            var expected = SalesOrderDraft.WithDiscountLimitCheck(commercial with { DiscountCheck = null }, limit, check.CheckedBy, check.CheckedAtUtc).DiscountCheck;
            if (expected is null || expected != (check with { ApprovedBy = null, ApprovedAtUtc = null, Reason = null })) return false;
            return check.ApprovedBy is null ? check.ApprovedAtUtc is null && check.Reason is null
                : check.RequiresApproval && !string.IsNullOrWhiteSpace(check.ApprovedBy) && check.ApprovedAtUtc is not null
                    && (commercial.Reference is null || check.ApprovedBy != check.CheckedBy)
                    && !string.IsNullOrWhiteSpace(check.Reason) && check.Reason == check.Reason.Trim()
                    && check.Reason.Length <= SalesOrderDraft.MaximumRejectionReasonLength;
        }
        catch (Exception failure) when (failure is ArgumentException or InvalidOperationException or FormatException or OverflowException or SalesRuleException)
        { return false; }
    }
    public static string SourceFactsDigest(SalesDiscountDecisionSourceV1 source) => Hash(source);
    public static string PreviewDigest(SalesDiscountDecisionDetailV1 detail) => Hash(detail with { PreviewDigest = "" });
    public static string OriginalResultDigest(SalesDraftReplayResult result) => Hash(result);
    public static string OriginalDecisionDigest(SalesDiscountOriginalReceiptV1 receipt) =>
        Hash(receipt with { OriginalDecisionDigest = "", CurrentResponseDigest = "" });
    public static string CurrentResponseDigest(SalesOrderDraftResponse response) =>
        Hash(response with { IsReplay = false, OriginalDiscountApprovalReceipt = null });
    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, SalesDraftJson.Options)));
}
