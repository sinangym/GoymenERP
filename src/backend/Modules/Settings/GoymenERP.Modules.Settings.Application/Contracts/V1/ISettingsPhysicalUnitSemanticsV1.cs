namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

public enum SettingsPhysicalUnitKindV1 { Kilogram = 1, Tonne = 2, Metre = 3, Piece = 4 }
public enum SettingsPhysicalUnitStatusV1 { Found = 1, NotFound = 2, Unavailable = 3 }

/// <summary>
/// Explicit global unit meaning, separate from a company's permission/profile and from Product conversion factors.
/// KgPerUnit is exactly 1 for kg and 1000 for the metric tonne. Metre/piece have no universal mass conversion;
/// their mass must come from the Product owner's versioned conversion. Code/name/dimension never select a kind.
/// </summary>
public sealed record SettingsPhysicalUnitSemanticsV1(SettingsPhysicalUnitStatusV1 Status, Guid UnitId,
    SettingsPhysicalUnitKindV1? Kind, decimal? KgPerUnit, string? SourceVersion);

public interface ISettingsPhysicalUnitSemanticsV1
{
    /// <summary>Identity-level meaning only. The caller still requires the company's applicable unit profile.</summary>
    Task<SettingsPhysicalUnitSemanticsV1> ReadAsync(Guid unitId, CancellationToken cancellationToken = default);
}
