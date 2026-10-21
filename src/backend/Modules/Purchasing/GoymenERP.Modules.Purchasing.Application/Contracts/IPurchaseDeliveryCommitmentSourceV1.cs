namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// Purchasing-owned, company-scoped delivery commitment facts for one purchase order line. This read
/// publishes expected-supply metadata only: it does not prove dispatch, in-transit state, receipt,
/// acceptance, physical stock or free stock. Reading creates no record or stock effect.
///
/// The consumer authorizes its own operation and fences company/actor before and after the awaited call.
/// Missing and foreign-company references are indistinguishable <see langword="null"/>.
/// </summary>
public interface IPurchaseDeliveryCommitmentSourceV1
{
    Task<PurchaseDeliveryCommitmentSourceV1?> ReadAsync(Guid purchaseOrderId, Guid purchaseOrderLineId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Immutable Purchasing source facts. Quantities remain the owner's exact text; no sum, tolerance,
/// conversion or commercial calculation is implied. <see cref="PurchaseOrderVersion"/> is the source
/// version observed by the consumer. Slice order is explicit through <see cref="PurchaseDeliveryCommitmentSliceV1.SliceNo"/>.
/// </summary>
public sealed record PurchaseDeliveryCommitmentSourceV1(
    Guid CompanyId,
    Guid PurchaseOrderId,
    Guid PurchaseOrderLineId,
    long PurchaseOrderVersion,
    IReadOnlyList<PurchaseDeliveryCommitmentSliceV1> Slices);

/// <summary>
/// One quantity/date commitment slice. A null <see cref="SupplierConfirmedAtUtc"/> means that this source
/// exposes no supplier-confirmation fact for the slice; it does not create a workflow status. A timestamp
/// is only the Purchasing owner's immutable confirmation fact, not dispatch or in-transit evidence.
/// </summary>
public sealed record PurchaseDeliveryCommitmentSliceV1(
    Guid DeliverySliceId,
    int SliceNo,
    string QuantityText,
    DateOnly ExpectedOn,
    DateTimeOffset? SupplierConfirmedAtUtc);