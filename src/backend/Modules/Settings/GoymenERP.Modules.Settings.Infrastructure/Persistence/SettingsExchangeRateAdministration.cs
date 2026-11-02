using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// DEC-FIN-010 / OPEN-012 (partial): the company's exchange-rate record (Settings; master report AV-02 "Birim / Para / Kur /
/// Vergi"), with KUR_GUNU SAT-D54/D55 and EK24 SAT-D169…D172.
/// <list type="bullet">
/// <item><b>Record</b>: per calendar day, append-only records — the official TCMB bulletin answer (Published with forex selling
/// rates per one unit / NoBulletin / Unverified), manual corrections (SAT-D169) and the official half-day answer (SAT-D171).
/// The day row is only the edit fence.</item>
/// <item><b>Authority</b>: three registered names granted to no role — read, correct (SAT-D169/D188 intended holders: Muhasebe,
/// Muhasebe Müdürü, Genel Müdür, Admin; the grant is a human gate) and import. Measured first and re-measured fresh before
/// every save.</item>
/// <item><b>Correction</b> (SAT-D169): one currency on a day TCMB published (SAT-D55: a manual record never makes or unmakes a
/// business day), a mandatory reason, Idempotency-Key, the day's expected version, correlation and one audit event. It is a
/// new record next to the official one; it affects only documents that read the rate afterwards (SAT-D172: an invoice keeps
/// the number it snapshotted). The size limit is open (OPEN-012).</item>
/// <item><b>Import</b>: asks the configured sources for past days only; a disabled source refuses (503) and records nothing.
/// An unverified answer never overwrites a definite one, and a different definite answer is reported as a conflict, never
/// written over.</item>
/// </list>
/// </summary>
internal sealed class SettingsExchangeRateAdministration(
    SettingsDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorizationService authorization,
    IErpAuthorityRevalidationService revalidation,
    IAuditEventCollector auditEvents,
    IExchangeRateBulletinSourceV1 bulletinSource,
    IExchangeRateHalfDaySourceV1 halfDaySource,
    TimeProvider timeProvider,
    ILogger<SettingsExchangeRateAdministration> logger) : ISettingsExchangeRateAdministrationV1
{
    private const string EntityType = "Settings.ExchangeRateDay";
    private const string CorrectCommandName = "settings.exchange-rate.correct.v1";
    private const string ImportCommandName = "settings.exchange-rate.import.v1";
    private const int CommandExecutionSchemaVersion = 1;
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<ExchangeRateDayListV1> ListAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var companyId = RequireContextCompany();
        await RequireAsync(SettingsExchangeRatePermissionManifestV1.Read, companyId, cancellationToken);
        if (from > to || to.DayNumber - from.DayNumber + 1 > ExchangeRateInputLimitsV1.MaximumListDays)
        {
            throw new SettingsPrerequisiteException(ExchangeRateProblemCodesV1.RequestInvalid);
        }

        ExchangeRateRows rows;
        try
        {
            rows = await ExchangeRateRows.ReadAsync(database, from, to, track: false, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SettingsPrerequisiteException(ExchangeRateProblemCodesV1.ProviderUnavailable);
        }

        await RequireStillAsync(SettingsExchangeRatePermissionManifestV1.Read, companyId, cancellationToken);
        var days = new List<ExchangeRateDayViewV1>();
        for (var date = to; date >= from; date = date.AddDays(-1))
        {
            days.Add(DayView(rows, date));
            if (date == DateOnly.MinValue)
            {
                break;
            }
        }

        return new ExchangeRateDayListV1(companyId, from, to, bulletinSource.IsEnabled, halfDaySource.IsEnabled, days);
    }

    public async Task<ExchangeRateResolutionV1> ResolveForCurrencyAsync(DateOnly rateDay, string currencyCode,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireContextCompany();
        await RequireAsync(SettingsExchangeRatePermissionManifestV1.Read, companyId, cancellationToken);
        if (!ForeignCurrency(currencyCode))
        {
            throw new SettingsPrerequisiteException(ExchangeRateProblemCodesV1.RequestInvalid);
        }

        var result = await ExchangeRateResolution.ResolveAsync(database, rateDay,
            ExchangeRateCurrencyIdentitiesV1.ForIsoCode(currencyCode), SettingsCurrencyIdentitiesV1.TurkishLira, cancellationToken);
        await RequireStillAsync(SettingsExchangeRatePermissionManifestV1.Read, companyId, cancellationToken);
        return result with { CurrencyCode = currencyCode };
    }

    public async Task<ExchangeRateCommandResultV1> CorrectAsync(DateOnly date, CorrectExchangeRateRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        const string permission = SettingsExchangeRatePermissionManifestV1.Correct;
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(permission, companyId, cancellationToken);
        if (!TryParseVersion(request.ExpectedVersion, out var expectedVersion))
        {
            return Unstored(Invalid("Beklenen sürüm (\"0\" veya pozitif tam sayı metni) zorunludur."), metadata);
        }

        if (!ForeignCurrency(request.CurrencyCode) || !TryParseRate(request.SellingRate, out var rate))
        {
            return Unstored(Invalid("Para birimi TRY dışında üç büyük harfli ISO 4217 kodu; kur 0'dan büyük, en çok "
                + $"{ExchangeRateInputLimitsV1.RateMaximumIntegerDigits} tam ve {ExchangeRateInputLimitsV1.RateMaximumScale} "
                + "ondalık basamaklı sayı metni olmalıdır."), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var currencyCode = request.CurrencyCode!;
        var subject = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            Date = date, ExpectedVersion = expectedVersion, CurrencyCode = currencyCode, SellingRate = rate, Reason = reason,
        });
        if (await FindExecutionAsync(CorrectCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(permission, companyId, cancellationToken);
            return Done("correct", metadata, subject, Replay(existing, requestHash, metadata));
        }

        var rows = await ReadForCommandAsync(date, date, cancellationToken);
        var header = rows.Days.SingleOrDefault(day => day.Date == date);
        var official = rows.Current(date, ExchangeRateRecordKindsV1.Bulletin, ExchangeRateProvenancesV1.Official);
        var refusal = (header?.Version ?? 0) != expectedVersion ? VersionConflict()
            : official?.State != ExchangeRateBulletinStatesV1.Published ? Problem(422, ExchangeRateProblemCodesV1.DayNotPublished,
                "Bu günün kuru düzeltilemez.",
                "Elle kur yalnız TCMB'nin bülten yayımladığı bir günün değerini düzeltir; bülten yok veya doğrulanmamış gün iş günü yapılamaz.")
            : null;
        if (refusal is not null)
        {
            await RequireStillAsync(permission, companyId, cancellationToken);
            return Done("correct", metadata, subject,
                await StoreRefusalAsync(CorrectCommandName, refusal, keyHash, requestHash, metadata, cancellationToken));
        }

        var before = CurrentValue(rows, date, currencyCode);
        if (before is { } current && current.Rate == rate)
        {
            await RequireStillAsync(permission, companyId, cancellationToken);
            return Done("correct", metadata, subject,
                await StoreRefusalAsync(CorrectCommandName, NoContent(), keyHash, requestHash, metadata, cancellationToken));
        }

        var now = timeProvider.GetUtcNow();
        var versionBefore = header!.Version;
        var record = Append(rows, companyId, date, ExchangeRateRecordKindsV1.Bulletin, ExchangeRateProvenancesV1.Manual,
            ExchangeRateBulletinStatesV1.Published, metadata.ActorId, now, reason, null);
        database.ExchangeRateRecordRates.Add(new ExchangeRateRecordRateRow
        {
            CompanyId = companyId, RecordId = record.Id, CurrencyCode = currencyCode, SellingRate = rate,
        });
        Audit(companyId, "settings.exchange-rate.corrected.v1", subject, "CorrectExchangeRate", versionBefore,
            rows.Days.Single(day => day.Date == date).Version, permission, scope,
            before is { } previous
                ? new { Date = date, CurrencyCode = currencyCode, SellingRate = Text(previous.Rate), previous.Provenance, previous.RecordId }
                : new { Date = date, CurrencyCode = currencyCode, SellingRate = (string?)null, Provenance = (string?)null, RecordId = (Guid?)null },
            new { Date = date, CurrencyCode = currencyCode, SellingRate = Text(rate), Provenance = ExchangeRateProvenancesV1.Manual,
                RecordId = record.Id, record.RecordVersion },
            reason, metadata);
        await RequireStillAsync(permission, companyId, cancellationToken);
        return Done("correct", metadata, subject,
            await SaveChangeAsync(CorrectCommandName, NoContent(), keyHash, requestHash, metadata, cancellationToken));
    }

    public async Task<ExchangeRateCommandResultV1> ImportAsync(ImportExchangeRatesRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        const string permission = SettingsExchangeRatePermissionManifestV1.Import;
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(permission, companyId, cancellationToken);
        var today = SettingsBusinessDate.Today(timeProvider);
        if (request.From is not { } from || request.To is not { } to || from > to
            || to.DayNumber - from.DayNumber + 1 > ExchangeRateInputLimitsV1.MaximumImportDays)
        {
            return Unstored(Invalid($"Başlangıç ve bitiş günü zorunludur; aralık en çok {ExchangeRateInputLimitsV1.MaximumImportDays} gün olabilir."),
                metadata);
        }

        if (to >= today)
        {
            // A day's bulletin is final only after its announcement; only past days are imported (the hour is OPEN-012's).
            return Unstored(Invalid("Yalnız bugünden önceki günler içe aktarılır."), metadata);
        }

        if (!bulletinSource.IsEnabled)
        {
            // Environment state, not the key's answer: nothing is stored, so the same key works once a human enables the source.
            return Unstored(Problem(503, ExchangeRateProblemCodesV1.SourceDisabled, "Kur kaynağı kapalı.",
                "TCMB kur kaynağı bu ortamda etkin değil; dış bağlantının açılması insan onayı gerektirir."), metadata);
        }

        var subject = $"{from:yyyy-MM-dd}..{to:yyyy-MM-dd}";
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new { From = from, To = to });
        if (await FindExecutionAsync(ImportCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(permission, companyId, cancellationToken);
            return Done("import", metadata, subject, Replay(existing, requestHash, metadata));
        }

        // B P3-1 ("internetten tekrar tekrar çekme"): a day whose official answer is already final (Published / NoBulletin, or a
        // definite half-day answer) is not asked again; only unrecorded or still unverified / undetermined days are.
        ExchangeRateRows known;
        bool publishedAfterRange;
        try
        {
            known = await ExchangeRateRows.ReadAsync(database, from, to, track: false, cancellationToken);
            publishedAfterRange = await ExchangeRateRows.AnyOfficialPublishedAfterAsync(database, to, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SettingsPrerequisiteException(ExchangeRateProblemCodesV1.ProviderUnavailable);
        }

        var fetched = new List<(DateOnly Date, ExchangeRateBulletinFetchV1? Bulletin, ExchangeRateHalfDayFetchV1? HalfDay,
            bool HalfDayFinal)>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var bulletinFinal = known.BulletinState(date) is ExchangeRateBulletinStatesV1.Published or ExchangeRateBulletinStatesV1.NoBulletin;
            var halfDayFinal = known.Current(date, ExchangeRateRecordKindsV1.HalfDay, ExchangeRateProvenancesV1.Official)?.State
                is ExchangeRateHalfDayStatesV1.FullDay or ExchangeRateHalfDayStatesV1.HalfDay;
            fetched.Add((date,
                bulletinFinal ? null : await FetchBulletinAsync(date, cancellationToken),
                halfDaySource.IsEnabled && !halfDayFinal ? await FetchHalfDayAsync(date, cancellationToken) : null,
                halfDayFinal));
        }

        // B P2-2: TCMB's 404 for a dated file is final only once a LATER bulletin exists (received in this run or already
        // recorded): before that a 404 can mean "not yet published" or an archive gap, so the day stays unverified and the
        // next run asks again.
        var publishedDays = fetched.Where(day => day.Bulletin?.State == ExchangeRateBulletinStatesV1.Published).Select(day => day.Date)
            .Concat(fetched.Where(day => known.BulletinState(day.Date) == ExchangeRateBulletinStatesV1.Published).Select(day => day.Date))
            .ToArray();
        for (var index = 0; index < fetched.Count; index++)
        {
            var day = fetched[index];
            if (day.Bulletin?.State == ExchangeRateBulletinStatesV1.NoBulletin
                && !publishedAfterRange && !publishedDays.Any(published => published > day.Date))
            {
                fetched[index] = day with
                {
                    Bulletin = new ExchangeRateBulletinFetchV1(day.Date, ExchangeRateBulletinStatesV1.Unverified, [],
                        Reference((day.Bulletin.SourceReference ?? string.Empty) + " (sonraki bir bülten görülmeden kesin sayılmaz)")),
                };
            }
        }

        var rows = await ReadForCommandAsync(from, to, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var outcomes = new List<ExchangeRateImportDayV1>();
        var recorded = new List<object>();
        foreach (var (date, bulletin, halfDay, halfDayFinal) in fetched)
        {
            var bulletinOutcome = bulletin is null
                ? ExchangeRateImportOutcomesV1.Unchanged
                : ImportBulletin(rows, companyId, date, bulletin, metadata.ActorId, now, recorded);
            var halfDayOutcome = halfDay is not null
                ? ImportHalfDay(rows, companyId, date, halfDay, metadata.ActorId, now, recorded)
                : halfDayFinal ? ExchangeRateImportOutcomesV1.Unchanged
                : ExchangeRateImportOutcomesV1.SourceDisabled;
            outcomes.Add(new ExchangeRateImportDayV1(date, bulletinOutcome, halfDayOutcome));
        }

        var answer = new ExchangeRateCommandAnswerV1(200, null, "İçe aktarıldı.", "Kur kaynağının cevapları işlendi.",
            new ExchangeRateImportSummaryV1(outcomes));
        if (recorded.Count == 0)
        {
            await RequireStillAsync(permission, companyId, cancellationToken);
            return Done("import", metadata, subject,
                await StoreRefusalAsync(ImportCommandName, answer, keyHash, requestHash, metadata, cancellationToken));
        }

        Audit(companyId, "settings.exchange-rate.imported.v1", subject, "ImportExchangeRates", null, null, permission, scope, null,
            new { From = from, To = to, Records = recorded }, "Resmî kaynaktan otomatik içe aktarma", metadata);
        await RequireStillAsync(permission, companyId, cancellationToken);
        return Done("import", metadata, subject,
            await SaveChangeAsync(ImportCommandName, answer, keyHash, requestHash, metadata, cancellationToken));
    }

    // ---- import ------------------------------------------------------------------------------------------------------------

    private async Task<ExchangeRateBulletinFetchV1> FetchBulletinAsync(DateOnly date, CancellationToken cancellationToken)
    {
        try
        {
            var answer = await bulletinSource.FetchAsync(date, cancellationToken);
            return Valid(date, answer) ? answer : Unverified(date, "Kaynak cevabı geçersiz.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Exchange-rate bulletin source failed. Date={Date}", date);
            return Unverified(date, "Kaynağa ulaşılamadı.");
        }

        static ExchangeRateBulletinFetchV1 Unverified(DateOnly date, string reference) =>
            new ExchangeRateBulletinFetchV1(date, ExchangeRateBulletinStatesV1.Unverified, [], reference);
    }

    private async Task<ExchangeRateHalfDayFetchV1> FetchHalfDayAsync(DateOnly date, CancellationToken cancellationToken)
    {
        try
        {
            var answer = await halfDaySource.FetchAsync(date, cancellationToken);
            return answer is not null && answer.Date == date && ExchangeRateHalfDayStatesV1.IsKnown(answer.State)
                ? answer
                : new ExchangeRateHalfDayFetchV1(date, ExchangeRateHalfDayStatesV1.Undetermined, "Kaynak cevabı geçersiz.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Exchange-rate half-day source failed. Date={Date}", date);
            return new ExchangeRateHalfDayFetchV1(date, ExchangeRateHalfDayStatesV1.Undetermined, "Kaynağa ulaşılamadı.");
        }
    }

    private static bool Valid(DateOnly date, ExchangeRateBulletinFetchV1? answer)
    {
        if (answer is null || answer.Date != date || !ExchangeRateBulletinStatesV1.IsRecordable(answer.State) || answer.Rates is null)
        {
            return false;
        }

        if (answer.State != ExchangeRateBulletinStatesV1.Published)
        {
            return answer.Rates.Count == 0;
        }

        return answer.Rates.Count > 0
               && answer.Rates.All(line => line is not null && ForeignCurrency(line.CurrencyCode) && TryParseRate(line.SellingRate, out _))
               && answer.Rates.Select(line => line.CurrencyCode).Distinct(StringComparer.Ordinal).Count() == answer.Rates.Count;
    }

    private string ImportBulletin(ExchangeRateRows rows, Guid companyId, DateOnly date, ExchangeRateBulletinFetchV1 answer,
        string actorId, DateTimeOffset now, List<object> recorded)
    {
        var current = rows.Current(date, ExchangeRateRecordKindsV1.Bulletin, ExchangeRateProvenancesV1.Official);
        if (current is not null && current.State != ExchangeRateBulletinStatesV1.Unverified)
        {
            if (answer.State == ExchangeRateBulletinStatesV1.Unverified)
            {
                return ExchangeRateImportOutcomesV1.Unchanged;
            }

            return answer.State == current.State && SameRates(rows.RatesOf(current.Id), answer.Rates)
                ? ExchangeRateImportOutcomesV1.Unchanged
                : ExchangeRateImportOutcomesV1.Conflict;
        }

        if (current is not null && answer.State == ExchangeRateBulletinStatesV1.Unverified)
        {
            return ExchangeRateImportOutcomesV1.Unchanged;
        }

        var record = Append(rows, companyId, date, ExchangeRateRecordKindsV1.Bulletin, ExchangeRateProvenancesV1.Official,
            answer.State, actorId, now, null, Reference(answer.SourceReference));
        foreach (var line in answer.Rates)
        {
            TryParseRate(line.SellingRate, out var rate);
            database.ExchangeRateRecordRates.Add(new ExchangeRateRecordRateRow
            {
                CompanyId = companyId, RecordId = record.Id, CurrencyCode = line.CurrencyCode, SellingRate = rate,
            });
        }

        recorded.Add(new { Date = date, Kind = record.Kind, record.State, RecordId = record.Id, Lines = answer.Rates.Count });
        return ExchangeRateImportOutcomesV1.Recorded;
    }

    private string ImportHalfDay(ExchangeRateRows rows, Guid companyId, DateOnly date, ExchangeRateHalfDayFetchV1 answer,
        string actorId, DateTimeOffset now, List<object> recorded)
    {
        var current = rows.Current(date, ExchangeRateRecordKindsV1.HalfDay, ExchangeRateProvenancesV1.Official);
        if (current is not null && (answer.State == ExchangeRateHalfDayStatesV1.Undetermined || current.State == answer.State))
        {
            return ExchangeRateImportOutcomesV1.Unchanged;
        }

        if (current is not null && current.State != ExchangeRateHalfDayStatesV1.Undetermined)
        {
            return ExchangeRateImportOutcomesV1.Conflict;
        }

        var record = Append(rows, companyId, date, ExchangeRateRecordKindsV1.HalfDay, ExchangeRateProvenancesV1.Official,
            answer.State, actorId, now, null, Reference(answer.SourceReference));
        recorded.Add(new { Date = date, Kind = record.Kind, record.State, RecordId = record.Id, Lines = 0 });
        return ExchangeRateImportOutcomesV1.Recorded;
    }

    private static bool SameRates(IReadOnlyList<ExchangeRateRecordRateRow> stored, IReadOnlyList<ExchangeRateLineV1> answer) =>
        stored.Count == answer.Count
        && answer.All(line => TryParseRate(line.SellingRate, out var rate)
                              && stored.Any(row => row.CurrencyCode == line.CurrencyCode && row.SellingRate == rate));

    private static string? Reference(string? reference) =>
        string.IsNullOrWhiteSpace(reference) ? null : reference.Trim() is { Length: > 400 } trimmed ? trimmed[..400] : reference.Trim();

    // ---- records -----------------------------------------------------------------------------------------------------------

    /// <summary>Appends one record to the day (creating its fence row on the first record) and moves the day's version.</summary>
    private ExchangeRateRecordRow Append(ExchangeRateRows rows, Guid companyId, DateOnly date, string kind, string provenance,
        string state, string actorId, DateTimeOffset now, string? reason, string? sourceReference)
    {
        var header = rows.Days.SingleOrDefault(day => day.Date == date);
        if (header is null)
        {
            header = new ExchangeRateDayRow
            {
                CompanyId = companyId, Date = date, Version = 1, LastRecordVersion = 1, UpdatedAtUtc = now, UpdatedBy = actorId,
            };
            database.ExchangeRateDays.Add(header);
            ((List<ExchangeRateDayRow>)rows.Days).Add(header);
        }
        else
        {
            header.Version = checked(header.Version + 1);
            header.LastRecordVersion = checked(header.LastRecordVersion + 1);
            header.UpdatedAtUtc = now;
            header.UpdatedBy = actorId;
        }

        var record = new ExchangeRateRecordRow
        {
            CompanyId = companyId,
            Id = Guid.NewGuid(),
            Date = date,
            RecordVersion = header.LastRecordVersion,
            Kind = kind,
            Provenance = provenance,
            State = state,
            Source = ExchangeRateSourcesV1.Tcmb,
            RecordedAtUtc = now,
            RecordedBy = actorId,
            Reason = reason,
            SourceReference = sourceReference,
        };
        database.ExchangeRateRecords.Add(record);
        return record;
    }

    private sealed record CurrentRate(decimal Rate, string Provenance, Guid RecordId);

    private static CurrentRate? CurrentValue(ExchangeRateRows rows, DateOnly date, string currencyCode)
    {
        if (rows.ManualRates(date).TryGetValue(currencyCode, out var manual))
        {
            return new CurrentRate(manual.Rate, ExchangeRateProvenancesV1.Manual, manual.Record.Id);
        }

        var official = rows.Current(date, ExchangeRateRecordKindsV1.Bulletin, ExchangeRateProvenancesV1.Official);
        var line = official is null ? null : rows.RatesOf(official.Id).SingleOrDefault(rate => rate.CurrencyCode == currencyCode);
        return line is null ? null : new CurrentRate(line.SellingRate, ExchangeRateProvenancesV1.Official, official!.Id);
    }

    private static ExchangeRateDayViewV1 DayView(ExchangeRateRows rows, DateOnly date)
    {
        var header = rows.Days.SingleOrDefault(day => day.Date == date);
        var bulletinState = rows.BulletinState(date);
        var values = new List<ExchangeRateValueViewV1>();
        if (bulletinState == ExchangeRateBulletinStatesV1.Published)
        {
            var official = rows.Current(date, ExchangeRateRecordKindsV1.Bulletin, ExchangeRateProvenancesV1.Official)!;
            var manual = rows.ManualRates(date);
            var codes = rows.RatesOf(official.Id).Select(rate => rate.CurrencyCode).Union(manual.Keys).Order(StringComparer.Ordinal);
            foreach (var code in codes)
            {
                var current = CurrentValue(rows, date, code)!;
                var record = rows.Records.Single(row => row.Id == current.RecordId);
                values.Add(new ExchangeRateValueViewV1(code, Text(current.Rate), current.Provenance, record.Id, Text(record.RecordVersion)));
            }
        }

        var records = rows.Records.Where(record => record.Date == date)
            .OrderByDescending(record => record.RecordVersion)
            .Select(record => new ExchangeRateRecordViewV1(record.Id, Text(record.RecordVersion), record.Kind, record.Provenance,
                record.State,
                rows.RatesOf(record.Id).Select(rate => new ExchangeRateLineV1(rate.CurrencyCode, Text(rate.SellingRate))).ToArray(),
                record.RecordedBy, record.RecordedAtUtc, record.Reason, record.SourceReference))
            .ToArray();
        return new ExchangeRateDayViewV1(date, Text(header?.Version ?? 0), bulletinState, rows.HalfDayState(date), values, records);
    }

    // ---- authority, metadata, idempotency, audit ----------------------------------------------------------------------------

    private Guid RequireContextCompany()
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId)
            || !companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw new UnauthorizedAccessException();
        }

        return companyContext.CompanyId;
    }

    private async Task<PermissionScope> RequireAsync(string permission, Guid companyId, CancellationToken cancellationToken)
    {
        if (!currentUser.CanAccessCompany(companyId))
        {
            throw new UnauthorizedAccessException();
        }

        var scope = await authorization.GetPermissionScopeAsync(permission, companyId, cancellationToken);
        return scope is { IsGranted: true, IsCompanyWide: true } ? scope : throw new UnauthorizedAccessException();
    }

    private async Task RequireStillAsync(string permission, Guid companyId, CancellationToken cancellationToken)
    {
        if (!currentUser.CanAccessCompany(companyId))
        {
            throw new UnauthorizedAccessException();
        }

        var scope = await revalidation.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        if (scope is not { IsGranted: true, IsCompanyWide: true })
        {
            throw new UnauthorizedAccessException();
        }
    }

    private void ValidateMetadata(SettingsCommandMetadataV1 metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!companyContext.IsResolved || metadata.CompanyId != companyContext.CompanyId
            || !string.Equals(metadata.ActorId, currentUser.UserId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(metadata.CorrelationId)
            || string.IsNullOrWhiteSpace(metadata.SourceChannel)
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey))
        {
            throw new InvalidOperationException("Ayarlar komut bağlamı geçersiz.");
        }
    }

    private async Task<ExchangeRateRows> ReadForCommandAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        try
        {
            var rows = await ExchangeRateRows.ReadAsync(database, from, to, track: true, cancellationToken);
            return rows with { Days = rows.Days.ToList() };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SettingsPrerequisiteException(ExchangeRateProblemCodesV1.ProviderUnavailable);
        }
    }

    private Task<CommandExecution?> FindExecutionAsync(string commandName, string keyHash, CancellationToken cancellationToken) =>
        database.CommandExecutions.AsNoTracking()
            .SingleOrDefaultAsync(execution => execution.CommandName == commandName && execution.IdempotencyKeyHash == keyHash,
                cancellationToken);

    private static ExchangeRateCommandResultV1 Replay(CommandExecution execution, string requestHash, SettingsCommandMetadataV1 metadata)
    {
        if (!string.Equals(execution.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(execution.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new ExchangeRateCommandResultV1(Problem(409, ExchangeRateProblemCodesV1.IdempotencyConflict,
                "İşlem anahtarı kullanılmış.", "Bu Idempotency-Key başka bir istekle kullanıldı."), false, metadata.CorrelationId);
        }

        var answer = JsonSerializer.Deserialize<ExchangeRateCommandAnswerV1>(execution.ResponseJson, Json);
        if (answer is null || answer.StatusCode != execution.HttpStatusCode
            || !string.Equals(Outcome(answer), execution.Outcome, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Kalıcı idempotency sonucu kur komut sözleşmesiyle uyumsuz.");
        }

        return new ExchangeRateCommandResultV1(answer, true, execution.OriginalCorrelationId);
    }

    private void AddExecution(string commandName, ExchangeRateCommandAnswerV1 answer, string keyHash, string requestHash,
        SettingsCommandMetadataV1 metadata) =>
        database.CommandExecutions.Add(CommandExecution.Complete(new CommandExecutionData(
            Guid.NewGuid(), metadata.CompanyId, commandName, keyHash, requestHash, metadata.ActorId, Outcome(answer),
            answer.StatusCode, JsonSerializer.Serialize(answer, Json), metadata.CorrelationId, timeProvider.GetUtcNow(),
            CommandExecutionSchemaVersion)));

    private async Task<ExchangeRateCommandResultV1> StoreRefusalAsync(string commandName, ExchangeRateCommandAnswerV1 answer,
        string keyHash, string requestHash, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        AddExecution(commandName, answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new ExchangeRateCommandResultV1(answer, false, metadata.CorrelationId);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var winner = await FindExecutionAsync(commandName, keyHash, cancellationToken);
            return winner is null ? throw new SettingsPrerequisiteException(ExchangeRateProblemCodesV1.ProviderUnavailable)
                : Replay(winner, requestHash, metadata);
        }
    }

    /// <summary>The change, its terminal answer and audit in one save; a lost race replays a same-key winner or is a 409.</summary>
    private async Task<ExchangeRateCommandResultV1> SaveChangeAsync(string commandName, ExchangeRateCommandAnswerV1 answer,
        string keyHash, string requestHash, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        AddExecution(commandName, answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new ExchangeRateCommandResultV1(answer, false, metadata.CorrelationId);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var winner = await FindExecutionAsync(commandName, keyHash, cancellationToken);
            if (winner is not null)
            {
                return Replay(winner, requestHash, metadata);
            }

            return await StoreRefusalAsync(commandName, VersionConflict(), keyHash, requestHash, metadata, cancellationToken);
        }
    }

    private static string Outcome(ExchangeRateCommandAnswerV1 answer) => answer.Code ?? answer.StatusCode switch
    {
        200 or 204 => "Succeeded",
        _ => "Refused",
    };

    private void Audit(Guid companyId, string eventType, string entityId, string operation, long? versionBefore,
        long? versionAfter, string permission, PermissionScope scope, object? before, object after, string reason,
        SettingsCommandMetadataV1 metadata)
    {
        var afterNode = JsonSerializer.SerializeToNode(after, Json)!.AsObject();
        afterNode["reason"] = reason;
        auditEvents.Enqueue(new AuditEventDraft(
            companyId, eventType, EntityType, entityId, operation, versionBefore, versionAfter, AuditActorTypes.User,
            metadata.ActorId, permission, scope.RoleCodes, before is null ? "{}" : JsonSerializer.Serialize(before, Json),
            afterNode.ToJsonString(Json),
            eventType switch
            {
                "settings.exchange-rate.corrected.v1" => "Kur elle düzeltildi; gerekçe kayıt içeriğindedir",
                "settings.exchange-rate.imported.v1" => "Kur kaydı resmî kaynaktan içe aktarıldı",
                _ => throw new InvalidOperationException("Tanımsız kur audit olayı."),
            },
            ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null, metadata.SourceChannel,
            metadata.ClientIpAddress, metadata.UserAgent, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
    }

    private ExchangeRateCommandResultV1 Done(string command, SettingsCommandMetadataV1 metadata, string subject,
        ExchangeRateCommandResultV1 result)
    {
        logger.LogInformation(
            "Settings exchange-rate command completed. Command={Command} CompanyId={CompanyId} Subject={Subject} ActorId={ActorId} CorrelationId={CorrelationId} Status={Status} Code={Code} IsReplay={IsReplay}",
            command, metadata.CompanyId, subject, metadata.ActorId, metadata.CorrelationId, result.Answer.StatusCode,
            result.Answer.Code, result.IsReplay);
        return result;
    }

    // ---- values ------------------------------------------------------------------------------------------------------------

    private static bool ForeignCurrency(string? code) =>
        ExchangeRateCurrencyIdentitiesV1.IsIsoCode(code) && !string.Equals(code, "TRY", StringComparison.Ordinal);

    /// <summary>Exact positive decimal text: digits with one optional point, within the stored digits; never rounded.</summary>
    internal static bool TryParseRate(string? text, out decimal rate)
    {
        rate = 0m;
        if (string.IsNullOrEmpty(text) || text.Count(character => character == '.') > 1
            || !text.All(character => char.IsAsciiDigit(character) || character == '.')
            || text[0] == '.' || text[^1] == '.')
        {
            return false;
        }

        var parts = text.Split('.');
        var integer = parts[0].TrimStart('0');
        var fraction = parts.Length > 1 ? parts[1].TrimEnd('0') : string.Empty;
        if (integer.Length > ExchangeRateInputLimitsV1.RateMaximumIntegerDigits
            || fraction.Length > ExchangeRateInputLimitsV1.RateMaximumScale
            || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out rate))
        {
            return false;
        }

        return rate > 0m;
    }

    private static string Text(decimal value) => (value / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static bool TryParseVersion(string? text, out long version)
    {
        version = 0;
        return !string.IsNullOrEmpty(text) && text.All(char.IsAsciiDigit) && (text.Length == 1 || text[0] != '0')
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out version);
    }

    private static ExchangeRateCommandAnswerV1? ReasonRefusal(string? reason, out string normalized)
    {
        normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return Problem(400, ExchangeRateProblemCodesV1.ReasonRequired, "Gerekçe gerekli.", "Kur düzeltmesi için gerekçe zorunludur.");
        }

        return normalized.Length > ExchangeRateInputLimitsV1.ReasonMaximumLength
            ? Problem(400, ExchangeRateProblemCodesV1.ReasonTooLong, "Gerekçe çok uzun.",
                $"Gerekçe en fazla {ExchangeRateInputLimitsV1.ReasonMaximumLength} karakter olabilir.")
            : null;
    }

    private static ExchangeRateCommandResultV1 Unstored(ExchangeRateCommandAnswerV1 answer, SettingsCommandMetadataV1 metadata) =>
        new ExchangeRateCommandResultV1(answer, false, metadata.CorrelationId);

    private static ExchangeRateCommandAnswerV1 NoContent() => new ExchangeRateCommandAnswerV1(204, null, "Kaydedildi.", "Değişiklik kaydedildi.");

    private static ExchangeRateCommandAnswerV1 Invalid(string detail) =>
        Problem(400, ExchangeRateProblemCodesV1.RequestInvalid, "İstek geçersiz.", detail);

    private static ExchangeRateCommandAnswerV1 VersionConflict() =>
        Problem(409, ExchangeRateProblemCodesV1.VersionConflict, "Kayıt değişmiş.",
            "Kur günü siz açtıktan sonra değişti; güncel hâlini okuyup yeniden deneyin.");

    private static ExchangeRateCommandAnswerV1 Problem(int status, string code, string title, string detail) =>
        new ExchangeRateCommandAnswerV1(status, code, title, detail);
}
