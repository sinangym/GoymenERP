using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Writing;

namespace GoymenERP.SecurityEvents.UnitTests;

internal static class SecurityEventFixtures
{
    public static readonly Guid CompanyA = Guid.Parse("a1a1a1a1-0000-4000-8000-000000000001");
    public static readonly DateTimeOffset OccurredAt = new(2026, 11, 11, 10, 30, 0, TimeSpan.Zero);

    public static SecurityEventV1 Denial(string eventType = SecurityEventTypesV1.AuthorizationDenied) => new()
    {
        EventId = Guid.NewGuid(),
        OccurredAtUtc = OccurredAt,
        ScopeKind = SecurityEventScopeKindsV1.Company,
        CompanyId = CompanyA,
        ActorType = SecurityEventActorTypesV1.User,
        ActorId = "user-42",
        EventType = eventType,
        Outcome = SecurityEventOutcomesV1.Denied,
        ReasonCode = SecurityEventReasonCodesV1.PermissionMissing,
        Severity = SecurityEventSeveritiesV1.Warning,
        PermissionCode = "purchasing.requests.approve",
        CorrelationId = "corr-1",
        SourceChannel = SourceChannels.Web,
    };

    public static SecurityEventV1 PermissionChange() => new()
    {
        EventId = Guid.NewGuid(),
        OccurredAtUtc = OccurredAt,
        ScopeKind = SecurityEventScopeKindsV1.Company,
        CompanyId = CompanyA,
        ActorType = SecurityEventActorTypesV1.User,
        ActorId = "admin-1",
        EventType = SecurityEventTypesV1.PermissionChanged,
        Outcome = SecurityEventOutcomesV1.Allowed,
        Severity = SecurityEventSeveritiesV1.High,
        PermissionCode = "purchasing.requests.approve",
        CorrelationId = "corr-2",
        SourceChannel = SourceChannels.Web,
    };

    public static SecurityEventV1 AiToolCall(string phase) => new()
    {
        EventId = Guid.NewGuid(),
        OccurredAtUtc = OccurredAt,
        ScopeKind = SecurityEventScopeKindsV1.Company,
        CompanyId = CompanyA,
        ActorType = SecurityEventActorTypesV1.Ai,
        ActorId = "assistant-1",
        EventType = SecurityEventTypesV1.AiToolCalled,
        Phase = phase,
        Outcome = SecurityEventOutcomesV1.Allowed,
        Severity = SecurityEventSeveritiesV1.Info,
        CorrelationId = "corr-3",
        SourceChannel = SourceChannels.AiAssisted,
        AiToolId = "purchasing.requests.read.v1",
        AiToolVersion = "1",
        AiRiskClass = AiToolRiskClassV1.Read,
        AiDataClass = AiDataClassV1.Internal,
    };

    public static Dictionary<string, SecurityEventRetentionClassOptions> RetentionClasses() => new(StringComparer.Ordinal)
    {
        [SecurityEventRetentionClassesV1.No134] = new() { Years = 2, PolicyRef = "TEST-No134" },
        [SecurityEventRetentionClassesV1.No157] = new() { Years = 1, PolicyRef = "TEST-No157" },
        [SecurityEventRetentionClassesV1.AiAuthorizationDenied] = new() { Years = 2, PolicyRef = "TEST-E9" },
    };

    public static SecurityEventRetentionResolver Retention() => new(RetentionClasses());
}
