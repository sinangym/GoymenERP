using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The host's <see cref="ISettingsReferenceListReaderV1"/> (API-98/B-14). The only source is the company's stored entries and
/// their latest versions; nothing is seeded and nothing is defaulted. A kind that is not opened (the operation type list)
/// answers not enabled with no entries, and every lookup of it as unavailable. It applies no Settings permission: the
/// consuming module authorizes its own read. Order: cancelled → <see cref="OperationCanceledException"/>; no access, or a
/// company other than the server-resolved one → <c>settings_access_denied</c>; then for a list an unreadable store →
/// <c>settings_provider_unavailable</c> and a stored row outside the published shape → <c>settings_profile_invalid</c>, and
/// for a lookup both → <see cref="SettingsReferenceListLookupOutcomeV1.Unavailable"/>.
/// </summary>
public sealed class SettingsReferenceListReaderV1 : ISettingsReferenceListReaderV1
{
    private readonly SettingsDbContext _database;
    private readonly ICompanyContext _companyContext;
    private readonly ICurrentUser _currentUser;

    public SettingsReferenceListReaderV1(SettingsDbContext database, ICompanyContext companyContext, ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(companyContext);
        ArgumentNullException.ThrowIfNull(currentUser);
        _database = database;
        _companyContext = companyContext;
        _currentUser = currentUser;
    }

    public async Task<SettingsReferenceListV1> ListAsync(Guid companyId, SettingsReferenceListKindV1 kind,
        CancellationToken cancellationToken = default)
    {
        RequireAccess(companyId, cancellationToken);
        if (SettingsReferenceListKindsV1.CodeOf(kind) is not { } kindCode)
        {
            return new SettingsReferenceListV1(companyId, kind, false, []);
        }

        ReferenceListEntrySnapshot[] snapshots;
        try
        {
            snapshots = await ReferenceListReads.LoadAsync(_database, kindCode, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException(SettingsReferenceListProblemCodesV1.ProviderUnavailable);
        }

        var entries = new List<SettingsReferenceListEntryV1>(snapshots.Length);
        foreach (var snapshot in snapshots)
        {
            entries.Add(ToEntry(snapshot, companyId, kindCode)
                ?? throw new SettingsPrerequisiteException(SettingsReferenceListProblemCodesV1.ProfileInvalid));
        }

        return new SettingsReferenceListV1(companyId, kind, true, entries);
    }

    public async Task<SettingsReferenceListLookupV1> GetAsync(Guid companyId, SettingsReferenceListKindV1 kind, Guid entryId,
        CancellationToken cancellationToken = default)
    {
        RequireAccess(companyId, cancellationToken);
        if (SettingsReferenceListKindsV1.CodeOf(kind) is not { } kindCode)
        {
            return new SettingsReferenceListLookupV1(SettingsReferenceListLookupOutcomeV1.Unavailable, null);
        }

        if (entryId == Guid.Empty)
        {
            return new SettingsReferenceListLookupV1(SettingsReferenceListLookupOutcomeV1.NotFound, null);
        }

        ReferenceListEntrySnapshot? snapshot;
        try
        {
            snapshot = await ReferenceListReads.FindAsync(_database, kindCode, entryId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            return new SettingsReferenceListLookupV1(SettingsReferenceListLookupOutcomeV1.Unavailable, null);
        }

        if (snapshot is null)
        {
            return new SettingsReferenceListLookupV1(SettingsReferenceListLookupOutcomeV1.NotFound, null);
        }

        if (ToEntry(snapshot, companyId, kindCode) is not { } entry || entry.Id != entryId)
        {
            return new SettingsReferenceListLookupV1(SettingsReferenceListLookupOutcomeV1.Unavailable, null);
        }

        return new SettingsReferenceListLookupV1(
            entry.Active ? SettingsReferenceListLookupOutcomeV1.Found : SettingsReferenceListLookupOutcomeV1.Inactive,
            entry);
    }

    /// <summary>The published shape of a stored entry, or null when the stored data is malformed.</summary>
    private static SettingsReferenceListEntryV1? ToEntry(ReferenceListEntrySnapshot snapshot, Guid companyId, string kindCode)
    {
        var entry = snapshot.Entry;
        if (snapshot.Latest is not { } latest
            || entry.CompanyId != companyId || latest.CompanyId != companyId
            || entry.EntryId == Guid.Empty || latest.EntryId != entry.EntryId
            || !string.Equals(entry.Kind, kindCode, StringComparison.Ordinal)
            || !ReferenceListRulesV1.IsCode(entry.Code)
            || latest.Version <= 0
            || !ReferenceListRulesV1.IsStoredName(latest.Name))
        {
            return null;
        }

        return new SettingsReferenceListEntryV1(entry.EntryId, entry.Code, latest.Name, latest.Active, latest.Version);
    }

    /// <summary>
    /// Cancelled → <see cref="OperationCanceledException"/>; no access, or a requested company other than the server-resolved
    /// one → <c>settings_access_denied</c>.
    /// </summary>
    private void RequireAccess(Guid companyId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(_currentUser.UserId)
            || !_companyContext.IsResolved || _companyContext.CompanyId == Guid.Empty
            || companyId != _companyContext.CompanyId
            || !_currentUser.CanAccessCompany(_companyContext.CompanyId))
        {
            throw new SettingsPrerequisiteException(SettingsReferenceListProblemCodesV1.AccessDenied);
        }
    }
}
