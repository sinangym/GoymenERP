using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>Master §28.1 and ALS-D43/D51 standard unit meanings. This provider never writes a profile or grants use.</summary>
public sealed class ApprovedSettingsPhysicalUnitSemanticsV1(ISettingsUnitIdentityCatalogV1 identities)
    : ISettingsPhysicalUnitSemanticsV1
{
    public const string CatalogVersion = "settings.standard-unit-semantics.v1";
    public async Task<SettingsPhysicalUnitSemanticsV1> ReadAsync(Guid unitId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UnitIdentityV1? identity;
        try { identity = await identities.ReadAsync(unitId, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return new(SettingsPhysicalUnitStatusV1.Unavailable, unitId, null, null, null); }
        if (identity is null) return new(SettingsPhysicalUnitStatusV1.NotFound, unitId, null, null, null);
        var expected = SettingsUnitIdentitiesV1.Standard.SingleOrDefault(unit => unit.UnitId == unitId);
        if (expected is null || identity != expected)
            return new(SettingsPhysicalUnitStatusV1.Unavailable, unitId, null, null, null);
        SettingsPhysicalUnitKindV1? kind = unitId == SettingsUnitIdentitiesV1.Kilogram ? SettingsPhysicalUnitKindV1.Kilogram
            : unitId == SettingsUnitIdentitiesV1.Tonne ? SettingsPhysicalUnitKindV1.Tonne
            : unitId == SettingsUnitIdentitiesV1.Metre ? SettingsPhysicalUnitKindV1.Metre
            : unitId == SettingsUnitIdentitiesV1.Piece ? SettingsPhysicalUnitKindV1.Piece : null;
        // A recognized business identity is not necessarily a supported physical meaning. In particular,
        // volume, time, kit/set and service identities must never fall through to the piece conversion path.
        if (kind is null) return new(SettingsPhysicalUnitStatusV1.Unavailable, unitId, null, null, null);
        decimal? factor = kind == SettingsPhysicalUnitKindV1.Kilogram ? 1m
            : kind == SettingsPhysicalUnitKindV1.Tonne ? 1000m : null;
        return new(SettingsPhysicalUnitStatusV1.Found, unitId, kind, factor, CatalogVersion);
    }
}
