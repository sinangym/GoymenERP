using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Api.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyApiEndpointSecurityContractTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private const string ContractPath =
        "src/backend/Modules/Counterparty/COUNTERPARTY_API_ENDPOINTS.json";
    private const string ExpectedOwner = "CHATGPT-CARI";
    private const string ExpectedModule = "Counterparty";
    private const string ExpectedTag = "Counterparty";
    private const string CounterpartyRoutePrefix = "/api/v1/counterparties";

    [Fact]
    public void Runtime_counterparty_endpoints_match_exact_owned_security_contract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var manifest = ReadManifest(repositoryRoot);
        var errors = ValidateManifest(repositoryRoot, manifest).ToList();

        var endpointDataSource = factory.Services.GetRequiredService<EndpointDataSource>();
        var runtimeEndpoints = endpointDataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Select(ToRuntimeEndpoint)
            .Where(endpoint => IsCounterpartyRoute(endpoint.Route))
            .ToArray();

        errors.AddRange(ValidateRuntime(manifest, runtimeEndpoints));

        Assert.True(errors.Count == 0, string.Join("\n\n", errors));
    }

    [Fact]
    public void Contract_drift_detection_rejects_missing_extra_and_security_downgrade()
    {
        var safeManifest = new EndpointManifest(
            1,
            ExpectedOwner,
            ExpectedModule,
            ExpectedTag,
            "docs/features/CARI_API_SECURITY_METADATA_CONTRACT_v1.md",
            [
                new EndpointEntry(
                    HttpMethods.Get,
                    CounterpartyRoutePrefix,
                    "dynamic_authorized_company_set",
                    ErpPermissions.CounterpartyPartiesRead,
                    RequiresCompanyContext: false,
                    OptionalCompanyContext: true),
                new EndpointEntry(
                    HttpMethods.Post,
                    CounterpartyRoutePrefix,
                    "endpoint_permission",
                    ErpPermissions.CounterpartyPartiesCreate,
                    RequiresCompanyContext: true,
                    OptionalCompanyContext: false),
            ]);

        var safeRuntime = new[]
        {
            RuntimeEndpoint.DynamicRead(HttpMethods.Get, CounterpartyRoutePrefix),
            RuntimeEndpoint.PermissionWrite(
                HttpMethods.Post,
                CounterpartyRoutePrefix,
                ErpPermissions.CounterpartyPartiesCreate),
        };

        Assert.Empty(ValidateRuntime(safeManifest, safeRuntime));

        var missing = safeRuntime.Skip(1).ToArray();
        Assert.Contains(
            ValidateRuntime(safeManifest, missing),
            error => error.Contains("Missing contracted Counterparty endpoint", StringComparison.Ordinal));

        var extra = safeRuntime.Append(
            RuntimeEndpoint.PermissionWrite(
                HttpMethods.Delete,
                $"{CounterpartyRoutePrefix}/{{partyId:guid}}",
                "counterparty.parties.delete"))
            .ToArray();
        Assert.Contains(
            ValidateRuntime(safeManifest, extra),
            error => error.Contains("Unexpected runtime Counterparty endpoint", StringComparison.Ordinal));

        var anonymous = safeRuntime
            .Select(endpoint => endpoint.Key.Method == HttpMethods.Get
                ? endpoint with { AllowsAnonymous = true }
                : endpoint)
            .ToArray();
        Assert.Contains(
            ValidateRuntime(safeManifest, anonymous),
            error => error.Contains("AllowAnonymous", StringComparison.Ordinal));

        var missingCompanyContext = safeRuntime
            .Select(endpoint => endpoint.Key.Method == HttpMethods.Post
                ? endpoint with { RequiresCompanyContext = false }
                : endpoint)
            .ToArray();
        Assert.Contains(
            ValidateRuntime(safeManifest, missingCompanyContext),
            error => error.Contains("RequiresCompanyContext", StringComparison.Ordinal));
    }

    private static EndpointManifest ReadManifest(string repositoryRoot)
    {
        var path = Path.GetFullPath(Path.Combine(
            repositoryRoot,
            ContractPath.Replace('/', Path.DirectorySeparatorChar)));
        var bytes = File.ReadAllBytes(path);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32,
        });
        var duplicateErrors = new List<string>();
        FindDuplicateProperties(document.RootElement, "$", duplicateErrors);
        if (duplicateErrors.Count > 0)
        {
            throw new InvalidOperationException(string.Join("\n", duplicateErrors));
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        return JsonSerializer.Deserialize<EndpointManifest>(bytes, options)
               ?? throw new InvalidOperationException(
                   "Counterparty API endpoint contract is empty.");
    }

    private static IReadOnlyList<string> ValidateManifest(
        string repositoryRoot,
        EndpointManifest manifest)
    {
        var errors = new List<string>();
        if (manifest.SchemaVersion != 1)
        {
            errors.Add($"Unsupported schema_version: {manifest.SchemaVersion}.");
        }

        if (!string.Equals(manifest.Owner, ExpectedOwner, StringComparison.Ordinal))
        {
            errors.Add($"Unexpected owner: {manifest.Owner}.");
        }

        if (!string.Equals(manifest.Module, ExpectedModule, StringComparison.Ordinal))
        {
            errors.Add($"Unexpected module: {manifest.Module}.");
        }

        if (!string.Equals(manifest.Tag, ExpectedTag, StringComparison.Ordinal))
        {
            errors.Add($"Unexpected tag: {manifest.Tag}.");
        }

        ValidateRepositoryFile(repositoryRoot, manifest.ContractSource, errors);

        if (manifest.Endpoints.Count != 4)
        {
            errors.Add(
                "The current-main Counterparty endpoint contract must contain exactly four "
                + $"entries; found {manifest.Endpoints.Count}.");
        }

        var keys = new HashSet<EndpointKey>();
        foreach (var endpoint in manifest.Endpoints)
        {
            var key = Key(endpoint.Method, endpoint.Route);
            if (!keys.Add(key))
            {
                errors.Add($"Duplicate endpoint contract: {key}.");
            }

            if (ContainsWildcard(endpoint.Route))
            {
                errors.Add($"Wildcard route is forbidden: {endpoint.Route}.");
            }

            if (!IsCounterpartyRoute(key.Route))
            {
                errors.Add($"Route is outside Counterparty ownership: {key.Route}.");
            }

            if (!ErpPermissions.IsKnown(endpoint.Permission))
            {
                errors.Add($"Unknown permission: {endpoint.Permission}.");
            }

            switch (endpoint.AuthorizationMode)
            {
                case "endpoint_permission":
                    if (!endpoint.RequiresCompanyContext || endpoint.OptionalCompanyContext)
                    {
                        errors.Add(
                            $"{key}: endpoint_permission requires mandatory, non-optional CompanyContext.");
                    }
                    break;
                case "dynamic_authorized_company_set":
                    if (endpoint.RequiresCompanyContext || !endpoint.OptionalCompanyContext)
                    {
                        errors.Add(
                            $"{key}: dynamic_authorized_company_set requires optional CompanyContext.");
                    }

                    if (!string.Equals(
                            endpoint.Permission,
                            ErpPermissions.CounterpartyPartiesRead,
                            StringComparison.Ordinal))
                    {
                        errors.Add(
                            $"{key}: dynamic read permission must be exactly "
                            + $"'{ErpPermissions.CounterpartyPartiesRead}'.");
                    }
                    break;
                default:
                    errors.Add(
                        $"{key}: unsupported authorization_mode '{endpoint.AuthorizationMode}'.");
                    break;
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateRuntime(
        EndpointManifest manifest,
        IReadOnlyCollection<RuntimeEndpoint> runtimeEndpoints)
    {
        var errors = new List<string>();
        var expected = manifest.Endpoints.ToDictionary(
            endpoint => Key(endpoint.Method, endpoint.Route),
            EndpointKeyComparer.Instance);
        var actual = new Dictionary<EndpointKey, RuntimeEndpoint>(EndpointKeyComparer.Instance);

        if (runtimeEndpoints.Count == 0)
        {
            errors.Add("No runtime Counterparty endpoints were discovered.");
            return errors;
        }

        foreach (var runtime in runtimeEndpoints)
        {
            if (!actual.TryAdd(runtime.Key, runtime))
            {
                errors.Add($"Duplicate runtime Counterparty endpoint: {runtime.Key}.");
                continue;
            }

            if (!expected.TryGetValue(runtime.Key, out var contract))
            {
                errors.Add($"Unexpected runtime Counterparty endpoint: {runtime.Key}.");
                continue;
            }

            ValidateRuntimeEndpoint(manifest.Tag, contract, runtime, errors);
        }

        foreach (var key in expected.Keys.Except(actual.Keys, EndpointKeyComparer.Instance))
        {
            errors.Add($"Missing contracted Counterparty endpoint: {key}.");
        }

        return errors;
    }

    private static void ValidateRuntimeEndpoint(
        string expectedTag,
        EndpointEntry contract,
        RuntimeEndpoint runtime,
        ICollection<string> errors)
    {
        if (runtime.Authorization.Count == 0)
        {
            errors.Add($"{runtime.Key}: authentication metadata is missing.");
        }

        if (runtime.AllowsAnonymous)
        {
            errors.Add($"{runtime.Key}: AllowAnonymous is forbidden.");
        }

        if (runtime.Authorization.Any(metadata => metadata.Roles is not null))
        {
            errors.Add($"{runtime.Key}: role-string authorization is forbidden.");
        }

        if (runtime.Authorization.Any(metadata => metadata.AuthenticationSchemes is not null))
        {
            errors.Add($"{runtime.Key}: endpoint authentication scheme override is forbidden.");
        }

        if (runtime.Tags.Count != 1
            || !string.Equals(runtime.Tags[0], expectedTag, StringComparison.Ordinal))
        {
            errors.Add(
                $"{runtime.Key}: tags must be exactly [{expectedTag}]; found "
                + $"[{string.Join(", ", runtime.Tags)}].");
        }

        if (runtime.RequiresCompanyContext != contract.RequiresCompanyContext)
        {
            errors.Add(
                $"{runtime.Key}: RequiresCompanyContext must be "
                + $"{contract.RequiresCompanyContext}.");
        }

        var nonEmptyPolicies = runtime.Authorization
            .Where(metadata => !string.IsNullOrWhiteSpace(metadata.Policy))
            .Select(metadata => metadata.Policy!)
            .ToArray();

        if (contract.AuthorizationMode == "endpoint_permission")
        {
            if (nonEmptyPolicies.Length != 1
                || !string.Equals(
                    nonEmptyPolicies[0],
                    contract.Permission,
                    StringComparison.Ordinal))
            {
                errors.Add(
                    $"{runtime.Key}: expected endpoint permission '{contract.Permission}' "
                    + $"exactly once; found [{string.Join(", ", nonEmptyPolicies)}].");
            }
        }
        else if (contract.AuthorizationMode == "dynamic_authorized_company_set")
        {
            if (nonEmptyPolicies.Length != 0)
            {
                errors.Add(
                    $"{runtime.Key}: dynamic authorized-company-set read must not declare "
                    + $"endpoint permission policies; found [{string.Join(", ", nonEmptyPolicies)}].");
            }

            if (!string.Equals(
                    contract.Permission,
                    ErpPermissions.CounterpartyPartiesRead,
                    StringComparison.Ordinal))
            {
                errors.Add(
                    $"{runtime.Key}: dynamic read contract permission drifted from "
                    + $"'{ErpPermissions.CounterpartyPartiesRead}'.");
            }
        }
    }

    private static RuntimeEndpoint ToRuntimeEndpoint(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata
            .OfType<IHttpMethodMetadata>()
            .SelectMany(metadata => metadata.HttpMethods)
            .Select(NormalizeMethod)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var route = NormalizeRoute(endpoint.RoutePattern.RawText);
        if (methods.Length != 1)
        {
            return new RuntimeEndpoint(
                new EndpointKey(string.Join("+", methods), route),
                endpoint.DisplayName ?? "<unnamed>",
                [],
                AllowsAnonymous: endpoint.Metadata.OfType<IAllowAnonymous>().Any(),
                RequiresCompanyContext: endpoint.Metadata
                    .OfType<RequiresCompanyContextAttribute>()
                    .Any(),
                Tags: []);
        }

        return new RuntimeEndpoint(
            Key(methods[0], route),
            endpoint.DisplayName ?? "<unnamed>",
            endpoint.Metadata
                .OfType<IAuthorizeData>()
                .Select(metadata => new AuthorizationMetadata(
                    metadata.Policy,
                    metadata.Roles,
                    metadata.AuthenticationSchemes))
                .ToArray(),
            endpoint.Metadata.OfType<IAllowAnonymous>().Any(),
            endpoint.Metadata.OfType<RequiresCompanyContextAttribute>().Any(),
            endpoint.Metadata
                .OfType<ITagsMetadata>()
                .SelectMany(metadata => metadata.Tags)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(tag => tag, StringComparer.Ordinal)
                .ToArray());
    }

    private static void FindDuplicateProperties(
        JsonElement element,
        string location,
        ICollection<string> errors)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    errors.Add($"Duplicate JSON property at {location}: {property.Name}.");
                }

                FindDuplicateProperties(
                    property.Value,
                    $"{location}.{property.Name}",
                    errors);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                FindDuplicateProperties(item, $"{location}[{index}]", errors);
                index++;
            }
        }
    }

    private static void ValidateRepositoryFile(
        string repositoryRoot,
        string repositoryPath,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath) || ContainsWildcard(repositoryPath))
        {
            errors.Add($"Invalid contract_source path: {repositoryPath}.");
            return;
        }

        var fullPath = Path.GetFullPath(Path.Combine(
            repositoryRoot,
            repositoryPath.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(repositoryRoot, fullPath);
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || !File.Exists(fullPath))
        {
            errors.Add($"contract_source is missing or outside the repository: {repositoryPath}.");
        }
    }

    private static bool IsCounterpartyRoute(string route)
    {
        return string.Equals(route, CounterpartyRoutePrefix, StringComparison.Ordinal)
               || route.StartsWith(
                   $"{CounterpartyRoutePrefix}/",
                   StringComparison.Ordinal);
    }

    private static bool ContainsWildcard(string value)
    {
        return value.Contains('*') || value.Contains('?');
    }

    private static EndpointKey Key(string method, string route)
    {
        return new EndpointKey(NormalizeMethod(method), NormalizeRoute(route));
    }

    private static string NormalizeMethod(string method)
    {
        return method.Trim().ToUpperInvariant();
    }

    private static string NormalizeRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return string.Empty;
        }

        var normalized = route.Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = $"/{normalized}";
        }

        while (normalized.Length > 1 && normalized.EndsWith('/'))
        {
            normalized = normalized[..^1];
        }

        return normalized;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GoymenERP.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Göymen ERP repository root was not found.");
    }

    private sealed record EndpointKey(string Method, string Route)
    {
        public override string ToString() => $"{Method} {Route}";
    }

    private sealed class EndpointKeyComparer : IEqualityComparer<EndpointKey>
    {
        public static EndpointKeyComparer Instance { get; } = new();

        public bool Equals(EndpointKey? x, EndpointKey? y)
        {
            return x is not null
                   && y is not null
                   && string.Equals(x.Method, y.Method, StringComparison.Ordinal)
                   && string.Equals(x.Route, y.Route, StringComparison.Ordinal);
        }

        public int GetHashCode(EndpointKey obj)
        {
            return HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.Method),
                StringComparer.Ordinal.GetHashCode(obj.Route));
        }
    }

    private sealed record AuthorizationMetadata(
        string? Policy,
        string? Roles,
        string? AuthenticationSchemes);

    private sealed record RuntimeEndpoint(
        EndpointKey Key,
        string DisplayName,
        IReadOnlyList<AuthorizationMetadata> Authorization,
        bool AllowsAnonymous,
        bool RequiresCompanyContext,
        IReadOnlyList<string> Tags)
    {
        public static RuntimeEndpoint DynamicRead(string method, string route)
        {
            return new RuntimeEndpoint(
                Key(method, route),
                $"{method} {route}",
                [new AuthorizationMetadata(null, null, null)],
                AllowsAnonymous: false,
                RequiresCompanyContext: false,
                [ExpectedTag]);
        }

        public static RuntimeEndpoint PermissionWrite(
            string method,
            string route,
            string permission)
        {
            return new RuntimeEndpoint(
                Key(method, route),
                $"{method} {route}",
                [
                    new AuthorizationMetadata(null, null, null),
                    new AuthorizationMetadata(permission, null, null),
                ],
                AllowsAnonymous: false,
                RequiresCompanyContext: true,
                [ExpectedTag]);
        }
    }

    private sealed record EndpointManifest(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("owner")] string Owner,
        [property: JsonPropertyName("module")] string Module,
        [property: JsonPropertyName("tag")] string Tag,
        [property: JsonPropertyName("contract_source")] string ContractSource,
        [property: JsonPropertyName("endpoints")] IReadOnlyList<EndpointEntry> Endpoints);

    private sealed record EndpointEntry(
        [property: JsonPropertyName("method")] string Method,
        [property: JsonPropertyName("route")] string Route,
        [property: JsonPropertyName("authorization_mode")] string AuthorizationMode,
        [property: JsonPropertyName("permission")] string Permission,
        [property: JsonPropertyName("requires_company_context")] bool RequiresCompanyContext,
        [property: JsonPropertyName("optional_company_context")] bool OptionalCompanyContext);
}
