namespace GoymenERP.Modules.Manufacturing.Application;

/// <summary>
/// Manufacturing-owned bounded selectors for the explicit Product/version and active location facts
/// required by <see cref="DeclareInventoryOutputRequestV1"/>. Implementations consume the owner modules'
/// read contracts; this contract adds no availability, ownership, block-state or permission policy.
/// </summary>
public interface IManufacturingOutputSelectionV1
{
    Task<ManufacturingOutputProductCandidatesV1> SearchProductsAsync(
        ManufacturingOutputProductQueryV1 query,
        CancellationToken cancellationToken);

    Task<ManufacturingOutputLocationCandidatesV1> SearchLocationsAsync(
        ManufacturingOutputLocationQueryV1 query,
        CancellationToken cancellationToken);
}

public sealed record ManufacturingOutputProductQueryV1(
    string? Q,
    DateOnly EffectiveOn,
    int Limit = 25);

public sealed record ManufacturingOutputLocationQueryV1(
    string? Q,
    int Limit = 25);

public sealed record ManufacturingOutputProductCandidatesV1(
    IReadOnlyList<ManufacturingOutputProductCandidateV1> Items,
    bool SearchWindowFull);

public sealed record ManufacturingOutputProductCandidateV1(
    Guid ProductId,
    string ProductVersion,
    string Code,
    string Name,
    string StockUnitCode);

public sealed record ManufacturingOutputLocationCandidatesV1(
    IReadOnlyList<ManufacturingOutputLocationCandidateV1> Items,
    bool SearchWindowFull);

public sealed record ManufacturingOutputLocationCandidateV1(
    Guid LocationId,
    string LocationCode,
    string LocationName,
    Guid SiteId,
    string SiteCode,
    string SiteName);

public enum ManufacturingOutputSelectionFailure
{
    Invalid,
    Denied,
    Unavailable,
}

public static class ManufacturingOutputSelectionCodesV1
{
    public const string InvalidQuery = "manufacturing_output_selection_invalid_query";
    public const string Denied = "manufacturing_authority_denied";
    public const string Unavailable = "manufacturing_output_selection_unavailable";
}

public sealed class ManufacturingOutputSelectionException(
    ManufacturingOutputSelectionFailure failure,
    string code,
    Exception? innerException = null)
    : Exception("Üretim çıktısı seçim kaynağı yanıt veremedi.", innerException)
{
    public ManufacturingOutputSelectionFailure Failure { get; } = failure;

    public string Code { get; } = code;
}
