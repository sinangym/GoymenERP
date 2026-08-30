using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Observability;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CorrelationBoundaryAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    public static TheoryData<string[]> InvalidCorrelationHeaderValues => new()
    {
        new[] { "   " },
        new[] { " correlation-padded " },
        new[] { " correlation-leading" },
        new[] { "correlation-trailing " },
        new[] { "\tcorrelation-tab-leading" },
        new[] { "correlation-tab-trailing\t" },
        new[] { "\u007fcorrelation-control-leading" },
        new[] { "correlation-control-trailing\u007f" },
        new[] { "\u2003correlation-unicode-whitespace" },
        new[] { "correlation-unicode-whitespace\u2003" },
        new[] { "\u00a0correlation-nbsp" },
        new[] { "correlation-nbsp\u00a0" },
        new[] { new string('a', 129) },
        new[] { "correlation-ğ" },
        new[] { "correlation/unsafe" },
        new[] { "correlation-a,correlation-b" },
        new[]
        {
            $"correlation-a-{Guid.NewGuid():N}",
            $"correlation-b-{Guid.NewGuid():N}",
        },
    };

    public static TheoryData<string[]> InvalidCausationHeaderValues => new()
    {
        new[] { "   " },
        new[] { " causation-padded " },
        new[] { " causation-leading" },
        new[] { "causation-trailing " },
        new[] { "\tcausation-tab-leading" },
        new[] { "causation-tab-trailing\t" },
        new[] { "\u007fcausation-control-leading" },
        new[] { "causation-control-trailing\u007f" },
        new[] { "\u2003causation-unicode-whitespace" },
        new[] { "causation-unicode-whitespace\u2003" },
        new[] { "\u00a0causation-nbsp" },
        new[] { "causation-nbsp\u00a0" },
        new[] { new string('b', 129) },
        new[] { "causation-ğ" },
        new[] { "causation/unsafe" },
        new[] { "causation-a,causation-b" },
        new[]
        {
            $"cause-a-{Guid.NewGuid():N}",
            $"cause-b-{Guid.NewGuid():N}",
        },
    };

    public static IEnumerable<object[]> ApplicationReachableControlIdentifierValues()
    {
        yield return new object[] { CorrelationContextMiddleware.CorrelationHeaderName, "\r" };
        yield return new object[] { CorrelationContextMiddleware.CorrelationHeaderName, "\n" };
        yield return new object[] { CorrelationContextMiddleware.CorrelationHeaderName, "\r\n" };
        yield return new object[] { CorrelationContextMiddleware.CorrelationHeaderName, "\0" };
        yield return new object[] { CorrelationContextMiddleware.CausationHeaderName, "\r" };
        yield return new object[] { CorrelationContextMiddleware.CausationHeaderName, "\n" };
        yield return new object[] { CorrelationContextMiddleware.CausationHeaderName, "\r\n" };
        yield return new object[] { CorrelationContextMiddleware.CausationHeaderName, "\0" };
    }

    [Fact]
    public async Task MissingCorrelation_GeneratesSafeServerIdentifier()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var correlationId = Assert.Single(
            response.Headers.GetValues(CorrelationContextMiddleware.CorrelationHeaderName));
        Assert.True(Guid.TryParseExact(correlationId, "N", out _));
        Assert.Equal(32, correlationId.Length);
        Assert.All(
            correlationId,
            character => Assert.True(char.IsAsciiHexDigit(character)));
    }

    [Fact]
    public async Task SingleCharacterSafeIdentifiers_AreAccepted()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            CorrelationContextMiddleware.CorrelationHeaderName,
            "A");
        client.DefaultRequestHeaders.Add(
            CorrelationContextMiddleware.CausationHeaderName,
            "B");

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "A",
            Assert.Single(response.Headers.GetValues(
                CorrelationContextMiddleware.CorrelationHeaderName)));
    }

    [Theory]
    [InlineData(CorrelationContextMiddleware.CorrelationHeaderName)]
    [InlineData(CorrelationContextMiddleware.CausationHeaderName)]
    public async Task PresentEmptyIdentifier_IsRejectedAtMiddlewareBoundary(
        string headerName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var nextCalled = false;
        var middleware = new CorrelationContextMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<CorrelationContextMiddleware>.Instance);
        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
        };
        httpContext.Response.Body = new MemoryStream();
        httpContext.Request.Headers[headerName] = string.Empty;
        var correlationContext = scope.ServiceProvider
            .GetRequiredService<ICorrelationContext>();
        Assert.True(httpContext.Request.Headers.TryGetValue(headerName, out var values));
        Assert.Single(values);
        Assert.Equal(string.Empty, values[0]);

        await middleware.InvokeAsync(httpContext, correlationContext);

        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        Assert.False(nextCalled);
        Assert.True(correlationContext.IsResolved);
        Assert.Null(correlationContext.CausationId);
        var generatedCorrelationId = Assert.Single(
            httpContext.Response.Headers[
                CorrelationContextMiddleware.CorrelationHeaderName]);
        Assert.True(Guid.TryParseExact(generatedCorrelationId, "N", out _));
        Assert.Equal(generatedCorrelationId, correlationContext.CorrelationId);
        httpContext.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(httpContext.Response.Body);
        Assert.Equal(
            ErpProblemCodes.CorrelationMetadataInvalid,
            document.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            generatedCorrelationId,
            document.RootElement.GetProperty("correlationId").GetString());
    }

    [Theory]
    [MemberData(nameof(ApplicationReachableControlIdentifierValues))]
    public async Task ApplicationReachableControlIdentifier_IsRejectedAtMiddlewareBoundary(
        string headerName,
        string invalidValue)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var nextCalled = false;
        var middleware = new CorrelationContextMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<CorrelationContextMiddleware>.Instance);
        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
        };
        httpContext.Response.Body = new MemoryStream();
        httpContext.Request.Headers[headerName] = invalidValue;
        if (headerName == CorrelationContextMiddleware.CausationHeaderName)
        {
            httpContext.Request.Headers[
                CorrelationContextMiddleware.CorrelationHeaderName] =
                    $"safe-correlation-{Guid.NewGuid():N}";
        }

        var correlationContext = scope.ServiceProvider
            .GetRequiredService<ICorrelationContext>();
        await middleware.InvokeAsync(httpContext, correlationContext);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        Assert.True(correlationContext.IsResolved);
        Assert.Null(correlationContext.CausationId);
        var generatedCorrelationId = Assert.Single(
            httpContext.Response.Headers[
                CorrelationContextMiddleware.CorrelationHeaderName]);
        Assert.True(Guid.TryParseExact(generatedCorrelationId, "N", out _));
        httpContext.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(httpContext.Response.Body);
        Assert.Equal(
            ErpProblemCodes.CorrelationMetadataInvalid,
            document.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            generatedCorrelationId,
            document.RootElement.GetProperty("correlationId").GetString());
        Assert.DoesNotContain(
            invalidValue,
            document.RootElement.GetRawText(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MaximumLengthSafeIdentifiers_AreAcceptedAndPropagatedToEffects()
    {
        var correlationId = $"{new string('A', 124)}-_.:";
        var causationId = $"{new string('B', 124)}-_.:";
        var idempotencyKey = $"correlation-boundary-{Guid.NewGuid():N}";
        var before = await CaptureEffectsAsync("OSM-H0", idempotencyKey);
        Assert.NotEqual(Guid.Empty, before.Location.Id);
        Assert.True(before.Location.Version > 0);
        Assert.Empty(before.CommandExecutions);
        using var client = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        client.DefaultRequestHeaders.Remove(
            CorrelationContextMiddleware.CorrelationHeaderName);
        client.DefaultRequestHeaders.Add(
            CorrelationContextMiddleware.CorrelationHeaderName,
            correlationId);
        client.DefaultRequestHeaders.Add(
            CorrelationContextMiddleware.CausationHeaderName,
            causationId);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/organization/locations/{before.Location.Id}/active-state",
            new SetLocationActiveState(
                !before.Location.IsActive,
                before.Location.Version,
                "128 karakter korelasyon sınırı kabul testi"));
        var after = await CaptureEffectsAsync("OSM-H0", idempotencyKey);

        Assert.Equal(128, correlationId.Length);
        Assert.Equal(128, causationId.Length);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            correlationId,
            Assert.Single(response.Headers.GetValues(
                CorrelationContextMiddleware.CorrelationHeaderName)));
        Assert.Equal(!before.Location.IsActive, after.Location.IsActive);
        Assert.Equal(before.Location.Version + 1, after.Location.Version);
        Assert.Equal(before.AuditCount + 1, after.AuditCount);
        var auditEvent = Assert.Single(
            await factory.GetAuditEventsIgnoringFiltersAsync(correlationId));
        Assert.Equal(correlationId, auditEvent.CorrelationId);
        Assert.Equal(causationId, auditEvent.CausationId);
        Assert.Single(after.CommandExecutions);
    }

    [Theory]
    [MemberData(nameof(InvalidCorrelationHeaderValues))]
    public async Task InvalidCorrelation_IsRejectedBeforeAnyPersistentEffect(
        string[] invalidCorrelationIds)
    {
        await AssertRejectedBeforeEffectsAsync(
            invalidCorrelationIds,
            causationIds: null);
    }

    [Theory]
    [MemberData(nameof(InvalidCausationHeaderValues))]
    public async Task InvalidOrMultipleCausation_IsRejectedBeforeAnyPersistentEffect(
        string[] invalidCausationIds)
    {
        await AssertRejectedBeforeEffectsAsync(
            [$"correlation-{Guid.NewGuid():N}"],
            invalidCausationIds);
    }

    [Fact]
    public async Task RejectedBoundaryRequest_DoesNotReserveIdempotencyKey()
    {
        var idempotencyKey = $"correlation-composition-{Guid.NewGuid():N}";
        var validCorrelationId = $"correlation-valid-{Guid.NewGuid():N}";
        var invalidCorrelationId = $" {validCorrelationId}";
        var before = await CaptureEffectsAsync("OSM-H2", idempotencyKey);
        var command = new SetLocationActiveState(
            !before.Location.IsActive,
            before.Location.Version,
            "Korelasyon ve idempotency bileşim kabul testi");

        using var invalidResponse = await SendCommandAsync(
            before.Location.Id,
            command,
            idempotencyKey,
            [invalidCorrelationId],
            causationIds: null);
        var afterInvalid = await CaptureEffectsAsync("OSM-H2", idempotencyKey);

        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.Equal(before.Location, afterInvalid.Location);
        Assert.Equal(before.AuditCount, afterInvalid.AuditCount);
        Assert.Empty(afterInvalid.CommandExecutions);
        Assert.Empty(
            await factory.GetAuditEventsIgnoringFiltersAsync(invalidCorrelationId));

        using var validResponse = await SendCommandAsync(
            before.Location.Id,
            command,
            idempotencyKey,
            [validCorrelationId],
            causationIds: null);
        var afterValid = await CaptureEffectsAsync("OSM-H2", idempotencyKey);

        Assert.Equal(HttpStatusCode.NoContent, validResponse.StatusCode);
        Assert.Equal(
            validCorrelationId,
            Assert.Single(validResponse.Headers.GetValues(
                CorrelationContextMiddleware.CorrelationHeaderName)));
        Assert.Equal(!before.Location.IsActive, afterValid.Location.IsActive);
        Assert.Equal(before.Location.Version + 1, afterValid.Location.Version);
        Assert.Equal(before.AuditCount + 1, afterValid.AuditCount);
        var auditEvent = Assert.Single(
            await factory.GetAuditEventsIgnoringFiltersAsync(validCorrelationId));
        Assert.Null(auditEvent.CausationId);
        var commandExecution = Assert.Single(afterValid.CommandExecutions);
        Assert.Equal(validCorrelationId, commandExecution.OriginalCorrelationId);
    }

    private async Task AssertRejectedBeforeEffectsAsync(
        string[] correlationIds,
        string[]? causationIds)
    {
        var idempotencyKey = $"correlation-rejection-{Guid.NewGuid():N}";
        var before = await CaptureEffectsAsync("OSM-H1", idempotencyKey);
        Assert.NotEqual(Guid.Empty, before.Location.Id);
        Assert.True(before.Location.Version > 0);
        Assert.Empty(before.CommandExecutions);
        using var response = await SendCommandAsync(
            before.Location.Id,
            new SetLocationActiveState(
                !before.Location.IsActive,
                before.Location.Version,
                "Geçersiz korelasyon sınırı kabul testi"),
            idempotencyKey,
            correlationIds,
            causationIds);
        var after = await CaptureEffectsAsync("OSM-H1", idempotencyKey);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before.Location, after.Location);
        Assert.Equal(before.AuditCount, after.AuditCount);
        Assert.Equal(before.CommandExecutions.Count, after.CommandExecutions.Count);
        Assert.Empty(after.CommandExecutions);
        foreach (var correlationId in correlationIds)
        {
            Assert.Empty(
                await factory.GetAuditEventsIgnoringFiltersAsync(correlationId));
        }
    }

    private async Task<HttpResponseMessage> SendCommandAsync(
        Guid locationId,
        SetLocationActiveState command,
        string idempotencyKey,
        string[] correlationIds,
        string[]? causationIds)
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
            "Idempotency-Key",
            idempotencyKey));
        Assert.True(request.Headers.TryAddWithoutValidation(
            CorrelationContextMiddleware.CorrelationHeaderName,
            correlationIds));
        if (causationIds is not null)
        {
            Assert.True(request.Headers.TryAddWithoutValidation(
                CorrelationContextMiddleware.CausationHeaderName,
                causationIds));
        }

        return await client.SendAsync(request);
    }

    private async Task<EffectSnapshot> CaptureEffectsAsync(
        string locationCode,
        string idempotencyKey)
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
        var commandExecutions = await factory.GetCommandExecutionsAsync(idempotencyKey);
        return new EffectSnapshot(location, auditCount, commandExecutions);
    }

    private sealed record EffectSnapshot(
        LocationSnapshot Location,
        int AuditCount,
        IReadOnlyList<CommandExecution> CommandExecutions);
}
