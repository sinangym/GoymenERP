using System.Net;
using System.Text.Json;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Reading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F023-2 / KIM-D10: every authorization refusal of the host (401, 403 of a policy, an invalid or foreign company
/// context, a 403 answered by an endpoint fence) is exactly one <c>authz.denied</c> security event, and the refusal
/// response itself is unchanged. The security event writer is replaced by a recording one so platform rows, which the
/// company read filter hides, are visible to the assertions.
/// </summary>
public sealed class AuthorizationDenialSecurityEventTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private const string Path = "/api/v1/security-events";

    [Fact]
    public async Task An_anonymous_request_is_401_and_one_anonymous_platform_session_invalid_row()
    {
        var writer = new RecordingWriter();
        using var host = WithServices(writer);
        var correlationId = NewCorrelationId();
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyB.ToString("D"));

        using var response = await client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var denial = Assert.Single(writer.Denials(correlationId));
        Assert.Empty(SecurityEventV1Validator.Validate(denial));
        Assert.Equal(SecurityEventReasonCodesV1.SessionInvalid, denial.ReasonCode);
        Assert.Equal(SecurityEventOutcomesV1.Denied, denial.Outcome);
        Assert.Equal(SecurityEventActorTypesV1.Anonymous, denial.ActorType);
        Assert.Null(denial.ActorId);
        Assert.Equal(SecurityEventScopeKindsV1.Platform, denial.ScopeKind);
        Assert.Null(denial.CompanyId);
        Assert.Null(denial.RequestedCompanyId);
        Assert.Null(denial.PermissionCode);
    }

    [Fact]
    public async Task A_policy_permission_refusal_is_403_and_one_company_row_with_the_permission()
    {
        var writer = new RecordingWriter();
        using var host = WithServices(writer);
        var correlationId = NewCorrelationId();
        using var client = Client(host, TestUsers.CompanyB, TestCompanyIds.CompanyB.ToString("D"), correlationId);

        using var response = await client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var denial = Assert.Single(writer.Denials(correlationId));
        Assert.Empty(SecurityEventV1Validator.Validate(denial));
        Assert.Equal(SecurityEventReasonCodesV1.PermissionMissing, denial.ReasonCode);
        Assert.Equal(ErpPermissions.PlatformSecurityEventsRead, denial.PermissionCode);
        Assert.Equal(SecurityEventActorTypesV1.User, denial.ActorType);
        Assert.True(Guid.TryParseExact(denial.ActorId, "D", out _));
        Assert.Equal(SecurityEventScopeKindsV1.Company, denial.ScopeKind);
        Assert.Equal(TestCompanyIds.CompanyB, denial.CompanyId);
        Assert.Null(denial.RequestedCompanyId);
        Assert.DoesNotContain(writer.All(correlationId), e => e.EventType != SecurityEventTypesV1.AuthorizationDenied);
    }

    [Fact]
    public async Task A_foreign_company_header_is_403_and_one_platform_mismatch_row_that_keeps_the_requested_company()
    {
        var writer = new RecordingWriter();
        using var host = WithServices(writer);
        var correlationId = NewCorrelationId();
        using var client = Client(host, TestUsers.CompanyA, TestCompanyIds.CompanyB.ToString("D"), correlationId);

        using var response = await client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var denial = Assert.Single(writer.Denials(correlationId));
        Assert.Empty(SecurityEventV1Validator.Validate(denial));
        Assert.Equal(SecurityEventReasonCodesV1.CompanyContextMismatch, denial.ReasonCode);
        Assert.Equal(SecurityEventActorTypesV1.User, denial.ActorType);
        Assert.Equal(SecurityEventScopeKindsV1.Platform, denial.ScopeKind);
        Assert.Null(denial.CompanyId);
        Assert.Equal(TestCompanyIds.CompanyB, denial.RequestedCompanyId);
        Assert.Null(denial.PermissionCode);
    }

    [Fact]
    public async Task An_unreadable_company_header_is_400_company_context_invalid_and_one_mismatch_row_without_a_company()
    {
        var writer = new RecordingWriter();
        using var host = WithServices(writer);
        var correlationId = NewCorrelationId();
        using var client = Client(host, TestUsers.CompanyA, "not-a-company", correlationId);

        using var response = await client.GetAsync(Path);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "company_context_invalid");
        var denial = Assert.Single(writer.Denials(correlationId));
        Assert.Empty(SecurityEventV1Validator.Validate(denial));
        Assert.Equal(SecurityEventReasonCodesV1.CompanyContextMismatch, denial.ReasonCode);
        Assert.Equal(SecurityEventActorTypesV1.User, denial.ActorType);
        Assert.Equal(SecurityEventScopeKindsV1.Platform, denial.ScopeKind);
        Assert.Null(denial.CompanyId);
        Assert.Null(denial.RequestedCompanyId);
    }

    [Fact]
    public async Task A_403_answered_by_the_endpoint_fence_is_one_permission_missing_row_with_the_endpoint_permission()
    {
        var writer = new RecordingWriter();
        using var host = WithServices(writer, services =>
        {
            // Test-local: the policy passes for this one permission, then the read service's fresh fence refuses.
            services.AddSingleton<IAuthorizationHandler>(new GrantingRequirementHandler(ErpPermissions.PlatformSecurityEventsRead));
            services.RemoveAll<ISecurityEventQueryServiceV1>();
            services.AddScoped<ISecurityEventQueryServiceV1, DenyingQueryService>();
        });
        var correlationId = NewCorrelationId();
        using var client = Client(host, TestUsers.CompanyA, TestCompanyIds.CompanyA.ToString("D"), correlationId);

        using var response = await client.GetAsync(Path);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, ErpProblemCodes.Forbidden);
        var denial = Assert.Single(writer.Denials(correlationId));
        Assert.Empty(SecurityEventV1Validator.Validate(denial));
        Assert.Equal(SecurityEventReasonCodesV1.PermissionMissing, denial.ReasonCode);
        Assert.Equal(ErpPermissions.PlatformSecurityEventsRead, denial.PermissionCode);
        Assert.Equal(SecurityEventScopeKindsV1.Company, denial.ScopeKind);
        Assert.Equal(TestCompanyIds.CompanyA, denial.CompanyId);
    }

    [Fact]
    public async Task An_authenticated_identity_without_an_internal_user_is_one_anonymous_session_invalid_row()
    {
        var writer = new RecordingWriter();
        using var host = WithServices(writer);
        var correlationId = NewCorrelationId();
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.Unprovisioned);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        using var response = await client.GetAsync("/api/v1/organization/companies");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var denial = Assert.Single(writer.Denials(correlationId));
        Assert.Empty(SecurityEventV1Validator.Validate(denial));
        Assert.Equal(SecurityEventReasonCodesV1.SessionInvalid, denial.ReasonCode);
        Assert.Equal(SecurityEventActorTypesV1.Anonymous, denial.ActorType);
        Assert.Null(denial.ActorId);
        Assert.Equal(SecurityEventScopeKindsV1.Platform, denial.ScopeKind);
        Assert.Null(denial.CompanyId);
        Assert.Null(denial.PermissionCode);
    }

    [Fact]
    public async Task An_allowed_request_writes_no_authz_denied_row()
    {
        var writer = new RecordingWriter();
        using var host = WithServices(writer);
        var correlationId = NewCorrelationId();
        using var client = Client(host, TestUsers.CompanyA, TestCompanyIds.CompanyA.ToString("D"), correlationId);

        using var response = await client.GetAsync("/api/v1/organization/companies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(writer.Denials(correlationId));
    }

    private WebApplicationFactory<Program> WithServices(RecordingWriter writer, Action<IServiceCollection>? configure = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISecurityEventWriterV1>();
            services.AddSingleton<ISecurityEventWriterV1>(writer);
            configure?.Invoke(services);
        }));

    private static HttpClient Client(WebApplicationFactory<Program> host, string subject, string companyHeader, string correlationId)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", subject);
        client.DefaultRequestHeaders.Add("X-Company-Id", companyHeader);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);
        return client;
    }

    private static string NewCorrelationId() => $"authz-denied-{Guid.NewGuid():N}";

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
    }

    private sealed class RecordingWriter : ISecurityEventWriterV1
    {
        private readonly List<SecurityEventV1> events = [];
        private readonly Lock gate = new();

        public Task<SecurityEventWriteResultV1> WriteAsync(SecurityEventV1 securityEvent, CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                events.Add(securityEvent);
            }

            return Task.FromResult(SecurityEventWriteResultV1.Written(
                SecurityEventWriteFailureModesV1.Resolve(securityEvent.EventType, securityEvent.Phase)));
        }

        public IReadOnlyList<SecurityEventV1> All(string correlationId)
        {
            lock (gate)
            {
                return [.. events.Where(e => e.CorrelationId == correlationId)];
            }
        }

        public IReadOnlyList<SecurityEventV1> Denials(string correlationId) =>
            [.. All(correlationId).Where(e => e.EventType == SecurityEventTypesV1.AuthorizationDenied)];
    }

    private sealed class GrantingRequirementHandler(string permission) : AuthorizationHandler<ErpPermissionRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ErpPermissionRequirement requirement)
        {
            if (string.Equals(requirement.Permission, permission, StringComparison.Ordinal))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class DenyingQueryService : ISecurityEventQueryServiceV1
    {
        public Task<SecurityEventQueryResultV1> GetEventsAsync(
            int pageSize,
            string? cursor,
            string? eventType,
            string? correlationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SecurityEventQueryResultV1.Denied);
    }
}
