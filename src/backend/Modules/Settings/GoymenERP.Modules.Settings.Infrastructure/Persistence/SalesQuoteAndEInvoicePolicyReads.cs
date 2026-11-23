using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>A stored loss reason list version with its entries in list order.</summary>
internal sealed record StoredSalesQuoteLossReasonList(
    SalesQuoteLossReasonListVersionRow Header,
    IReadOnlyList<SalesQuoteLossReasonEntryRow> Entries);

/// <summary>A stored e-Fatura scenario code list version with its codes in list order.</summary>
internal sealed record StoredEInvoiceScenarioCodeList(
    EInvoiceScenarioCodeListVersionRow Header,
    IReadOnlyList<string> Codes);

/// <summary>
/// The reads the quote and e-Fatura scenario provider and administration share. Every query runs under the company query
/// filter, so only the server-resolved company's rows are ever seen. A stored list without entries, or with entries that
/// break the list rules (gaps in positions, repeated ids or codes, a malformed code), is a malformed source:
/// <c>settings_profile_invalid</c>, never repaired.
/// </summary>
internal static class SalesQuoteAndEInvoicePolicyReads
{
    public static async Task<StoredSalesQuoteLossReasonList[]> LoadLossReasonListsAsync(SettingsDbContext database,
        CancellationToken cancellationToken)
    {
        var headers = await database.SalesQuoteLossReasonListVersions.AsNoTracking()
            .OrderBy(row => row.Version)
            .ToArrayAsync(cancellationToken);
        var entries = await database.SalesQuoteLossReasonEntries.AsNoTracking()
            .OrderBy(row => row.ListVersion)
            .ThenBy(row => row.Position)
            .ToArrayAsync(cancellationToken);
        var byVersion = entries.GroupBy(row => row.ListVersion).ToDictionary(group => group.Key, group => group.ToArray());
        if (byVersion.Keys.Any(version => headers.All(header => header.Version != version)))
        {
            throw Invalid();
        }

        return headers.Select(header =>
        {
            var rows = byVersion.TryGetValue(header.Version, out var found) ? found : [];
            if (rows.Length == 0 || rows.Where((row, index) => row.Position != index).Any()
                || rows.Select(row => row.ReasonId).Distinct().Count() != rows.Length
                || rows.Select(row => row.Code).Distinct(StringComparer.Ordinal).Count() != rows.Length)
            {
                throw Invalid();
            }

            return new StoredSalesQuoteLossReasonList(header, rows);
        }).ToArray();
    }

    public static Task<SalesQuoteDefaultValidityVersionRow[]> LoadDefaultValiditiesAsync(SettingsDbContext database,
        CancellationToken cancellationToken) =>
        database.SalesQuoteDefaultValidityVersions.AsNoTracking().OrderBy(row => row.Version).ToArrayAsync(cancellationToken);

    public static async Task<StoredEInvoiceScenarioCodeList[]> LoadCodeListsAsync(SettingsDbContext database,
        CancellationToken cancellationToken)
    {
        var headers = await database.EInvoiceScenarioCodeListVersions.AsNoTracking()
            .OrderBy(row => row.Version)
            .ToArrayAsync(cancellationToken);
        var codes = await database.EInvoiceScenarioCodes.AsNoTracking()
            .OrderBy(row => row.ListVersion)
            .ThenBy(row => row.Position)
            .ToArrayAsync(cancellationToken);
        var byVersion = codes.GroupBy(row => row.ListVersion).ToDictionary(group => group.Key, group => group.ToArray());
        if (byVersion.Keys.Any(version => headers.All(header => header.Version != version)))
        {
            throw Invalid();
        }

        return headers.Select(header =>
        {
            var rows = byVersion.TryGetValue(header.Version, out var found) ? found : [];
            if (rows.Length == 0 || rows.Where((row, index) => row.Position != index).Any()
                || rows.Any(row => !EInvoiceScenarioCodeFormatV1.IsWellFormed(row.Code))
                || rows.Select(row => row.Code).Distinct(StringComparer.Ordinal).Count() != rows.Length)
            {
                throw Invalid();
            }

            return new StoredEInvoiceScenarioCodeList(header, rows.Select(row => row.Code).ToArray());
        }).ToArray();
    }

    public static Task<EInvoiceScenarioDefaultVersionRow[]> LoadScenarioDefaultsAsync(SettingsDbContext database,
        CancellationToken cancellationToken) =>
        database.EInvoiceScenarioDefaultVersions.AsNoTracking().OrderBy(row => row.Version).ToArrayAsync(cancellationToken);

    public static IReadOnlyList<SalesPolicyTimelineEntryV1<StoredSalesQuoteLossReasonList>> LossReasonTimeline(
        IEnumerable<StoredSalesQuoteLossReasonList> lists) =>
        SalesPolicyTimelineV1.Build(lists, list => list.Header.Version, list => list.Header.EffectiveFrom);

    public static IReadOnlyList<SalesPolicyTimelineEntryV1<SalesQuoteDefaultValidityVersionRow>> DefaultValidityTimeline(
        IEnumerable<SalesQuoteDefaultValidityVersionRow> rows) =>
        SalesPolicyTimelineV1.Build(rows, row => row.Version, row => row.EffectiveFrom);

    public static IReadOnlyList<SalesPolicyTimelineEntryV1<StoredEInvoiceScenarioCodeList>> CodeListTimeline(
        IEnumerable<StoredEInvoiceScenarioCodeList> lists) =>
        SalesPolicyTimelineV1.Build(lists, list => list.Header.Version, list => list.Header.EffectiveFrom);

    public static IReadOnlyList<SalesPolicyTimelineEntryV1<EInvoiceScenarioDefaultVersionRow>> ScenarioDefaultTimeline(
        IEnumerable<EInvoiceScenarioDefaultVersionRow> rows) =>
        SalesPolicyTimelineV1.Build(rows, row => row.Version, row => row.EffectiveFrom);

    /// <summary>
    /// Whether every default version whose window reaches a date covered by a code list version carries one of that list's
    /// codes (the SAT-D102 rule "a served default is a code of the list in force on the same date"). Superseded versions
    /// never apply and are ignored.
    /// </summary>
    public static bool DefaultsStayInLists(
        IReadOnlyList<SalesPolicyTimelineEntryV1<StoredEInvoiceScenarioCodeList>> lists,
        IReadOnlyList<SalesPolicyTimelineEntryV1<EInvoiceScenarioDefaultVersionRow>> defaults)
    {
        foreach (var scenarioDefault in defaults.Where(entry => !entry.IsSuperseded))
        {
            foreach (var list in lists.Where(entry => !entry.IsSuperseded))
            {
                if (Overlaps(scenarioDefault.EffectiveFrom, scenarioDefault.EffectiveThrough, list.EffectiveFrom,
                        list.EffectiveThrough)
                    && !list.Value.Codes.Contains(scenarioDefault.Value.ScenarioCode, StringComparer.Ordinal))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Whether some code list version covers every date of the default's window (no date of a default without a list in
    /// force: the default would answer while the list answers "not found").
    /// </summary>
    public static bool DefaultsCoveredByLists(
        IReadOnlyList<SalesPolicyTimelineEntryV1<StoredEInvoiceScenarioCodeList>> lists,
        IReadOnlyList<SalesPolicyTimelineEntryV1<EInvoiceScenarioDefaultVersionRow>> defaults)
    {
        var firstListStart = lists.Where(entry => !entry.IsSuperseded).Select(entry => (DateOnly?)entry.EffectiveFrom).Min();
        return defaults.Where(entry => !entry.IsSuperseded)
            .All(entry => firstListStart is { } start && entry.EffectiveFrom >= start);
    }

    private static bool Overlaps(DateOnly firstFrom, DateOnly? firstThrough, DateOnly secondFrom, DateOnly? secondThrough) =>
        (firstThrough is not { } firstEnd || firstEnd >= secondFrom)
        && (secondThrough is not { } secondEnd || secondEnd >= firstFrom);

    private static SettingsPrerequisiteException Invalid() => new("settings_profile_invalid");
}
