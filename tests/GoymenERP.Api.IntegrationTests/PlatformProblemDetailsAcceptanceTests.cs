using System.Net;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Errors;
using GoymenERP.BuildingBlocks.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class PlatformProblemDetailsAcceptanceTests(
    GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    [Fact]
    public async Task AnonymousProtectedRequest_ReturnsStableAuthenticationProblem()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/organization/companies");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var correlationId = Assert.Single(
            response.Headers.GetValues("X-Correlation-Id"));
        Assert.True(Guid.TryParseExact(correlationId, "N", out _));
        await AssertPlatformProblemAsync(
            response,
            ErpProblemCodes.AuthenticationRequired,
            correlationId);
    }

    [Fact]
    public async Task AuthenticatedIdentityWithoutErpProvisioning_ReturnsStableForbiddenProblem()
    {
        using var client = factory.CreateClientFor(TestUsers.Unprovisioned);
        var correlationId = Assert.Single(
            client.DefaultRequestHeaders.GetValues("X-Correlation-Id"));

        using var response = await client.GetAsync("/api/v1/organization/companies");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await AssertPlatformProblemAsync(
            response,
            ErpProblemCodes.Forbidden,
            correlationId);
        Assert.DoesNotContain(TestUsers.Unprovisioned, body, StringComparison.Ordinal);
        Assert.DoesNotContain("permission", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("role", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnauthorizedCompanySelection_UsesTheSameStableForbiddenProblem()
    {
        using var client = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyB);
        var correlationId = Assert.Single(
            client.DefaultRequestHeaders.GetValues("X-Correlation-Id"));

        using var response = await client.GetAsync("/api/v1/organization/locations");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await AssertPlatformProblemAsync(
            response,
            ErpProblemCodes.Forbidden,
            correlationId);
        Assert.DoesNotContain(
            TestCompanyIds.CompanyB.ToString(),
            body,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(
        StatusCodes.Status500InternalServerError,
        ErpProblemCodes.InternalError)]
    [InlineData(
        StatusCodes.Status503ServiceUnavailable,
        ErpProblemCodes.ServiceUnavailable)]
    public async Task PlatformFailures_AreRedactedAndCarryStableCode(
        int statusCode,
        string expectedCode)
    {
        const string secret = "Server=secret;Password=do-not-leak";
        var correlationId = Guid.NewGuid().ToString("N");
        await using var scope = factory.Services.CreateAsyncScope();
        var correlationContext = scope.ServiceProvider
            .GetRequiredService<ICorrelationContext>();
        correlationContext.Set(
            correlationId,
            null,
            SourceChannels.Web,
            null,
            null);
        var problemDetailsService = scope.ServiceProvider
            .GetRequiredService<IProblemDetailsService>();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
        };
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.Body = new MemoryStream();

        var written = await problemDetailsService.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = new ProblemDetails
                {
                    Status = statusCode,
                    Title = secret,
                    Detail = secret,
                    Instance = secret,
                    Extensions =
                    {
                        ["connectionString"] = secret,
                    },
                },
            });

        Assert.True(written);
        httpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(
            httpContext.Response.Body,
            Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal(statusCode, root.GetProperty("status").GetInt32());
        Assert.Equal(expectedCode, root.GetProperty("code").GetString());
        Assert.Equal(correlationId, root.GetProperty("correlationId").GetString());
        Assert.False(root.TryGetProperty("connectionString", out _));
        Assert.False(root.TryGetProperty("instance", out _));
        Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ModuleOwnedStatus_IsNotAssignedAPlatformCode()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var problemDetailsService = scope.ServiceProvider
            .GetRequiredService<IProblemDetailsService>();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
        };
        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        httpContext.Response.Body = new MemoryStream();

        var written = await problemDetailsService.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Modül sonucu",
                },
            });

        Assert.True(written);
        httpContext.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(httpContext.Response.Body);
        Assert.False(document.RootElement.TryGetProperty("code", out _));
        Assert.Equal(
            "Modül sonucu",
            document.RootElement.GetProperty("title").GetString());
    }

    private static async Task<string> AssertPlatformProblemAsync(
        HttpResponseMessage response,
        string expectedCode,
        string expectedCorrelationId)
    {
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("type").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()));
        Assert.Equal((int)response.StatusCode, root.GetProperty("status").GetInt32());
        Assert.Equal(expectedCode, root.GetProperty("code").GetString());
        Assert.Equal(
            expectedCorrelationId,
            root.GetProperty("correlationId").GetString());
        return body;
    }
}
