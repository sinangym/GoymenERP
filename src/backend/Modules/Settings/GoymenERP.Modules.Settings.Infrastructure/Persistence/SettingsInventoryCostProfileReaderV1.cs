using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

internal sealed class SettingsInventoryCostProfileReaderV1(SettingsDbContext database, ICompanyContext company)
    : ISettingsInventoryCostProfileReaderV1
{
    // Existing Accounting operation identity; the consumer does not introduce a separate money policy.
    internal const string Operation = "accounting.journal-amount.v1";
    public async Task<MoneyProfileV1> ReadAsync(Guid companyId, DateOnly effectiveOn, CancellationToken cancellationToken = default)
    {
        void Fence()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (companyId == Guid.Empty || !company.IsResolved || company.CompanyId != companyId)
                throw new SettingsPrerequisiteException("settings_access_denied");
        }
        Fence();
        if (effectiveOn == default) throw new SettingsPrerequisiteException("settings_profile_invalid");
        var currency = SettingsCurrencyIdentitiesV1.TurkishLira;
        var rows = await MoneyProfileReads.LoadAsync(database, currency, Operation, cancellationToken);
        Fence();
        if (rows.Length == 0) rows = MoneyProfileReads.Baseline(companyId, currency, Operation);
        if (rows.Length == 0) throw new SettingsPrerequisiteException("settings_money_profile_not_found");
        MoneyProfileReads.Validate(rows, companyId, currency, Operation);
        var entry = SalesPolicyTimelineV1.For(MoneyProfileReads.Timeline(rows), effectiveOn)
            ?? throw new SettingsPrerequisiteException("settings_profile_invalid");
        var row = entry.Value;
        if (!row.IsActive) throw new SettingsPrerequisiteException("settings_profile_invalid");
        Fence();
        return new(companyId, currency, row.CurrencyCode, Operation, new(row.ProfileId, row.Version), row.Scale,
            row.IsActive, entry.EffectiveFrom, entry.EffectiveThrough, row.UnitPriceScale);
    }
}
