using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Sales.Domain.ApprovalRequests;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application.Contracts;

public sealed record SalesOverDeliveryRequestTargetV1(Guid RequestId, Guid OrderId, int RequestNo, int SubjectRevision, Guid LineId, string RequestVersion);
public sealed record SalesOverDeliveryRequestDecisionCommandV1(SalesOverDeliveryRequestTargetV1 Target, string ExpectedVersion, string? Reason);
public enum SalesOverDeliveryRequestDecisionReadStateV1 { Found, NotFound, Denied, Unavailable }
public sealed record SalesOverDeliveryRequestDecisionReadResultV1(SalesOverDeliveryRequestDecisionReadStateV1 State, SalesOverDeliveryRequestDecisionDetailV1? Detail);
public sealed record SalesOverDeliveryRequestOriginalVersionReadResultV1(SalesOverDeliveryRequestDecisionReadStateV1 State,
    [property: JsonIgnore] SalesOverDeliveryRequestOriginalReceiptV1? Receipt)
{ [JsonIgnore] public long? CurrentVersion { get; init; } }
public interface ISalesOverDeliveryRequestDecisionReadV1
{
    Task<SalesOverDeliveryRequestDecisionReadResultV1> ReadOverDeliveryRequestDecisionAsync(Guid requestId, Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
    Task<SalesOverDeliveryRequestOriginalVersionReadResultV1> ReadOverDeliveryRequestOriginalVersionAsync(Guid requestId, string expectedRequestVersion, Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
    Task<SalesApprovalRequestCommandResult> ApproveSelectedOverDeliveryRequestAsync(SalesOverDeliveryRequestTargetV1 target, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<SalesApprovalRequestCommandResult> RejectSelectedOverDeliveryRequestAsync(SalesOverDeliveryRequestTargetV1 target, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default);
}

// Current owner identity/quantities only. This port grants neither OrdersRead nor price visibility.
public sealed record SalesOverDeliveryRequestDecisionDetailV1(Guid CompanyId, Guid RequestId, SalesApprovalRequestView Request,
    SalesOverDeliveryRequestOrderV1 Order, bool ApprovalSupported, bool RejectionSupported, string SourceFactsDigest, string PreviewDigest);
public sealed record SalesOverDeliveryRequestOrderV1(Guid Id, string DocumentNo, long Version, int Revision, string State, string ApprovalState,
    Guid PartyId, Guid CompanyAccountId, Guid CurrencyId, DateOnly BusinessDate, SalesOverDeliveryRequestLineV1? Line);
public sealed record SalesOverDeliveryRequestLineV1(Guid Id, int LineNo, Guid ProductId, string ProductCode, string ProductName,
    Guid OrderUnitId, string OrderUnitCode, string CurrentQuantity);
public sealed record SalesOverDeliveryRequestDecisionSourceV1([property: JsonIgnore] SalesApprovalRequest Request, [property: JsonIgnore] SalesOrderDraft Order);
public interface ISalesOverDeliveryRequestDecisionReadStoreV1
{
    Task<SalesOverDeliveryRequestDecisionSourceV1?> ReadOverDeliveryRequestDecisionSourceAsync(Guid requestId, Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default);
    Task<SalesOverDeliveryRequestOriginalVersionReadResultV1> ReadOverDeliveryRequestOriginalVersionAsync(Guid requestId, long expectedRequestVersion, string actorId, Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default);
}

/// <summary>Actual request Audit/attempt. Approval additionally binds the original order version step; rejection has no order step.</summary>
public sealed record SalesOverDeliveryRequestOriginalReceiptV1(Guid CompanyId, SalesOverDeliveryRequestTargetV1 Target, string OrderDocumentNo,
    string CommandName, string ActorId, string IdempotencyKeyHash, string PayloadHash, long ExpectedRequestVersion, long ResultRequestVersion,
    long? OriginalOrderVersionBefore, long? OriginalOrderVersionAfter, string OriginalCorrelationId, DateTimeOffset DecidedAtUtc,
    SalesApprovalRequestView OriginalRequest, Guid RequestAuditEventId, string RequestAuditIntegrityHash,
    Guid? OrderAuditEventId, string? OrderAuditIntegrityHash, string SourceFactsDigest, string OriginalResultDigest, string OriginalDecisionDigest)
{ public string CurrentResponseDigest { get; init; } = ""; }

public static class SalesOverDeliveryRequestDecisionDigestsV1
{
    public static string SourceFactsDigest(Guid companyId, Guid requestId, SalesApprovalRequestView request, string orderDocumentNo) => Hash(new
    { companyId, requestId, request.Kind, request.SubjectId, request.RequestNo, request.SubjectRevision, request.LineId, request.Ordered,
        request.Quantity, request.RequestedTotal, request.LoadingInstructionId, request.ShipmentId, request.Note, request.RequestedBy, request.RequestedAtUtc, orderDocumentNo });
    public static string PreviewDigest(SalesOverDeliveryRequestDecisionDetailV1 detail) => Hash(detail with { PreviewDigest = "" });
    public static string CurrentResponseDigest(SalesApprovalRequestCommandResult result) => Hash(result with { IsReplay = false, OriginalOverDeliveryReceipt = null });
    public static string OriginalDecisionDigest(SalesOverDeliveryRequestOriginalReceiptV1 receipt) => Hash(receipt with { OriginalDecisionDigest = "", CurrentResponseDigest = "" });
    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, SalesDraftJson.Options)));
}
