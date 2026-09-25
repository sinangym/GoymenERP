namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// R19 / AT-038 user command: record one same-company label split or merge (DEC-LBL-002/003) under a
/// fresh Inventory-owned command authority, over the accepted #438 writer
/// (<see cref="IInventoryLabelGenealogyWriterV1"/>).
/// <para>
/// The command authority is <see cref="InventoryPermissionManifestV1.LabelTransformationRecord"/>, a separate
/// Inventory-owned name (Business #66 5806047097: an Inventory command never reuses a read, opening,
/// allocation, Sales or Logistics permission). It is measured company-wide and fresh immediately before the
/// writer's single transaction; the writer's own reads, replay lookup and save are that one owner transaction.
/// An unregistered name is a setup gap (503), never a grant. Nothing is measured after the commit.
/// </para>
/// <para>
/// The caller (the central host) supplies only the transport envelope: correlation, causation, the
/// Idempotency-Key and the channel. Actor, permission and role codes are derived here from the measured
/// scope; a transport actor that differs from the authenticated actor is refused.
/// </para>
/// </summary>
public interface IInventoryLabelTransformationCommandV1
{
    Task<InventoryLabelTransformationResultV1> RecordAsync(
        TransformInventoryLabelsV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The owner's HTTP contract for the central publication. The central host maps exactly this path and
/// operation id, answers 201 with <see cref="InventoryLabelTransformationViewV1"/> for a new record and
/// 200 with the same body plus <c>Idempotency-Replayed: true</c> and
/// <c>Idempotency-Original-Correlation-Id</c> for a replay, and lists exactly the codes below.
/// </summary>
public static class InventoryLabelTransformationApiV1
{
    public const string Path = "/api/v1/inventory/label-transformations";
    public const string RecordOperationId = "RecordInventoryLabelTransformationV1";
}

/// <summary>The command's own refusal codes beside the writer's <see cref="InventoryLabelGenealogyCodesV1"/>.</summary>
public static class InventoryLabelTransformationCommandCodesV1
{
    public const string Unauthenticated = "unauthenticated";
    public const string Forbidden = "forbidden";
    public const string PermissionUnregistered = "inventory_permission_unregistered";
}

/// <summary>
/// The CLOSED problem codes of the record route, per HTTP status. 401/403 are platform problems; their codes are
/// listed for logs and tests. Anything else the writer raises is an internal fault, never a guessed 4xx.
/// </summary>
public static class InventoryLabelTransformationHttpCodesV1
{
    public static IReadOnlySet<string> Status400Codes { get; } = Set(
        InventoryLabelGenealogyCodesV1.ValidationFailed,
        InventoryScaleCodes.ScaleExceeded);

    public static IReadOnlySet<string> Status404Codes { get; } = Set(InventoryLabelGenealogyCodesV1.LabelNotFound);

    public static IReadOnlySet<string> Status409Codes { get; } = Set(
        InventoryLabelGenealogyCodesV1.IdempotencyConflict,
        InventoryLabelGenealogyCodesV1.LabelVersionConflict,
        InventoryLabelGenealogyCodesV1.SourcePositionConflict,
        InventoryLabelGenealogyCodesV1.ActiveAllocationConflict,
        InventoryLabelGenealogyCodesV1.LabelCodeTaken,
        InventoryLabelGenealogyCodesV1.TransformationConflict,
        InventoryLabelGenealogyCodesV1.ConcurrentChange);

    /// <summary>
    /// The kilogram profile answers the writer reports while checking the company's approved scale are a
    /// provider/setup state, not the caller's input, so the command answers them as 503.
    /// </summary>
    public static IReadOnlySet<string> Status503Codes { get; } = Set(
        InventoryLabelTransformationCommandCodesV1.PermissionUnregistered,
        InventoryLabelGenealogyCodesV1.CorruptState,
        InventoryScaleCodes.ProfileUnusable,
        "settings_provider_unavailable",
        "settings_unit_profile_not_found",
        "settings_profile_invalid",
        "settings_access_denied",
        "settings_decimal_invalid",
        "settings_decimal_scale_exceeded");

    private static IReadOnlySet<string> Set(params string[] codes) => codes.ToHashSet(StringComparer.Ordinal);
}
