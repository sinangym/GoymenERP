namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// WP15 (DALGA_PLANI_v2 1.16, YOL-D4 (c)): permissions of the legal deadline calendar. Registered, not granted; company-wide;
/// not Critical. Giving either to a role is a human gate.
/// </summary>
public static class SettingsLegalDeadlinePermissionsV1
{
    /// <summary>Reads and records the company's legal deadline calendar versions (Ayarlar).</summary>
    public const string CalendarManage = "settings.legal-deadline-calendar.manage";

    /// <summary>Sees the upcoming legal deadline counters (Work "yaklaşan süreler" view).</summary>
    public const string DeadlinesRead = "settings.legal-deadlines.read";
}

/// <summary>
/// The closed catalogue of periodic legal obligations the calendar can carry (YOL-D4 (c): beyanname, e-defter, BA/BS ...).
/// The catalogue names an obligation only; when it falls due is never in code: it is the recorded calendar version, entered
/// after the financial advisor's confirmation (docs/delivery/MALI_MUSAVIR_SORULARI_v1.md). An obligation without an entry is
/// "tanımsız" and has no counter.
/// </summary>
public enum LegalDeadlineObligationV1
{
    VatReturn = 1,
    WithholdingReturn = 2,
    ELedgerBerat = 3,
    BaBsForms = 4,
    ProvisionalTaxReturn = 5,
    CorporateTaxReturn = 6,
    StampTaxReturn = 7,
    SocialSecurityReturn = 8,
}

/// <summary>
/// One recorded calendar entry: the obligation's period length in months (<c>1</c>, <c>3</c> or <c>12</c>; periods end at
/// calendar month ends aligned to the year: months 1..12, quarters ending March/June/September/December, the year ending
/// December), how many months after the period end the due month is, and the day of that month (a day past the month's last
/// day means the last day). No holiday or weekend shift is applied: whether and how a due day moves is not decided
/// (NEEDS-SINAN WP15-2) and the screen says so.
/// </summary>
public sealed record SettingsLegalDeadlineEntryV1(LegalDeadlineObligationV1 Obligation, int PeriodMonths, int MonthsAfterPeriodEnd,
    int DueDayOfMonth);

/// <summary>The latest recorded calendar of a company; <see cref="Version"/> 0 and no entries means "not defined".</summary>
public sealed record SettingsLegalDeadlineCalendarV1(Guid CompanyId, long Version, IReadOnlyList<SettingsLegalDeadlineEntryV1> Entries)
{
    public bool IsDefined => Version > 0;

    public string VersionText => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"legal.deadline-calendar:{Version}");
}

public interface ISettingsLegalDeadlineCalendarProviderV1
{
    /// <summary>The latest recorded calendar; an unrecorded calendar answers version 0 with no entries (never a default).</summary>
    Task<SettingsLegalDeadlineCalendarV1> GetLegalDeadlineCalendarAsync(Guid companyId, CancellationToken cancellationToken = default);
}

public static class SettingsLegalDeadlineCalendarLimitsV1
{
    /// <summary>Allowed period lengths: monthly, quarterly, yearly.</summary>
    public static readonly IReadOnlyList<int> PeriodMonths = [1, 3, 12];

    /// <summary>Input range only; the decided values live in recorded versions.</summary>
    public const int MaximumMonthsAfterPeriodEnd = 12;

    public const int MaximumDueDayOfMonth = 31;
}

public sealed record RecordLegalDeadlineEntryV1(string? Obligation, int? PeriodMonths, int? MonthsAfterPeriodEnd, int? DueDayOfMonth);

/// <summary>An empty entry list is a valid version: it records "no obligation is tracked" explicitly.</summary>
public sealed record RecordLegalDeadlineCalendarRequestV1(string? ExpectedVersion, IReadOnlyList<RecordLegalDeadlineEntryV1>? Entries,
    string? Reason);

public sealed record LegalDeadlineEntryViewV1(string Obligation, int PeriodMonths, int MonthsAfterPeriodEnd, int DueDayOfMonth);

public sealed record LegalDeadlineCalendarVersionViewV1(string Version, int EntryCount, string RecordedBy, DateTimeOffset RecordedAtUtc,
    string Reason);

public sealed record LegalDeadlineCalendarViewV1(Guid CompanyId, bool IsDefined, string Version,
    IReadOnlyList<LegalDeadlineEntryViewV1> Entries, IReadOnlyList<LegalDeadlineCalendarVersionViewV1> Versions);

public interface ISettingsLegalDeadlineCalendarAdministrationV1
{
    Task<LegalDeadlineCalendarViewV1> GetLegalDeadlineCalendarAsync(CancellationToken cancellationToken = default);

    Task<CompanyPolicyCommandResultV1> RecordLegalDeadlineCalendarAsync(RecordLegalDeadlineCalendarRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}
