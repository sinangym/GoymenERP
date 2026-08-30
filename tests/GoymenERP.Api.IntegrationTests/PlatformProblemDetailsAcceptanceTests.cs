using System.Net;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Observability;
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
    public static TheoryData<string[]?> InvalidCompanyContextHeaderValues => new()
    {
        (string[]?)null,
        new[] { string.Empty },
        new[] { "   " },
        new[] { "not-a-company-guid" },
        new[] { Guid.Empty.ToString() },
        new[] { $"{TestCompanyIds.CompanyA},{TestCompanyIds.CompanyB}" },
        new[] { TestCompanyIds.CompanyA.ToString(), TestCompanyIds.CompanyB.ToString() },
    };

    [Fact]
    public void RequestMetadataProblemCodes_AreUniqueLowerSnakeCaseConstants()
    {
        var codes = new[]
        {
            ErpProblemCodes.CompanyContextInvalid,
            ErpProblemCodes.CorrelationMetadataInvalid,
        };

        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code =>
        {
            Assert.NotEmpty(code);
            Assert.DoesNotContain("__", code, StringComparison.Ordinal);
            Assert.All(code, character => Assert.True(
                char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character)
                || character == '_'));
            Assert.True(char.IsAsciiLetterLower(code[0]));
            Assert.True(char.IsAsciiLetterLower(code[^1]) || char.IsAsciiDigit(code[^1]));
        });
    }

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

    [Fact]
    public async Task MalformedCorrelation_IsUniformAcrossPublicUnknownAndProtectedRoutes()
    {
        const string hostileCorrelation = "unsafe/correlation";
        var requests = new[]
        {
            (Path: "/health/live", User: (string?)null),
            (Path: "/_acceptance/unknown-route", User: (string?)null),
            (Path: "/api/v1/organization/locations", User: TestUsers.Unprovisioned),
        };
        RequestMetadataProblem? expected = null;

        foreach (var request in requests)
        {
            using var client = factory.CreateClient();
            if (request.User is not null)
            {
                client.DefaultRequestHeaders.Add("X-Development-User", request.User);
            }

            client.DefaultRequestHeaders.Accept.ParseAdd("text/plain");

            Assert.True(client.DefaultRequestHeaders.TryAddWithoutValidation(
                CorrelationContextMiddleware.CorrelationHeaderName,
                hostileCorrelation));

            using var response = await client.GetAsync(request.Path);
            var problem = await AssertRequestMetadataProblemAsync(
                response,
                ErpProblemCodes.CorrelationMetadataInvalid,
                hostileCorrelation,
                request.Path,
                TestUsers.Unprovisioned);

            expected ??= problem;
            Assert.Equal(expected.Status, problem.Status);
            Assert.Equal(expected.Type, problem.Type);
            Assert.Equal(expected.Title, problem.Title);
            Assert.Equal(expected.Detail, problem.Detail);
            Assert.Equal(expected.Code, problem.Code);
        }
    }

    [Theory]
    [MemberData(nameof(InvalidCompanyContextHeaderValues))]
    public async Task InvalidRequiredCompanyContext_UsesStablePlatformProblem(
        string[]? headerValues)
    {
        using var client = factory.CreateClientFor(TestUsers.CompanyA);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/xml");
        var expectedCorrelationId = Assert.Single(
            client.DefaultRequestHeaders.GetValues(
                CorrelationContextMiddleware.CorrelationHeaderName));
        if (headerValues is not null)
        {
            Assert.True(client.DefaultRequestHeaders.TryAddWithoutValidation(
                "X-Company-Id",
                headerValues));
        }

        using var response = await client.GetAsync("/api/v1/organization/locations");
        var problem = await AssertRequestMetadataProblemAsync(
            response,
            ErpProblemCodes.CompanyContextInvalid,
            headerValues?.SelectMany(value => new[] { value }).ToArray() ?? []);

        Assert.Equal(expectedCorrelationId, problem.CorrelationId);
    }

    [Fact]
    public async Task CompanyContextProblem_PreservesAuthenticationUnknownAndOptionalRoutePrecedence()
    {
        using var anonymousClient = factory.CreateClient();
        anonymousClient.DefaultRequestHeaders.Add("X-Company-Id", "not-a-company-guid");
        using var anonymousResponse = await anonymousClient.GetAsync(
            "/api/v1/organization/locations");
        var anonymousCorrelationId = Assert.Single(
            anonymousResponse.Headers.GetValues(
                CorrelationContextMiddleware.CorrelationHeaderName));
        await AssertPlatformProblemAsync(
            anonymousResponse,
            ErpProblemCodes.AuthenticationRequired,
            anonymousCorrelationId);

        using var unknownClient = factory.CreateClientFor(TestUsers.GroupAdmin);
        unknownClient.DefaultRequestHeaders.Add("X-Company-Id", "not-a-company-guid");
        using var unknownResponse = await unknownClient.GetAsync(
            "/_acceptance/unknown-company-context-route");
        Assert.Equal(HttpStatusCode.NotFound, unknownResponse.StatusCode);
        Assert.DoesNotContain(
            ErpProblemCodes.CompanyContextInvalid,
            await unknownResponse.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        using var optionalClient = factory.CreateClientFor(TestUsers.GroupAdmin);
        optionalClient.DefaultRequestHeaders.Add("X-Company-Id", "not-a-company-guid");
        using var optionalResponse = await optionalClient.GetAsync(
            "/api/v1/organization/companies");
        Assert.Equal(HttpStatusCode.OK, optionalResponse.StatusCode);
    }

    [Fact]
    public async Task InvalidRequiredCompanyContext_PrecedesEndpointPermissionWithoutDisclosure()
    {
        var location = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyA,
            "OSM-H3");
        var command = new SetLocationActiveState(
            !location.IsActive,
            location.Version,
            "Şirket bağlamı ve yetki önceliği kabul testi");

        using var invalidClient = factory.CreateClientFor(TestUsers.CompanyAViewer);
        invalidClient.DefaultRequestHeaders.Add("X-Company-Id", "not-a-company-guid");
        using var invalidResponse = await invalidClient.PutAsJsonAsync(
            $"/api/v1/organization/locations/{location.Id}/active-state",
            command);
        var invalidProblem = await AssertRequestMetadataProblemAsync(
            invalidResponse,
            ErpProblemCodes.CompanyContextInvalid,
            "not-a-company-guid",
            "permission",
            "role");
        Assert.Equal(
            Assert.Single(invalidClient.DefaultRequestHeaders.GetValues("X-Correlation-Id")),
            invalidProblem.CorrelationId);

        using var validClient = factory.CreateClientFor(
            TestUsers.CompanyAViewer,
            TestCompanyIds.CompanyA);
        var validCorrelationId = Assert.Single(
            validClient.DefaultRequestHeaders.GetValues("X-Correlation-Id"));
        using var validResponse = await validClient.PutAsJsonAsync(
            $"/api/v1/organization/locations/{location.Id}/active-state",
            command);
        await AssertPlatformProblemAsync(
            validResponse,
            ErpProblemCodes.Forbidden,
            validCorrelationId);

        Assert.Equal(
            location,
            await factory.GetLocationIgnoringFiltersAsync(
                TestCompanyIds.CompanyA,
                "OSM-H3"));
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
        var logEntries = new ConcurrentQueue<CollectedLogEntry>();
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
        Assert.DoesNotContain(logEntries, entry =>
            entry.FormattedMessage.Contains(
                UnhandledExceptionStartupFilter.Secret,
                StringComparison.Ordinal)
            || entry.Exception?.ToString().Contains(
                UnhandledExceptionStartupFilter.Secret,
                StringComparison.Ordinal) == true
            || entry.State.Values.Any(value => value?.ToString()?.Contains(
                UnhandledExceptionStartupFilter.Secret,
                StringComparison.Ordinal) == true));
        var handlerLog = Assert.Single(logEntries, entry =>
            entry.CategoryName == typeof(ErpUnhandledExceptionHandler).FullName);
        Assert.Equal(LogLevel.Error, handlerLog.LogLevel);
        Assert.Null(handlerLog.Exception);
        Assert.Equal(
            ErpProblemCodes.InternalError,
            handlerLog.State["ErrorCode"]);
        Assert.Equal(correlationId, handlerLog.State["CorrelationId"]);
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
        var location = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyA,
            "EXT-BASTUG");
        using var selectedCompanyClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        using var badRequest = await selectedCompanyClient.PutAsJsonAsync(
            $"/api/v1/organization/locations/{location.Id}/active-state",
            new SetLocationActiveState(
                !location.IsActive,
                location.Version,
                string.Empty));
        var badRequestProblem = await AssertModuleProblemWithoutPlatformCodeAsync(
            badRequest,
            HttpStatusCode.BadRequest);
        Assert.Equal(
            "Gerekçe gerekli.",
            badRequestProblem.GetProperty("title").GetString());
        Assert.Equal(
            "Kritik değişiklik için 1-1000 karakter arasında bir gerekçe girin.",
            badRequestProblem.GetProperty("detail").GetString());

        using var notFound = await selectedCompanyClient.GetAsync(
            $"/api/v1/organization/locations/{Guid.NewGuid()}");
        var notFoundProblem = await AssertModuleProblemWithoutPlatformCodeAsync(
            notFound,
            HttpStatusCode.NotFound);
        Assert.Equal(
            "Kayıt bulunamadı.",
            notFoundProblem.GetProperty("title").GetString());
        Assert.Equal(
            "İstenen kayıt bulunamadı veya bu bağlamda kullanılamıyor.",
            notFoundProblem.GetProperty("detail").GetString());

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

    [Fact]
    public async Task ModuleOwnedBadRequestCode_IsNotCanonicalizedAsPlatformMetadataProblem()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var problemDetailsService = scope.ServiceProvider
            .GetRequiredService<IProblemDetailsService>();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
        };
        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        httpContext.Response.Body = new MemoryStream();

        var written = await problemDetailsService.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Modül doğrulama sonucu",
                    Extensions =
                    {
                        ["code"] = "validation_failed",
                    },
                },
            });

        Assert.True(written);
        httpContext.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(httpContext.Response.Body);
        Assert.Equal(
            "validation_failed",
            document.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            "Modül doğrulama sonucu",
            document.RootElement.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("company_context_", "invalid")]
    [InlineData("correlation_metadata_", "invalid")]
    public async Task ReservedCodeWithoutPlatformProducerMarker_IsRemovedFailClosed(
        string prefix,
        string suffix)
    {
        var reservedCode = string.Concat(prefix, suffix);
        await using var scope = factory.Services.CreateAsyncScope();
        var problemDetailsService = scope.ServiceProvider
            .GetRequiredService<IProblemDetailsService>();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
        };
        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        httpContext.Response.Body = new MemoryStream();

        var written = await problemDetailsService.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Modül tarafından üretilen sonuç",
                    Extensions =
                    {
                        ["code"] = reservedCode,
                    },
                },
            });

        Assert.True(written);
        httpContext.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(httpContext.Response.Body);
        Assert.False(document.RootElement.TryGetProperty("code", out _));
        Assert.Equal(
            "Modül tarafından üretilen sonuç",
            document.RootElement.GetProperty("title").GetString());
        Assert.False(document.RootElement.TryGetProperty("correlationId", out _));
    }

    private static async Task<RequestMetadataProblem> AssertRequestMetadataProblemAsync(
        HttpResponseMessage response,
        string expectedCode,
        params string[] forbiddenFragments)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var correlationId = root.GetProperty("correlationId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(correlationId));
        Assert.Equal(
            correlationId,
            Assert.Single(response.Headers.GetValues(
                CorrelationContextMiddleware.CorrelationHeaderName)));
        Assert.Equal(expectedCode, root.GetProperty("code").GetString());
        Assert.False(root.TryGetProperty("instance", out _));
        Assert.Equal(
            new[]
            {
                "code",
                "correlationId",
                "detail",
                "status",
                "title",
                "type",
            },
            root.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        foreach (var fragment in forbiddenFragments.Where(
                     value => !string.IsNullOrEmpty(value)))
        {
            Assert.DoesNotContain(fragment, body, StringComparison.OrdinalIgnoreCase);
        }

        return new RequestMetadataProblem(
            root.GetProperty("status").GetInt32(),
            root.GetProperty("type").GetString(),
            root.GetProperty("title").GetString(),
            root.GetProperty("detail").GetString(),
            root.GetProperty("code").GetString(),
            correlationId);
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
        ConcurrentQueue<CollectedLogEntry> entries) : ILoggerProvider
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
        ConcurrentQueue<CollectedLogEntry> entries,
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
            var structuredState = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            entries.Enqueue(new CollectedLogEntry(
                categoryName,
                logLevel,
                formatter(state, exception),
                exception,
                structuredState));
        }
    }

    private sealed record CollectedLogEntry(
        string CategoryName,
        LogLevel LogLevel,
        string FormattedMessage,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> State);

    private sealed record RequestMetadataProblem(
        int Status,
        string? Type,
        string? Title,
        string? Detail,
        string? Code,
        string? CorrelationId);

    private sealed class EmptyScope : IDisposable
    {
        public static EmptyScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
