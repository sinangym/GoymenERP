using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// DEC-FIN-010 rate record: one calendar day of one company (the edit fence of its records). Created with the day's first
/// record; its version moves with every appended record.
/// </summary>
internal sealed class ExchangeRateDayRow
{
    public Guid CompanyId { get; set; }
    public DateOnly Date { get; set; }
    public long Version { get; set; }
    public long LastRecordVersion { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// One immutable, append-only record of a day (KUR_GUNU, SAT-D169…D171): an official bulletin answer (Published / NoBulletin /
/// Unverified), a manual rate correction (Manual, Published, with a reason) or an official half-day answer (FullDay / HalfDay /
/// Undetermined). The day's current answer of a kind is its record with the highest version; nothing is ever overwritten.
/// </summary>
internal sealed class ExchangeRateRecordRow
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public long RecordVersion { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Provenance { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? SourceReference { get; set; }
}

/// <summary>One currency's forex selling rate (per one unit, in TRY) carried by a bulletin or manual record.</summary>
internal sealed class ExchangeRateRecordRateRow
{
    public Guid CompanyId { get; set; }
    public Guid RecordId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal SellingRate { get; set; }
}

/// <summary>The rows of a day range, and their reading as the calculator's contract (<see cref="RateDayExchangeRate"/>).</summary>
internal sealed record ExchangeRateRows(
    IReadOnlyList<ExchangeRateDayRow> Days,
    IReadOnlyList<ExchangeRateRecordRow> Records,
    IReadOnlyList<ExchangeRateRecordRateRow> Rates)
{
    public static async Task<ExchangeRateRows> ReadAsync(SettingsDbContext database, DateOnly from, DateOnly to, bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<T> Query<T>(DbSet<T> set) where T : class => track ? set : set.AsNoTracking();
        var days = await Query(database.ExchangeRateDays)
            .Where(day => day.Date >= from && day.Date <= to)
            .ToListAsync(cancellationToken);
        var records = await Query(database.ExchangeRateRecords)
            .Where(record => record.Date >= from && record.Date <= to)
            .ToListAsync(cancellationToken);
        var ids = records.Select(record => record.Id).ToArray();
        var rates = ids.Length == 0
            ? []
            : await Query(database.ExchangeRateRecordRates)
                .Where(rate => ids.Contains(rate.RecordId))
                .ToListAsync(cancellationToken);
        return new ExchangeRateRows(days, records, rates);
    }

    public static Task<bool> AnyOfficialPublishedAfterAsync(SettingsDbContext database, DateOnly date,
        CancellationToken cancellationToken) =>
        database.ExchangeRateRecords.AsNoTracking().AnyAsync(record => record.Date > date
            && record.Kind == ExchangeRateRecordKindsV1.Bulletin && record.Provenance == ExchangeRateProvenancesV1.Official
            && record.State == ExchangeRateBulletinStatesV1.Published, cancellationToken);

    public static Task<bool> AnyBeforeAsync(SettingsDbContext database, DateOnly date, CancellationToken cancellationToken) =>
        database.ExchangeRateRecords.AsNoTracking().AnyAsync(record => record.Date < date, cancellationToken);

    /// <summary>The day's current record of a kind and provenance (highest record version), or null.</summary>
    public ExchangeRateRecordRow? Current(DateOnly date, string kind, string provenance) => Records
        .Where(record => record.Date == date && record.Kind == kind && record.Provenance == provenance)
        .MaxBy(record => record.RecordVersion);

    public IReadOnlyList<ExchangeRateRecordRateRow> RatesOf(Guid recordId) =>
        Rates.Where(rate => rate.RecordId == recordId).OrderBy(rate => rate.CurrencyCode, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// The latest manual rate per currency on the day: each correction carries one currency, and a later correction of the same
    /// currency supersedes the earlier one; other currencies keep theirs.
    /// </summary>
    public IReadOnlyDictionary<string, (ExchangeRateRecordRow Record, decimal Rate)> ManualRates(DateOnly date)
    {
        var result = new Dictionary<string, (ExchangeRateRecordRow, decimal)>(StringComparer.Ordinal);
        foreach (var record in Records
                     .Where(record => record.Date == date && record.Kind == ExchangeRateRecordKindsV1.Bulletin
                                      && record.Provenance == ExchangeRateProvenancesV1.Manual)
                     .OrderBy(record => record.RecordVersion))
        {
            foreach (var rate in RatesOf(record.Id))
            {
                result[rate.CurrencyCode] = (record, rate.SellingRate);
            }
        }

        return result;
    }

    public string BulletinState(DateOnly date) =>
        Current(date, ExchangeRateRecordKindsV1.Bulletin, ExchangeRateProvenancesV1.Official)?.State
        ?? ExchangeRateBulletinStatesV1.NotRecorded;

    public string HalfDayState(DateOnly date) =>
        Current(date, ExchangeRateRecordKindsV1.HalfDay, ExchangeRateProvenancesV1.Official)?.State
        ?? ExchangeRateHalfDayStatesV1.Undetermined;

    /// <summary>The calculator's day records: the official answer of every recorded day and, next to it, the manual rates.</summary>
    public IReadOnlyList<ExchangeRateBulletinDay> BulletinDays()
    {
        var result = new List<ExchangeRateBulletinDay>();
        foreach (var date in Records.Select(record => record.Date).Distinct())
        {
            var official = Current(date, ExchangeRateRecordKindsV1.Bulletin, ExchangeRateProvenancesV1.Official);
            switch (official?.State)
            {
                case ExchangeRateBulletinStatesV1.Published:
                    result.Add(ExchangeRateBulletinDay.Published(date, ExchangeRateBulletinSource.Tcmb,
                        RatesOf(official.Id).Select(Rate)));
                    break;
                case ExchangeRateBulletinStatesV1.NoBulletin:
                    result.Add(ExchangeRateBulletinDay.ConfirmedNoBulletin(date, ExchangeRateBulletinSource.Tcmb));
                    break;
                case ExchangeRateBulletinStatesV1.Unverified:
                    result.Add(ExchangeRateBulletinDay.Unconfirmed(date, ExchangeRateBulletinSource.Tcmb));
                    break;
            }

            var manual = ManualRates(date);
            if (manual.Count > 0)
            {
                var latest = manual.Values.MaxBy(entry => entry.Record.RecordVersion).Record;
                result.Add(ExchangeRateBulletinDay.ManualPublished(date, ExchangeRateBulletinSource.Tcmb,
                    manual.Select(entry => ExchangeRate.Of(ExchangeRateCurrencyIdentitiesV1.ForIsoCode(entry.Key),
                        SettingsCurrencyIdentitiesV1.TurkishLira, entry.Value.Rate)),
                    new ExchangeRateManualEntry(latest.RecordedBy, latest.Reason ?? string.Empty)));
            }
        }

        return result;
    }

    public IReadOnlyList<ExchangeRateHalfDayMark> HalfDayMarks() => Records
        .Where(record => record.Kind == ExchangeRateRecordKindsV1.HalfDay)
        .Select(record => record.Date)
        .Distinct()
        .Select(date => new ExchangeRateHalfDayMark(date, HalfDayState(date) switch
        {
            ExchangeRateHalfDayStatesV1.FullDay => ExchangeRateHalfDayStatus.FullDay,
            ExchangeRateHalfDayStatesV1.HalfDay => ExchangeRateHalfDayStatus.HalfDay,
            _ => ExchangeRateHalfDayStatus.Undetermined,
        }))
        .ToArray();

    private static ExchangeRate Rate(ExchangeRateRecordRateRow row) => ExchangeRate.Of(
        ExchangeRateCurrencyIdentitiesV1.ForIsoCode(row.CurrencyCode), SettingsCurrencyIdentitiesV1.TurkishLira, row.SellingRate);
}
