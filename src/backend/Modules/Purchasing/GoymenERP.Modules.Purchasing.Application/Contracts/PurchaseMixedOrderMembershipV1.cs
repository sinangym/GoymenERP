using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>Explicit Draft membership edit. Native create and approval request bodies/hashes remain unchanged.</summary>
public sealed record BindPurchaseOrderServicesRequest(string ExpectedVersion, IReadOnlyList<PurchaseMixedOrderApprovalSelectionV1> Services);
public interface IPurchaseMixedOrderMembershipServiceV1
{
    Task<PurchaseOrderResponse> BindServicesAsync(Guid orderId, BindPurchaseOrderServicesRequest request,
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default);
}
public static class PurchaseMixedOrderMembershipV1
{
    public const string Command = "purchasing.purchase-order.service-bind.v1";
    public const string Event = "purchasing.purchase-order.services-bound.v1";
    public static BindPurchaseOrderServicesRequest Freeze(BindPurchaseOrderServicesRequest value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _ = PurchaseMixedOrderApprovalIntentV1.Version(value.ExpectedVersion); // Only the parent advances in this command.
        if (value.Services is not { Count: > 0 } services || services.Any(x => x is null || x.ServiceOrderId == Guid.Empty
            || x.LineIds is not { Count: > 0 } || x.LineIds.Any(id => id == Guid.Empty)
            || x.LineIds.Distinct().Count() != x.LineIds.Count)
            || services.Select(x => x.ServiceOrderId).Distinct().Count() != services.Count)
            throw new PurchasingServiceException(400, "validation_failed");
        foreach (var child in services) _ = SelectedChildVersion(child.ExpectedVersion);
        return new(value.ExpectedVersion, Array.AsReadOnly(services.Select(x => x with { LineIds = Array.AsReadOnly(x.LineIds.ToArray()) }).ToArray()));
    }
    public static long SelectedChildVersion(string text)
    {
        // A selected child's version is read, not advanced. Do not impose approval's incrementability on a Draft metadata edit.
        if (!long.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value)
            || value < 1 || value.ToString(System.Globalization.CultureInfo.InvariantCulture) != text)
            throw new PurchasingServiceException(400, "validation_failed");
        return value;
    }
}
// Private original result, proven by the actual parent Attempt/Audit and immutable selected child revisions.
public sealed record PurchaseMixedOrderMembershipReceiptV1(Guid CompanyId, Guid OrderId, string ActorId,
    string KeyHash, string PayloadHash, string OriginalCorrelationId, DateTimeOffset RecordedAtUtc,
    BindPurchaseOrderServicesRequest FrozenRequest, PurchaseOrderDetailView Visible, PurchaseOrderDetailView Withheld);
public sealed record PurchaseMixedOrderMembershipAnswerV1(
    [property: JsonIgnore] PurchaseMixedOrderMembershipReceiptV1 Receipt, bool IsReplay);
public interface IPurchaseMixedOrderMembershipStoreV1
{
    bool MembershipEntryIsClean => false;
    Task<PurchaseMixedOrderMembershipAnswerV1> BindServicesAsync(Guid orderId, BindPurchaseOrderServicesRequest request,
        PurchasingWriteEnvelope envelope, string hash, bool cleanBeforeAuthority,
        Func<StoredPurchaseOrder, string, PurchaseOrderDetailView> project, Func<CancellationToken, Task> freshFence,
        CancellationToken cancellationToken = default) => Task.FromException<PurchaseMixedOrderMembershipAnswerV1>(
            new PurchasingServiceException(503, "service_unavailable"));
}
