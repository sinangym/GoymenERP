using System.Net;
using System.Net.Http.Json;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class IdempotencyAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    [Fact]
    public async Task SameKeyAndPayload_ReplaysPriorResultWithoutSecondBusinessOrAuditEvent()
    {
        const string key = "idem-replay-same-payload";
        var firstCorrelation = $"idem-first-{Guid.NewGuid():N}";
        var replayCorrelation = $"idem-replay-{Guid.NewGuid():N}";
        var before = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyA,
            "OSM-H7");
        var command = new SetLocationActiveState(
            !before.IsActive,
            before.Version,
            "Ağ tekrarı idempotency kabul testi");

        using var firstClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        SetRequestIdentity(firstClient, key, firstCorrelation);
        var first = await firstClient.PutAsJsonAsync(
            $"/api/v1/organization/locations/{before.Id}/active-state",
            command);

        using var replayClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        SetRequestIdentity(replayClient, key, replayCorrelation);
        var replay = await replayClient.PutAsJsonAsync(
            $"/api/v1/organization/locations/{before.Id}/active-state",
            command);
        var after = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyA,
            "OSM-H7");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal(
            firstCorrelation,
            Assert.Single(replay.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        Assert.Equal(before.Version + 1, after.Version);
        Assert.Equal(!before.IsActive, after.IsActive);
        Assert.Single(await factory.GetAuditEventsIgnoringFiltersAsync(firstCorrelation));
        Assert.Empty(await factory.GetAuditEventsIgnoringFiltersAsync(replayCorrelation));
        Assert.Single(await factory.GetCommandExecutionsAsync(key));
    }

    [Fact]
    public async Task SameKeyWithDifferentPayload_IsRejectedWithoutSecondMutation()
    {
        const string key = "idem-payload-conflict";
        var firstCorrelation = $"idem-payload-first-{Guid.NewGuid():N}";
        var conflictCorrelation = $"idem-payload-conflict-{Guid.NewGuid():N}";
        var before = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyA,
            "GAZ-DEP");
        using var firstClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        SetRequestIdentity(firstClient, key, firstCorrelation);
        var first = await firstClient.PutAsJsonAsync(
            $"/api/v1/organization/locations/{before.Id}/active-state",
            new SetLocationActiveState(
                !before.IsActive,
                before.Version,
                "İlk payload"));

        using var conflictClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        SetRequestIdentity(conflictClient, key, conflictCorrelation);
        var conflict = await conflictClient.PutAsJsonAsync(
            $"/api/v1/organization/locations/{before.Id}/active-state",
            new SetLocationActiveState(
                !before.IsActive,
                before.Version,
                "Farklı payload"));
        using var otherActorClient = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyA);
        SetRequestIdentity(
            otherActorClient,
            key,
            $"idem-actor-conflict-{Guid.NewGuid():N}");
        var otherActorConflict = await otherActorClient.PutAsJsonAsync(
            $"/api/v1/organization/locations/{before.Id}/active-state",
            new SetLocationActiveState(
                !before.IsActive,
                before.Version,
                "İlk payload"));
        var after = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyA,
            "GAZ-DEP");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, otherActorConflict.StatusCode);
        Assert.False(conflict.Headers.Contains("Idempotency-Replayed"));
        Assert.Equal(before.Version + 1, after.Version);
        Assert.Empty(await factory.GetAuditEventsIgnoringFiltersAsync(conflictCorrelation));
        Assert.Single(await factory.GetCommandExecutionsAsync(key));
    }

    [Fact]
    public async Task VersionConflict_IsPersistedAndSafelyReplayed()
    {
        const string key = "idem-version-conflict-replay";
        var firstCorrelation = $"idem-version-first-{Guid.NewGuid():N}";
        var replayCorrelation = $"idem-version-replay-{Guid.NewGuid():N}";
        var location = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyA,
            "EXT-BASTUG");
        var command = new SetLocationActiveState(
            !location.IsActive,
            location.Version + 10,
            "Version conflict replay testi");

        using var firstClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        SetRequestIdentity(firstClient, key, firstCorrelation);
        var first = await firstClient.PutAsJsonAsync(
            $"/api/v1/organization/locations/{location.Id}/active-state",
            command);
        using var replayClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        SetRequestIdentity(replayClient, key, replayCorrelation);
        var replay = await replayClient.PutAsJsonAsync(
            $"/api/v1/organization/locations/{location.Id}/active-state",
            command);
        var after = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyA,
            "EXT-BASTUG");

        Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal(location, after);
        Assert.Empty(await factory.GetAuditEventsIgnoringFiltersAsync(firstCorrelation));
        Assert.Empty(await factory.GetAuditEventsIgnoringFiltersAsync(replayCorrelation));
        Assert.Single(await factory.GetCommandExecutionsAsync(key));
    }

    [Fact]
    public async Task NotFound_IsPersistedAndSafelyReplayed()
    {
        const string key = "idem-not-found-replay";
        var missingLocationId = Guid.NewGuid();
        var firstCorrelation = $"idem-not-found-first-{Guid.NewGuid():N}";
        var replayCorrelation = $"idem-not-found-replay-{Guid.NewGuid():N}";
        var command = new SetLocationActiveState(
            false,
            1,
            "Bulunamayan kayıt replay testi");

        using var firstClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        SetRequestIdentity(firstClient, key, firstCorrelation);
        var first = await firstClient.PutAsJsonAsync(
            $"/api/v1/organization/locations/{missingLocationId}/active-state",
            command);
        using var replayClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        SetRequestIdentity(replayClient, key, replayCorrelation);
        var replay = await replayClient.PutAsJsonAsync(
            $"/api/v1/organization/locations/{missingLocationId}/active-state",
            command);

        Assert.Equal(HttpStatusCode.NotFound, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, replay.StatusCode);
        Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal(
            firstCorrelation,
            Assert.Single(replay.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        Assert.Empty(await factory.GetAuditEventsIgnoringFiltersAsync(firstCorrelation));
        Assert.Empty(await factory.GetAuditEventsIgnoringFiltersAsync(replayCorrelation));
        Assert.Single(await factory.GetCommandExecutionsAsync(key));
    }

    [Fact]
    public async Task StaleExpectedVersionWithNewKey_DoesNotCreateSecondBusinessEvent()
    {
        var before = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyA,
            "EXT-SELIMOGLU");
        var firstCorrelation = $"concurrency-first-{Guid.NewGuid():N}";
        var staleCorrelation = $"concurrency-stale-{Guid.NewGuid():N}";
        using var firstClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        SetRequestIdentity(firstClient, $"concurrency-first-{Guid.NewGuid():N}", firstCorrelation);
        var first = await firstClient.PutAsJsonAsync(
            $"/api/v1/organization/locations/{before.Id}/active-state",
            new SetLocationActiveState(
                !before.IsActive,
                before.Version,
                "İlk optimistic concurrency işlemi"));
        using var staleClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        SetRequestIdentity(staleClient, $"concurrency-stale-{Guid.NewGuid():N}", staleCorrelation);
        var stale = await staleClient.PutAsJsonAsync(
            $"/api/v1/organization/locations/{before.Id}/active-state",
            new SetLocationActiveState(
                before.IsActive,
                before.Version,
                "Stale optimistic concurrency işlemi"));
        var after = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyA,
            "EXT-SELIMOGLU");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(before.Version + 1, after.Version);
        Assert.Single(await factory.GetAuditEventsIgnoringFiltersAsync(firstCorrelation));
        Assert.Empty(await factory.GetAuditEventsIgnoringFiltersAsync(staleCorrelation));
    }

    [Fact]
    public async Task SameKey_IsIsolatedByCompany()
    {
        const string key = "idem-company-isolation";
        var companyABefore = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyA,
            "EXT-DOGRULUK");
        var companyBBefore = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyB,
            "EXT-DOGRULUK");
        using var companyAClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        using var companyBClient = factory.CreateClientFor(
            TestUsers.CompanyB,
            TestCompanyIds.CompanyB);
        SetRequestIdentity(companyAClient, key, $"idem-company-a-{Guid.NewGuid():N}");
        SetRequestIdentity(companyBClient, key, $"idem-company-b-{Guid.NewGuid():N}");

        var responses = await Task.WhenAll(
            companyAClient.PutAsJsonAsync(
                $"/api/v1/organization/locations/{companyABefore.Id}/active-state",
                new SetLocationActiveState(
                    !companyABefore.IsActive,
                    companyABefore.Version,
                    "Şirket A idempotency izolasyonu")),
            companyBClient.PutAsJsonAsync(
                $"/api/v1/organization/locations/{companyBBefore.Id}/active-state",
                new SetLocationActiveState(
                    !companyBBefore.IsActive,
                    companyBBefore.Version,
                    "Şirket B idempotency izolasyonu")));
        var executions = await factory.GetCommandExecutionsAsync(key);

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NoContent, response.StatusCode));
        Assert.Equal(2, executions.Count);
        Assert.Contains(executions, execution => execution.CompanyId == TestCompanyIds.CompanyA);
        Assert.Contains(executions, execution => execution.CompanyId == TestCompanyIds.CompanyB);
    }

    private static void SetRequestIdentity(
        HttpClient client,
        string idempotencyKey,
        string correlationId)
    {
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
        client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);
    }
}
