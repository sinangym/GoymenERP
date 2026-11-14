using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace GoymenERP.Modules.Audit.Infrastructure.Services;

/// <summary>An ISO 8601 date period (<c>PnYnMnD</c>); time parts are not accepted.</summary>
public readonly partial record struct RefusedAttemptRetentionPeriod(int Years, int Months, int Days)
{
    public static bool TryParse(string? value, out RefusedAttemptRetentionPeriod period)
    {
        period = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = PeriodPattern().Match(value.Trim());
        if (!match.Success
            || !TryPart(match.Groups["years"], out var years)
            || !TryPart(match.Groups["months"], out var months)
            || !TryPart(match.Groups["days"], out var days)
            || years + (long)months + days <= 0)
        {
            return false;
        }

        period = new RefusedAttemptRetentionPeriod(years, months, days);
        return true;
    }

    /// <summary>The end of retention for a row written at <paramref name="writtenAtUtc"/>.</summary>
    public DateTimeOffset RetainUntil(DateTimeOffset writtenAtUtc) =>
        writtenAtUtc.ToUniversalTime().AddYears(Years).AddMonths(Months).AddDays(Days);

    public override string ToString()
    {
        var text = "P";
        if (Years > 0) text += Years.ToString(CultureInfo.InvariantCulture) + "Y";
        if (Months > 0) text += Months.ToString(CultureInfo.InvariantCulture) + "M";
        if (Days > 0) text += Days.ToString(CultureInfo.InvariantCulture) + "D";
        return text;
    }

    private static bool TryPart(Group group, out int value)
    {
        value = 0;
        return !group.Success
            || int.TryParse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    [GeneratedRegex(@"\AP(?:(?<years>[0-9]{1,4})Y)?(?:(?<months>[0-9]{1,4})M)?(?:(?<days>[0-9]{1,5})D)?\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex PeriodPattern();
}

/// <summary>
/// Retention of refused attempt rows, bound from <c>Audit:RefusedAttempts:Retention</c> (KIM-D8: 2 years at the start,
/// changeable; automatic disposal needs a separate approval). The duration is never a code constant: a missing or
/// malformed value fails closed at startup. This slice implements no disposal path (ADR-009's append-only trigger
/// refuses deletes), so enabling automatic disposal also fails closed until that separately approved slice exists.
/// </summary>
public sealed class RefusedAttemptRetentionOptions
{
    public const string SectionName = "Audit:RefusedAttempts:Retention";
    public const int PolicyRefMaxLength = 64;

    public RefusedAttemptRetentionOptions(RefusedAttemptRetentionPeriod period, string policyRef, bool autoDisposalEnabled)
    {
        if (period.Years < 0 || period.Months < 0 || period.Days < 0
            || period.Years + (long)period.Months + period.Days <= 0)
        {
            throw new InvalidOperationException($"Reddedilen deneme saklama süresi geçersiz: {SectionName}:Period.");
        }

        if (string.IsNullOrWhiteSpace(policyRef) || policyRef.Trim().Length > PolicyRefMaxLength)
        {
            throw new InvalidOperationException($"Reddedilen deneme saklama ayarı eksik veya geçersiz: {SectionName}:PolicyRef.");
        }

        if (autoDisposalEnabled)
        {
            throw new InvalidOperationException(
                $"Reddedilen deneme kayıtlarının otomatik imhası ayrı onay ve ayrı dilim gerektirir: {SectionName}:AutoDisposalEnabled kapalı kalmalıdır.");
        }

        Period = period;
        PolicyRef = policyRef.Trim();
        AutoDisposalEnabled = autoDisposalEnabled;
    }

    public RefusedAttemptRetentionPeriod Period { get; }

    public string PolicyRef { get; }

    public bool AutoDisposalEnabled { get; }

    public static RefusedAttemptRetentionOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(SectionName);
        if (!RefusedAttemptRetentionPeriod.TryParse(section["Period"], out var period))
        {
            throw new InvalidOperationException(
                $"Reddedilen deneme saklama süresi eksik veya geçersiz: {SectionName}:Period (ISO 8601, ör. P2Y).");
        }

        var autoDisposal = section["AutoDisposalEnabled"];
        bool autoDisposalEnabled;
        if (string.IsNullOrWhiteSpace(autoDisposal))
        {
            autoDisposalEnabled = false;
        }
        else if (!bool.TryParse(autoDisposal, out autoDisposalEnabled))
        {
            throw new InvalidOperationException(
                $"Reddedilen deneme otomatik imha ayarı geçersiz: {SectionName}:AutoDisposalEnabled.");
        }

        return new RefusedAttemptRetentionOptions(period, section["PolicyRef"] ?? string.Empty, autoDisposalEnabled);
    }

    /// <summary>Whether a refused attempt row written at <paramref name="writtenAtUtc"/> is past its retention.</summary>
    public bool IsExpired(DateTimeOffset writtenAtUtc, DateTimeOffset nowUtc) =>
        Period.RetainUntil(writtenAtUtc) <= nowUtc.ToUniversalTime();
}

/// <summary>
/// The short standalone write of a refused attempt (F-023: "kısa zaman aşımı ayar"), bound from
/// <c>Audit:RefusedAttempts:StandaloneWrite</c>. A missing or non-positive timeout fails closed at startup.
/// </summary>
public sealed class RefusedAttemptStandaloneWriteOptions
{
    public const string SectionName = "Audit:RefusedAttempts:StandaloneWrite";

    public RefusedAttemptStandaloneWriteOptions(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
        {
            throw new InvalidOperationException($"Reddedilen deneme yazma zaman aşımı geçersiz: {SectionName}:Timeout.");
        }

        Timeout = timeout;
    }

    public TimeSpan Timeout { get; }

    public static RefusedAttemptStandaloneWriteOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var value = configuration.GetSection(SectionName)["Timeout"];
        return string.IsNullOrWhiteSpace(value)
               || !TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var timeout)
            ? throw new InvalidOperationException(
                $"Reddedilen deneme yazma zaman aşımı eksik veya geçersiz: {SectionName}:Timeout.")
            : new RefusedAttemptStandaloneWriteOptions(timeout);
    }
}
