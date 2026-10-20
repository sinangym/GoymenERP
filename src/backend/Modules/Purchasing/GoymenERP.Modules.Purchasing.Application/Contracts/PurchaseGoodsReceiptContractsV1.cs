using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>Two measured facts, never a client conversion. Company, unit, owner, custodian, documents and block are server facts.</summary>
public sealed record RecordPurchaseGoodsReceiptV1(
    string ExpectedPurchaseOrderVersion, string ExpectedProductVersion, Guid AcceptanceId,
    string AcceptedQuantity, string MeasuredQuantityKg, Guid LocationId,
    Guid LotId, Guid LabelId, string LabelCode, string? HeatNumber, string? CoilNumber,
    Guid MovementId, DateTimeOffset OccurredAtUtc);

public sealed record PurchaseGoodsReceiptViewV1(
    Guid CompanyId, Guid AcceptanceId, Guid PurchaseOrderId, Guid PurchaseOrderLineId, string PurchaseOrderVersion,
    Guid ProductId, string ProductVersion, Guid UnitId, string AcceptedQuantity, string RemainingQuantity,
    Guid LotId, Guid LabelId, string LabelCode, string LabelVersion, Guid MovementId, Guid LocationId,
    string MeasuredQuantityKg, string InitialBlock, DateTimeOffset OccurredAtUtc,
    [property: JsonRequired] PurchaseGoodsReceiptQuantityV2? StockItemQuantity = null);

/// <summary>
/// Immutable Purchasing read projection of Inventory's stored quantity-v2 receipt snapshot.
/// It is historical context, never a conversion request or a reread of current Product/Settings masters.
/// </summary>
public sealed record PurchaseGoodsReceiptQuantityV2(
    Guid CompanyId,
    PurchaseGoodsReceiptQuantitySubjectV2 Subject,
    PurchaseGoodsReceiptQuantitySourceV2 Source,
    PurchaseGoodsReceiptQuantityValueV2 BaseQuantity,
    PurchaseGoodsReceiptAlternateQuantityV2? AlternateQuantity);

public sealed record PurchaseGoodsReceiptQuantitySubjectV2(string Kind, Guid Id);
public sealed record PurchaseGoodsReceiptQuantitySourceV2(string Kind, Guid Id, string Version);
public sealed record PurchaseGoodsReceiptQuantityValueV2(
    string Value, Guid UnitId, Guid ProfileId, string ProfileVersion, string Dimension, int Scale);
public sealed record PurchaseGoodsReceiptAlternateQuantityV2(
    PurchaseGoodsReceiptQuantityValueV2 Quantity,
    PurchaseGoodsReceiptQuantitySourceV2 ConversionSource);

public sealed record PurchaseGoodsReceiptResponseV1(PurchaseGoodsReceiptViewV1 Receipt, bool IsReplay, string OriginalCorrelationId);
public sealed record PurchaseGoodsReceiptDetailV1(PurchaseGoodsReceiptViewV1 Receipt);
public sealed record PurchaseGoodsReceiptPageV1(IReadOnlyList<PurchaseGoodsReceiptViewV1> Items, bool SearchWindowFull);
public sealed record PurchaseReceiptLocationV1(Guid LocationId, string LocationCode, string LocationName, string SiteName);
public sealed record PurchaseReceiptSourceV1(Guid CompanyId, Guid PurchaseOrderId, Guid PurchaseOrderLineId,
    string PurchaseOrderVersion, Guid ProductId, string ProductVersion, Guid UnitId, DateOnly EffectiveOn);
public sealed record PurchaseGoodsReceiptOptionsV1(PurchaseReceiptSourceV1 Source,
    IReadOnlyList<PurchaseReceiptLocationV1> Locations, bool SearchWindowFull);

public interface IPurchaseGoodsReceiptServiceV1
{
    Task<PurchaseGoodsReceiptResponseV1> RecordGoodsReceiptAsync(Guid orderId, Guid lineId, RecordPurchaseGoodsReceiptV1 request,
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<PurchaseGoodsReceiptOptionsV1> GetGoodsReceiptOptionsAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default);
    Task<PurchaseGoodsReceiptDetailV1> GetGoodsReceiptAsync(Guid orderId, Guid acceptanceId,
        CancellationToken cancellationToken = default);
    Task<PurchaseGoodsReceiptPageV1> GetGoodsReceiptsAsync(Guid orderId, int limit = 100, CancellationToken cancellationToken = default);
}

public static class PurchaseGoodsReceiptApiV1
{
    public const string RecordPath = PurchasingApiV1.OrderDetailPath + "/lines/{lineId}/goods-receipts";
    public const string OptionsPath = PurchasingApiV1.OrderDetailPath + "/lines/{lineId}/goods-receipt-options";
    public const string ReceiptsPath = PurchasingApiV1.OrderDetailPath + "/goods-receipts";
    public const string ReceiptDetailPath = ReceiptsPath + "/{acceptanceId}";
    public const string InitialBlock = "Free";
    public const string QuantitySubjectKind = "Product";
    public const string QuantitySourceKind = "purchasing.goods-acceptance";
    public const string QuantityDimension = "Mass";
    public const int MaximumKilogramScale = 3;
}

public static class PurchaseGoodsReceiptCodesV1
{
    public const string Unavailable = "purchasing_goods_receipt_unavailable";
    public const string DocumentSourceUnavailable = "purchasing_receipt_document_source_unavailable";
    public const string SourceInvalid = "purchasing_goods_receipt_source_invalid";
}

/// <summary>
/// Sinan #66/5829412526 explicitly defers mandatory receipt documents. This is an accepted
/// NEW-receipt policy, not a fallback for an unavailable source. Delivery-note/weighbridge-ticket
/// images or PDFs are optional; attachment storage/security remains with its existing owner.
/// Future activation needs an accepted policy/source change, never a rewrite of stored receipts.
/// </summary>
public static class PurchaseGoodsReceiptDocumentPolicyV1
{
    public const bool MandatoryDocumentsEnabled = false;
    public static IReadOnlyList<string> RequiredForNewReceipt { get; } = Array.Empty<string>();
}

/// <summary>
/// Host owns ONE transaction/connection. Both existing participants borrow it, Purchasing first, Inventory
/// second with Purchasing's returned post-stage order version. Invoke beforeCommit AFTER every stage/read,
/// immediately before sole commit. Any throw rolls back all rows/audits/outbox and disposes participants,
/// clears pending tracked/audit state; no implicit callback retry and no local DB provisioning.
/// </summary>
public interface IPurchaseGoodsReceiptTransactionV1
{
    Task<T> ExecuteAsync<T>(Guid companyId, Func<IPurchaseGoodsReceiptParticipantsV1, CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforeCommit, CancellationToken cancellationToken = default);
}

public interface IPurchaseGoodsReceiptParticipantsV1
{
    /// <summary>
    /// Inventory-owned same-key/actor/acceptance lookup in THIS borrowed transaction; null only for a new
    /// attempt. Reconstruct from immutable original rows, never mutable Product/Settings/location masters.
    /// Different actor/target under the key is a non-disclosing idempotency conflict. This read grants nothing.
    /// </summary>
    Task<PurchaseReceiptStockRequestV1?> ReadOriginalAsync(Guid acceptanceId, PurchasingWriteEnvelope envelope, CancellationToken cancellationToken);

    /// <summary>
    /// First attempts only. Resolve authorized location/owner/custodian from ACCEPTED owner sources.
    /// Under Sinan #66/5829412526, mandatory documents are deferred: do not query an unavailable document
    /// policy or throw DocumentSourceUnavailable for that deferral. The service supplies the accepted
    /// new-receipt document policy. Missing position facts still refuse and roll back both participants.
    /// </summary>
    Task<PurchaseReceiptFactsV1> ResolveFirstAsync(PurchaseReceiptSourceV1 source, Guid locationId, CancellationToken cancellationToken);

    Task<PurchaseAcceptanceStageResultV1> StagePurchaseAsync(PurchaseAcceptanceStageRequestV1 request,
        PurchasingWriteEnvelope envelope, CancellationToken cancellationToken);
    Task<PurchaseReceiptStockResultV1> StageInventoryAsync(PurchaseReceiptStockRequestV1 request,
        PurchasingWriteEnvelope envelope, CancellationToken cancellationToken);
}

/// <summary>Owner reads only; central adapts Organization's named location selector and Inventory's immutable receipt projection.</summary>
public interface IPurchaseGoodsReceiptReadsV1
{
    Task<PurchaseGoodsReceiptOptionsV1> GetOptionsAsync(PurchaseReceiptSourceV1 source, CancellationToken cancellationToken);
    Task<PurchaseGoodsReceiptViewV1?> GetReceiptAsync(Guid orderId, Guid acceptanceId, CancellationToken cancellationToken);
}

// The optional legacy member preserves adapter source compatibility. It does not override the
// accepted new-receipt policy; only ReadOriginalAsync supplies immutable historical requirements.
public sealed record PurchaseReceiptFactsV1(Guid CompanyId, Guid ProductId, string ProductVersion, Guid LocationId,
    string OwnerKind, Guid OwnerId, string CustodianKind, Guid CustodianId, IReadOnlyList<string>? RequiredDocuments = null);

/// <summary>Server-only Inventory adapter DTO. New receipts default to Free; replay adapters preserve the immutable original block.</summary>
public sealed record PurchaseReceiptStockRequestV1(Guid CompanyId, Guid AcceptanceId, Guid PurchaseOrderId, Guid PurchaseOrderLineId,
    string PurchaseOrderVersionAfter, Guid ProductId, string ExpectedProductVersion, Guid PurchaseUnitId,
    Guid LotId, Guid LabelId, string LabelCode, string? HeatNumber, string? CoilNumber, IReadOnlyList<string> RequiredDocuments,
    string OwnerKind, Guid OwnerId, string CustodianKind, Guid CustodianId, Guid LocationId,
    Guid MovementId, string QuantityKg, DateTimeOffset OccurredAtUtc)
{
    // Sinan #66/5829087145 supersedes mandatory quality quarantine for NEW receipts only.
    // ReadOriginalAsync must copy Inventory's stored value, never apply today's default to history.
    // This DTO is not the public request: callers cannot submit block/document/ownership facts.
    public string InitialBlock { get; init; } = PurchaseGoodsReceiptApiV1.InitialBlock;
}

public sealed record PurchaseReceiptStockResultV1(Guid CompanyId, Guid AcceptanceId, Guid PurchaseOrderId, Guid PurchaseOrderLineId,
    string PurchaseOrderVersion, Guid ProductId, string ProductVersion, Guid PurchaseUnitId, Guid LotId, Guid LabelId,
    string LabelVersion, Guid MovementId, string QuantityKg, bool IsReplay, string OriginalCorrelationId,
    PurchaseGoodsReceiptQuantityV2? StockItemQuantity);

public static class PurchaseGoodsReceiptJsonV1
{
    private static readonly Regex Quantity = new("\\A(?:0|[1-9][0-9]{0,14})(?:\\.[0-9]{0,2}[1-9])?\\z", RegexOptions.CultureInvariant);
    public static RecordPurchaseGoodsReceiptV1 Read(string json)
    {
        var request = JsonSerializer.Deserialize<RecordPurchaseGoodsReceiptV1>(json, PurchasingJson.Options)
            ?? throw new JsonException("Receipt body is required.");
        Validate(request);
        return request;
    }

    public static void Validate(RecordPurchaseGoodsReceiptV1 request)
    {
        if (!long.TryParse(request.ExpectedPurchaseOrderVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            || version < 1 || version.ToString(CultureInfo.InvariantCulture) != request.ExpectedPurchaseOrderVersion
            || !Token(request.ExpectedProductVersion) || request.AcceptanceId == Guid.Empty || request.LocationId == Guid.Empty
            || request.LotId == Guid.Empty || request.LabelId == Guid.Empty || request.MovementId == Guid.Empty
            || !Token(request.LabelCode) || request.HeatNumber is not null && !Token(request.HeatNumber)
            || request.CoilNumber is not null && !Token(request.CoilNumber)
            || !Positive(request.AcceptedQuantity) || !Positive(request.MeasuredQuantityKg)
            || request.OccurredAtUtc == default || request.OccurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new JsonException("Invalid receipt shape; exact quantities, identities, versions and UTC time are required.");
        }
    }

    private static bool Token(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 64
        && value == value.Trim() && !value.Any(char.IsControl);
    private static bool Positive(string? value) => value is not null && Quantity.IsMatch(value) && value != "0";
}
