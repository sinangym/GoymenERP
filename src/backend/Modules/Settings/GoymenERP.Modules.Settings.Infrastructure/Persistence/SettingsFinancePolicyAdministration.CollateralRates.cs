using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

public sealed partial class SettingsFinancePolicyAdministration
{
    public async Task<SettingsCollateralRatesWorkspaceV1> GetCollateralRatesAsync(CancellationToken cancellationToken = default)
    {
        var scope = Capture(cancellationToken); await RequireAsync(scope, [SettingsFinancePolicyPermissionsV1.Prepare], false, cancellationToken);
        var types = await ReadAsync(() => collateralTypes.ListAsync(scope.CompanyId, cancellationToken)); Same(scope, cancellationToken);
        var banks = await GetBankInstitutionsAsync(scope.CompanyId, Today(), cancellationToken); Same(scope, cancellationToken);
        var rows = await ReadAsync(() => database.CollateralRatePolicies.AsNoTracking().OrderByDescending(x => x.Version).ToArrayAsync(cancellationToken));
        await RequireAsync(scope, [SettingsFinancePolicyPermissionsV1.Prepare], true, cancellationToken);
        if (types.CompanyId != scope.CompanyId) throw Unavailable();
        return new(scope.CompanyId, Today(), types, banks, rows.Select(CollateralRateView).ToArray());
    }

    public Task<SettingsFinancePolicyCommandResultV1> PrepareCollateralRateAsync(PrepareSettingsCollateralRateV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default) =>
        ExecuteAsync("settings.collateral-rate.prepare.v1", null, request, metadata, [SettingsFinancePolicyPermissionsV1.Prepare], async (scope, today, token) =>
        {
            if (!FinancePolicyRulesV1.TryVersion(request.ExpectedVersion, out var expected) || !FinancePolicyRulesV1.IsWindow(request.ValidFrom, request.ValidThrough, today)
                || !Reason(request.Reason, out var reason) || !CollateralTypeRulesV1.TryRate(request.RatePercent, out var rate)
                || !CollateralRateScopeValid(request.Scope, request.CollateralTypeId, request.ExpectedCollateralTypeVersion, request.BankInstitutionId,
                    request.ExpectedBankSourceVersion, request.MaximumRemainingDays)) return new(Invalid());
            var key = CollateralRateScopeKey(request.Scope, request.CollateralTypeId, request.BankInstitutionId);
            var latest = await database.CollateralRatePolicies.Where(x => x.ScopeKey == key).OrderByDescending(x => x.Version).Select(x => (long?)x.Version).FirstOrDefaultAsync(token) ?? 0;
            if (latest != expected) return new(Conflict());
            var row = new CollateralRatePolicyRow { CompanyId = scope.CompanyId, PolicyId = Guid.NewGuid(), Version = checked(latest + 1), StateVersion = 1, State = "Draft",
                ValidFrom = request.ValidFrom, ValidThrough = request.ValidThrough, PreparedBy = scope.ActorId, PreparedAtUtc = clock.GetUtcNow(), Reason = reason, LastReason = reason,
                Scope = request.Scope.ToString(), ScopeKey = key, CollateralTypeId = request.CollateralTypeId, CollateralTypeVersion = request.ExpectedCollateralTypeVersion,
                BankInstitutionId = request.BankInstitutionId, BankSourceVersion = request.ExpectedBankSourceVersion, MaximumRemainingDays = request.MaximumRemainingDays, RatePercent = rate };
            var view = CollateralRateView(row);
            if (!await CollateralReferencesMatchAsync(scope, view, token)) return new(Problem(409, SettingsFinancePolicyCodesV1.ReferenceChanged, "Teminat türü veya açık banka seçiminin kaynağı değişmiş."));
            database.CollateralRatePolicies.Add(row);
            return new(Saved(row), "Settings.CollateralRatePolicy", row.PolicyId, "settings.collateral-rate.prepared.v1", null, row.Version, null, view, reason);
        }, cancellationToken);

    public Task<SettingsFinancePolicyCommandResultV1> PublishCollateralRateAsync(Guid policyId, PublishSettingsFinancePolicyV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default) =>
        CollateralLifecycleAsync(false, false, policyId, request, request.ExpectedStateVersion, null, request.Reason, metadata, cancellationToken);
    public Task<SettingsFinancePolicyCommandResultV1> RetireCollateralRateAsync(Guid policyId, RetireSettingsFinancePolicyV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default) =>
        CollateralLifecycleAsync(false, true, policyId, request, request.ExpectedStateVersion, request.RetiredFrom, request.Reason, metadata, cancellationToken);

    private Task<SettingsFinancePolicyCommandResultV1> CollateralLifecycleAsync<T>(bool bank, bool retiring, Guid policyId, T request,
        string? expectedVersion, DateOnly? retiredFrom, string? inputReason, SettingsCommandMetadataV1 metadata, CancellationToken token) =>
        ExecuteAsync($"settings.{(bank ? "bank-institutions" : "collateral-rate")}.{(retiring ? "retire" : "publish")}.v1", policyId, request, metadata,
            [SettingsFinancePolicyPermissionsV1.Prepare, SettingsFinancePolicyPermissionsV1.Publish], async (scope, today, cancellationToken) =>
            {
                if (!FinancePolicyRulesV1.TryVersion(expectedVersion, out var expected) || !Reason(inputReason, out var reason)) return new(Invalid());
                var bankRow = bank ? await database.BankInstitutionPolicies.SingleOrDefaultAsync(x => x.PolicyId == policyId, cancellationToken) : null;
                var rateRow = bank ? null : await database.CollateralRatePolicies.SingleOrDefaultAsync(x => x.PolicyId == policyId, cancellationToken);
                IFinancePolicyVersionRow? row = bank ? bankRow : rateRow;
                if (row is null) return new(Problem(404, SettingsFinancePolicyCodesV1.NotFound, "Politika bulunamadı."));
                var entries = bank ? await database.BankInstitutionEntries.Where(x => x.PolicyId == policyId).ToArrayAsync(cancellationToken) : [];
                object before = bank ? BankView(bankRow!, entries) : CollateralRateView(rateRow!);
                if (row.StateVersion != expected || row.State != (retiring ? "Published" : "Draft")) return new(Conflict());
                if (retiring)
                { if (retiredFrom is null || retiredFrom < today) return new(Invalid()); }
                else
                {
                    if (!FinancePolicyRulesV1.IsWindow(row.ValidFrom, row.ValidThrough, today)) return new(Invalid());
                    if (bank)
                    {
                        if (!BankEntriesValid(entries, scope.CompanyId, row.PolicyId)) throw Unavailable();
                        var others = await database.BankInstitutionPolicies.AsNoTracking().Where(x => x.PolicyId != policyId && x.State != "Draft").ToArrayAsync(cancellationToken);
                        if (others.Any(x => FinancePolicyRulesV1.Overlaps(x.ValidFrom, x.ValidThrough, x.RetiredFrom, row.ValidFrom, row.ValidThrough)))
                            return new(Problem(409, SettingsFinancePolicyCodesV1.WindowConflict, "Banka listesinin tarih penceresi çakışıyor; mevcut yayını açıkça sonlandırın."));
                    }
                    else
                    {
                        if (!await CollateralReferencesMatchAsync(scope, CollateralRateView(rateRow!), cancellationToken))
                            return new(Problem(409, SettingsFinancePolicyCodesV1.ReferenceChanged, "Teminat veya banka kaynağının sürümü değişmiş."));
                        var others = await database.CollateralRatePolicies.AsNoTracking().Where(x => x.PolicyId != policyId && x.ScopeKey == rateRow!.ScopeKey && x.State != "Draft").ToArrayAsync(cancellationToken);
                        // All 'N days or less' predicates overlap at zero. A second active maximum is not a disjoint band.
                        if (others.Any(x => FinancePolicyRulesV1.Overlaps(x.ValidFrom, x.ValidThrough, x.RetiredFrom, row.ValidFrom, row.ValidThrough)))
                            return new(Problem(409, SettingsFinancePolicyCodesV1.WindowConflict, "Aynı teminat/banka/özgüllük kapsamında iki oran yayımlanamaz."));
                    }
                }
                row.State = retiring ? "Retired" : "Published"; row.StateVersion = checked(row.StateVersion + 1); row.LastReason = reason;
                if (retiring) row.RetiredFrom = retiredFrom; else { row.PublishedBy = scope.ActorId; row.PublishedAtUtc = clock.GetUtcNow(); }
                object after = bank ? BankView(bankRow!, entries) : CollateralRateView(rateRow!);
                return new(Saved(row), bank ? "Settings.BankInstitutionCatalog" : "Settings.CollateralRatePolicy", row.PolicyId,
                    $"settings.{(bank ? "bank-institutions" : "collateral-rate")}.{(retiring ? "retired" : "published")}.v1", expected, row.StateVersion, before, after, reason);
            }, token);

    private async Task<bool> CollateralReferencesMatchAsync(Scope scope, SettingsCollateralRatePolicyV1 policy, CancellationToken token)
    {
        if (!CollateralRateScopeValid(policy.Scope, policy.CollateralTypeId, policy.CollateralTypeVersion, policy.BankInstitutionId, policy.BankSourceVersion, policy.MaximumRemainingDays)) return false;
        if (policy.Scope == SettingsCollateralRateScopeV1.Company) return true;
        var types = await ReadAsync(() => collateralTypes.ListAsync(scope.CompanyId, token)); Same(scope, token);
        var banks = await GetBankInstitutionsAsync(scope.CompanyId, Today(), token); Same(scope, token);
        return types.CompanyId == scope.CompanyId && types.Entries.Any(x => x.Id == policy.CollateralTypeId && x.Active && x.Version == policy.CollateralTypeVersion)
            && banks.Outcome == SettingsFinancePolicyReadOutcomeV1.Available && banks.Entries.Any(x => x.BankInstitutionId == policy.BankInstitutionId && x.Active && x.SourceVersion == policy.BankSourceVersion);
    }

    private static bool CollateralRateScopeValid(SettingsCollateralRateScopeV1 scope, Guid? typeId, string? typeVersion, Guid? bankId, string? bankVersion, int? maximumDays) =>
        scope == SettingsCollateralRateScopeV1.Company ? typeId is null && typeVersion is null && bankId is null && bankVersion is null && maximumDays is null
            : scope is SettingsCollateralRateScopeV1.TypeBank or SettingsCollateralRateScopeV1.TypeBankRemainingDays
                && typeId is { } type && type != Guid.Empty && !string.IsNullOrWhiteSpace(typeVersion)
                && bankId is { } bank && bank != Guid.Empty && !string.IsNullOrWhiteSpace(bankVersion)
                && (scope == SettingsCollateralRateScopeV1.TypeBank ? maximumDays is null : maximumDays is >= 0);
    private static string CollateralRateScopeKey(SettingsCollateralRateScopeV1 scope, Guid? typeId, Guid? bankId) =>
        scope == SettingsCollateralRateScopeV1.Company ? "Company" : $"{scope}:{typeId:D}:{bankId:D}";
    private static SettingsCollateralRatePolicyV1 CollateralRateView(CollateralRatePolicyRow row) => new(row.CompanyId, row.PolicyId, row.Version, row.StateVersion,
        State(row), row.ValidFrom, row.ValidThrough, row.RetiredFrom, row.PreparedBy, row.PreparedAtUtc, row.PublishedBy, row.PublishedAtUtc, row.Reason,
        Enum.Parse<SettingsCollateralRateScopeV1>(row.Scope), row.CollateralTypeId, row.CollateralTypeVersion, row.BankInstitutionId, row.BankSourceVersion, row.MaximumRemainingDays, row.RatePercent);
}
