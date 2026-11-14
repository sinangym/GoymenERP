using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace GoymenERP.Modules.SecurityEvents.Infrastructure.Writing;

/// <summary>
/// Aggregation settings bound from <c>SecurityEvents:Aggregation</c> (#66 6806860620 item 15, E7). Window and
/// threshold are configuration, never code constants, and are unset until Sinan sets measured values. Unset
/// means aggregation is disabled and every event is written as its own row. Setting exactly one fails closed.
/// The aggregator itself is follow-up AI-2c; AI-2a only carries the schema and the validated options.
/// </summary>
public sealed class SecurityEventAggregationOptions
{
    public const string SectionName = "SecurityEvents:Aggregation";

    public TimeSpan? Window { get; init; }

    public int? Threshold { get; init; }

    public bool IsEnabled => Window is not null && Threshold is not null;

    public static SecurityEventAggregationOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(SectionName);
        var options = new SecurityEventAggregationOptions
        {
            Window = ParseOptional<TimeSpan>(section[nameof(Window)], nameof(Window), static text =>
                TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var value) ? value : (TimeSpan?)null),
            Threshold = ParseOptional<int>(section[nameof(Threshold)], nameof(Threshold), static text =>
                int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : (int?)null),
        };
        options.Validate();
        return options;
    }

    private static T? ParseOptional<T>(string? text, string key, Func<string, T?> parse)
        where T : struct
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return parse(text.Trim()) ?? throw new InvalidOperationException(
            $"Güvenlik olayı toplulaştırma ayarı okunamadı: {SectionName}:{key}.");
    }

    public void Validate()
    {
        if ((Window is null) != (Threshold is null))
        {
            throw new InvalidOperationException(
                $"Güvenlik olayı toplulaştırma ayarı eksik: {SectionName}:Window ve {SectionName}:Threshold birlikte tanımlanmalı veya ikisi de boş bırakılmalıdır.");
        }

        if (Window is { } window && window <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"Güvenlik olayı toplulaştırma penceresi pozitif olmalıdır: {SectionName}:Window.");
        }

        if (Threshold is < 1)
        {
            throw new InvalidOperationException($"Güvenlik olayı toplulaştırma eşiği en az 1 olmalıdır: {SectionName}:Threshold.");
        }
    }
}
