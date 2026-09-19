namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// A Settings-owned unit identity (PRD-UNIT-1). Company-independent — kg is kg in every company —
/// and NOT a profile: whether a company may use the unit, from when and at what scale is its unit
/// profile (<see cref="ISettingsPrerequisiteProvider.GetUnitProfileAsync"/>). The code is a label,
/// the <paramref name="UnitId"/> is the identity.
/// </summary>
public sealed record UnitIdentityV1(Guid UnitId, string Code, string DimensionCode, bool IsKilogram);

/// <summary>
/// Read-only unit identities (PRD-UNIT-1). A separate port on purpose:
/// <see cref="ISettingsPrerequisiteProvider"/> is implemented by other modules' test doubles and
/// does not change. The company is never an input and no permission is implied.
/// <para>
/// A malformed source is refused with <c>settings_profile_invalid</c>; it is never an empty list or
/// a guessed identity.
/// </para>
/// </summary>
public interface ISettingsUnitIdentityCatalogV1
{
    /// <summary>The identity, or null when the unit is unknown.</summary>
    Task<UnitIdentityV1?> ReadAsync(Guid unitId, CancellationToken cancellationToken = default);

    /// <summary>Every known identity, ordered by <see cref="UnitIdentityV1.Code"/> (ordinal).</summary>
    Task<IReadOnlyList<UnitIdentityV1>> ListAsync(CancellationToken cancellationToken = default);
}
