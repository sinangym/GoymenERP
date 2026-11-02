using System.Security.Cryptography;
using System.Text;

namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// DEC-FIN-010 / OPEN-012 (partial) exchange-rate record names. Registered, NOT granted: no role, seed or user receives them
/// here (a human grant gate). Company-wide only. Not Critical.
/// </summary>
public static class SettingsExchangeRatePermissionManifestV1
{
    /// <summary>Read the company's rate record (day, TCMB bulletin status, half-day status, rates) and a rate-day resolution.</summary>
    public const string Read = "settings.exchange-rates.read";

    /// <summary>
    /// SAT-D169 / SAT-D188: the reasoned manual rate correction. Intended holders: Muhasebe, Muhasebe Müdürü, Genel Müdür and
    /// Admin (EK28); the grant is a human gate. The correction's size limit is open (OPEN-012).
    /// </summary>
    public const string Correct = "settings.exchange-rates.correct";

    /// <summary>
    /// Running the import from the configured official sources for a day range. The sources are disabled by default; enabling
    /// the outbound connection in any environment is a human gate (KUR_GUNU, EK24).
    /// </summary>
    public const string Import = "settings.exchange-rates.import";
}

public static class ExchangeRateProblemCodesV1
{
    public const string RequestInvalid = "exchange_rate_request_invalid";
    public const string CommandMetadataInvalid = "exchange_rate_command_metadata_invalid";
    public const string ReasonRequired = "exchange_rate_reason_required";
    public const string ReasonTooLong = "exchange_rate_reason_too_long";
    public const string IdempotencyConflict = "exchange_rate_idempotency_conflict";
    public const string VersionConflict = "exchange_rate_version_conflict";

    /// <summary>SAT-D55 / KUR_GUNU: a manual rate is only a value change on a day TCMB officially published.</summary>
    public const string DayNotPublished = "exchange_rate_day_not_published";

    /// <summary>A rate with more decimals or digits than the record stores (a storage limit, never rounded).</summary>
    public const string ValueDoesNotFit = "exchange_rate_value_does_not_fit";

    /// <summary>The official bulletin source is not enabled in this environment (the default; enabling it is a human gate).</summary>
    public const string SourceDisabled = "exchange_rate_source_disabled";

    public const string AccessDenied = "settings_access_denied";
    public const string ProviderUnavailable = "settings_provider_unavailable";
}

/// <summary>What the record knows about a day's TCMB bulletin (KUR_GUNU implementation note).</summary>
public static class ExchangeRateBulletinStatesV1
{
    /// <summary>TCMB published a bulletin: the day is a business day (SAT-D55).</summary>
    public const string Published = "Published";

    /// <summary>TCMB's own source definitely says there is no bulletin (e.g. the dated file does not exist).</summary>
    public const string NoBulletin = "NoBulletin";

    /// <summary>An attempt without a definite answer (network error, timeout, unexpected answer).</summary>
    public const string Unverified = "Unverified";

    /// <summary>Nothing recorded for the day.</summary>
    public const string NotRecorded = "NotRecorded";

    public static bool IsRecordable(string? state) => state is Published or NoBulletin or Unverified;
}

/// <summary>What the record knows about whether a day is an arife or half day (SAT-D170/D171).</summary>
public static class ExchangeRateHalfDayStatesV1
{
    public const string FullDay = "FullDay";
    public const string HalfDay = "HalfDay";

    /// <summary>The source was asked and gave no definite answer, or nothing is recorded: the rate is "belirlenemedi".</summary>
    public const string Undetermined = "Undetermined";

    public static bool IsKnown(string? state) => state is FullDay or HalfDay or Undetermined;
}

public static class ExchangeRateProvenancesV1
{
    public const string Official = "Official";
    public const string Manual = "Manual";
}

public static class ExchangeRateSourcesV1
{
    /// <summary>DEC-FIN-010: TCMB indicative forex selling rates; the only source admitted.</summary>
    public const string Tcmb = "Tcmb";
}

public static class ExchangeRateResolutionStatesV1
{
    public const string Resolved = "Resolved";
    public const string Unavailable = "Unavailable";
}

/// <summary>Storage and response bounds (technical limits, not business policy).</summary>
public static class ExchangeRateInputLimitsV1
{
    /// <summary>The reason column width shared with <c>ExchangeRateManualEntry.MaximumReasonLength</c>.</summary>
    public const int ReasonMaximumLength = 1000;

    /// <summary>The stored rate's decimals (decimal(28,10)); a bulletin line per 100 units normalised to one unit fits.</summary>
    public const int RateMaximumScale = 10;

    /// <summary>The stored rate's integer digits (decimal(28,10)).</summary>
    public const int RateMaximumIntegerDigits = 18;

    /// <summary>The most days one list read answers.</summary>
    public const int MaximumListDays = 62;

    /// <summary>The most days one import run asks the sources for.</summary>
    public const int MaximumImportDays = 31;
}

/// <summary>
/// The currency identity of an ISO 4217 code, by the documented Settings derivation (<see cref="SettingsCurrencyIdentitiesV1"/>):
/// UUIDv5 in namespace <c>9df84417-8c25-59c8-a67a-959ac880a890</c>, name <c>currency:&lt;CODE&gt;</c>. For TRY it is exactly
/// <see cref="SettingsCurrencyIdentitiesV1.TurkishLira"/> (pinned by a test). An identity is not a money profile: what a company
/// may use and at what scale is still its approved profile.
/// </summary>
public static class ExchangeRateCurrencyIdentitiesV1
{
    private static readonly Guid Namespace = Guid.Parse("9df84417-8c25-59c8-a67a-959ac880a890");

    public static bool IsIsoCode(string? code) => code is { Length: 3 } && code.All(char.IsAsciiLetterUpper);

    public static Guid ForIsoCode(string code)
    {
        if (!IsIsoCode(code))
        {
            throw new ArgumentException("Para birimi üç büyük harfli ISO 4217 kodu olmalıdır.", nameof(code));
        }

        var name = Encoding.UTF8.GetBytes("currency:" + code);
        var namespaceBytes = Namespace.ToByteArray(bigEndian: true);
        var hash = SHA1.HashData([.. namespaceBytes, .. name]);
        var bytes = hash.AsSpan(0, 16).ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}

/// <summary>One currency's rate on a day, as the record serves it (one source-currency unit in TRY).</summary>
public sealed record ExchangeRateValueViewV1(
    string CurrencyCode,
    string SellingRate,
    string Provenance,
    Guid RecordId,
    string RecordVersion);

/// <summary>One append-only record of a day: an official bulletin answer, a manual correction or a half-day answer.</summary>
public sealed record ExchangeRateRecordViewV1(
    Guid RecordId,
    string RecordVersion,
    string Kind,
    string Provenance,
    string State,
    IReadOnlyList<ExchangeRateLineV1> Rates,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc,
    string? Reason,
    string? SourceReference);

public static class ExchangeRateRecordKindsV1
{
    public const string Bulletin = "Bulletin";
    public const string HalfDay = "HalfDay";
}

/// <summary>A day of the record: its current bulletin and half-day answers, the rates in force and every record behind them.</summary>
public sealed record ExchangeRateDayViewV1(
    DateOnly Date,
    string Version,
    string BulletinState,
    string HalfDayState,
    IReadOnlyList<ExchangeRateValueViewV1> Rates,
    IReadOnlyList<ExchangeRateRecordViewV1> Records);

public sealed record ExchangeRateDayListV1(
    Guid CompanyId,
    DateOnly From,
    DateOnly To,
    bool BulletinSourceEnabled,
    bool HalfDaySourceEnabled,
    IReadOnlyList<ExchangeRateDayViewV1> Days);

/// <summary>A currency line: ISO 4217 code and the forex selling rate per ONE unit as exact decimal text.</summary>
public sealed record ExchangeRateLineV1(string CurrencyCode, string SellingRate);

/// <summary>SAT-D169: a reasoned manual rate for one currency on a day TCMB published (later documents only, SAT-D172).</summary>
public sealed record CorrectExchangeRateRequestV1(string? ExpectedVersion, string? CurrencyCode, string? SellingRate, string? Reason);

/// <summary>Import the official answers for an inclusive day range.</summary>
public sealed record ImportExchangeRatesRequestV1(DateOnly? From, DateOnly? To);

/// <summary>
/// What one import run did per day: <c>Recorded</c> (a new official answer appended), <c>Unchanged</c> (the same definite
/// answer already recorded, or a still unverified one), <c>Conflict</c> (a different definite answer is already recorded;
/// nothing is overwritten) or <c>SourceDisabled</c>.
/// </summary>
public sealed record ExchangeRateImportDayV1(DateOnly Date, string BulletinOutcome, string HalfDayOutcome);

public static class ExchangeRateImportOutcomesV1
{
    public const string Recorded = "Recorded";
    public const string Unchanged = "Unchanged";
    public const string Conflict = "Conflict";
    public const string SourceDisabled = "SourceDisabled";
}

public sealed record ExchangeRateImportSummaryV1(IReadOnlyList<ExchangeRateImportDayV1> Days);

/// <summary>A command's terminal answer (stored for replay): 204, 200 with an import summary, or a problem.</summary>
public sealed record ExchangeRateCommandAnswerV1(int StatusCode, string? Code, string Title, string Detail,
    ExchangeRateImportSummaryV1? Import = null);

public sealed record ExchangeRateCommandResultV1(ExchangeRateCommandAnswerV1 Answer, bool IsReplay, string OriginalCorrelationId);

/// <summary>
/// SAT-D172: what an invoice snapshots — the number and where it came from. Resolved: the bulletin date, the rate, its
/// provenance and the record (id + version) behind it. The rate is exact decimal text (one source-currency unit in the
/// target currency), never rounded. Unavailable: the code ("kur bulunamadı/belirlenemedi") and the day that
/// stopped the resolver. Never a fallback rate.
/// </summary>
public sealed record ExchangeRateResolutionV1(
    DateOnly RateDay,
    Guid SourceCurrencyId,
    Guid TargetCurrencyId,
    string? CurrencyCode,
    string State,
    string Source,
    bool HalfDayRuleApplied,
    DateOnly? PreviousFullBusinessDay,
    DateOnly? BulletinDate,
    string? SellingRate,
    string? Provenance,
    Guid? RecordId,
    string? RecordVersion,
    string? Code,
    DateOnly? StoppedAt);

/// <summary>
/// The rate record owner's administration (Settings; AV-02 "Birim / Para / Kur / Vergi"). Every member requires its name
/// company-wide in the server-resolved company; <see cref="UnauthorizedAccessException"/> means 403. Commands carry an
/// Idempotency-Key, correlation and (correction) an expected version and a reason; each real change is audited.
/// </summary>
public interface ISettingsExchangeRateAdministrationV1
{
    Task<ExchangeRateDayListV1> ListAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    Task<ExchangeRateResolutionV1> ResolveForCurrencyAsync(DateOnly rateDay, string currencyCode,
        CancellationToken cancellationToken = default);

    Task<ExchangeRateCommandResultV1> CorrectAsync(DateOnly date, CorrectExchangeRateRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<ExchangeRateCommandResultV1> ImportAsync(ImportExchangeRatesRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The read port documents and checks use (SAT-D47, SAT-D90, SAT-D170, SAT-D172, ALS-D6): the rate of rate day D in the
/// server-resolved company, computed by the one calculator (<c>RateDayExchangeRate</c>). The caller authorizes its own use
/// case; this port needs an authenticated actor with access to the company (<c>settings_access_denied</c> otherwise). An
/// unreadable store is <c>settings_provider_unavailable</c>. The result is computed fresh on every call; nothing is frozen here.
/// </summary>
public interface ISettingsExchangeRateResolverV1
{
    Task<ExchangeRateResolutionV1> ResolveAsync(DateOnly rateDay, Guid sourceCurrencyId, Guid targetCurrencyId,
        CancellationToken cancellationToken = default);
}

/// <summary>One official bulletin answer for a day (KUR_GUNU): published with lines, a definite "no bulletin", or unverified.</summary>
public sealed record ExchangeRateBulletinFetchV1(
    DateOnly Date,
    string State,
    IReadOnlyList<ExchangeRateLineV1> Rates,
    string? SourceReference);

/// <summary>One official half-day answer for a day (SAT-D171).</summary>
public sealed record ExchangeRateHalfDayFetchV1(DateOnly Date, string State, string? SourceReference);

/// <summary>
/// The official TCMB bulletin source. Implementations must never fill a day from another day (KUR_GUNU): only a dated bulletin
/// is "published", only TCMB's definite answer is "no bulletin", anything else is "unverified".
/// </summary>
public interface IExchangeRateBulletinSourceV1
{
    bool IsEnabled { get; }

    Task<ExchangeRateBulletinFetchV1> FetchAsync(DateOnly date, CancellationToken cancellationToken = default);
}

/// <summary>The automatic official arife / half-day source (SAT-D171). Disabled until a human enables a reviewed adapter.</summary>
public interface IExchangeRateHalfDaySourceV1
{
    bool IsEnabled { get; }

    Task<ExchangeRateHalfDayFetchV1> FetchAsync(DateOnly date, CancellationToken cancellationToken = default);
}
