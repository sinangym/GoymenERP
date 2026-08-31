using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Organization.Application.Contracts;

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
}
