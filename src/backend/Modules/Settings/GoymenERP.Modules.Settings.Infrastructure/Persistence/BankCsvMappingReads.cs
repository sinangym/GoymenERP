using GoymenERP.Modules.Settings.Application;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The reads the bank CSV mapping provider and administration share. Every query runs under the company query filter, so
/// only the server-resolved company's rows are ever seen.
/// </summary>
internal static class BankCsvMappingReads
{
    /// <summary>Every version of every bank, ordered by bank key and version.</summary>
    public static Task<BankCsvMappingVersionRow[]> LoadAllAsync(SettingsDbContext database,
        CancellationToken cancellationToken) =>
        database.BankCsvMappingVersions.AsNoTracking()
            .OrderBy(row => row.BankKey)
            .ThenBy(row => row.Version)
            .ToArrayAsync(cancellationToken);

    /// <summary>Every version of one bank, ordered by version.</summary>
    public static Task<BankCsvMappingVersionRow[]> LoadBankAsync(SettingsDbContext database, string bankKey,
        CancellationToken cancellationToken) =>
        database.BankCsvMappingVersions.AsNoTracking()
            .Where(row => row.BankKey == bankKey)
            .OrderBy(row => row.Version)
            .ToArrayAsync(cancellationToken);

    /// <summary>The rows grouped per bank (ordinal key order), each bank's versions oldest first.</summary>
    public static IReadOnlyList<IReadOnlyList<BankCsvMappingVersionRow>> ByBank(IEnumerable<BankCsvMappingVersionRow> rows) =>
        rows.GroupBy(row => row.BankKey, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => (IReadOnlyList<BankCsvMappingVersionRow>)group.OrderBy(row => row.Version).ToArray())
            .ToArray();

    /// <summary>The values of a stored row in the shape the shared rules check.</summary>
    public static BankCsvMappingValuesV1 Values(BankCsvMappingVersionRow row) =>
        new BankCsvMappingValuesV1(
            row.State,
            row.Delimiter,
            row.SkipLeadingRows,
            row.HasHeaderRow,
            row.DecimalSeparator,
            row.ThousandsSeparator,
            row.DateFormat,
            row.AmountLayout,
            row.SignedAmountPositiveMeans,
            row.TransactionDateColumn,
            row.ValueDateColumn,
            row.DescriptionColumn,
            row.AmountColumn,
            row.InflowColumn,
            row.OutflowColumn,
            row.BalanceColumn,
            row.ReferenceColumn,
            row.CurrencyColumn);
}
