using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>
/// The unit identities of the approved unit-profile catalog (PRD-UNIT-1): one identity per unit that
/// has an approved profile, with the code, dimension and kilogram flag that catalog carries. Today that
/// is exactly kg / weight / kilogram (<see cref="ApprovedUnitProfileCatalog.GdcKilogramV1"/>). Nothing
/// is added here that the reviewed source data does not hold.
/// <para>
/// Identities are company-independent, so every profile of one unit must agree on its code, dimension
/// and kilogram flag, and two units may not share a code. Otherwise the whole source is refused with
/// <c>settings_profile_invalid</c>, at construction (startup), never per read.
/// </para>
/// </summary>
public sealed class ApprovedSettingsUnitIdentityCatalog : ISettingsUnitIdentityCatalogV1
{
    private readonly IReadOnlyDictionary<Guid, UnitIdentityV1> _byUnit;
    private readonly IReadOnlyList<UnitIdentityV1> _ordered;

    public ApprovedSettingsUnitIdentityCatalog()
        : this(ApprovedUnitProfileCatalog.Approved)
    {
    }

    internal ApprovedSettingsUnitIdentityCatalog(ApprovedUnitProfileCatalog profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var identities = new List<UnitIdentityV1>();
        foreach (var unit in profiles.Entries.GroupBy(entry => entry.UnitId))
        {
            var distinct = unit
                .Select(entry => new UnitIdentityV1(entry.UnitId, entry.UnitCode, entry.DimensionCode, entry.IsKilogram))
                .Distinct()
                .ToArray();
            if (distinct.Length != 1)
            {
                throw Invalid();
            }

            identities.Add(distinct[0]);
        }

        if (identities.Select(identity => identity.Code).Distinct(StringComparer.Ordinal).Count() != identities.Count)
        {
            throw Invalid();
        }

        _ordered = identities.OrderBy(identity => identity.Code, StringComparer.Ordinal).ToArray();
        _byUnit = _ordered.ToDictionary(identity => identity.UnitId);
    }

    public Task<UnitIdentityV1?> ReadAsync(Guid unitId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_byUnit.TryGetValue(unitId, out var identity) ? identity : null);
    }

    public Task<IReadOnlyList<UnitIdentityV1>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_ordered);
    }

    private static SettingsPrerequisiteException Invalid() => new("settings_profile_invalid");
}
