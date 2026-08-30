using System.Net;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Errors;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Api.IntegrationTests;

public sealed class PlatformProblemDetailsAcceptanceTests(
    GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    [Fact]
    public async Task AnonymousProtectedRequest_ReturnsStableAuthenticationProblem()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("text/plain");

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
        client.DefaultRequestHeaders.Accept.ParseAdd("application/xml");
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
        httpContext.Request.Headers.Accept = "text/plain";
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
    public async Task UnhandledExceptionPipeline_ReturnsRedactedStableProblem()
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var logEntries = new ConcurrentQueue<string>();
        using var exceptionFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter,
                    UnhandledExceptionStartupFilter>());
            builder.ConfigureLogging(logging =>
                logging.AddProvider(new CollectingLoggerProvider(logEntries)));
        });
        using var client = exceptionFactory.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("text/plain");
        client.DefaultRequestHeaders.Add(
            "X-Development-User",
            TestUsers.CompanyA);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        using var response = await client.GetAsync(
            UnhandledExceptionStartupFilter.Path);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await AssertPlatformProblemAsync(
            response,
            ErpProblemCodes.InternalError,
            correlationId);
        Assert.DoesNotContain(
            UnhandledExceptionStartupFilter.Secret,
            body,
            StringComparison.Ordinal);
        Assert.DoesNotContain(logEntries, entry => entry.Contains(
            UnhandledExceptionStartupFilter.Secret,
            StringComparison.Ordinal));
        Assert.Contains(logEntries, entry => entry.Contains(
            ErpProblemCodes.InternalError,
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnhealthyLiveCheck_ReturnsRedactedStableServiceUnavailableProblem()
    {
        const string secret = "dependency=secret-health-check";
        var correlationId = Guid.NewGuid().ToString("N");
        using var unhealthyFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddHealthChecks().AddCheck(
                    "platform-unhealthy-acceptance",
                    () => HealthCheckResult.Unhealthy(secret))));
        using var client = unhealthyFactory.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("text/plain");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await AssertPlatformProblemAsync(
            response,
            ErpProblemCodes.ServiceUnavailable,
            correlationId);
        Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
        Assert.DoesNotContain("platform-unhealthy-acceptance", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OrganizationModuleStatuses_PreserveTheirOwnProblemDetailsContract()
    {
        using var missingCompanyClient = factory.CreateClientFor(TestUsers.CompanyA);
        using var badRequest = await missingCompanyClient.GetAsync(
            "/api/v1/organization/sites");
        await AssertModuleProblemWithoutPlatformCodeAsync(
            badRequest,
            HttpStatusCode.BadRequest);

        using var selectedCompanyClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        using var notFound = await selectedCompanyClient.GetAsync(
            $"/api/v1/organization/locations/{Guid.NewGuid()}");
        await AssertModuleProblemWithoutPlatformCodeAsync(
            notFound,
            HttpStatusCode.NotFound);

        var location = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyA,
            "EXT-BASTUG");
        using var conflictClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        using var conflict = await conflictClient.PutAsJsonAsync(
            $"/api/v1/organization/locations/{location.Id}/active-state",
            new SetLocationActiveState(
                !location.IsActive,
                location.Version + 1,
                "Platform hata sözleşmesi modül koruma testi"));
        var conflictProblem = await AssertModuleProblemWithoutPlatformCodeAsync(
            conflict,
            HttpStatusCode.Conflict);
        var conflictDetails = conflictProblem.GetProperty("conflict");
        Assert.Equal(
            location.Version + 1,
            conflictDetails.GetProperty("expectedVersion").GetInt64());
        Assert.Equal(
            location.Version,
            conflictDetails.GetProperty("currentVersion").GetInt64());
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

    private static async Task<JsonElement> AssertModuleProblemWithoutPlatformCodeAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        Assert.False(document.RootElement.TryGetProperty("code", out _));
        return document.RootElement.Clone();
    }

    private sealed class UnhandledExceptionStartupFilter : IStartupFilter
    {
        public const string Path = "/_acceptance/platform-error/unhandled";

        public const string Secret =
            "Server=secret;Password=exception-must-not-leak";

        public Action<IApplicationBuilder> Configure(
            Action<IApplicationBuilder> next)
        {
            return application =>
            {
                next(application);
                application.Use(async (httpContext, nextMiddleware) =>
                {
                    if (httpContext.Request.Path == Path)
                    {
                        throw new InvalidOperationException(Secret);
                    }

                    await nextMiddleware();
                });
            };
        }
    }

    private sealed class CollectingLoggerProvider(
        ConcurrentQueue<string> entries) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new CollectingLogger(entries, categoryName);
        }

        public void Dispose()
        {
        }
    }

    private sealed class CollectingLogger(
        ConcurrentQueue<string> entries,
        string categoryName) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return EmptyScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            entries.Enqueue(
                $"{categoryName}|{formatter(state, exception)}|{exception}");
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public static EmptyScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
