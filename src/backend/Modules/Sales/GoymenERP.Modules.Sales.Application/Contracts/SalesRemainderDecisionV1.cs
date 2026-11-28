using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application.Contracts;

public enum SalesRemainderDecisionReadStateV1 { Found, NotFound, Denied, Unavailable }
public sealed record SalesRemainderDecisionReadResultV1(SalesRemainderDecisionReadStateV1 State, SalesRemainderDecisionDetailV1? Detail);
public sealed record SalesRemainderOriginalVersionReadResultV1(SalesRemainderDecisionReadStateV1 State,
    [property: JsonIgnore] SalesRemainderOriginalReceiptV1? Receipt)
{ [JsonIgnore] public long? CurrentVersion { get; init; } }
public interface ISalesRemainderDecisionReadV1
{
    Task<SalesRemainderPendingReadResultV1> ListPendingRemainderDecisionsAsync(int take, Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
    Task<SalesRemainderDecisionReadResultV1> ReadRemainderDecisionAsync(Guid draftId, Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
    Task<SalesRemainderOriginalVersionReadResultV1> ReadRemainderOriginalVersionAsync(Guid draftId, string expectedVersion, Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}
public sealed record SalesRemainderPendingV1(Guid CompanyId, Guid DraftId, string DocumentNo, string? DraftCreatedBy, long CurrentVersion,
    int ChangeNo, int RequestRevision, Guid LineId, string RequestedBy, DateTimeOffset RequestedAtUtc);
public sealed record SalesRemainderPendingReadResultV1(SalesRemainderDecisionReadStateV1 State, Guid CompanyId,
    IReadOnlyList<SalesRemainderPendingV1>? Items, bool HasMore, bool? IsSalesSupport);
/// <summary>Current quantity-only decision. The native nineteen-field recorded change is reused without price/general order access.</summary>
public sealed record SalesRemainderDecisionDetailV1(Guid CompanyId, Guid DraftId, string DocumentNo, string Version,
    int Revision, string State, string ApprovalState, SalesDraftRemainderChangeV1 Change, SalesRemainderDecisionLineV1? Line,
    bool ApprovalSupported, bool RejectionSupported, string SourceFactsDigest, string PreviewDigest)
{
    [JsonIgnore] public string? DraftCreatedBy { get; init; }
    [JsonIgnore] public bool? IsSalesSupport { get; init; }
}
public sealed record SalesRemainderDecisionLineV1(Guid Id, int LineNo, Guid ProductId, string ProductCode, string ProductName,
    Guid OrderUnitId, string OrderUnitCode, string CurrentQuantity, string Remainder);
public sealed record SalesRemainderDecisionSourceV1([property: JsonIgnore] SalesOrderDraft Order, [property: JsonIgnore] RemainderChangeFacts Change);
public interface ISalesRemainderDecisionReadStoreV1
{
    Task<IReadOnlyList<SalesRemainderPendingV1>> ListPendingRemainderDecisionSourceAsync(int take, Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default);
    Task<SalesRemainderDecisionSourceV1?> ReadRemainderDecisionSourceAsync(Guid draftId, Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default);
    Task<SalesRemainderOriginalVersionReadResultV1> ReadRemainderOriginalVersionAsync(Guid draftId, long expectedVersion, string actorId, Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default);
    Task<SalesOrderDraftResponse> AttachRemainderOriginalReceiptAsync(SalesOrderDraftResponse response, Guid draftId, int changeNo,
        SalesDraftRemainderDecisionRequest request, CancellationToken cancellationToken = default);
}
/// <summary>
/// Original decision provenance, not a fabricated original full draft answer. Native remainder replay restores the CURRENT
/// draft; CurrentResponseDigest binds that observed response separately from the immutable Audit/decision step N to N+1.
/// </summary>
public sealed record SalesRemainderOriginalReceiptV1(Guid CompanyId, Guid DraftId, string DocumentNo, int ChangeNo, Guid LineId,
    string CommandName, string ActorId, string IdempotencyKeyHash, string PayloadHash, long ExpectedVersion, long ResultVersion,
    string OriginalCorrelationId, DateTimeOffset RecordedAtUtc, SalesDraftRemainderChangeV1 OriginalChange,
    Guid AuditEventId, string AuditIntegrityHash, string SourceFactsDigest, string OriginalDecisionDigest)
{
    public string CurrentResponseDigest { get; init; } = "";
    [JsonIgnore] public string? DraftCreatedBy { get; init; }
    [JsonIgnore] public bool CreatorProofEstablished { get; init; }
}

public static class SalesRemainderDecisionDigestsV1
{
    public static string SourceFactsDigest(Guid companyId, Guid draftId, string documentNo, SalesDraftRemainderChangeV1 change) => Hash(new
    { companyId, draftId, documentNo, change.ChangeNo, change.Revision, change.LineId, change.LineNo, change.Kind,
        change.OrderedQuantity, change.RequestedQuantity, change.Reason, change.RequestedBy, change.RequestedAtUtc,
        change.LoadedAtRequest, change.ShippedAtRequest, change.DeliveredAtRequest });
    public static string PreviewDigest(SalesRemainderDecisionDetailV1 detail) => Hash(detail with { PreviewDigest = "" });
    public static string OriginalDecisionDigest(SalesRemainderOriginalReceiptV1 receipt) => Hash(receipt with { OriginalDecisionDigest = "", CurrentResponseDigest = "" });
    public static string CurrentResponseDigest(SalesOrderDraftResponse response) => Hash(response with { IsReplay = false, OriginalRemainderDecisionReceipt = null });
    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, SalesDraftJson.Options)));
}
