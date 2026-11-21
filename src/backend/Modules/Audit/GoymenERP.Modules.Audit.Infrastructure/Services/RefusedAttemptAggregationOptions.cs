using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace GoymenERP.Modules.Audit.Infrastructure.Services;

/// <summary>
/// Volume control of the separate short refused attempt writes (F-023, F023-4), bound from
/// <c>Audit:RefusedAttempts:Aggregation</c>. KIM-D11: at the start every refusal is its own row; aggregation is opened
/// only after measurement, with values Sinan enters. Window and threshold are therefore configuration, never code
/// constants, and the shipped value is empty. Both empty means disabled (every refusal is its own row); setting exactly
/// one fails closed at startup, as does a non-positive value (the <c>SecurityEventAggregationOptions</c> rule).
/// </summary>
public sealed class RefusedAttemptAggregationOptions
{
    public const string SectionName = "Audit:RefusedAttempts:Aggregation";

    public RefusedAttemptAggregationOptions(TimeSpan? window, int? threshold)
    {
        if ((window is null) != (threshold is null))
        {
            throw new InvalidOperationException(
                $"Reddedilen deneme toplulaştırma ayarı eksik: {SectionName}:Window ve {SectionName}:Threshold birlikte tanımlanmalı veya ikisi de boş bırakılmalıdır.");
        }

        // The upper bound is a technical guard against window-end overflow, not a policy value.
        if (window is { } value && (value <= TimeSpan.Zero || value.TotalMilliseconds > int.MaxValue))
        {
            throw new InvalidOperationException(
                $"Reddedilen deneme toplulaştırma penceresi geçersiz: {SectionName}:Window.");
        }

        if (threshold is < 1)
        {
            throw new InvalidOperationException(
                $"Reddedilen deneme toplulaştırma eşiği en az 1 olmalıdır: {SectionName}:Threshold.");
        }

        Window = window;
        Threshold = threshold;
    }

    /// <summary>The disabled setting: every refusal is written as its own row (KIM-D11 start state).</summary>
    public static RefusedAttemptAggregationOptions Disabled { get; } = new(window: null, threshold: null);

    /// <summary>The length of one aggregation window, counted from the first refusal of a key.</summary>
    public TimeSpan? Window { get; }

    /// <summary>How many refusals of one key inside one window are still written as their own rows.</summary>
    public int? Threshold { get; }

    public bool IsEnabled => Window is not null && Threshold is not null;

    public static RefusedAttemptAggregationOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(SectionName);
        var window = ParseOptional(section[nameof(Window)], nameof(Window), static text =>
            TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var value) ? value : (TimeSpan?)null);
        var threshold = ParseOptional(section[nameof(Threshold)], nameof(Threshold), static text =>
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : (int?)null);
        return new RefusedAttemptAggregationOptions(window, threshold);
    }

    private static T? ParseOptional<T>(string? text, string key, Func<string, T?> parse)
        where T : struct
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return parse(text.Trim()) ?? throw new InvalidOperationException(
            $"Reddedilen deneme toplulaştırma ayarı okunamadı: {SectionName}:{key}.");
    }
}
