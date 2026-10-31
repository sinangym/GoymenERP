using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>The stored rows of one company's tax master, read under the company filter.</summary>
internal sealed record TaxMasterRows(
    IReadOnlyList<TaxItemTypeDefaultSeriesRow> Series,
    IReadOnlyList<TaxItemTypeDefaultRecordRow> Records,
    IReadOnlyList<TaxItemTypeDefaultWithdrawalRow> RecordWithdrawals,
    IReadOnlyList<TaxCodeListRow> Lists,
    IReadOnlyList<TaxCodeListVersionRow> ListVersions,
    IReadOnlyList<TaxCodeListEntryRow> ListEntries,
    IReadOnlyList<TaxCodeListWithdrawalRow> ListWithdrawals,
    TaxWithholdingThresholdRow? Threshold,
    IReadOnlyList<TaxWithholdingThresholdRecordRow> ThresholdRecords,
    IReadOnlyList<TaxWithholdingThresholdWithdrawalRow> ThresholdWithdrawals)
{
    public static async Task<TaxMasterRows> ReadAsync(SettingsDbContext database, bool track,
        CancellationToken cancellationToken)
    {
        var series = track
            ? await database.TaxItemTypeDefaultSeries.ToListAsync(cancellationToken)
            : await database.TaxItemTypeDefaultSeries.AsNoTracking().ToListAsync(cancellationToken);
        var records = await database.TaxItemTypeDefaultRecords.AsNoTracking().ToListAsync(cancellationToken);
        var recordWithdrawals = await database.TaxItemTypeDefaultWithdrawals.AsNoTracking().ToListAsync(cancellationToken);
        var lists = track
            ? await database.TaxCodeLists.ToListAsync(cancellationToken)
            : await database.TaxCodeLists.AsNoTracking().ToListAsync(cancellationToken);
        var listVersions = await database.TaxCodeListVersions.AsNoTracking().ToListAsync(cancellationToken);
        var listEntries = await database.TaxCodeListEntries.AsNoTracking().ToListAsync(cancellationToken);
        var listWithdrawals = await database.TaxCodeListWithdrawals.AsNoTracking().ToListAsync(cancellationToken);
        var threshold = track
            ? await database.TaxWithholdingThresholds.SingleOrDefaultAsync(cancellationToken)
            : await database.TaxWithholdingThresholds.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var thresholdRecords = await database.TaxWithholdingThresholdRecords.AsNoTracking().ToListAsync(cancellationToken);
        var thresholdWithdrawals = await database.TaxWithholdingThresholdWithdrawals.AsNoTracking().ToListAsync(cancellationToken);
        return new TaxMasterRows(series, records, recordWithdrawals, lists, listVersions, listEntries, listWithdrawals,
            threshold, thresholdRecords, thresholdWithdrawals);
    }

    /// <summary>The company's data, the reviewed SAT-D139 catalog entries of <paramref name="companyId"/> included.</summary>
    public TaxMasterDataV1 ToData(Guid companyId)
    {
        var withdrawnRecords = RecordWithdrawals.Select(withdrawal => withdrawal.RecordId).ToHashSet();
        var withdrawnThresholds = ThresholdWithdrawals.Select(withdrawal => withdrawal.RecordId).ToHashSet();
        var withdrawnLists = ListWithdrawals.Select(withdrawal => (withdrawal.Kind, withdrawal.ListVersion)).ToHashSet();
        var entries = ListEntries
            .GroupBy(entry => (entry.Kind, entry.ListVersion))
            .ToDictionary(group => group.Key, group => (IReadOnlyList<TaxCodeListEntryViewV1>)group
                .OrderBy(entry => entry.Code, StringComparer.Ordinal)
                .Select(entry => new TaxCodeListEntryViewV1(entry.Code, entry.Name))
                .ToArray());
        var defaults = ApprovedTaxItemTypeDefaultCatalog.For(companyId)
            .Concat(Records.Select(row => new TaxItemTypeDefaultDataV1(row.Id, row.CompanyId, row.ItemType,
                TaxDefaultSourcesV1.Record, row.RecordVersion, row.EffectiveFrom, row.VatApplies, row.VatPercent, row.ExemptionCode,
                row.WithholdingApplies, row.WithholdingPercent, row.WithholdingCode, withdrawnRecords.Contains(row.Id))))
            .ToArray();
        return new TaxMasterDataV1(
            defaults,
            ListVersions.Select(row => new TaxCodeListVersionDataV1(row.Kind, row.ListVersion, row.EffectiveFrom,
                entries.GetValueOrDefault((row.Kind, row.ListVersion)) ?? [],
                withdrawnLists.Contains((row.Kind, row.ListVersion)))).ToArray(),
            ThresholdRecords.Select(row => new TaxWithholdingThresholdDataV1(row.Id, row.RecordVersion, row.EffectiveFrom,
                row.Amount, row.CurrencyCode, withdrawnThresholds.Contains(row.Id))).ToArray());
    }
}
