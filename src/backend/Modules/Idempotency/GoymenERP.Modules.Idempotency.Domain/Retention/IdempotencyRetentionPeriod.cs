using System.Globalization;
using System.Text.RegularExpressions;

namespace GoymenERP.Modules.Idempotency.Domain.Retention;

/// <summary>
/// The retention of an idempotency record as an ISO 8601 date period (<c>PnYnMnD</c>); time parts are not accepted.
/// The value always comes from configuration (OPEN-021 / NS-12): it is never a code constant.
/// </summary>
public readonly partial record struct IdempotencyRetentionPeriod
{
    public const int MaxTextLength = 32;

    private IdempotencyRetentionPeriod(int years, int months, int days)
    {
        Years = years;
        Months = months;
        Days = days;
    }

    public int Years { get; }

    public int Months { get; }

    public int Days { get; }

    public bool IsDefined => Years + (long)Months + Days > 0;

    public static bool TryParse(string? value, out IdempotencyRetentionPeriod period)
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

        period = new IdempotencyRetentionPeriod(years, months, days);
        return true;
    }

    /// <summary>
    /// The newest completion instant that is already past retention at <paramref name="nowUtc"/>: a record qualifies
    /// for disposal only when it completed strictly before this instant.
    /// </summary>
    public DateTimeOffset CutoffAt(DateTimeOffset nowUtc)
    {
        if (!IsDefined)
        {
            throw new InvalidOperationException("Idempotency saklama süresi tanımlı değil.");
        }

        return nowUtc.ToUniversalTime().AddYears(-Years).AddMonths(-Months).AddDays(-Days);
    }

    /// <summary>Whether a record completed at <paramref name="completedAtUtc"/> is past retention at <paramref name="nowUtc"/>.</summary>
    public bool IsExpired(DateTimeOffset completedAtUtc, DateTimeOffset nowUtc) =>
        completedAtUtc.ToUniversalTime() < CutoffAt(nowUtc);

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
