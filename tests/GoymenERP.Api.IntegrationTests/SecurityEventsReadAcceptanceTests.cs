using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Persistence;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Reading;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// AI-2b, ADR-SEC-001 section 5: GET /api/v1/security-events. The permission is registered but granted to no
/// shipped role; these cases hand it to a test-local role only. The read is recorded as security.events.read
/// before any row is read (fail-closed), only the resolved company's company-scoped rows are visible, and the
/// fresh authority fence releases nothing when the grant disappears during the read.
/// </summary>
public sealed class SecurityEventsReadAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private const string Path = "/api/v1/security-events";
    private static readonly Guid ReaderRoleId = Guid.Parse("5ec0e7a1-0b2c-4d6e-8f10-00000000a2b1");
    private static readonly DateTimeOffset SeededAt = new(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Without_the_grant_the_read_is_forbidden_and_not_recorded()
    {
        await PrepareReaderAsync();
        var correlationId = NewCorrelationId();
        using var client = Client(factory, TestUsers.CompanyB, TestCompanyIds.CompanyB, correlationId);

        var response = await client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await CountReadEventsAsync(TestCompanyIds.CompanyB, correlationId));
    }

    [Fact]
    public async Task The_read_is_recorded_before_the_rows_are_read()
    {
        var readerUserId = await PrepareReaderAsync();
        var correlationId = NewCorrelationId();
        using var client = Client(factory, TestUsers.CompanyA, TestCompanyIds.CompanyA, correlationId);

        // The filter selects this very request's correlation: the only row is the read event it wrote first.
        var response = await client.GetAsync($"{Path}?correlationId={correlationId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.True(response.Headers.CacheControl?.Private);
        var page = Assert.IsType<SecurityEventPageV1>(await response.Content.ReadFromJsonAsync<SecurityEventPageV1>());
        var readEvent = Assert.Single(page.Items);
        Assert.Equal(SecurityEventTypesV1.SecurityEventsRead, readEvent.EventType);
        Assert.Equal(SecurityEventOutcomesV1.Allowed, readEvent.Outcome);
        Assert.Equal(SecurityEventActorTypesV1.User, readEvent.ActorType);
        Assert.Equal(readerUserId.ToString("D"), readEvent.ActorId);
        Assert.Equal(ErpPermissions.PlatformSecurityEventsRead, readEvent.PermissionCode);
        Assert.Equal(TestCompanyIds.CompanyA, readEvent.CompanyId);
        Assert.Null(readEvent.ReasonCode);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task Only_the_companys_own_company_scoped_rows_are_paged()
    {
        await PrepareReaderAsync();
        var seededCorrelation = NewCorrelationId();
        var writer = factory.Services.GetRequiredService<ISecurityEventWriterV1>();
        for (var minute = 0; minute < 3; minute++)
        {
            Assert.True((await writer.WriteAsync(Denial(TestCompanyIds.CompanyA, seededCorrelation, minute))).Recorded);
        }

        Assert.True((await writer.WriteAsync(Denial(TestCompanyIds.CompanyB, seededCorrelation, 5))).Recorded);
        Assert.True((await writer.WriteAsync(PlatformLoginFailure(seededCorrelation))).Recorded);
        using var client = Client(factory, TestUsers.CompanyA, TestCompanyIds.CompanyA, NewCorrelationId());

        var first = await client.GetFromJsonAsync<SecurityEventPageV1>(
            $"{Path}?pageSize=2&correlationId={seededCorrelation}");
        Assert.NotNull(first);
        Assert.Equal(2, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        var second = await client.GetFromJsonAsync<SecurityEventPageV1>(
            $"{Path}?pageSize=2&correlationId={seededCorrelation}&cursor={first.NextCursor}");
        Assert.NotNull(second);
        Assert.Null(second.NextCursor);

        var all = first.Items.Concat(second.Items).ToList();
        Assert.Equal(3, all.Count);
        Assert.All(all, item =>
        {
            Assert.Equal(TestCompanyIds.CompanyA, item.CompanyId);
            Assert.Equal(SecurityEventTypesV1.AuthorizationDenied, item.EventType);
            Assert.Equal(64, item.IntegrityHash.Length);
        });
        Assert.Equal(
            new[] { SeededAt.AddMinutes(2), SeededAt.AddMinutes(1), SeededAt },
            all.Select(item => item.OccurredAtUtc));
        Assert.Equal(all.Count, all.Select(item => item.Id).Distinct().Count());

        var byType = await client.GetFromJsonAsync<SecurityEventPageV1>(
            $"{Path}?correlationId={seededCorrelation}&eventType={SecurityEventTypesV1.LoginFailed}");
        Assert.NotNull(byType);
        Assert.Empty(byType.Items);
    }

    [Fact]
    public async Task A_grant_revoked_during_the_read_releases_nothing()
    {
        await PrepareReaderAsync();
        var correlationId = NewCorrelationId();
        var authority = new SequencedAuthority(denyAtCall: 2);
        using var host = WithServices(services =>
        {
            services.RemoveAll<IErpAuthorityRevalidationService>();
            services.AddSingleton<IErpAuthorityRevalidationService>(authority);
        });
        using var client = Client(host, TestUsers.CompanyA, TestCompanyIds.CompanyA, correlationId);

        var response = await client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("forbidden", problem.RootElement.GetProperty("code").GetString());
        Assert.False(problem.RootElement.TryGetProperty("items", out _));
        Assert.False(problem.RootElement.TryGetProperty("nextCursor", out _));
        Assert.Equal(2, authority.FreshCalls);
        Assert.All(authority.Requests, request =>
        {
            Assert.Equal(ErpPermissions.PlatformSecurityEventsRead, request.Permission);
            Assert.Equal(TestCompanyIds.CompanyA, request.CompanyId);
        });
        // The read happened, so it stays recorded even though nothing was released.
        Assert.Equal(1, await CountReadEventsAsync(TestCompanyIds.CompanyA, correlationId));
    }

    [Fact]
    public async Task An_unrecordable_read_is_refused_with_503_before_any_row_is_read()
    {
        await PrepareReaderAsync();
        var authority = new SequencedAuthority();
        var writer = new RefusingWriter();
        using var host = WithServices(services =>
        {
            services.RemoveAll<IErpAuthorityRevalidationService>();
            services.AddSingleton<IErpAuthorityRevalidationService>(authority);
            services.RemoveAll<ISecurityEventWriterV1>();
            services.AddSingleton<ISecurityEventWriterV1>(writer);
        });
        using var client = Client(host, TestUsers.CompanyA, TestCompanyIds.CompanyA, NewCorrelationId());

        var response = await client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("service_unavailable", problem.RootElement.GetProperty("code").GetString());
        Assert.False(problem.RootElement.TryGetProperty("items", out _));
        var attempted = Assert.Single(writer.Attempts);
        Assert.Equal(SecurityEventTypesV1.SecurityEventsRead, attempted.EventType);
        // Only the opening authority check ran: the fence after the read was never reached.
        Assert.Equal(1, authority.FreshCalls);
    }

    [Theory]
    [InlineData("cursor=not-a-cursor")]
    [InlineData("eventType=unknown.event.type")]
    [InlineData("eventType=" + "a234567890123456789012345678901234567890123456789012345678901234567890")]
    public async Task An_invalid_cursor_or_filter_is_a_400_and_is_not_recorded(string query)
    {
        await PrepareReaderAsync();
        var correlationId = NewCorrelationId();
        using var client = Client(factory, TestUsers.CompanyA, TestCompanyIds.CompanyA, correlationId);

        var response = await client.GetAsync($"{Path}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountReadEventsAsync(TestCompanyIds.CompanyA, correlationId));
    }

    /// <summary>A test-local role holding the read permission, assigned company-wide to the Company A user.</summary>
    private async Task<Guid> PrepareReaderAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var readerUserId = await identity.ExternalIdentities
            .Where(external => external.Subject == TestUsers.CompanyA)
            .Select(external => external.UserId)
            .SingleAsync();
        if (await identity.Roles.AnyAsync(role => role.Id == ReaderRoleId))
        {
            return readerUserId;
        }

        var role = new RoleDefinition(
            ReaderRoleId,
            "TEST_SECURITY_EVENTS_READER",
            "Güvenlik Olayı Okuma Test Rolü",
            isSystem: false);
        role.AddPermission(ErpPermissions.PlatformSecurityEventsRead);
        var assignment = new RoleAssignment(
            Guid.NewGuid(),
            readerUserId,
            ReaderRoleId,
            TestCompanyIds.CompanyA,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            null,
            null);
        assignment.AddScope(AccessScopeType.Company, TestCompanyIds.CompanyA);
        identity.Roles.Add(role);
        identity.RoleAssignments.Add(assignment);
        await identity.SaveChangesAsync();
        return readerUserId;
    }

    private async Task<int> CountReadEventsAsync(Guid companyId, string correlationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<SecurityEventsDbContext>();
        return await dbContext.SecurityEvents.CountAsync(x =>
            x.EventType == SecurityEventTypesV1.SecurityEventsRead && x.CorrelationId == correlationId);
    }

    private WebApplicationFactory<Program> WithServices(Action<IServiceCollection> configure) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(configure));

    private static HttpClient Client(
        WebApplicationFactory<Program> host,
        string subject,
        Guid companyId,
        string correlationId)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", subject);
        client.DefaultRequestHeaders.Add("X-Company-Id", companyId.ToString("D"));
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);
        return client;
    }

    private static string NewCorrelationId() => $"se-read-{Guid.NewGuid():N}";

    private static SecurityEventV1 Denial(Guid companyId, string correlationId, int minute) => new()
    {
        EventId = Guid.NewGuid(),
        OccurredAtUtc = SeededAt.AddMinutes(minute),
        ScopeKind = SecurityEventScopeKindsV1.Company,
        CompanyId = companyId,
        ActorType = SecurityEventActorTypesV1.User,
        ActorId = "7c4f2b1e-3d5a-4e6f-8a9b-00000000a2b1",
        EventType = SecurityEventTypesV1.AuthorizationDenied,
        Outcome = SecurityEventOutcomesV1.Denied,
        ReasonCode = SecurityEventReasonCodesV1.PermissionMissing,
        Severity = SecurityEventSeveritiesV1.Warning,
        PermissionCode = "purchasing.requests.approve",
        CorrelationId = correlationId,
        SourceChannel = SourceChannels.Web,
    };

    private static SecurityEventV1 PlatformLoginFailure(string correlationId) => new()
    {
        EventId = Guid.NewGuid(),
        OccurredAtUtc = SeededAt.AddMinutes(4),
        ScopeKind = SecurityEventScopeKindsV1.Platform,
        RequestedCompanyId = TestCompanyIds.CompanyA,
        ActorType = SecurityEventActorTypesV1.Anonymous,
        EventType = SecurityEventTypesV1.LoginFailed,
        Outcome = SecurityEventOutcomesV1.Denied,
        ReasonCode = SecurityEventReasonCodesV1.CredentialsInvalid,
        Severity = SecurityEventSeveritiesV1.Warning,
        CorrelationId = correlationId,
        SourceChannel = SourceChannels.Web,
    };

    private sealed class SequencedAuthority(int? denyAtCall = null) : IErpAuthorityRevalidationService
    {
        public int FreshCalls { get; private set; }

        public List<(string Permission, Guid CompanyId)> Requests { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FreshCalls++;
            Requests.Add((permission, companyId));
            return Task.FromResult(denyAtCall == FreshCalls
                ? PermissionScope.Denied
                : new PermissionScope(
                    true,
                    true,
                    new HashSet<string>(StringComparer.Ordinal),
                    new HashSet<Guid>(),
                    new HashSet<Guid>(),
                    new HashSet<Guid>()));
        }
    }

    private sealed class RefusingWriter : ISecurityEventWriterV1
    {
        public List<SecurityEventV1> Attempts { get; } = [];

        public Task<SecurityEventWriteResultV1> WriteAsync(
            SecurityEventV1 securityEvent,
            CancellationToken cancellationToken = default)
        {
            Attempts.Add(securityEvent);
            return Task.FromResult(SecurityEventWriteResultV1.NotWritten(
                SecurityEventWriteFailureMode.FailClosed,
                SecurityEventWriteFailureKindsV1.StoreUnavailable));
        }
    }
}
