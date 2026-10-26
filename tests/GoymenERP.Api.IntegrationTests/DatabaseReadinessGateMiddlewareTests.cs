using System.Text.Json;
using GoymenERP.Api.Startup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GoymenERP.Api.IntegrationTests;

public sealed class DatabaseReadinessGateMiddlewareTests
{
    private const string PrivateFailure = "private-database-and-credential-detail";

    [Theory]
    [InlineData(HealthStatus.Healthy, StatusCodes.Status200OK)]
    [InlineData(HealthStatus.Degraded, StatusCodes.Status200OK)]
    [InlineData(HealthStatus.Unhealthy, StatusCodes.Status503ServiceUnavailable)]
    public async Task PublicReadinessCompletesBeforeDatabaseDependentAuthentication(
        HealthStatus status,
        int expectedStatus)
    {
        await using var services = CreateServices(status);
        var context = CreateContext(services, "/health/ready", allowAnonymous: true);
        context.Request.Headers["Authorization"] = "Bearer synthetic-invalid-identity";
        var middleware = new DatabaseReadinessGateMiddleware(_ =>
            throw new InvalidOperationException("Database-dependent authentication was reached."));

        await middleware.InvokeAsync(context, services.GetRequiredService<HealthCheckService>());

        Assert.Equal(expectedStatus, context.Response.StatusCode);
        var body = await ReadBodyAsync(context);
        Assert.DoesNotContain(PrivateFailure, body, StringComparison.Ordinal);
        if (expectedStatus == StatusCodes.Status503ServiceUnavailable)
        {
            using var problem = JsonDocument.Parse(body);
            Assert.Equal(expectedStatus, problem.RootElement.GetProperty("status").GetInt32());
        }
        else
        {
            Assert.Equal(status.ToString(), body);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProtectedOrUnmatchedReadinessPathRetainsDownstreamRoutingAndAuthentication(
        bool hasProtectedEndpoint)
    {
        await using var services = CreateServices(HealthStatus.Unhealthy);
        var context = CreateContext(services, "/health/ready", allowAnonymous: false);
        if (hasProtectedEndpoint)
        {
            context.SetEndpoint(new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(new AuthorizeAttribute()),
                "Protected endpoint"));
        }
        var middleware = new DatabaseReadinessGateMiddleware(httpContext =>
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, services.GetRequiredService<HealthCheckService>());

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Empty(await ReadBodyAsync(context));
    }

    [Theory]
    [InlineData(HealthStatus.Degraded)]
    [InlineData(HealthStatus.Unhealthy)]
    public async Task BusinessRequestsRemainBlockedBeforeAuthenticationWhenDatabaseIsNotHealthy(
        HealthStatus status)
    {
        await using var services = CreateServices(status);
        var context = CreateContext(services, "/api/v1/organization/companies", allowAnonymous: false);
        var middleware = new DatabaseReadinessGateMiddleware(_ =>
            throw new InvalidOperationException("An unhealthy business request reached authentication."));

        await middleware.InvokeAsync(context, services.GetRequiredService<HealthCheckService>());

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.DoesNotContain(PrivateFailure, await ReadBodyAsync(context), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HealthyBusinessRequestsContinueToAuthentication()
    {
        await using var services = CreateServices(HealthStatus.Healthy);
        var context = CreateContext(services, "/api/v1/organization/companies", allowAnonymous: false);
        var middleware = new DatabaseReadinessGateMiddleware(httpContext =>
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, services.GetRequiredService<HealthCheckService>());

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task LivenessContinuesWithoutDatabaseReadiness()
    {
        await using var services = CreateServices(HealthStatus.Unhealthy);
        var context = CreateContext(services, "/health/live", allowAnonymous: true);
        var called = false;
        var middleware = new DatabaseReadinessGateMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, services.GetRequiredService<HealthCheckService>());

        Assert.True(called);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    private static ServiceProvider CreateServices(HealthStatus status)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        services.AddHealthChecks()
            .AddCheck("database", () => new HealthCheckResult(status, PrivateFailure), tags: ["ready"])
            .AddCheck("unrelated-liveness", () => HealthCheckResult.Unhealthy(PrivateFailure));
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext CreateContext(
        IServiceProvider services,
        string path,
        bool allowAnonymous)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        if (allowAnonymous)
        {
            context.SetEndpoint(new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(new AllowAnonymousAttribute()),
                "Public health endpoint"));
        }

        return context;
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }
}
