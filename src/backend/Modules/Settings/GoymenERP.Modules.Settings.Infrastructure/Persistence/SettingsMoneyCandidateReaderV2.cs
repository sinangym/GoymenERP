using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>Operation-specific candidates from the same stored timeline as the monetary read.
/// An empty authoring target is not a candidate, and a passivated profile has no catalog fallback.</summary>
public sealed class SettingsMoneyCandidateReaderV2(SettingsDbContext database, ICompanyContext company,
    ICurrentUser user, TimeProvider time) : ISettingsMoneyCandidateReaderV2
{
    public async Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(string operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var companyId = company.IsResolved ? company.CompanyId : Guid.Empty;
        var actor = user.UserId;
        RequireCurrent(companyId, actor);
        if (!MoneyProfileAuthoringCatalogV1.Targets(companyId).Any(key => key.Operation == operation))
            throw new SettingsPrerequisiteException("settings_provider_unavailable");
        try
        {
            var storedCurrencies = await database.MoneyProfileVersions.AsNoTracking().Where(row => row.Operation == operation)
                .Select(row => row.CurrencyId).Distinct().ToArrayAsync(cancellationToken);
            RequireCurrent(companyId, actor);
            if (storedCurrencies.Any(currency => !MoneyProfileAuthoringCatalogV1.Allows(companyId, currency, operation)))
                throw new SettingsPrerequisiteException("settings_profile_invalid");
            var result = new List<MoneyCurrencyCandidateV1>();
            var today = SettingsBusinessDate.Today(time);
            foreach (var currency in MoneyProfileAuthoringCatalogV1.Currencies)
            {
                var rows = await MoneyProfileReads.LoadAsync(database, currency.CurrencyId, operation, cancellationToken);
                RequireCurrent(companyId, actor);
                if (rows.Length == 0) rows = MoneyProfileReads.Baseline(companyId, currency.CurrencyId, operation);
                MoneyProfileReads.Validate(rows, companyId, currency.CurrencyId, operation);
                if (SalesPolicyTimelineV1.InForceOn(MoneyProfileReads.Timeline(rows), today)?.Value is { IsActive: true })
                    result.Add(currency);
            }
            RequireCurrent(companyId, actor);
            return MoneyCurrencyCandidatesV1.EnsureWellFormed(result.OrderBy(row => row.Code, StringComparer.Ordinal).ToArray());
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException("settings_provider_unavailable");
        }
    }

    private void RequireCurrent(Guid companyId, string? actor)
    {
        if (companyId == Guid.Empty || !company.IsResolved || company.CompanyId != companyId
            || string.IsNullOrWhiteSpace(actor) || !user.IsAuthenticated || user.UserId != actor || !user.CanAccessCompany(companyId))
            throw new SettingsPrerequisiteException("settings_access_denied");
    }
}
