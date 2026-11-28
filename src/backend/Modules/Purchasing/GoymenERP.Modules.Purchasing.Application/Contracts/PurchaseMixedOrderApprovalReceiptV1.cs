using System.Text.Json.Serialization;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>Private original command facts, reconstructed only from actual immutable native Attempt/Audit/revision artifacts.</summary>
public sealed record PurchaseMixedOrderApprovalReceiptV1(Guid CompanyId, Guid OrderId, string ActorId,
    string IdempotencyKeyHash, string PayloadHash, string OriginalCorrelationId, long ExpectedVersion, long ResultVersion,
    DateTimeOffset RecordedAtUtc, Guid AuditEventId, string AuditIntegrityHash, PurchaseMixedOrderRecordedApprovalV1 Original);

// This exact mixed-path-only metadata is appended to the native parent Audit after-state. Native legacy Audit JSON is unchanged.
public sealed record PurchaseMixedOrderRecordedApprovalV1(Guid CompanyId, Guid OrderId, string ActorId,
    string IdempotencyKeyHash, string PayloadHash, string OriginalCorrelationId, DateTimeOffset RecordedAtUtc,
    ApprovePurchaseOrderRequest FrozenRequest, IReadOnlyList<PurchaseOrderServiceMembership> Membership,
    IReadOnlyList<PurchaseMixedOrderRecordedChildV1> Children, PurchaseOrderDetailView Visible, PurchaseOrderDetailView Withheld)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? HeldFromPriceRevisionVersion { get; init; }
}
public sealed record PurchaseMixedOrderRecordedChildV1(PurchaseServiceOrderState OriginalBefore,
    PurchaseServiceOrderState Prepared, PurchaseServiceOrderState After);
public sealed record PurchaseMixedOrderApprovalPreparationV1([property: JsonIgnore] IReadOnlyList<PurchaseServiceOrderState> Services,
    [property: JsonIgnore] IReadOnlyDictionary<Guid, PurchaseOrderLineTheoreticalKilograms> TheoreticalKilograms);
public sealed record PurchaseMixedOrderApprovalStoreAnswerV1(
    [property: JsonIgnore] PurchaseMixedOrderApprovalReceiptV1? Receipt,
    PurchasingTerminalRefusal? Refusal, bool IsReplay, string OriginalCorrelationId);

public interface IPurchaseMixedOrderApprovalStoreV1
{
    bool MixedApprovalEntryIsClean => false;
    Task<PurchaseMixedOrderApprovalStoreAnswerV1> ApproveMixedOrderAsync(Guid orderId, ApprovePurchaseOrderRequest request,
        PurchasingWriteEnvelope envelope, string requestHash, bool cleanBeforeAuthority,
        Func<StoredPurchaseOrder, IReadOnlyList<PurchaseServiceOrderState>, CancellationToken, Task<PurchaseMixedOrderApprovalPreparationV1>> prepare,
        Func<StoredPurchaseOrder, string, PurchaseOrderDetailView> project,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default) =>
        Task.FromException<PurchaseMixedOrderApprovalStoreAnswerV1>(new PurchasingServiceException(503, "service_unavailable"));
}

public static class PurchaseMixedOrderApprovalIntentV1
{
    // Same existing ADR-011 Purchasing/Audit key identity; the public command hash remains PurchasingRequestHash.
    public static string KeyHash(string key) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key.Trim())));
    public static ApprovePurchaseOrderRequest Freeze(ApprovePurchaseOrderRequest request)
    {
        if (request.MixedServices is not { Count: > 0 } values || values.Any(x => x is null || x.ServiceOrderId == Guid.Empty
            || x.LineIds is not { Count: > 0 } || x.LineIds.Any(id => id == Guid.Empty)
            || x.LineIds.Distinct().Count() != x.LineIds.Count)
            || values.Select(x => x.ServiceOrderId).Distinct().Count() != values.Count)
            throw new PurchasingServiceException(400, "validation_failed");
        _ = Version(request.ExpectedVersion);
        foreach (var child in values) _ = PurchaseMixedOrderMembershipV1.SelectedChildVersion(child.ExpectedVersion);
        return request with { MixedServices = Array.AsReadOnly(values.Select(x => x with { LineIds = Array.AsReadOnly(x.LineIds.ToArray()) }).ToArray()) };
    }
    public static long Version(string text)
    {
        if (!long.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var version)
            || version < 1 || version == long.MaxValue || version.ToString(System.Globalization.CultureInfo.InvariantCulture) != text)
            throw new PurchasingServiceException(400, "validation_failed");
        return version;
    }
}
