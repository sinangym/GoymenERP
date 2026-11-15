namespace GoymenERP.Modules.Inventory.Application;

/// <summary>Stable owner declaration for the dated shortage warning read (KLN-D42, STK-S31).</summary>
public static class InventoryShortageWarningApiV1
{
    public const string Path = "/api/v1/inventory/shortage-warnings";
    public const string GetOperationId = "GetInventoryShortageWarningsV1";
}

/// <summary>
/// KLN-D42 (it replaces minimum stock): per product, the open Sales kilograms against free unreserved stock plus open purchase
/// kilograms, with the dated "covered late" mark and similar-product stock shown as information only. The figures come from the
/// one backend calculator (<c>ShortageWarningCalculator</c>); the screen only reads them. Production orders are not counted,
/// and reading creates nothing — no purchase request, no allocation, no stock effect.
/// </summary>
public interface IInventoryShortageWarningsV1
{
    Task<InventoryShortageWarningsV1> ReadAsync(
        InventoryShortageWarningQueryV1 query,
        CancellationToken cancellationToken = default);
}

/// <summary><see cref="ProductId"/> narrows to one product; <see cref="OnlyWarnings"/> hides fully covered products.</summary>
public sealed record InventoryShortageWarningQueryV1(Guid? ProductId = null, bool OnlyWarnings = true);

/// <summary>
/// The company's answer. <see cref="SupplyDates"/> says whether the Purchasing owner answered purchase delivery dates
/// (<see cref="InventoryShortageSupplyDatesV1"/>); without them no line is called late or on time when it needs a purchase —
/// it is <see cref="InventoryShortageCoverageNamesV1.CoverageDateUnknown"/>.
/// </summary>
public sealed record InventoryShortageWarningsV1(
    Guid CompanyId,
    string SupplyDates,
    IReadOnlyList<InventoryShortageProductV1> Items);

/// <summary>
/// One product. Kilogram figures are exact invariant texts. For <see cref="InventoryShortageStatusNamesV1.Unconvertible"/>
/// (a demand or purchase line not in kilograms, so the product cannot be summed without a conversion owner) every kilogram
/// figure is null and no line is listed: nothing is guessed.
/// </summary>
public sealed record InventoryShortageProductV1(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string Status,
    string? OpenDemandKg,
    string? ReservedKg,
    string? NetDemandKg,
    string? FreeStockKg,
    string? OpenSupplyKg,
    string? ShortageKg,
    DateOnly? EarliestDueDate,
    IReadOnlyList<InventoryShortageLineV1> Lines,
    InventoryShortageSimilarStockV1 SimilarStock);

/// <summary>One open Sales line and how the calculator matched it (earliest due first: free stock, then purchases).</summary>
public sealed record InventoryShortageLineV1(
    Guid OrderId,
    string DocumentNo,
    Guid LineId,
    int LineNo,
    DateOnly? DueDate,
    string OpenKg,
    string ReservedKg,
    string FromStockKg,
    string FromSupplyKg,
    string UncoveredKg,
    string Coverage,
    DateOnly? LatestSupplyDate);

/// <summary>
/// KLN-D42 (3): similar products' free stock, information only (never counted as cover). The similarity rule and its tolerance
/// belong to the similar-product owner (KLN-D40/D70); until it is wired the status is
/// <see cref="InventoryShortageSimilarStatusNamesV1.NotConfigured"/>.
/// </summary>
public sealed record InventoryShortageSimilarStockV1(
    string Status,
    IReadOnlyList<InventoryShortageSimilarProductV1> Items);

public sealed record InventoryShortageSimilarProductV1(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string FreeStockKg);

public static class InventoryShortageStatusNamesV1
{
    public const string Covered = "Covered";
    public const string CoverageDateUnknown = "CoverageDateUnknown";
    public const string LateCovered = "LateCovered";
    public const string Shortage = "Shortage";
    public const string Unconvertible = "Unconvertible";
}

public static class InventoryShortageCoverageNamesV1
{
    public const string Covered = "Covered";
    public const string CoverageDateUnknown = "CoverageDateUnknown";
    public const string LateCovered = "LateCovered";
    public const string Uncovered = "Uncovered";
}

public static class InventoryShortageSupplyDatesV1
{
    public const string Answered = "Answered";
    public const string NotAnswered = "NotAnswered";
}

public static class InventoryShortageSimilarStatusNamesV1
{
    public const string NotConfigured = "NotConfigured";
    public const string Answered = "Answered";
    public const string Unavailable = "Unavailable";
}

public static class InventoryShortageWarningCodesV1
{
    public const string Unauthenticated = "inventory_shortage_warning_unauthenticated";
    public const string Forbidden = "inventory_shortage_warning_forbidden";
    public const string InvalidQuery = "inventory_shortage_warning_invalid_query";
    public const string Unavailable = "inventory_shortage_warning_unavailable";
}

public sealed class InventoryShortageWarningException(int statusCode, string code, Exception? innerException = null)
    : Exception(code, innerException)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public enum InventoryShortageSourceOutcomeV1
{
    Answered = 1,

    /// <summary>The owner could not answer reliably; the whole read is 503, never an assumed zero.</summary>
    Unavailable = 2,
}

/// <summary>
/// One open Sales order line as the Sales owner answers it through its own ports: the open quantity in the order unit
/// (<see cref="OpenQuantityText"/>, exact invariant text) and the line's requested delivery date as the due date.
/// </summary>
public sealed record InventoryShortageDemandLineV1(
    Guid CompanyId,
    Guid OrderId,
    string DocumentNo,
    Guid LineId,
    int LineNo,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    Guid UnitId,
    DateOnly? DueDate,
    string OpenQuantityText);

public sealed record InventoryShortageDemandAnswerV1(
    InventoryShortageSourceOutcomeV1 Outcome,
    IReadOnlyList<InventoryShortageDemandLineV1>? Lines);

/// <summary>The context company's open Sales lines (open quantity above zero), read only from Sales-declared ports.</summary>
public interface IInventoryShortageDemandSourceV1
{
    Task<InventoryShortageDemandAnswerV1> ListOpenDemandAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// One open approved purchase line as the Purchasing owner answers it: its remaining quantity in the line unit and, when the
/// owner answers one, the expected delivery date. A null date is unknown, never assumed on time.
/// </summary>
public sealed record InventoryShortageSupplyLineV1(
    Guid CompanyId,
    Guid OrderId,
    Guid LineId,
    Guid ProductId,
    Guid UnitId,
    string RemainingQuantityText,
    DateOnly? ExpectedDate);

/// <summary><see cref="DatesAnswered"/> is true only when the owner's port carries purchase delivery dates at all.</summary>
public sealed record InventoryShortageSupplyAnswerV1(
    InventoryShortageSourceOutcomeV1 Outcome,
    IReadOnlyList<InventoryShortageSupplyLineV1>? Lines,
    bool DatesAnswered);

/// <summary>The context company's open approved purchase lines. Inventory never reads Purchasing; the host adapts the port.</summary>
public interface IInventoryShortageSupplySourceV1
{
    Task<InventoryShortageSupplyAnswerV1> ListOpenSupplyAsync(CancellationToken cancellationToken = default);
}

/// <summary>Fail-closed default when no host adapter is registered: the read is 503, never "no purchases".</summary>
public sealed class InventoryUnavailableShortageSupplySourceV1 : IInventoryShortageSupplySourceV1
{
    public Task<InventoryShortageSupplyAnswerV1> ListOpenSupplyAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new InventoryShortageSupplyAnswerV1(InventoryShortageSourceOutcomeV1.Unavailable, null, false));
}

/// <summary>
/// KLN-D42 (3) seam: similar products of <c>productId</c> with their free stock, information only. The similar-product owner
/// (STK-S30, KLN-D40/D70 tolerance from Settings) replaces the default without an Inventory contract change.
/// </summary>
public interface IInventoryShortageSimilarStockSourceV1
{
    Task<InventoryShortageSimilarStockV1> ReadAsync(Guid productId, CancellationToken cancellationToken = default);
}

/// <summary>Default until the similar-product rule is wired: nothing is invented as "similar".</summary>
public sealed class InventoryNotConfiguredShortageSimilarStockSourceV1 : IInventoryShortageSimilarStockSourceV1
{
    private static readonly InventoryShortageSimilarStockV1 NotConfigured =
        new(InventoryShortageSimilarStatusNamesV1.NotConfigured, []);

    public Task<InventoryShortageSimilarStockV1> ReadAsync(Guid productId, CancellationToken cancellationToken = default) =>
        Task.FromResult(NotConfigured);
}
