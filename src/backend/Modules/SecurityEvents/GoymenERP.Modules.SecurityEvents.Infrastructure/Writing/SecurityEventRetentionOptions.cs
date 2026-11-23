using GoymenERP.BuildingBlocks.Security.Events;
using Microsoft.Extensions.Configuration;

namespace GoymenERP.Modules.SecurityEvents.Infrastructure.Writing;

/// <summary>One retention class, bound from <c>SecurityEvents:Retention:&lt;Class&gt;</c>.</summary>
public sealed class SecurityEventRetentionClassOptions
{
    public int Years { get; set; }

    public string? PolicyRef { get; set; }
}

/// <summary>
/// Resolves <c>RetainUntilUtc</c> and <c>RetentionPolicyRef</c> from configuration. Durations are never code
/// constants; a missing or malformed class fails closed at startup.
/// </summary>
public sealed class SecurityEventRetentionResolver
{
    public const string SectionName = "SecurityEvents:Retention";

    private readonly IReadOnlyDictionary<string, SecurityEventRetentionClassOptions> classes;

    public SecurityEventRetentionResolver(IReadOnlyDictionary<string, SecurityEventRetentionClassOptions> classes)
    {
        ArgumentNullException.ThrowIfNull(classes);
        var unknown = classes.Keys.Where(key => !SecurityEventRetentionClassesV1.All.Contains(key, StringComparer.Ordinal)).ToArray();
        if (unknown.Length > 0)
        {
            throw new InvalidOperationException(
                $"Güvenlik olayı saklama ayarında tanımsız sınıf var: {string.Join(", ", unknown)}.");
        }

        foreach (var retentionClass in SecurityEventRetentionClassesV1.All)
        {
            if (!classes.TryGetValue(retentionClass, out var options)
                || options.Years < 1
                || string.IsNullOrWhiteSpace(options.PolicyRef)
                || options.PolicyRef.Length > Domain.Events.SecurityEventRecord.RetentionPolicyRefMaxLength)
            {
                throw new InvalidOperationException(
                    $"Güvenlik olayı saklama ayarı eksik veya geçersiz: {SectionName}:{retentionClass} (Years ve PolicyRef zorunlu).");
            }
        }

        this.classes = classes.ToDictionary(pair => pair.Key, pair => new SecurityEventRetentionClassOptions
        {
            Years = pair.Value.Years,
            PolicyRef = pair.Value.PolicyRef,
        }, StringComparer.Ordinal);
    }

    public static SecurityEventRetentionResolver FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(SectionName);
        var classes = new Dictionary<string, SecurityEventRetentionClassOptions>(StringComparer.Ordinal);
        foreach (var child in section.GetChildren())
        {
            var options = new SecurityEventRetentionClassOptions();
            child.Bind(options);
            classes[child.Key] = options;
        }

        return new SecurityEventRetentionResolver(classes);
    }

    public (DateTimeOffset RetainUntilUtc, string RetentionPolicyRef) Resolve(string eventType, DateTimeOffset occurredAtUtc)
    {
        var retentionClass = SecurityEventRetentionClassesV1.ForEventType(eventType)
            ?? throw new InvalidOperationException("Security event type has no retention class.");
        var options = classes.TryGetValue(retentionClass, out var value)
            ? value
            : throw new InvalidOperationException("Security event retention class is not configured.");
        return (occurredAtUtc.AddYears(options.Years), options.PolicyRef!);
    }
}
