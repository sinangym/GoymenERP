using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The host's <see cref="ISettingsPrerequisiteProvider"/> once the Settings module is composed (F-029 S1, URUN_F029 spec
/// §5.4(4)). The port's signature does not change (spec P2); only where the unit profile comes from does:
/// <list type="number">
/// <item>cancelled → <see cref="OperationCanceledException"/>; no access → <c>settings_access_denied</c> (the approved
/// provider's order, before anything about the source is revealed);</item>
/// <item>until the server-resolved company's one-time baseline transfer (URN-D5) the approved provider answers exactly as
/// before (<see cref="ApprovedUnitProfileCatalog"/>), byte-identical;</item>
/// <item>once the transfer marker exists the Settings table is the only source: the unit's version for the date by the
/// catalog's own rule (<see cref="SalesPolicyTimelineV1.For{T}"/>: the last version starting on or before the date, or the
/// first one with its REAL window, so the consumer's own window check rejects it); a unit without a stored version →
/// <see cref="SettingsPrerequisiteCodesV1.UnitProfileNotFound"/>;</item>
/// <item>an unreadable store → <c>settings_provider_unavailable</c> (never a catalog fallback); a malformed stored timeline →
/// <c>settings_profile_invalid</c>.</item>
/// </list>
/// The money profile and the period eligibility are not part of F-029 S1 and are answered by the approved provider unchanged.
/// </summary>
public sealed class SettingsUnitProfilePrerequisiteProvider : ISettingsPrerequisiteProvider
{
    private readonly SettingsDbContext _database;
    private readonly ICompanyContext _companyContext;
    private readonly ICurrentUser _currentUser;
    private readonly ISettingsPrerequisiteProvider _catalog;

    public SettingsUnitProfilePrerequisiteProvider(SettingsDbContext database, ICompanyContext companyContext,
        ICurrentUser currentUser)
        : this(database, companyContext, currentUser, new ApprovedSettingsPrerequisiteProvider(companyContext, currentUser))
    {
    }

    /// <summary>Test seam: another catalog provider in place of the approved one.</summary>
    internal SettingsUnitProfilePrerequisiteProvider(SettingsDbContext database, ICompanyContext companyContext,
        ICurrentUser currentUser, ISettingsPrerequisiteProvider catalog)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(companyContext);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(catalog);
        _database = database;
        _companyContext = companyContext;
        _currentUser = currentUser;
        _catalog = catalog;
    }

    public async Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn,
        CancellationToken cancellationToken = default)
    {
        RequireAccess(cancellationToken);
        var companyId = _companyContext.CompanyId;
        UnitProfileVersionRow[]? rows;
        try
        {
            rows = await UnitProfileReads.IsTransferredAsync(_database, cancellationToken)
                ? await UnitProfileReads.LoadUnitAsync(_database, unitId, cancellationToken)
                : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException("settings_provider_unavailable");
        }

        if (rows is null)
        {
            return await _catalog.GetUnitProfileAsync(unitId, effectiveOn, cancellationToken);
        }

        var entry = SalesPolicyTimelineV1.For(UnitProfileReads.Timeline(rows), effectiveOn)
                    ?? throw new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.UnitProfileNotFound);
        var row = entry.Value;
        return new UnitProfileV1(
            companyId,
            row.UnitId,
            row.UnitCode,
            row.DimensionCode,
            row.IsKilogram,
            new ProfileVersionV1(row.ProfileId, entry.Version),
            row.Scale,
            row.IsActive,
            entry.EffectiveFrom,
            entry.EffectiveThrough);
    }

    public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
        CancellationToken cancellationToken = default) =>
        _catalog.GetMoneyProfileAsync(currencyId, operation, effectiveOn, cancellationToken);

    public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn,
        CancellationToken cancellationToken = default) =>
        _catalog.GetPeriodEligibilityAsync(operation, occurredOn, valueOn, cancellationToken);

    /// <summary>Cancelled → <see cref="OperationCanceledException"/>; no access → <c>settings_access_denied</c> (the catalog's order).</summary>
    private void RequireAccess(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(_currentUser.UserId)
            || !_companyContext.IsResolved || _companyContext.CompanyId == Guid.Empty
            || !_currentUser.CanAccessCompany(_companyContext.CompanyId))
        {
            throw new SettingsPrerequisiteException("settings_access_denied");
        }
    }
}
