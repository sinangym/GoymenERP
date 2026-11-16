using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

internal static class MoneyProfileReads
{
    public static async Task<MoneyProfileVersionRow[]> LoadAsync(SettingsDbContext database, Guid currencyId, string operation,
        CancellationToken token)
    {
        var transferred = await database.MoneyProfileBaselineTransfers.AsNoTracking()
            .AnyAsync(row => row.CurrencyId == currencyId && row.Operation == operation, token);
        var rows = await database.MoneyProfileVersions.AsNoTracking()
            .Where(row => row.CurrencyId == currencyId && row.Operation == operation).OrderBy(row => row.Version).ToArrayAsync(token);
        if (transferred != (rows.Length != 0)) throw new SettingsPrerequisiteException("settings_profile_invalid");
        return rows;
    }

    public static IReadOnlyList<SalesPolicyTimelineEntryV1<MoneyProfileVersionRow>> Timeline(IEnumerable<MoneyProfileVersionRow> rows) =>
        SalesPolicyTimelineV1.Build(rows, row => row.Version, row => row.EffectiveFrom);

    public static MoneyProfileVersionRow[] Baseline(Guid company, Guid currency, string operation)
    {
        var entries = ApprovedMoneyProfileCatalog.Approved.Entries
            .Where(entry => entry.CompanyId == company && entry.CurrencyId == currency && entry.Operations.Contains(operation))
            .OrderBy(entry => entry.EffectiveFrom).ThenBy(entry => entry.Version).ToArray();
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var expectedEnd = index + 1 < entries.Length ? entries[index + 1].EffectiveFrom.AddDays(-1) : (DateOnly?)null;
            if (entry.EffectiveThrough != expectedEnd || entry.Source.Length > 400 || operation.Length > 128)
                throw new SettingsPrerequisiteException("settings_profile_invalid");
        }
        return entries.Select(entry => new MoneyProfileVersionRow
        {
            CompanyId = company, CurrencyId = currency, Operation = operation, CurrencyCode = entry.CurrencyCode,
            ProfileId = entry.ProfileId, Version = entry.Version, Scale = entry.Scale,
            UnitPriceScale = entry.UnitPriceScales is { } scales && scales.TryGetValue(operation, out var scale) ? scale : null,
            IsActive = entry.IsActive,
            EffectiveFrom = entry.EffectiveFrom, SourceKind = SalesPolicySourceKindsV1.CompiledBaselineV0,
            DecisionReference = entry.Source,
        }).ToArray();
    }

    public static void Validate(IEnumerable<MoneyProfileVersionRow> rows, Guid company, Guid currency, string operation)
    {
        var baseline = Baseline(company, currency, operation);
        if (baseline.Length == 0) throw new SettingsPrerequisiteException("settings_profile_invalid");
        foreach (var row in rows)
        {
            if (row.CompanyId != company || row.CurrencyId != currency || row.Operation != operation || row.ProfileId == Guid.Empty
                || row.Version <= 0 || row.CurrencyCode != baseline[^1].CurrencyCode || row.Scale is < 0 or > 28
                || row.UnitPriceScale is < 0 or > 28)
                throw new SettingsPrerequisiteException("settings_profile_invalid");
        }
    }
}
