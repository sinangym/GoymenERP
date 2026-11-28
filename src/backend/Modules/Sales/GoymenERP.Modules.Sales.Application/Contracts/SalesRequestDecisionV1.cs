using System.Security.Cryptography;
using System.Text.Json;
using GoymenERP.Modules.Sales.Domain.Requests;

namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>Current, price-free owner facts. An Approved request still has the native rejection action (SAT-D23).</summary>
public sealed record SalesRequestDecisionDetailV1(SalesRequestDetailView Detail, bool ApprovalSupported,
    bool RejectionSupported, string SourceFactsDigest, string PreviewDigest);

public enum SalesRequestDecisionReadStateV1 { Found, NotFound, Denied, Unavailable }
public sealed record SalesRequestDecisionReadResultV1(SalesRequestDecisionReadStateV1 State, SalesRequestDecisionDetailV1? Detail);

public interface ISalesRequestDecisionReadV1
{
    Task<SalesRequestDecisionReadResultV1> ReadRequestDecisionAsync(Guid requestId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}

/// <summary>Owner-internal selected read; the caller supplies the same captured permission/consumer fence for each SQL await.</summary>
public interface ISalesRequestDecisionReadStoreV1
{
    Task<SalesRequest?> ReadRequestDecisionAsync(Guid requestId, Func<CancellationToken, Task> freshFence,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A genuine ORIGINAL DECISION from an immutable attempt and Audit event. It is not an old HTTP detail snapshot.
/// CurrentResponseDigest binds the separately observed native response, whose replay intentionally reads today's row.
/// No Audit JSON or current line is used to invent original decision facts.
/// </summary>
public sealed record SalesRequestOriginalDecisionReceiptV1(Guid CompanyId, Guid RequestId, string DocumentNo,
    Guid PartyId, Guid CompanyAccountId, DateOnly BusinessDate, string CommandName, string ActorId, string IdempotencyKeyHash,
    string PayloadHash, string ExpectedVersion, string ResultVersion, string State, SalesRequestDecisionV1 Decision,
    string OriginalCorrelationId, Guid AuditEventId, string AuditIntegrityHash, string OriginalDecisionDigest, string CurrentResponseDigest);

public static class SalesRequestDecisionDigestsV1
{
    public static string SourceFactsDigest(SalesRequestDetailView detail) => Hash(detail);
    public static string PreviewDigest(SalesRequestDecisionDetailV1 preview) => Hash(preview with { PreviewDigest = "" });
    public static string CurrentResponseDigest(SalesRequestResponse response) =>
        Hash(response with { IsReplay = false, OriginalDecisionReceipt = null });
    public static string OriginalDecisionDigest(SalesRequestOriginalDecisionReceiptV1 receipt) =>
        Hash(receipt with { OriginalDecisionDigest = "", CurrentResponseDigest = "" });

    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, SalesDraftJson.Options)));
}
