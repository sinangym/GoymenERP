using System.Security.Cryptography;
using System.Text.Json;
using GoymenERP.Modules.Sales.Domain.ApprovalRequests;
using GoymenERP.Modules.Sales.Domain.Quotes;

namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>The selected request and its own version are distinct from the native quote version.</summary>
public sealed record SalesQuoteConversionTargetV1(Guid RequestId, Guid QuoteId, int RequestNo, int SubjectRevision, string RequestVersion);
public sealed record SalesQuoteConversionDecisionDetailV1(Guid RequestId, SalesApprovalRequestView Request, SalesQuoteDetailView Quote,
    bool ApprovalSupported, bool RejectionSupported, string SourceFactsDigest, string PreviewDigest);
public enum SalesQuoteDecisionReadStateV1 { Found, NotFound, Denied, Unavailable }
public sealed record SalesQuoteDecisionReadResultV1(SalesQuoteDecisionReadStateV1 State, SalesQuoteConversionDecisionDetailV1? Detail);

public interface ISalesQuoteConversionDecisionReadV1
{
    Task<SalesQuoteDecisionReadResultV1> ReadQuoteConversionDecisionAsync(Guid requestId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
    Task<SalesQuoteResponse> ApproveSelectedQuoteConversionAsync(SalesQuoteConversionTargetV1 target,
        SalesQuoteExpiryApprovalRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<SalesApprovalRequestCommandResult> RejectSelectedQuoteConversionAsync(SalesQuoteConversionTargetV1 target,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default);
}

public sealed record SalesQuoteDecisionSourceV1(SalesApprovalRequest Request, SalesQuote Quote);
public interface ISalesQuoteDecisionReadStoreV1
{
    Task<SalesQuoteDecisionSourceV1?> ReadQuoteConversionDecisionAsync(Guid requestId,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default);
}

/// <summary>Private native boundary context, never a new public request/hash format.</summary>
public sealed record SalesQuoteDecisionCommandV1(SalesQuoteConversionTargetV1 Target, string ExpectedVersion, string? NoteOrReason);

/// <summary>Original decision only. CurrentResponseDigest separately binds the native current-row HTTP replay.</summary>
public sealed record SalesQuoteOriginalDecisionReceiptV1(Guid CompanyId, SalesQuoteConversionTargetV1 Target, string CommandName,
    string ActorId, string IdempotencyKeyHash, string PayloadHash, string ExpectedVersion, string ResultVersion,
    string OriginalState, DateTimeOffset DecidedAtUtc, string? NoteOrReason, string OriginalCorrelationId,
    Guid AuditEventId, string AuditIntegrityHash, string OriginalDecisionDigest, string CurrentResponseDigest);

public static class SalesQuoteDecisionDigestsV1
{
    public static string SourceFactsDigest(SalesApprovalRequestView request, SalesQuoteDetailView quote) => Hash(new { request, quote });
    public static string PreviewDigest(SalesQuoteConversionDecisionDetailV1 detail) => Hash(detail with { PreviewDigest = "" });
    public static string CurrentResponseDigest(SalesQuoteResponse response) => Hash(response with { IsReplay = false, OriginalDecisionReceipt = null });
    public static string CurrentResponseDigest(SalesApprovalRequestCommandResult response) => Hash(response with { IsReplay = false, OriginalDecisionReceipt = null });
    public static string OriginalDecisionDigest(SalesQuoteOriginalDecisionReceiptV1 receipt) => Hash(receipt with { OriginalDecisionDigest = "", CurrentResponseDigest = "" });
    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, SalesDraftJson.Options)));
}
