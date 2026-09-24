using System.Globalization;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// A single-use Inventory participant enlisted in Manufacturing's already-open transaction.
/// Manufacturing stages its fact first and supplies the returned post-stage order version. Inventory
/// stages exactly one input issue, unused-input return or output receipt and never owns commit,
/// rollback, connection lifetime or Manufacturing state.
/// </summary>
public interface IInventoryManufacturingRealizationParticipantV1 : IAsyncDisposable
{
    Task<InventoryManufacturingInputIssueResultV1> StageInputIssueAsync(
        InventoryManufacturingInputIssueRequestV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<InventoryManufacturingInputReturnResultV1> StageInputReturnAsync(
        InventoryManufacturingInputReturnRequestV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<InventoryManufacturingOutputResultV1> StageOutputAsync(
        InventoryManufacturingOutputRequestV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Inventory consumes the exact, already-authorized parent label. The host must preserve the
/// label version captured when the Manufacturing order was created; Inventory revalidates it at
/// effect time and advances it with the movement.
/// </summary>
public sealed record InventoryManufacturingInputIssueRequestV1(
    Guid CompanyId,
    Guid ManufacturingOrderId,
    Guid IssueId,
    string ManufacturingOrderVersionAfter,
    Guid ParentLabelId,
    string ExpectedParentLabelVersion,
    Guid MovementId,
    string QuantityKg,
    DateTimeOffset OccurredAtUtc);

public sealed record InventoryManufacturingInputIssueResultV1(
    Guid CompanyId,
    Guid ManufacturingOrderId,
    Guid IssueId,
    string ManufacturingOrderVersion,
    Guid ParentLabelId,
    string ParentLabelVersion,
    Guid MovementId,
    string QuantityKg,
    bool IsReplay,
    string OriginalCorrelationId);

/// <summary>
/// An unused-input return adds stock to the same company-owned physical position and label that
/// production consumed. It does not create a receipt, a new label, a reservation or a transfer.
/// </summary>
public sealed record InventoryManufacturingInputReturnRequestV1(
    Guid CompanyId,
    Guid ManufacturingOrderId,
    Guid ReturnId,
    string ManufacturingOrderVersionAfter,
    Guid ParentLabelId,
    string ExpectedParentLabelVersion,
    Guid MovementId,
    string QuantityKg,
    DateTimeOffset OccurredAtUtc);

public sealed record InventoryManufacturingInputReturnResultV1(
    Guid CompanyId,
    Guid ManufacturingOrderId,
    Guid ReturnId,
    string ManufacturingOrderVersion,
    Guid ParentLabelId,
    string ParentLabelVersion,
    Guid MovementId,
    string QuantityKg,
    bool IsReplay,
    string OriginalCorrelationId);

/// <summary>
/// Explicit physical identity for one Manufacturing output. Inventory deliberately does not infer
/// product, lot, location, ownership, custody, block state or required documents from the parent
/// roll or output class. Those facts must come from an authorized host policy before enlistment.
/// </summary>
public sealed record InventoryManufacturingOutputRequestV1(
    Guid CompanyId,
    Guid ManufacturingOrderId,
    Guid OutputId,
    string ManufacturingOrderVersionAfter,
    Guid ParentLabelId,
    string ExpectedParentLabelVersion,
    string OutputClass,
    Guid ProductId,
    string ExpectedProductVersion,
    Guid LotId,
    Guid LabelId,
    string LabelCode,
    string? HeatNumber,
    string? CoilNumber,
    IReadOnlyList<string> RequiredDocuments,
    StockPartyKind OwnerKind,
    Guid OwnerId,
    StockPartyKind CustodianKind,
    Guid CustodianId,
    Guid LocationId,
    BlockState InitialBlock,
    Guid MovementId,
    string QuantityKg,
    DateTimeOffset OccurredAtUtc);

public sealed record InventoryManufacturingOutputResultV1(
    Guid CompanyId,
    Guid ManufacturingOrderId,
    Guid OutputId,
    string ManufacturingOrderVersion,
    Guid ParentLabelId,
    string ParentLabelVersion,
    string OutputClass,
    Guid ProductId,
    string ProductVersion,
    Guid LotId,
    Guid LabelId,
    string LabelVersion,
    Guid MovementId,
    string QuantityKg,
    bool IsReplay,
    string OriginalCorrelationId);

public static class InventoryManufacturingRealizationCodesV1
{
    public const string InvalidRequest = "inventory_manufacturing_realization_invalid_request";
    public const string InvalidVersion = "inventory_manufacturing_realization_version_invalid";
    public const string InvalidQuantity = "inventory_manufacturing_realization_quantity_invalid";
    public const string InvalidReference = "inventory_manufacturing_realization_invalid_reference";
    public const string LabelVersionConflict = "inventory_manufacturing_parent_label_version_conflict";
    public const string ParentLabelNotReleasable = "inventory_manufacturing_parent_label_not_releasable";
    public const string ParentLabelContextConflict = "inventory_manufacturing_parent_label_context_conflict";
    public const string PhysicalStockInsufficient = "inventory_manufacturing_physical_stock_insufficient";
    public const string ParentInputNotIssued = "inventory_manufacturing_parent_input_not_issued";
    public const string ReturnExceedsIssued = "inventory_manufacturing_input_return_exceeds_issued";
    public const string OutputLabelAlreadyRegistered = "inventory_manufacturing_output_label_already_registered";
    public const string OutputLotOrLabelConflict = "inventory_manufacturing_output_lot_or_label_conflict";
    public const string IdempotencyConflict = "inventory_manufacturing_realization_idempotency_conflict";
    public const string ConcurrentChange = "inventory_manufacturing_realization_concurrent_change";
    public const string QuantityProfileUnavailable = "inventory_manufacturing_quantity_profile_unavailable";
    public const string Unavailable = "inventory_manufacturing_realization_unavailable";

    public static bool IsDefined(int status, string code) => status switch
    {
        400 => code is InvalidRequest or InvalidVersion or InvalidQuantity,
        404 => code == InvalidReference,
        409 => code is LabelVersionConflict or ParentLabelNotReleasable or ParentLabelContextConflict
            or PhysicalStockInsufficient or ParentInputNotIssued or ReturnExceedsIssued or OutputLabelAlreadyRegistered
            or OutputLotOrLabelConflict or IdempotencyConflict or ConcurrentChange,
        503 => code is QuantityProfileUnavailable or Unavailable,
        _ => false,
    };
}

public sealed class InventoryManufacturingRealizationException : Exception
{
    private InventoryManufacturingRealizationException(int status, string code) : base(code)
    {
        Status = status;
        Code = code;
    }

    public int Status { get; }
    public string Code { get; }

    public static InventoryManufacturingRealizationException Refused(int status, string code) =>
        InventoryManufacturingRealizationCodesV1.IsDefined(status, code)
            ? new(status, code)
            : throw new InvalidOperationException(
                "Inventory manufacturing-realization refusal is outside its closed contract.");
}

public static class InventoryManufacturingRealizationContractV1
{
    private const int IdentityMaximumLength = 128;

    public const string InputIssueSourceKind = "manufacturing.input-issue";
    public const string InputReturnSourceKind = "manufacturing.input-return";
    public const string OutputSourceKind = "manufacturing.output-declaration";
    public const string InputIssueCommandName = "inventory.manufacturing-input-issue.stage.v1";
    public const string InputReturnCommandName = "inventory.manufacturing-input-return.stage.v1";
    public const string OutputCommandName = "inventory.manufacturing-output-declaration.stage.v1";

    public static Kilograms Validate(InventoryManufacturingInputIssueRequestV1 request,
        InventoryCommandMetadata metadata, Guid enlistedCompanyId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateInput(request.CompanyId, request.ManufacturingOrderId, request.IssueId,
            request.ManufacturingOrderVersionAfter, request.ParentLabelId, request.ExpectedParentLabelVersion,
            request.MovementId, request.OccurredAtUtc, metadata, enlistedCompanyId);
        return Quantity(request.QuantityKg);
    }

    public static Kilograms Validate(InventoryManufacturingInputReturnRequestV1 request,
        InventoryCommandMetadata metadata, Guid enlistedCompanyId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateInput(request.CompanyId, request.ManufacturingOrderId, request.ReturnId,
            request.ManufacturingOrderVersionAfter, request.ParentLabelId, request.ExpectedParentLabelVersion,
            request.MovementId, request.OccurredAtUtc, metadata, enlistedCompanyId);
        return Quantity(request.QuantityKg);
    }

    public static Kilograms Validate(InventoryManufacturingOutputRequestV1 request,
        InventoryCommandMetadata metadata, Guid enlistedCompanyId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateInput(request.CompanyId, request.ManufacturingOrderId, request.OutputId,
            request.ManufacturingOrderVersionAfter, request.ParentLabelId, request.ExpectedParentLabelVersion,
            request.MovementId, request.OccurredAtUtc, metadata, enlistedCompanyId);
        if (request.ProductId == Guid.Empty || request.LotId == Guid.Empty || request.LabelId == Guid.Empty
            || request.OwnerId == Guid.Empty || request.CustodianId == Guid.Empty || request.LocationId == Guid.Empty
            || request.RequiredDocuments is null || !Enum.IsDefined(request.OwnerKind)
            || !Enum.IsDefined(request.CustodianKind) || !Enum.IsDefined(request.InitialBlock))
        {
            throw Refused(400, InventoryManufacturingRealizationCodesV1.InvalidRequest);
        }

        RequireToken(request.OutputClass, 64);
        RequireToken(request.ExpectedProductVersion, 128);
        RequireToken(request.LabelCode, 64);
        RequireOptional(request.HeatNumber, 64);
        RequireOptional(request.CoilNumber, 64);
        foreach (var document in request.RequiredDocuments)
        {
            RequireToken(document, 64);
        }

        return Quantity(request.QuantityKg);
    }

    private static void ValidateInput(Guid companyId, Guid orderId, Guid factId,
        string manufacturingOrderVersionAfter, Guid parentLabelId, string expectedParentLabelVersion,
        Guid movementId, DateTimeOffset occurredAtUtc, InventoryCommandMetadata metadata, Guid enlistedCompanyId)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (enlistedCompanyId == Guid.Empty || companyId == Guid.Empty || orderId == Guid.Empty
            || factId == Guid.Empty || parentLabelId == Guid.Empty || movementId == Guid.Empty
            || occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw Refused(400, InventoryManufacturingRealizationCodesV1.InvalidRequest);
        }

        if (companyId != enlistedCompanyId)
        {
            throw Refused(404, InventoryManufacturingRealizationCodesV1.InvalidReference);
        }

        RequireVersion(manufacturingOrderVersionAfter);
        RequireVersion(expectedParentLabelVersion);
        RequireMetadata(metadata);
    }

    private static Kilograms Quantity(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 40
            || value.Any(character => !char.IsAsciiDigit(character) && character != '.')
            || !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            || !string.Equals(parsed.ToString("0.###", CultureInfo.InvariantCulture), value,
                StringComparison.Ordinal))
        {
            throw Refused(400, InventoryManufacturingRealizationCodesV1.InvalidQuantity);
        }

        try
        {
            return Kilograms.Positive(parsed);
        }
        catch (InventoryRuleException)
        {
            throw Refused(400, InventoryManufacturingRealizationCodesV1.InvalidQuantity);
        }
    }

    private static void RequireVersion(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 19 || value[0] is < '1' or > '9'
            || value.Any(character => character is < '0' or > '9')
            || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            throw Refused(400, InventoryManufacturingRealizationCodesV1.InvalidVersion);
        }
    }

    private static void RequireMetadata(InventoryCommandMetadata metadata)
    {
        RequireIdentity(metadata.ActorId);
        RequireIdentity(metadata.PermissionCode);
        RequireIdentity(metadata.CorrelationId);
        RequireIdentity(metadata.IdempotencyKey);
        RequireIdentity(metadata.SourceChannel);
        if (metadata.CausationId is not null) RequireIdentity(metadata.CausationId);
        if (metadata.RoleCodes is null || metadata.RoleCodes.Any(role => !ValidIdentity(role)))
        {
            throw Refused(400, InventoryManufacturingRealizationCodesV1.InvalidRequest);
        }
    }

    private static void RequireIdentity(string? value)
    {
        if (!ValidIdentity(value))
        {
            throw Refused(400, InventoryManufacturingRealizationCodesV1.InvalidRequest);
        }
    }

    private static bool ValidIdentity(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= IdentityMaximumLength
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && !value.Any(char.IsControl);

    private static void RequireToken(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal) || value.Any(char.IsControl))
        {
            throw Refused(400, InventoryManufacturingRealizationCodesV1.InvalidRequest);
        }
    }

    private static void RequireOptional(string? value, int maximumLength)
    {
        if (value is not null) RequireToken(value, maximumLength);
    }

    private static InventoryManufacturingRealizationException Refused(int status, string code) =>
        InventoryManufacturingRealizationException.Refused(status, code);
}
