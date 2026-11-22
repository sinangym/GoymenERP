using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Reading;

namespace GoymenERP.Modules.SecurityEvents.Infrastructure.Ai;

/// <summary>
/// WP-C10 (DALGA_PLANI_v2 C.10, AI-D18): the read-only security event digest behind the AI security tools. It reads
/// ONLY through the module's own company read (<see cref="ISecurityEventQueryServiceV1"/>), which measures
/// <c>platform.security-events.read</c> company-wide for the server-side actor, records each read as
/// <c>security.events.read</c> before any row is read (fail-closed) and re-measures the authority before releasing.
/// One page of the newest <see cref="PageSize"/> rows is read per event type; nothing is estimated: a type whose page
/// had more rows is marked incomplete. Personal data is masked here (AI-D34 d): the actor identity is replaced with a
/// per-digest pseudonym (<c>Kişi 1</c>, <c>Kişi 2</c> …) that cannot be linked across calls; the correlation and
/// causation identities, the requested company, the policy reference, the retention date and the integrity hash are
/// not released.
/// </summary>
public sealed class SecurityEventDigestReader(ISecurityEventQueryServiceV1 events)
{
    public const int PageSize = 100;
    public const string FailedLogins = "failed-logins";
    public const string AuthorizationDenials = "authorization-denials";
    public const string CriticalChanges = "critical-changes";
    public const string PersonPseudonymPrefix = "Kişi ";

    /// <summary>The closed category → event type map. A type not listed here is never read by the AI tools.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Categories { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [FailedLogins] = [SecurityEventTypesV1.LoginFailed],
            [AuthorizationDenials] =
            [
                SecurityEventTypesV1.AuthorizationDenied,
                SecurityEventTypesV1.AiAuthorizationDenied,
                SecurityEventTypesV1.AiRequestRefused,
            ],
            [CriticalChanges] =
            [
                SecurityEventTypesV1.PermissionChanged,
                SecurityEventTypesV1.PolicyPublished,
                SecurityEventTypesV1.ExportPerformed,
                SecurityEventTypesV1.RetentionPurgeRequested,
            ],
        };

    public static IReadOnlyList<string> CategoryNames { get; } = [FailedLogins, AuthorizationDenials, CriticalChanges];

    public async Task<SecurityEventDigestV1> ReadAsync(
        Guid expectedCompanyId,
        IReadOnlyCollection<string> categories,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(categories);
        if (expectedCompanyId == Guid.Empty || categories.Count == 0 || categories.Any(name => !Categories.ContainsKey(name)))
        {
            return SecurityEventDigestV1.Of(SecurityEventDigestStatusV1.Unavailable);
        }

        var rows = new List<(string Category, SecurityEventItemV1 Item)>();
        var incompleteTypes = new List<string>();
        foreach (var category in CategoryNames.Where(categories.Contains))
        {
            foreach (var eventType in Categories[category])
            {
                var result = await events.GetEventsAsync(PageSize, cursor: null, eventType, correlationId: null, cancellationToken);
                switch (result.Status)
                {
                    case SecurityEventQueryStatusV1.Ok when result.Page is not null:
                        break;
                    case SecurityEventQueryStatusV1.Denied:
                        return SecurityEventDigestV1.Of(SecurityEventDigestStatusV1.Denied);
                    default:
                        return SecurityEventDigestV1.Of(SecurityEventDigestStatusV1.Unavailable);
                }

                foreach (var item in result.Page.Items)
                {
                    // The owner answered for another company or another type: release nothing (fail-closed).
                    if (item.CompanyId != expectedCompanyId || !string.Equals(item.EventType, eventType, StringComparison.Ordinal))
                    {
                        return SecurityEventDigestV1.Of(SecurityEventDigestStatusV1.Unavailable);
                    }

                    rows.Add((category, item));
                }

                if (result.Page.NextCursor is not null)
                {
                    incompleteTypes.Add(eventType);
                }
            }
        }

        if (rows.Select(row => row.Item.Id).Distinct().Count() != rows.Count)
        {
            return SecurityEventDigestV1.Of(SecurityEventDigestStatusV1.Unavailable);
        }

        var ordered = rows
            .OrderByDescending(row => row.Item.OccurredAtUtc)
            .ThenByDescending(row => row.Item.Id)
            .ToArray();
        var pseudonyms = new Dictionary<string, string>(StringComparer.Ordinal);
        var masked = ordered.Select(row => new SecurityEventDigestItemV1(
            row.Item.OccurredAtUtc,
            row.Category,
            row.Item.EventType,
            row.Item.Outcome,
            row.Item.Severity,
            row.Item.ReasonCode,
            row.Item.PermissionCode,
            row.Item.ActorType,
            PseudonymOf(row.Item.ActorId, pseudonyms),
            row.Item.RequestedCompanyId is { } requested && requested != row.Item.CompanyId,
            Math.Max(1, row.Item.OccurrenceCount),
            row.Item.AggregationWindowStartUtc,
            row.Item.AggregationWindowEndUtc,
            row.Item.SourceChannel,
            row.Item.AiToolId)).ToArray();
        var version = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',
            ordered.Select(row => $"{row.Item.Id:D}:{row.Item.IntegrityHash}")))));
        return new SecurityEventDigestV1(SecurityEventDigestStatusV1.Ok, masked, incompleteTypes, version);
    }

    private static string? PseudonymOf(string? actorId, Dictionary<string, string> pseudonyms)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return null;
        }

        if (!pseudonyms.TryGetValue(actorId, out var pseudonym))
        {
            pseudonym = PersonPseudonymPrefix + (pseudonyms.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            pseudonyms.Add(actorId, pseudonym);
        }

        return pseudonym;
    }
}

public enum SecurityEventDigestStatusV1
{
    Ok,
    Denied,
    Unavailable,
}

/// <summary>Masked rows of the requested categories, newest first; <c>IncompleteEventTypes</c> had more rows.</summary>
public sealed record SecurityEventDigestV1(
    SecurityEventDigestStatusV1 Status,
    IReadOnlyList<SecurityEventDigestItemV1> Items,
    IReadOnlyList<string> IncompleteEventTypes,
    string Version)
{
    public static SecurityEventDigestV1 Of(SecurityEventDigestStatusV1 status) => new(status, [], [], string.Empty);

    public bool Complete => IncompleteEventTypes.Count == 0;
}

/// <summary>
/// One released row. No company id, actor id, correlation/causation id, policy, retention or integrity value leaves;
/// <c>Actor</c> is a per-digest pseudonym and <c>CrossCompanyAttempt</c> only says that another company was asked for.
/// </summary>
public sealed record SecurityEventDigestItemV1(
    DateTimeOffset OccurredAtUtc,
    string Category,
    string EventType,
    string Outcome,
    string Severity,
    string? ReasonCode,
    string? PermissionCode,
    string ActorType,
    string? Actor,
    bool CrossCompanyAttempt,
    int OccurrenceCount,
    DateTimeOffset? AggregationWindowStartUtc,
    DateTimeOffset? AggregationWindowEndUtc,
    string SourceChannel,
    string? AiToolId);
