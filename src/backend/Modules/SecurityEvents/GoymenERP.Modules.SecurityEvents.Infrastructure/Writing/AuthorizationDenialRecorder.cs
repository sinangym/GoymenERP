using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;

namespace GoymenERP.Modules.SecurityEvents.Infrastructure.Writing;

/// <summary>
/// F023-2 / KIM-D10: builds the single <c>authz.denied</c> envelope for a refused request from the server-resolved
/// request context and hands it to <see cref="ISecurityEventWriterV1"/>. Request scoped; at most one write per
/// request. It never throws for a write failure and never changes the refusal.
/// </summary>
/// <remarks>
/// Actor: a user actor only for an authenticated request whose user id is the canonical internal id (Guid "D");
/// otherwise anonymous, without an actor id. Scope: company only when the request's company was resolved and the
/// actor may access it; otherwise platform with no company id (ADR-SEC-001 §3). The requested company is stored only
/// for <c>company_context_mismatch</c> and only for a user actor. An anonymous request never stores it.
/// </remarks>
internal sealed class AuthorizationDenialRecorder(
    ISecurityEventWriterV1 writer,
    ICurrentUser currentUser,
    ICompanyContext companyContext,
    ICorrelationContext correlationContext,
    TimeProvider timeProvider)
    : IAuthorizationDenialRecorderV1
{
    private static readonly SecurityEventWriteFailureMode FailureMode = SecurityEventWriteFailureModesV1.Resolve(
        SecurityEventTypesV1.AuthorizationDenied,
        SecurityEventPhasesV1.Single);

    private SecurityEventWriteResultV1? _result;

    public bool HasAttempted { get; private set; }

    public async Task<SecurityEventWriteResultV1> RecordAsync(
        AuthorizationDenialV1 denial,
        CancellationToken cancellationToken = default)
    {
        if (HasAttempted)
        {
            // One refusal per request: a later refusal in the same request is not a second event.
            return _result ?? SecurityEventWriteResultV1.NotWritten(FailureMode, SecurityEventWriteFailureKindsV1.DuplicateEventId);
        }

        HasAttempted = true;
        _result = await WriteOnceAsync(denial, cancellationToken);
        return _result;
    }

    private async Task<SecurityEventWriteResultV1> WriteOnceAsync(
        AuthorizationDenialV1? denial,
        CancellationToken cancellationToken)
    {
        if (denial is null
            || !AuthorizationDenialReasonCodesV1.IsDefined(denial.ReasonCode)
            || !correlationContext.IsResolved)
        {
            return Fail(SecurityEventWriteFailureKindsV1.InvalidEvent);
        }

        var userActorId = ResolveUserActorId();
        var companyId = userActorId is not null ? ResolveAccessibleCompanyId() : null;
        var requestedCompanyId = userActorId is not null
            && denial.ReasonCode == AuthorizationDenialReasonCodesV1.CompanyContextMismatch
            && denial.RequestedCompanyId is { } requested
            && requested != Guid.Empty
                ? requested
                : (Guid?)null;
        var permissionCode = denial.PermissionCode is { } permission && ErpPermissions.IsKnown(permission)
            ? permission
            : null;

        var securityEvent = new SecurityEventV1
        {
            EventId = Guid.NewGuid(),
            OccurredAtUtc = timeProvider.GetUtcNow().ToUniversalTime(),
            ScopeKind = companyId is not null ? SecurityEventScopeKindsV1.Company : SecurityEventScopeKindsV1.Platform,
            CompanyId = companyId,
            RequestedCompanyId = requestedCompanyId,
            ActorType = userActorId is not null ? SecurityEventActorTypesV1.User : SecurityEventActorTypesV1.Anonymous,
            ActorId = userActorId,
            EventType = SecurityEventTypesV1.AuthorizationDenied,
            Phase = SecurityEventPhasesV1.Single,
            Outcome = SecurityEventOutcomesV1.Denied,
            ReasonCode = denial.ReasonCode,
            Severity = SecurityEventSeveritiesV1.Warning,
            PermissionCode = permissionCode,
            CorrelationId = correlationContext.CorrelationId,
            CausationId = correlationContext.CausationId,
            SourceChannel = correlationContext.SourceChannel,
        };

        try
        {
            return await writer.WriteAsync(securityEvent, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The refusal stands even when the request was aborted before the row was stored.
            return Fail(SecurityEventWriteFailureKindsV1.StoreUnavailable);
        }
    }

    private string? ResolveUserActorId()
    {
        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not { } userId
            || !Guid.TryParseExact(userId, "D", out var internalUserId)
            || internalUserId == Guid.Empty)
        {
            return null;
        }

        var canonical = internalUserId.ToString("D");
        return string.Equals(canonical, userId, StringComparison.Ordinal) ? canonical : null;
    }

    private Guid? ResolveAccessibleCompanyId()
    {
        if (!companyContext.IsResolved)
        {
            return null;
        }

        var companyId = companyContext.CompanyId;
        return companyId != Guid.Empty && currentUser.CanAccessCompany(companyId) ? companyId : null;
    }

    private static SecurityEventWriteResultV1 Fail(string failureKind)
    {
        SecurityEventWriteMetrics.RecordWriteFailure(SecurityEventTypesV1.AuthorizationDenied, FailureMode, failureKind);
        return SecurityEventWriteResultV1.NotWritten(FailureMode, failureKind);
    }
}
