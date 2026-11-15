using System.Diagnostics.Metrics;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Writing;
using static GoymenERP.SecurityEvents.UnitTests.SecurityEventFixtures;

namespace GoymenERP.SecurityEvents.UnitTests;

/// <summary>
/// F023-2 / KIM-D10: the request scoped authz.denied recorder. Scope, actor and company come from the server-resolved
/// request context; one refusal per request; a write failure never throws and raises the loss metric.
/// </summary>
public sealed class AuthorizationDenialRecorderTests
{
    private static readonly Guid CompanyB = Guid.Parse("b2b2b2b2-0000-4000-8000-000000000002");
    private const string CorrelationId = "corr-authz-1";

    [Fact]
    public async Task An_anonymous_session_refusal_is_one_platform_row_without_actor_company_or_requested_company()
    {
        var writer = new CapturingWriter();
        var recorder = Recorder(writer, new FakeCurrentUser(userId: null), company: null);

        var result = await recorder.RecordAsync(new AuthorizationDenialV1
        {
            ReasonCode = AuthorizationDenialReasonCodesV1.SessionInvalid,
            RequestedCompanyId = CompanyB,
        });

        Assert.True(result.Recorded);
        var written = Assert.Single(writer.Events);
        Assert.Empty(SecurityEventV1Validator.Validate(written));
        Assert.Equal(SecurityEventTypesV1.AuthorizationDenied, written.EventType);
        Assert.Equal(SecurityEventOutcomesV1.Denied, written.Outcome);
        Assert.Equal(SecurityEventReasonCodesV1.SessionInvalid, written.ReasonCode);
        Assert.Equal(SecurityEventActorTypesV1.Anonymous, written.ActorType);
        Assert.Null(written.ActorId);
        Assert.Equal(SecurityEventScopeKindsV1.Platform, written.ScopeKind);
        Assert.Null(written.CompanyId);
        Assert.Null(written.RequestedCompanyId);
        Assert.Equal(CorrelationId, written.CorrelationId);
        Assert.Equal(SourceChannels.Web, written.SourceChannel);
        Assert.Equal(SecurityEventPhasesV1.Single, written.Phase);
        Assert.Equal(ServerNow, written.OccurredAtUtc);
    }

    [Fact]
    public async Task A_permission_refusal_in_an_accessible_company_is_a_company_row_with_the_registered_permission()
    {
        var writer = new CapturingWriter();
        var recorder = Recorder(writer, new FakeCurrentUser(UserActorId, CompanyA), company: CompanyA);

        var result = await recorder.RecordAsync(new AuthorizationDenialV1
        {
            ReasonCode = AuthorizationDenialReasonCodesV1.PermissionMissing,
            PermissionCode = ErpPermissions.PlatformSecurityEventsRead,
        });

        Assert.True(result.Recorded);
        var written = Assert.Single(writer.Events);
        Assert.Empty(SecurityEventV1Validator.Validate(written));
        Assert.Equal(SecurityEventReasonCodesV1.PermissionMissing, written.ReasonCode);
        Assert.Equal(ErpPermissions.PlatformSecurityEventsRead, written.PermissionCode);
        Assert.Equal(SecurityEventActorTypesV1.User, written.ActorType);
        Assert.Equal(UserActorId, written.ActorId);
        Assert.Equal(SecurityEventScopeKindsV1.Company, written.ScopeKind);
        Assert.Equal(CompanyA, written.CompanyId);
        Assert.Null(written.RequestedCompanyId);
    }

    [Fact]
    public async Task A_foreign_company_refusal_is_a_platform_row_that_keeps_the_requested_company_for_the_user()
    {
        var writer = new CapturingWriter();
        var recorder = Recorder(writer, new FakeCurrentUser(UserActorId, CompanyA), company: null);

        await recorder.RecordAsync(new AuthorizationDenialV1
        {
            ReasonCode = AuthorizationDenialReasonCodesV1.CompanyContextMismatch,
            RequestedCompanyId = CompanyB,
        });

        var written = Assert.Single(writer.Events);
        Assert.Empty(SecurityEventV1Validator.Validate(written));
        Assert.Equal(SecurityEventReasonCodesV1.CompanyContextMismatch, written.ReasonCode);
        Assert.Equal(SecurityEventScopeKindsV1.Platform, written.ScopeKind);
        Assert.Null(written.CompanyId);
        Assert.Equal(CompanyB, written.RequestedCompanyId);
        Assert.Equal(UserActorId, written.ActorId);
    }

    [Fact]
    public async Task A_resolved_company_the_actor_cannot_access_is_never_the_row_company()
    {
        var writer = new CapturingWriter();
        var recorder = Recorder(writer, new FakeCurrentUser(UserActorId, CompanyA), company: CompanyB);

        await recorder.RecordAsync(new AuthorizationDenialV1 { ReasonCode = AuthorizationDenialReasonCodesV1.PermissionMissing });

        var written = Assert.Single(writer.Events);
        Assert.Equal(SecurityEventScopeKindsV1.Platform, written.ScopeKind);
        Assert.Null(written.CompanyId);
        Assert.Null(written.RequestedCompanyId);
    }

    [Fact]
    public async Task A_requested_company_is_stored_only_for_a_company_context_refusal()
    {
        var writer = new CapturingWriter();
        var recorder = Recorder(writer, new FakeCurrentUser(UserActorId, CompanyA), company: CompanyA);

        await recorder.RecordAsync(new AuthorizationDenialV1
        {
            ReasonCode = AuthorizationDenialReasonCodesV1.PermissionMissing,
            RequestedCompanyId = CompanyB,
        });

        Assert.Null(Assert.Single(writer.Events).RequestedCompanyId);
    }

    [Fact]
    public async Task Only_the_first_refusal_of_a_request_is_written()
    {
        var writer = new CapturingWriter();
        var recorder = Recorder(writer, new FakeCurrentUser(UserActorId, CompanyA), company: CompanyA);
        Assert.False(recorder.HasAttempted);

        var first = await recorder.RecordAsync(new AuthorizationDenialV1 { ReasonCode = AuthorizationDenialReasonCodesV1.PermissionMissing });
        var second = await recorder.RecordAsync(new AuthorizationDenialV1 { ReasonCode = AuthorizationDenialReasonCodesV1.SessionInvalid });

        Assert.True(recorder.HasAttempted);
        Assert.Same(first, second);
        Assert.Equal(SecurityEventReasonCodesV1.PermissionMissing, Assert.Single(writer.Events).ReasonCode);
    }

    [Theory]
    [InlineData("not.a.registered.permission")]
    [InlineData("Purchasing Requests Approve")]
    public async Task An_unregistered_permission_name_is_dropped_and_the_refusal_is_still_written(string permission)
    {
        var writer = new CapturingWriter();
        var recorder = Recorder(writer, new FakeCurrentUser(UserActorId, CompanyA), company: CompanyA);

        var result = await recorder.RecordAsync(new AuthorizationDenialV1
        {
            ReasonCode = AuthorizationDenialReasonCodesV1.PermissionMissing,
            PermissionCode = permission,
        });

        Assert.True(result.Recorded);
        Assert.Null(Assert.Single(writer.Events).PermissionCode);
    }

    [Theory]
    [InlineData("credentials_invalid")]
    [InlineData("policy_refused")]
    [InlineData("")]
    public async Task A_reason_outside_the_closed_authz_list_is_not_written_and_raises_the_alarm(string reason)
    {
        using var metrics = new FailureMetricListener();
        var writer = new CapturingWriter();
        var recorder = Recorder(writer, new FakeCurrentUser(UserActorId, CompanyA), company: CompanyA);

        var result = await recorder.RecordAsync(new AuthorizationDenialV1 { ReasonCode = reason });

        Assert.False(result.Recorded);
        Assert.True(result.CallerMayContinue);
        Assert.Equal(SecurityEventWriteFailureMode.FailOpenWithAlarm, result.FailureMode);
        Assert.Equal(SecurityEventWriteFailureKindsV1.InvalidEvent, result.FailureKind);
        Assert.Empty(writer.Events);
        Assert.Contains(metrics.Snapshot(), m => m.EventType == SecurityEventTypesV1.AuthorizationDenied
            && m.FailureKind == SecurityEventWriteFailureKindsV1.InvalidEvent);
    }

    [Theory]
    [InlineData("5E0C1D2A-7B3F-4C8E-9A10-000000000042")]
    [InlineData("company-a-user")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task A_user_id_that_is_not_the_canonical_internal_id_is_recorded_as_anonymous(string userId)
    {
        var writer = new CapturingWriter();
        var recorder = Recorder(writer, new FakeCurrentUser(userId, CompanyA), company: CompanyA);

        await recorder.RecordAsync(new AuthorizationDenialV1
        {
            ReasonCode = AuthorizationDenialReasonCodesV1.CompanyContextMismatch,
            RequestedCompanyId = CompanyB,
        });

        var written = Assert.Single(writer.Events);
        Assert.Empty(SecurityEventV1Validator.Validate(written));
        Assert.Equal(SecurityEventActorTypesV1.Anonymous, written.ActorType);
        Assert.Null(written.ActorId);
        Assert.Equal(SecurityEventScopeKindsV1.Platform, written.ScopeKind);
        Assert.Null(written.CompanyId);
        Assert.Null(written.RequestedCompanyId);
    }

    [Fact]
    public async Task A_request_without_a_resolved_correlation_is_not_written_and_raises_the_alarm()
    {
        using var metrics = new FailureMetricListener();
        var writer = new CapturingWriter();
        var recorder = new AuthorizationDenialRecorder(
            writer,
            new FakeCurrentUser(UserActorId, CompanyA),
            new CompanyContext(),
            new CorrelationContext(),
            ServerClock());

        var result = await recorder.RecordAsync(new AuthorizationDenialV1 { ReasonCode = AuthorizationDenialReasonCodesV1.SessionInvalid });

        Assert.False(result.Recorded);
        Assert.True(result.CallerMayContinue);
        Assert.Empty(writer.Events);
        Assert.Contains(metrics.Snapshot(), m => m.EventType == SecurityEventTypesV1.AuthorizationDenied
            && m.FailureKind == SecurityEventWriteFailureKindsV1.InvalidEvent);
    }

    [Fact]
    public async Task A_cancelled_write_does_not_throw_keeps_the_refusal_and_raises_the_alarm()
    {
        using var metrics = new FailureMetricListener();
        var recorder = Recorder(new CancellingWriter(), new FakeCurrentUser(UserActorId, CompanyA), company: CompanyA);

        var result = await recorder.RecordAsync(new AuthorizationDenialV1 { ReasonCode = AuthorizationDenialReasonCodesV1.PermissionMissing });

        Assert.False(result.Recorded);
        Assert.True(result.CallerMayContinue);
        Assert.Equal(SecurityEventWriteFailureKindsV1.StoreUnavailable, result.FailureKind);
        Assert.True(recorder.HasAttempted);
        Assert.Contains(metrics.Snapshot(), m => m.EventType == SecurityEventTypesV1.AuthorizationDenied
            && m.FailureMode == nameof(SecurityEventWriteFailureMode.FailOpenWithAlarm)
            && m.FailureKind == SecurityEventWriteFailureKindsV1.StoreUnavailable);
    }

    [Fact]
    public void The_authz_reason_list_is_closed_and_inside_the_security_event_reason_list()
    {
        Assert.Equal(
            new[]
            {
                SecurityEventReasonCodesV1.PermissionMissing,
                SecurityEventReasonCodesV1.CompanyContextMismatch,
                SecurityEventReasonCodesV1.SessionInvalid,
            },
            AuthorizationDenialReasonCodesV1.All.ToArray());
        Assert.All(AuthorizationDenialReasonCodesV1.All, reason => Assert.True(SecurityEventReasonCodesV1.IsDefined(reason)));
        Assert.Equal(
            SecurityEventWriteFailureMode.FailOpenWithAlarm,
            SecurityEventWriteFailureModesV1.Resolve(SecurityEventTypesV1.AuthorizationDenied, SecurityEventPhasesV1.Single));
    }

    private static AuthorizationDenialRecorder Recorder(ISecurityEventWriterV1 writer, ICurrentUser currentUser, Guid? company)
    {
        var companyContext = new CompanyContext();
        if (company is { } companyId)
        {
            companyContext.Set(companyId);
        }

        var correlationContext = new CorrelationContext();
        correlationContext.Set(CorrelationId, causationId: null, SourceChannels.Web, clientIpAddress: null, userAgent: null);
        return new AuthorizationDenialRecorder(writer, currentUser, companyContext, correlationContext, ServerClock());
    }

    private sealed class FakeCurrentUser(string? userId, params Guid[] companies) : ICurrentUser
    {
        public bool IsAuthenticated => userId is not null;

        public string? UserId => userId;

        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>(companies);

        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class CapturingWriter : ISecurityEventWriterV1
    {
        public List<SecurityEventV1> Events { get; } = [];

        public Task<SecurityEventWriteResultV1> WriteAsync(SecurityEventV1 securityEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(securityEvent);
            var failureMode = SecurityEventWriteFailureModesV1.Resolve(securityEvent.EventType, securityEvent.Phase);
            return Task.FromResult(SecurityEventV1Validator.IsValid(securityEvent)
                ? SecurityEventWriteResultV1.Written(failureMode)
                : SecurityEventWriteResultV1.NotWritten(failureMode, SecurityEventWriteFailureKindsV1.InvalidEvent));
        }
    }

    private sealed class CancellingWriter : ISecurityEventWriterV1
    {
        public Task<SecurityEventWriteResultV1> WriteAsync(SecurityEventV1 securityEvent, CancellationToken cancellationToken = default) =>
            throw new OperationCanceledException();
    }

    private sealed record FailureMeasurement(string? EventType, string? FailureMode, string? FailureKind);

    private sealed class FailureMetricListener : IDisposable
    {
        private readonly MeterListener listener = new();
        private readonly List<FailureMeasurement> measurements = [];
        private readonly Lock gate = new();

        public FailureMetricListener()
        {
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == SecurityEventWriteMetrics.MeterName
                    && instrument.Name == SecurityEventWriteMetrics.WriteFailureCounterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                string? eventType = null, failureMode = null, failureKind = null;
                foreach (var tag in tags)
                {
                    switch (tag.Key)
                    {
                        case SecurityEventWriteMetrics.EventTypeTag: eventType = tag.Value as string; break;
                        case SecurityEventWriteMetrics.FailureModeTag: failureMode = tag.Value as string; break;
                        case SecurityEventWriteMetrics.FailureKindTag: failureKind = tag.Value as string; break;
                    }
                }

                lock (gate)
                {
                    measurements.Add(new FailureMeasurement(eventType, failureMode, failureKind));
                }
            });
            listener.Start();
        }

        public IReadOnlyList<FailureMeasurement> Snapshot()
        {
            lock (gate)
            {
                return [.. measurements];
            }
        }

        public void Dispose() => listener.Dispose();
    }
}
