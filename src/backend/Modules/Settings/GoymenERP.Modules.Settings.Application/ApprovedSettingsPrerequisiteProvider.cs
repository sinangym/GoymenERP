using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>
/// The real Settings provider for what is approved today: unit profiles, from
/// <see cref="ApprovedUnitProfileCatalog"/> (T02-a — GDC kg). Money profiles and period
/// eligibility are NOT approved sources yet and stay exactly as
/// <see cref="UnavailableSettingsPrerequisiteProvider"/> answers them.
/// <para>
/// Outcomes of <see cref="GetUnitProfileAsync"/>, in order:
/// <list type="number">
/// <item>cancelled → <see cref="OperationCanceledException"/>;</item>
/// <item>no authenticated actor, no actor id, unresolved or empty company, or an actor who is
/// not a member of it → <c>settings_access_denied</c>, before anything about the source is
/// revealed (the same check as the unavailable adapter);</item>
/// <item>no approved profile source for the server-resolved company →
/// <c>settings_provider_unavailable</c> (unreachable: there is nothing to ask);</item>
/// <item>the company has a source but no profile for this unit →
/// <see cref="UnitProfileNotFoundCode"/> (reachable, but empty for this identity — kept apart
/// from both "unavailable" and "invalid");</item>
/// <item>a malformed catalog → <c>settings_profile_invalid</c>, raised when the catalog is
/// built, never repaired;</item>
/// <item>otherwise the owner's exact profile. A date outside every window still returns the
/// real version with its real window; consumers must check activity, window and kilogram
/// identity themselves, as the port requires, and a date before approval is therefore
/// rejected by them rather than served as valid.</item>
/// </list>
/// </para>
/// <para>
/// The company is never a request input. A returned profile is reference metadata, not an
/// authorization for any operation: the consuming use case still checks its own permission.
/// </para>
/// </summary>
public sealed class ApprovedSettingsPrerequisiteProvider : ISettingsPrerequisiteProvider
{
    /// <summary>
    /// Kept as an alias so existing callers still compile. Consumers map the published contract
    /// value <see cref="SettingsPrerequisiteCodesV1.UnitProfileNotFound"/> instead.
    /// </summary>
    public const string UnitProfileNotFoundCode = SettingsPrerequisiteCodesV1.UnitProfileNotFound;

    private readonly ICompanyContext _companyContext;
    private readonly ICurrentUser _currentUser;
    private readonly ApprovedUnitProfileCatalog _catalog;
    private readonly UnavailableSettingsPrerequisiteProvider _notApprovedYet;

    /// <summary>
    /// The only constructor the host and other modules can reach: it always serves the reviewed
    /// <see cref="ApprovedUnitProfileCatalog.Approved"/> catalog.
    /// </summary>
    public ApprovedSettingsPrerequisiteProvider(ICompanyContext companyContext, ICurrentUser currentUser)
        : this(companyContext, currentUser, ApprovedUnitProfileCatalog.Approved)
    {
    }

    /// <summary>
    /// Test seam, internal on purpose (FİNANS #279 P3-1): no module that references Settings can
    /// build an "approved" provider over profiles it made up; only Settings' own tests can.
    /// </summary>
    internal ApprovedSettingsPrerequisiteProvider(
        ICompanyContext companyContext, ICurrentUser currentUser, ApprovedUnitProfileCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(companyContext);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(catalog);
        _companyContext = companyContext;
        _currentUser = currentUser;
        _catalog = catalog;
        _notApprovedYet = new UnavailableSettingsPrerequisiteProvider(companyContext, currentUser);
    }

    public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(_currentUser.UserId)
            || !_companyContext.IsResolved || _companyContext.CompanyId == Guid.Empty
            || !_currentUser.CanAccessCompany(_companyContext.CompanyId))
        {
            return Task.FromException<UnitProfileV1>(new SettingsPrerequisiteException("settings_access_denied"));
        }

        var companyId = _companyContext.CompanyId;
        if (!_catalog.Serves(companyId))
        {
            return Task.FromException<UnitProfileV1>(
                new SettingsPrerequisiteException("settings_provider_unavailable"));
        }

        var entry = _catalog.VersionFor(companyId, unitId, effectiveOn);
        if (entry is null)
        {
            return Task.FromException<UnitProfileV1>(new SettingsPrerequisiteException(UnitProfileNotFoundCode));
        }

        return Task.FromResult(new UnitProfileV1(
            entry.CompanyId,
            entry.UnitId,
            entry.UnitCode,
            entry.DimensionCode,
            entry.IsKilogram,
            new ProfileVersionV1(entry.ProfileId, entry.Version),
            entry.Scale,
            entry.IsActive,
            entry.EffectiveFrom,
            entry.EffectiveThrough));
    }

    /// <summary>Not an approved source yet: the Sales money operation identity is still an owner
    /// input (#66 5715011050).</summary>
    public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation,
        DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
        _notApprovedYet.GetMoneyProfileAsync(currencyId, operation, effectiveOn, cancellationToken);

    /// <summary>Not an approved source yet.</summary>
    public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation,
        DateOnly occurredOn, DateOnly? valueOn, CancellationToken cancellationToken = default) =>
        _notApprovedYet.GetPeriodEligibilityAsync(operation, occurredOn, valueOn, cancellationToken);
}
