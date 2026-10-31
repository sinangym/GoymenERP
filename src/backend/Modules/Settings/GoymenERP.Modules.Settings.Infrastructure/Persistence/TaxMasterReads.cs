using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>The stored rows of one company's tax master, read under the company filter.</summary>
internal sealed record TaxMasterRows(
    IReadOnlyList<TaxCategoryRow> Categories,
    IReadOnlyList<TaxRateRecordRow> Records,
    IReadOnlyList<TaxRateRecordWithdrawalRow> RecordWithdrawals,
    IReadOnlyList<TaxCodeListRow> Lists,
    IReadOnlyList<TaxCodeListVersionRow> ListVersions,
    IReadOnlyList<TaxCodeListEntryRow> ListEntries,
    IReadOnlyList<TaxCodeListWithdrawalRow> ListWithdrawals)
{
    public static async Task<TaxMasterRows> ReadAsync(SettingsDbContext database, bool trackCategoriesAndLists,
        CancellationToken cancellationToken)
    {
        var categories = trackCategoriesAndLists
            ? await database.TaxCategories.ToListAsync(cancellationToken)
            : await database.TaxCategories.AsNoTracking().ToListAsync(cancellationToken);
        var records = await database.TaxRateRecords.AsNoTracking().ToListAsync(cancellationToken);
        var recordWithdrawals = await database.TaxRateRecordWithdrawals.AsNoTracking().ToListAsync(cancellationToken);
        var lists = trackCategoriesAndLists
            ? await database.TaxCodeLists.ToListAsync(cancellationToken)
            : await database.TaxCodeLists.AsNoTracking().ToListAsync(cancellationToken);
        var listVersions = await database.TaxCodeListVersions.AsNoTracking().ToListAsync(cancellationToken);
        var listEntries = await database.TaxCodeListEntries.AsNoTracking().ToListAsync(cancellationToken);
        var listWithdrawals = await database.TaxCodeListWithdrawals.AsNoTracking().ToListAsync(cancellationToken);
        return new TaxMasterRows(categories, records, recordWithdrawals, lists, listVersions, listEntries, listWithdrawals);
    }

    public TaxMasterDataV1 ToData()
    {
        var withdrawnRecords = RecordWithdrawals.Select(withdrawal => withdrawal.RecordId).ToHashSet();
        var withdrawnLists = ListWithdrawals.Select(withdrawal => (withdrawal.Kind, withdrawal.ListVersion)).ToHashSet();
        var entries = ListEntries
            .GroupBy(entry => (entry.Kind, entry.ListVersion))
            .ToDictionary(group => group.Key, group => (IReadOnlyList<TaxCodeListEntryViewV1>)group
                .OrderBy(entry => entry.Code, StringComparer.Ordinal)
                .Select(entry => new TaxCodeListEntryViewV1(entry.Code, entry.Name))
                .ToArray());
        return new TaxMasterDataV1(
            Categories.ToDictionary(row => row.Id, row => new TaxCategoryDataV1(row.Id, row.Kind, row.Code, row.Name)),
            Records.Select(row => new TaxRateRecordDataV1(row.Id, row.Kind, row.ClassId, row.BuyerCategoryId, row.RecordVersion,
                row.EffectiveFrom, row.Applies, row.RatePercent, row.WithholdingCode, withdrawnRecords.Contains(row.Id))).ToArray(),
            ListVersions.Select(row => new TaxCodeListVersionDataV1(row.Kind, row.ListVersion, row.EffectiveFrom,
                entries.GetValueOrDefault((row.Kind, row.ListVersion)) ?? [],
                withdrawnLists.Contains((row.Kind, row.ListVersion)))).ToArray());
    }
}
