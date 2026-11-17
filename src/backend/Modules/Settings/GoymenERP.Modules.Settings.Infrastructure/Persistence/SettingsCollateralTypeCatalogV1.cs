using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

public sealed class SettingsCollateralTypeCatalogV1 : ISettingsCollateralTypeCatalogV1
{
    private readonly SettingsDbContext _database;
    private readonly ICompanyContext _companyContext;
    private readonly ICurrentUser _currentUser;

    public SettingsCollateralTypeCatalogV1(SettingsDbContext database, ICompanyContext companyContext, ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(companyContext);
        ArgumentNullException.ThrowIfNull(currentUser);
        _database = database;
        _companyContext = companyContext;
        _currentUser = currentUser;
    }

    public async Task<SettingsCollateralTypesViewV1> ListAsync(Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var actor = RequireAccess(companyId, cancellationToken);
        CollateralTypeSnapshot[] snapshots;
        try
        {
            snapshots = await CollateralTypeReads.LoadAsync(_database, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException(SettingsCollateralTypeProblemCodesV1.ProviderUnavailable);
        }

        // The owner's answer belongs to the actor and company captured before the asynchronous read. A consumer's
        // additional fence does not replace this owner boundary, including when the store returned an empty catalog.
        RequireSameAccess(companyId, actor, cancellationToken);
        var entries = new List<SettingsCollateralTypeViewV1>(snapshots.Length);
        foreach (var snapshot in snapshots)
        {
            entries.Add(ToEntry(snapshot, companyId)
                ?? throw new SettingsPrerequisiteException(SettingsCollateralTypeProblemCodesV1.ProfileInvalid));
        }

        RequireSameAccess(companyId, actor, cancellationToken);
        return new SettingsCollateralTypesViewV1(companyId, entries);
    }

    /// <summary>The published shape of a stored entry, or null when the stored data is malformed.</summary>
    private static SettingsCollateralTypeViewV1? ToEntry(CollateralTypeSnapshot snapshot, Guid companyId)
    {
        var entry = snapshot.Entry;
        if (snapshot.Latest is not { } latest
            || entry.CompanyId != companyId || latest.CompanyId != companyId
            || entry.EntryId == Guid.Empty || latest.EntryId != entry.EntryId
            || !Enum.TryParse<SettingsCollateralKindV1>(entry.Kind, out var kind) || !CollateralTypeRulesV1.IsKind(kind)
            || !ReferenceListRulesV1.IsCode(entry.Code)
            || latest.Version <= 0
            || !ReferenceListRulesV1.IsStoredName(latest.Name)
            || !CollateralTypeRulesV1.TryRate(latest.RatePercent, out var rate))
        {
            return null;
        }

        return new SettingsCollateralTypeViewV1(entry.EntryId, entry.Code, latest.Name, kind, latest.Active, rate,
            latest.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), latest.RecordedBy, latest.RecordedAtUtc);
    }

    /// <summary>
    /// Cancelled → <see cref="OperationCanceledException"/>; no access, or a requested company other than the server-resolved
    /// one → <c>settings_access_denied</c>.
    /// </summary>
    private string RequireAccess(Guid companyId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actor = _currentUser.UserId;
        if (!_currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(actor)
            || !_companyContext.IsResolved || _companyContext.CompanyId == Guid.Empty
            || companyId != _companyContext.CompanyId
            || !_currentUser.CanAccessCompany(_companyContext.CompanyId))
        {
            throw new SettingsPrerequisiteException(SettingsCollateralTypeProblemCodesV1.AccessDenied);
        }

        return actor;
    }

    private void RequireSameAccess(Guid companyId, string actor, CancellationToken cancellationToken)
    {
        if (!string.Equals(RequireAccess(companyId, cancellationToken), actor, StringComparison.Ordinal))
        {
            throw new SettingsPrerequisiteException(SettingsCollateralTypeProblemCodesV1.AccessDenied);
        }
    }
}
