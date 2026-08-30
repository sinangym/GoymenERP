using System.Net;
using System.Net.Http.Json;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Idempotency.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class IdempotencyHeaderBoundaryAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    public static TheoryData<string[]> InvalidIdempotencyHeaderValues => new()
    {
        new[] { string.Empty },
        new[] { "   " },
        new[] { new string('a', 129) },
        new[] { $" {new string('b', 129)} " },
        new[] { "idempotency-a,idempotency-b" },
        new[] { "idempotency-a, idempotency-b" },
        new[]
        {
            $"idempotency-a-{Guid.NewGuid():N}",
            $"idempotency-b-{Guid.NewGuid():N}",
        },
    };

    public static TheoryData<string> ExistingOpaqueSingleValues => new()
    {
        "idempotency+/=~!opaque",
        "idempotency-\u007f-control",
        "idempotency-ğ-unicode",
        "idempotency with internal space",
    };

    [Theory]
    [MemberData(nameof(InvalidIdempotencyHeaderValues))]
    public async Task InvalidCanonicalOrMultipleIdempotencyKey_IsRejectedBeforeEffects(
        string[] invalidIdempotencyKeys)
    {
        var correlationId = $"idem-header-reject-{Guid.NewGuid():N}";
        var before = await CaptureEffectsAsync("OSM-H1");

        using var response = await SendCommandAsync(
            before.Location.Id,
            new SetLocationActiveState(
                !before.Location.IsActive,
                before.Location.Version,
                "Geçersiz idempotency üstbilgisi kabul testi"),
            invalidIdempotencyKeys,
            correlationId);
        var after = await CaptureEffectsAsync("OSM-H1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("Idempotency-Replayed"));
        Assert.False(response.Headers.Contains("Idempotency-Original-Correlation-Id"));
        Assert.Equal(before.Location, after.Location);
        Assert.Equal(before.AuditCount, after.AuditCount);
        Assert.Equal(before.CommandExecutionCount, after.CommandExecutionCount);
        Assert.Empty(await factory.GetAuditEventsIgnoringFiltersAsync(correlationId));
    }

    [Theory]
    [MemberData(nameof(ExistingOpaqueSingleValues))]
    public async Task ExistingOpaqueSingleValue_IsAcceptedAndSafelyReplayed(string idempotencyKey)
    {
        var firstCorrelationId = $"idem-header-opaque-first-{Guid.NewGuid():N}";
        var replayCorrelationId = $"idem-header-opaque-replay-{Guid.NewGuid():N}";
        var before = await CaptureEffectsAsync("OSM-H0");
        var command = new SetLocationActiveState(
            !before.Location.IsActive,
            before.Location.Version,
            "Opaque idempotency üstbilgisi uyumluluk testi");

        using var firstResponse = await SendCommandAsync(
            before.Location.Id,
            command,
            [idempotencyKey],
            firstCorrelationId);
        using var replayResponse = await SendCommandAsync(
            before.Location.Id,
            command,
            [idempotencyKey],
            replayCorrelationId);
        var after = await CaptureEffectsAsync("OSM-H0");

        Assert.Equal(HttpStatusCode.NoContent, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, replayResponse.StatusCode);
        Assert.Equal(
            "true",
            Assert.Single(replayResponse.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal(
            firstCorrelationId,
            Assert.Single(replayResponse.Headers.GetValues(
                "Idempotency-Original-Correlation-Id")));
        Assert.Equal(!before.Location.IsActive, after.Location.IsActive);
        Assert.Equal(before.Location.Version + 1, after.Location.Version);
        Assert.Equal(before.AuditCount + 1, after.AuditCount);
        Assert.Equal(before.CommandExecutionCount + 1, after.CommandExecutionCount);
        Assert.Single(await factory.GetAuditEventsIgnoringFiltersAsync(firstCorrelationId));
        Assert.Empty(await factory.GetAuditEventsIgnoringFiltersAsync(replayCorrelationId));
        Assert.Single(await factory.GetCommandExecutionsAsync(idempotencyKey));
    }

    [Fact]
    public async Task PaddedMaximumCanonicalKey_ReplaysWithUnpaddedEquivalent()
    {
        var canonicalKey = new string('A', 128);
        var paddedKey = $" {canonicalKey} ";
        var firstCorrelationId = $"idem-header-padded-first-{Guid.NewGuid():N}";
        var replayCorrelationId = $"idem-header-padded-replay-{Guid.NewGuid():N}";
        var before = await CaptureEffectsAsync("OSM-H2");
        var command = new SetLocationActiveState(
            !before.Location.IsActive,
            before.Location.Version,
            "Canonical idempotency eşdeğerliği kabul testi");

        using var firstResponse = await SendCommandAsync(
            before.Location.Id,
            command,
            [paddedKey],
            firstCorrelationId);
        using var replayResponse = await SendCommandAsync(
            before.Location.Id,
            command,
            [canonicalKey],
            replayCorrelationId);
        var after = await CaptureEffectsAsync("OSM-H2");

        Assert.Equal(130, paddedKey.Length);
        Assert.Equal(128, canonicalKey.Length);
        Assert.Equal(HttpStatusCode.NoContent, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, replayResponse.StatusCode);
        Assert.Equal(
            "true",
            Assert.Single(replayResponse.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal(
            firstCorrelationId,
            Assert.Single(replayResponse.Headers.GetValues(
                "Idempotency-Original-Correlation-Id")));
        Assert.Equal(!before.Location.IsActive, after.Location.IsActive);
        Assert.Equal(before.Location.Version + 1, after.Location.Version);
        Assert.Equal(before.AuditCount + 1, after.AuditCount);
        Assert.Equal(before.CommandExecutionCount + 1, after.CommandExecutionCount);
        Assert.Single(await factory.GetAuditEventsIgnoringFiltersAsync(firstCorrelationId));
        Assert.Empty(await factory.GetAuditEventsIgnoringFiltersAsync(replayCorrelationId));
        Assert.Single(await factory.GetCommandExecutionsAsync(canonicalKey));
    }

    private async Task<HttpResponseMessage> SendCommandAsync(
        Guid locationId,
        SetLocationActiveState command,
        string[] idempotencyKeys,
        string correlationId)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/organization/locations/{locationId}/active-state")
        {
            Content = JsonContent.Create(command),
        };
        Assert.True(request.Headers.TryAddWithoutValidation(
            "X-Development-User",
            TestUsers.CompanyA));
        Assert.True(request.Headers.TryAddWithoutValidation(
            "X-Company-Id",
            TestCompanyIds.CompanyA.ToString()));
        Assert.True(request.Headers.TryAddWithoutValidation(
            "X-Correlation-Id",
            correlationId));
        Assert.True(request.Headers.TryAddWithoutValidation(
            "Idempotency-Key",
            idempotencyKeys));

        return await client.SendAsync(request);
    }

    private async Task<EffectSnapshot> CaptureEffectsAsync(string locationCode)
    {
        var location = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyA,
            locationCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var auditDbContext = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var auditCount = await auditDbContext.AuditEvents
            .IgnoreQueryFilters()
            .CountAsync(auditEvent =>
                auditEvent.CompanyId == TestCompanyIds.CompanyA
                && auditEvent.EntityId == location.Id.ToString("D"));
        var commandExecutionDbContext = scope.ServiceProvider
            .GetRequiredService<CommandExecutionDbContext>();
        var commandExecutionCount = await commandExecutionDbContext.CommandExecutions
            .IgnoreQueryFilters()
            .CountAsync(execution => execution.CompanyId == TestCompanyIds.CompanyA);

        return new EffectSnapshot(location, auditCount, commandExecutionCount);
    }

    private sealed record EffectSnapshot(
        LocationSnapshot Location,
        int AuditCount,
        int CommandExecutionCount);
}
