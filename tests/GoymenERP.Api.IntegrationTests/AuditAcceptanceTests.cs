using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class AuditAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    [Fact]
    public async Task CriticalCommand_ProducesCompleteCompanyScopedAuditEvent()
    {
        var correlationId = $"audit-{Guid.NewGuid():N}";
        var causationId = $"request-{Guid.NewGuid():N}";
        var location = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyA,
            "OSM-H3");
        using var client = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);
        client.DefaultRequestHeaders.Add("X-Causation-Id", causationId);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/organization/locations/{location.Id}/active-state",
            new SetLocationActiveState(
                !location.IsActive,
                location.Version,
                "  Planlı lokasyon bakımına hazırlık  "));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            correlationId,
            Assert.Single(response.Headers.GetValues("X-Correlation-Id")));
        var auditEvent = Assert.Single(
            await factory.GetAuditEventsIgnoringFiltersAsync(correlationId));
        Assert.Equal(TestCompanyIds.CompanyA, auditEvent.CompanyId);
        Assert.Equal(location.Id.ToString("D"), auditEvent.EntityId);
        Assert.Equal("Organization.Location", auditEvent.EntityType);
        Assert.Equal(
            "organization.location.active-state.changed.v1",
            auditEvent.EventType);
        Assert.Equal(
            ErpPermissions.OrganizationLocationsStatusUpdate,
            auditEvent.PermissionCode);
        Assert.Contains("ORGANIZATION_OPERATOR", auditEvent.RoleCodesJson, StringComparison.Ordinal);
        Assert.Equal("Planlı lokasyon bakımına hazırlık", auditEvent.Reason);
        Assert.Equal("Web", auditEvent.SourceChannel);
        Assert.Equal("None", auditEvent.AiInvolvement);
        Assert.Equal(correlationId, auditEvent.CorrelationId);
        Assert.Equal(causationId, auditEvent.CausationId);
        Assert.Equal(location.Version, auditEvent.EntityVersionBefore);
        Assert.Equal(location.Version + 1, auditEvent.EntityVersionAfter);
        Assert.Equal(64, auditEvent.IdempotencyKeyHash.Length);
        Assert.Equal(64, auditEvent.IntegrityHash.Length);
        Assert.Equal(
            AuditEventIntegrity.Compute(auditEvent.ToIntegrityEnvelope()),
            auditEvent.IntegrityHash);
        Assert.DoesNotContain("Idempotency-Key", auditEvent.IdempotencyKeyHash, StringComparison.Ordinal);

        using var before = JsonDocument.Parse(auditEvent.BeforeJson);
        using var after = JsonDocument.Parse(auditEvent.AfterJson);
        Assert.Equal(location.IsActive, before.RootElement.GetProperty("IsActive").GetBoolean());
        Assert.Equal(!location.IsActive, after.RootElement.GetProperty("IsActive").GetBoolean());
    }

    [Fact]
    public async Task SiteScopedAuditPermission_CannotReadCompanyWideAudit()
    {
        using var client = factory.CreateClientFor(
            TestUsers.SiteAuditReader,
            TestCompanyIds.CompanyA);

        var response = await client.GetAsync("/api/v1/audit/events");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AuditRead_RequiresPermissionAndKeepsSelectedCompanyBoundary()
    {
        using var operatorClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        var denied = await operatorClient.GetAsync("/api/v1/audit/events");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var companyACorrelationId = await CreateAuditedChangeAsync(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA,
            "OSM-H5");
        await CreateAuditedChangeAsync(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA,
            "OSM-H4");
        var companyBCorrelationId = await CreateAuditedChangeAsync(
            TestUsers.CompanyB,
            TestCompanyIds.CompanyB,
            "OSM-H5");
        using var adminClient = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyA);

        var companyAResult = await adminClient.GetFromJsonAsync<AuditEventPage>(
            $"/api/v1/audit/events?correlationId={companyACorrelationId}");
        var companyBProbe = await adminClient.GetFromJsonAsync<AuditEventPage>(
            $"/api/v1/audit/events?correlationId={companyBCorrelationId}");

        var companyAEvent = Assert.Single(
            Assert.IsType<AuditEventPage>(companyAResult).Items);
        Assert.Equal(TestCompanyIds.CompanyA, companyAEvent.CompanyId);
        Assert.Empty(Assert.IsType<AuditEventPage>(companyBProbe).Items);

        var firstPage = await adminClient.GetFromJsonAsync<AuditEventPage>(
            "/api/v1/audit/events?pageSize=1");
        var firstPageResult = Assert.IsType<AuditEventPage>(firstPage);
        Assert.Single(firstPageResult.Items);
        Assert.NotNull(firstPageResult.NextCursor);
        var secondPage = await adminClient.GetFromJsonAsync<AuditEventPage>(
            $"/api/v1/audit/events?pageSize=1&cursor={Uri.EscapeDataString(firstPageResult.NextCursor)}");
        Assert.NotEqual(
            Assert.Single(firstPageResult.Items).Id,
            Assert.Single(Assert.IsType<AuditEventPage>(secondPage).Items).Id);

        var invalidCursor = await adminClient.GetAsync(
            "/api/v1/audit/events?cursor=gecersiz-cursor");
        Assert.Equal(HttpStatusCode.BadRequest, invalidCursor.StatusCode);
        Assert.Equal(
            "application/problem+json",
            invalidCursor.Content.Headers.ContentType?.MediaType);
        using var invalidCursorProblem = JsonDocument.Parse(
            await invalidCursor.Content.ReadAsStringAsync());
        Assert.False(invalidCursorProblem.RootElement.TryGetProperty("code", out _));
    }

    // BLG-03, from Codex's parallel candidate 8e390339 (adapted: the refusal is a 403, not an empty page).
    [Fact]
    public async Task AuditRead_FinalFreshAuthorityDenial_RefusesWithoutEventsOrCursor()
    {
        await CreateAuditedChangeAsync(TestUsers.CompanyA, TestCompanyIds.CompanyA, "OSM-H5");
        await CreateAuditedChangeAsync(TestUsers.CompanyA, TestCompanyIds.CompanyA, "OSM-H4");
        var authority = new SequencedAuditAuthority(denyAtCall: 2);
        using var host = CreateAuditHost(authority);
        using var client = CreateAuditClient(host);

        var response = await client.GetAsync("/api/v1/audit/events?pageSize=1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("forbidden", problem.RootElement.GetProperty("code").GetString());
        Assert.False(problem.RootElement.TryGetProperty("items", out _));
        Assert.False(problem.RootElement.TryGetProperty("nextCursor", out _));
        Assert.Equal(2, authority.FreshCalls);
        Assert.All(authority.Requests, request =>
        {
            Assert.Equal(ErpPermissions.IdentityAuditRead, request.Permission);
            Assert.Equal(TestCompanyIds.CompanyA, request.CompanyId);
        });
    }

    [Fact]
    public async Task AuditRead_StableFreshAuthority_ReturnsExpectedPageAndCursor()
    {
        await CreateAuditedChangeAsync(TestUsers.CompanyA, TestCompanyIds.CompanyA, "OSM-H5");
        await CreateAuditedChangeAsync(TestUsers.CompanyA, TestCompanyIds.CompanyA, "OSM-H4");
        var authority = new SequencedAuditAuthority();
        using var host = CreateAuditHost(authority);
        using var client = CreateAuditClient(host);

        var response = await client.GetAsync("/api/v1/audit/events?pageSize=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = Assert.IsType<AuditEventPage>(await response.Content.ReadFromJsonAsync<AuditEventPage>());
        Assert.Single(result.Items);
        Assert.NotNull(result.NextCursor);
        Assert.Equal(2, authority.FreshCalls);
    }

    [Fact]
    public async Task MissingReasonAndInvalidCorrelation_AreRejectedBeforeMutation()
    {
        var location = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyA,
            "OSM-H6");
        using var missingReasonClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        var missingReason = await missingReasonClient.PutAsJsonAsync(
            $"/api/v1/organization/locations/{location.Id}/active-state",
            new SetLocationActiveState(!location.IsActive, location.Version, "  "));
        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);

        using var invalidCorrelationClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        invalidCorrelationClient.DefaultRequestHeaders.Remove("X-Correlation-Id");
        invalidCorrelationClient.DefaultRequestHeaders.Add(
            "X-Correlation-Id",
            "geçersiz korelasyon");
        var invalidCorrelation = await invalidCorrelationClient.PutAsJsonAsync(
            $"/api/v1/organization/locations/{location.Id}/active-state",
            new SetLocationActiveState(
                !location.IsActive,
                location.Version,
                "Geçersiz korelasyon testi"));
        var after = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyA,
            "OSM-H6");

        Assert.Equal(HttpStatusCode.BadRequest, invalidCorrelation.StatusCode);
        Assert.Equal(location, after);
    }

    // F023-5a: the optional outcome filter narrows the same company-scoped keyset; company B never sees
    // company A's refused attempts, and only the two stored outcomes are accepted (exact, ordinal).
    [Fact]
    public async Task AuditRead_OutcomeFilter_ReturnsOnlyThatOutcomeWithinTheSelectedCompany()
    {
        var correlationId = await CreateAuditedChangeAsync(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA,
            "OSM-H5");
        await RecordRefusedAttemptAsync(TestCompanyIds.CompanyA, correlationId);
        await RecordRefusedAttemptAsync(TestCompanyIds.CompanyA, correlationId);
        await RecordRefusedAttemptAsync(TestCompanyIds.CompanyB, correlationId);
        using var companyAClient = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
        using var companyBClient = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyB);
        var filter = $"correlationId={correlationId}";

        var all = await ReadPageAsync(companyAClient, $"/api/v1/audit/events?{filter}");
        Assert.Equal(3, all.Items.Count);
        var refused = await ReadPageAsync(companyAClient, $"/api/v1/audit/events?{filter}&outcome=Refused");
        Assert.Equal(2, refused.Items.Count);
        Assert.All(refused.Items, item =>
        {
            Assert.Equal(AuditOutcomes.Refused, item.Outcome);
            Assert.Equal(TestCompanyIds.CompanyA, item.CompanyId);
        });
        var succeeded = await ReadPageAsync(companyAClient, $"/api/v1/audit/events?{filter}&outcome=Succeeded");
        var succeededItem = Assert.Single(succeeded.Items);
        Assert.Equal(AuditOutcomes.Succeeded, succeededItem.Outcome);
        Assert.Equal(TestCompanyIds.CompanyA, succeededItem.CompanyId);
        var trimmed = await ReadPageAsync(
            companyAClient,
            $"/api/v1/audit/events?{filter}&outcome={Uri.EscapeDataString(" Refused ")}");
        Assert.Equal(
            refused.Items.Select(item => item.Id).ToArray(),
            trimmed.Items.Select(item => item.Id).ToArray());

        // The keyset cursor stays valid under the same filter and walks only refused rows.
        var firstPage = await ReadPageAsync(
            companyAClient,
            $"/api/v1/audit/events?{filter}&outcome=Refused&pageSize=1");
        var firstItem = Assert.Single(firstPage.Items);
        Assert.NotNull(firstPage.NextCursor);
        var secondPage = await ReadPageAsync(
            companyAClient,
            $"/api/v1/audit/events?{filter}&outcome=Refused&pageSize=1&cursor={Uri.EscapeDataString(firstPage.NextCursor)}");
        var secondItem = Assert.Single(secondPage.Items);
        Assert.Null(secondPage.NextCursor);
        Assert.Equal(AuditOutcomes.Refused, secondItem.Outcome);
        Assert.Equal(
            refused.Items.Select(item => item.Id).ToArray(),
            new[] { firstItem.Id, secondItem.Id });

        // Acceptance 4: company B reading Refused sees only its own row, none of company A's.
        var companyBRefused = await ReadPageAsync(companyBClient, $"/api/v1/audit/events?{filter}&outcome=Refused");
        var companyBItem = Assert.Single(companyBRefused.Items);
        Assert.Equal(TestCompanyIds.CompanyB, companyBItem.CompanyId);
        Assert.Equal(AuditOutcomes.Refused, companyBItem.Outcome);
        Assert.DoesNotContain(companyBItem.Id, all.Items.Select(item => item.Id));

        foreach (var invalid in new[] { "refused", "REFUSED", "Denied", new string('R', 33) })
        {
            using var response = await companyAClient.GetAsync(
                $"/api/v1/audit/events?{filter}&outcome={Uri.EscapeDataString(invalid)}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var body = await response.Content.ReadAsStringAsync();
            using var problem = JsonDocument.Parse(body);
            Assert.False(problem.RootElement.TryGetProperty("code", out _));
            Assert.False(problem.RootElement.TryGetProperty("items", out _));
            Assert.DoesNotContain(correlationId, body, StringComparison.Ordinal);
        }
    }

    private async Task<string> CreateAuditedChangeAsync(
        string userId,
        Guid companyId,
        string locationCode)
    {
        var correlationId = $"scope-{Guid.NewGuid():N}";
        var location = await factory.GetLocationIgnoringFiltersAsync(companyId, locationCode);
        using var client = factory.CreateClientFor(userId, companyId);
        client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);
        var response = await client.PutAsJsonAsync(
            $"/api/v1/organization/locations/{location.Id}/active-state",
            new SetLocationActiveState(
                !location.IsActive,
                location.Version,
                "Audit şirket kapsamı testi"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return correlationId;
    }

    private static async Task<AuditEventPage> ReadPageAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<AuditEventPage>(await response.Content.ReadFromJsonAsync<AuditEventPage>());
    }

    private async Task RecordRefusedAttemptAsync(Guid companyId, string correlationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
        var result = await scope.ServiceProvider.GetRequiredService<IRefusedAttemptRecorderV1>()
            .RecordStandaloneAsync(new RefusedAttemptV1
            {
                CompanyId = companyId,
                CommandName = "purchasing.purchase-request.approve.v1",
                Operation = "Approve",
                TargetType = "Purchasing.PurchaseRequest",
                TargetId = Guid.NewGuid().ToString("D"),
                ExpectedVersion = 3,
                ActorType = AuditActorTypes.User,
                ActorId = "f023-5a-outcome-filter-test",
                PermissionCode = "purchasing.requests.approve",
                RoleCodes = ["role-a"],
                HttpStatus = StatusCodes.Status409Conflict,
                ProblemCode = "approval_state_changed",
                RefusalClass = RefusedAttemptClassesV1.BusinessRule,
                CorrelationId = correlationId,
                SourceChannel = SourceChannels.Web,
                IdempotencyKey = $"idem-{Guid.NewGuid():N}",
            });
        Assert.True(result.Recorded, result.FailureKind);
    }

    private WebApplicationFactory<Program> CreateAuditHost(SequencedAuditAuthority authority) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IErpAuthorityRevalidationService>();
            services.AddSingleton<IErpAuthorityRevalidationService>(authority);
        }));

    private static HttpClient CreateAuditClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        return client;
    }

    private sealed class SequencedAuditAuthority(int? denyAtCall = null) : IErpAuthorityRevalidationService
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
}
