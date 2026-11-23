using System.Globalization;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

public sealed partial class SettingsFinancePolicyAdministration
{
    public async Task<SettingsBankInstitutionsWorkspaceV1> GetBankInstitutionsAsync(CancellationToken cancellationToken = default)
    {
        var scope = Capture(cancellationToken); await RequireAsync(scope, [SettingsFinancePolicyPermissionsV1.Prepare], false, cancellationToken);
        var rows = await ReadAsync(() => database.BankInstitutionPolicies.AsNoTracking().OrderByDescending(x => x.Version).ToArrayAsync(cancellationToken));
        Same(scope, cancellationToken);
        var entries = await ReadAsync(() => database.BankInstitutionEntries.AsNoTracking().ToArrayAsync(cancellationToken));
        await RequireAsync(scope, [SettingsFinancePolicyPermissionsV1.Prepare], true, cancellationToken);
        return new(scope.CompanyId, Today(), rows.Select(x => BankView(x, entries.Where(e => e.PolicyId == x.PolicyId))).ToArray());
    }

    public Task<SettingsFinancePolicyCommandResultV1> PrepareBankInstitutionsAsync(PrepareSettingsBankInstitutionsV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default) =>
        ExecuteAsync("settings.bank-institutions.prepare.v1", null, request, metadata, [SettingsFinancePolicyPermissionsV1.Prepare], async (scope, today, token) =>
        {
            if (!FinancePolicyRulesV1.TryVersion(request.ExpectedVersion, out var expected) || !FinancePolicyRulesV1.IsWindow(request.ValidFrom, request.ValidThrough, today)
                || !Reason(request.Reason, out var reason) || request.Entries is not { Count: <= 256 }) return new(Invalid());
            var latest = await database.BankInstitutionPolicies.OrderByDescending(x => x.Version).Select(x => (long?)x.Version).FirstOrDefaultAsync(token) ?? 0;
            if (latest != expected) return new(Conflict());
            var known = await database.BankInstitutionEntries.Select(x => x.BankInstitutionId).Distinct().ToArrayAsync(token);
            var entries = new List<BankInstitutionEntryRow>(); var id = Guid.NewGuid();
            foreach (var input in request.Entries)
            {
                if (input is null || input.Name?.Trim() is not { Length: > 0 and <= 200 } name
                    || input.EftCode?.Trim() is not { Length: > 0 and <= 32 } eft
                    || input.BankInstitutionId is { } knownId && (knownId == Guid.Empty || !known.Contains(knownId))) return new(Invalid());
                entries.Add(new() { CompanyId = scope.CompanyId, PolicyId = id, BankInstitutionId = input.BankInstitutionId ?? Guid.NewGuid(), Name = name, EftCode = eft, Active = input.Active });
            }
            if (entries.Select(x => x.BankInstitutionId).Distinct().Count() != entries.Count
                || entries.Select(x => x.EftCode).Distinct(StringComparer.Ordinal).Count() != entries.Count) return new(Invalid());
            var row = new BankInstitutionPolicyRow { CompanyId = scope.CompanyId, PolicyId = id, Version = checked(latest + 1), StateVersion = 1,
                State = "Draft", ValidFrom = request.ValidFrom, ValidThrough = request.ValidThrough, PreparedBy = scope.ActorId,
                PreparedAtUtc = clock.GetUtcNow(), Reason = reason, LastReason = reason };
            database.BankInstitutionPolicies.Add(row); database.BankInstitutionEntries.AddRange(entries);
            return new(Saved(row), "Settings.BankInstitutionCatalog", id, "settings.bank-institutions.prepared.v1", null, row.Version, null, BankView(row, entries), reason);
        }, cancellationToken);

    public Task<SettingsFinancePolicyCommandResultV1> PublishBankInstitutionsAsync(Guid policyId, PublishSettingsFinancePolicyV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default) =>
        CollateralLifecycleAsync(true, false, policyId, request, request.ExpectedStateVersion, null, request.Reason, metadata, cancellationToken);
    public Task<SettingsFinancePolicyCommandResultV1> RetireBankInstitutionsAsync(Guid policyId, RetireSettingsFinancePolicyV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default) =>
        CollateralLifecycleAsync(true, true, policyId, request, request.ExpectedStateVersion, request.RetiredFrom, request.Reason, metadata, cancellationToken);

    public async Task<SettingsBankInstitutionCatalogV1> GetBankInstitutionsAsync(Guid companyId, DateOnly businessDate, CancellationToken cancellationToken = default)
    {
        var scope = Capture(cancellationToken); if (scope.CompanyId != companyId) throw new SettingsPrerequisiteException(SettingsFinancePolicyCodesV1.AccessDenied);
        var rows = await ReadAsync(() => database.BankInstitutionPolicies.AsNoTracking().Where(x => x.State != "Draft" && x.ValidFrom <= businessDate
            && (x.ValidThrough == null || businessDate <= x.ValidThrough) && (x.RetiredFrom == null || businessDate < x.RetiredFrom)).ToArrayAsync(cancellationToken));
        Same(scope, cancellationToken);
        if (rows.Length != 1) return new(SettingsFinancePolicyReadOutcomeV1.Unavailable, companyId, [], null, SettingsFinancePolicyCodesV1.NotConfigured);
        var row = rows[0]; if (!ValidHeader(row)) throw Unavailable();
        var entries = await ReadAsync(() => database.BankInstitutionEntries.AsNoTracking().Where(x => x.PolicyId == row.PolicyId).ToArrayAsync(cancellationToken));
        Same(scope, cancellationToken);
        if (!BankEntriesValid(entries, companyId, row.PolicyId)) throw Unavailable();
        return new(SettingsFinancePolicyReadOutcomeV1.Available, companyId, BankView(row, entries).Entries, BankSource(row), null);
    }

    public async Task<SettingsBankInstitutionSnapshotV1?> ReadBankInstitutionProofAsync(Guid companyId, Guid bankInstitutionId,
        string sourceVersion, CancellationToken cancellationToken = default)
    {
        var scope = Capture(cancellationToken); if (scope.CompanyId != companyId) throw new SettingsPrerequisiteException(SettingsFinancePolicyCodesV1.AccessDenied);
        if (bankInstitutionId == Guid.Empty || string.IsNullOrWhiteSpace(sourceVersion)) return null;
        var rows = await ReadAsync(() => (from e in database.BankInstitutionEntries.AsNoTracking()
            join p in database.BankInstitutionPolicies.AsNoTracking() on new { e.CompanyId, e.PolicyId } equals new { p.CompanyId, p.PolicyId }
            where e.BankInstitutionId == bankInstitutionId && p.State != "Draft" select new { Entry = e, Policy = p }).ToArrayAsync(cancellationToken));
        Same(scope, cancellationToken);
        var matches = rows.Where(x => BankSource(x.Policy) == sourceVersion).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length != 1 || !ValidHeader(matches[0].Policy) || !BankEntriesValid([matches[0].Entry], companyId, matches[0].Policy.PolicyId)) throw Unavailable();
        var entry = matches[0].Entry;
        return new(entry.BankInstitutionId, entry.Name, entry.EftCode, entry.Active, sourceVersion);
    }

    private static string BankSource(BankInstitutionPolicyRow row) => $"settings.bank-institutions:{row.PolicyId:D}:{row.Version.ToString(CultureInfo.InvariantCulture)}";
    private static bool BankEntriesValid(IReadOnlyList<BankInstitutionEntryRow> entries, Guid companyId, Guid policyId) =>
        entries.All(x => x.CompanyId == companyId && x.PolicyId == policyId && x.BankInstitutionId != Guid.Empty
            && !string.IsNullOrWhiteSpace(x.Name) && !string.IsNullOrWhiteSpace(x.EftCode))
        && entries.Select(x => x.BankInstitutionId).Distinct().Count() == entries.Count
        && entries.Select(x => x.EftCode).Distinct(StringComparer.Ordinal).Count() == entries.Count;
    private static SettingsBankInstitutionPolicyV1 BankView(BankInstitutionPolicyRow row, IEnumerable<BankInstitutionEntryRow> entries) =>
        new(row.CompanyId, row.PolicyId, row.Version, row.StateVersion, State(row), row.ValidFrom, row.ValidThrough, row.RetiredFrom,
            row.PreparedBy, row.PreparedAtUtc, row.PublishedBy, row.PublishedAtUtc, row.Reason, entries.OrderBy(x => x.EftCode, StringComparer.Ordinal)
                .Select(x => new SettingsBankInstitutionSnapshotV1(x.BankInstitutionId, x.Name, x.EftCode, x.Active, BankSource(row))).ToArray());
}
