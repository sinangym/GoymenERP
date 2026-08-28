using GoymenERP.Api.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class PlatformApiEndpointSecurityMetadataTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private const string HealthRoute = "/health/live";

    private static readonly IReadOnlyDictionary<EndpointKey, ExpectedEndpointContract>
        ReviewedPlatformEndpoints =
            new Dictionary<EndpointKey, ExpectedEndpointContract>
            {
                [Key(HttpMethods.Get, "/api/v1/organization/companies")] =
                    new(PermissionPolicy: null, RequiresCompanyContext: false, "Organization"),
                [Key(HttpMethods.Get, "/api/v1/organization/context")] =
                    new(ErpPermissions.OrganizationContextRead, true, "Organization"),
                [Key(HttpMethods.Get, "/api/v1/organization/sites")] =
                    new(ErpPermissions.OrganizationSitesRead, true, "Organization"),
                [Key(HttpMethods.Get, "/api/v1/organization/locations")] =
                    new(ErpPermissions.OrganizationLocationsRead, true, "Organization"),
                [Key(HttpMethods.Get, "/api/v1/organization/locations/{id:guid}")] =
                    new(ErpPermissions.OrganizationLocationsRead, true, "Organization"),
                [Key(HttpMethods.Put, "/api/v1/organization/locations/{id:guid}/active-state")] =
                    new(ErpPermissions.OrganizationLocationsStatusUpdate, true, "Organization"),
                [Key(HttpMethods.Get, "/api/v1/organization/work-centers")] =
                    new(ErpPermissions.OrganizationWorkCentersRead, true, "Organization"),
                [Key(HttpMethods.Get, "/api/v1/identity/me")] =
                    new(PermissionPolicy: null, RequiresCompanyContext: false, "Identity"),
                [Key(HttpMethods.Get, "/api/v1/audit/events")] =
                    new(ErpPermissions.IdentityAuditRead, true, "Audit"),
            };

    private static readonly IReadOnlySet<EndpointKey> ReviewedAuthenticatedOnlyGlobals =
        new HashSet<EndpointKey>
        {
            Key(HttpMethods.Get, "/api/v1/organization/companies"),
            Key(HttpMethods.Get, "/api/v1/identity/me"),
        };

    [Fact]
    public void PlatformApiEndpoints_MatchExactReviewedInventoryAndSecurityMetadata()
    {
        var endpointDataSource = factory.Services.GetRequiredService<EndpointDataSource>();
        var contracts = endpointDataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Select(ToContract)
            .OrderBy(contract => contract.Route, StringComparer.Ordinal)
            .ThenBy(contract => contract.DisplayName, StringComparer.Ordinal)
            .ToArray();

        var violations = FindViolations(contracts);

        Assert.True(
            violations.Count == 0,
            $"Platform API security metadata contract violations:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void Guard_DetectsAuthorizationAndCompanyContextDowngrades()
    {
        var safeInventory = BuildReviewedSyntheticInventory();
        Assert.Empty(FindViolations(safeInventory));

        var tenantKey = Key(HttpMethods.Get, "/api/v1/organization/context");

        AssertMutationRejected(
            safeInventory,
            tenantKey,
            contract => contract with
            {
                Authorization = [new(Policy: null, Roles: null, AuthenticationSchemes: null)],
            },
            "expected permission policy");
        AssertMutationRejected(
            safeInventory,
            tenantKey,
            contract => contract with
            {
                Authorization = [new("wrong.permission", Roles: null, AuthenticationSchemes: null)],
            },
            "expected permission policy");
        AssertMutationRejected(
            safeInventory,
            tenantKey,
            contract => contract with
            {
                Authorization =
                [
                    new(
                        ErpPermissions.OrganizationContextRead,
                        Roles: "UnexpectedRole",
                        AuthenticationSchemes: null),
                ],
            },
            "role authorization metadata");
        AssertMutationRejected(
            safeInventory,
            tenantKey,
            contract => contract with
            {
                Authorization =
                [
                    new(
                        ErpPermissions.OrganizationContextRead,
                        Roles: null,
                        AuthenticationSchemes: "UnexpectedScheme"),
                ],
            },
            "authentication scheme metadata");
        AssertMutationRejected(
            safeInventory,
            tenantKey,
            contract => contract with { AllowsAnonymous = true },
            "AllowAnonymous");
        AssertMutationRejected(
            safeInventory,
            tenantKey,
            contract => contract with { RequiresCompanyContext = false },
            "company context metadata");
    }

    [Fact]
    public void Guard_DetectsAdversarialEndpointInventoryDrift()
    {
        var safeInventory = BuildReviewedSyntheticInventory();
        var tenantKey = Key(HttpMethods.Get, "/api/v1/organization/context");

        AssertMutationRejected(
            safeInventory,
            tenantKey,
            contract => contract with { HttpMethods = [] },
            "exactly one HTTP method");
        AssertMutationRejected(
            safeInventory,
            tenantKey,
            contract => contract with { HttpMethods = [HttpMethods.Get, HttpMethods.Post] },
            "exactly one HTTP method");

        var duplicateInventory = safeInventory
            .Append(FindContract(safeInventory, tenantKey) with
            {
                DisplayName = "duplicate GET /api/v1/organization/context",
            })
            .ToArray();
        Assert.Contains(
            FindViolations(duplicateInventory),
            violation => violation.Contains("Duplicate platform endpoint", StringComparison.Ordinal));

        AssertRouteMutationRejected(
            safeInventory,
            tenantKey,
            "/api/v1/organization/context-alternate");
        AssertRouteMutationRejected(
            safeInventory,
            tenantKey,
            "/api/organization/context");
        AssertRouteMutationRejected(
            safeInventory,
            tenantKey,
            "/api/v1/organizations/context");
        AssertRouteMutationRejected(
            safeInventory,
            tenantKey,
            "/api/v1/organization-context");
        AssertRouteMutationRejected(
            safeInventory,
            tenantKey,
            "/API/v1/organization/context");

        AssertMutationRejected(
            safeInventory,
            tenantKey,
            contract => contract with { Tags = ["Organizations"] },
            "endpoint tags");
        AssertMutationRejected(
            safeInventory,
            tenantKey,
            contract => contract with
            {
                Route = "/tag-only-platform-endpoint",
                Tags = ["Organization"],
            },
            "/tag-only-platform-endpoint");

        var missingInventory = safeInventory
            .Where(contract => !Matches(contract, tenantKey))
            .ToArray();
        Assert.Contains(
            FindViolations(missingInventory),
            violation => violation.Contains("Missing reviewed platform endpoint", StringComparison.Ordinal));

        var extraInventory = safeInventory
            .Append(CreateSafeSyntheticContract(
                Key(HttpMethods.Get, "/api/v1/other/new-endpoint"),
                new(PermissionPolicy: "other.read", RequiresCompanyContext: true, "Other")))
            .ToArray();
        Assert.Contains(
            FindViolations(extraInventory),
            violation => violation.Contains("Unexpected runtime endpoint", StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> FindViolations(
        IReadOnlyCollection<EndpointContract> allContracts)
    {
        var violations = new List<string>();
        if (allContracts.Count == 0)
        {
            violations.Add("No runtime route endpoints were discovered; the guard cannot pass vacuously.");
            return violations;
        }

        var healthEndpoints = allContracts
            .Where(contract => contract.Route.Equals(HealthRoute, StringComparison.Ordinal))
            .ToArray();
        if (healthEndpoints.Length != 1)
        {
            violations.Add(
                $"Expected exactly one reviewed non-platform endpoint '{HealthRoute}', found {healthEndpoints.Length}.");
        }

        var platformContracts = allContracts
            .Where(contract => !contract.Route.Equals(HealthRoute, StringComparison.Ordinal))
            .ToArray();
        if (platformContracts.Length == 0)
        {
            violations.Add("No platform endpoints were discovered; the guard cannot pass vacuously.");
            return violations;
        }

        foreach (var contract in platformContracts)
        {
            if (contract.HttpMethods.Count != 1
                || string.IsNullOrWhiteSpace(contract.HttpMethods.SingleOrDefault()))
            {
                violations.Add(
                    $"{contract.DisplayName}: platform endpoint must declare exactly one HTTP method; found [{string.Join(", ", contract.HttpMethods)}].");
                continue;
            }

            var key = Key(contract.HttpMethods[0], contract.Route);
            if (!ReviewedPlatformEndpoints.TryGetValue(key, out var expected))
            {
                violations.Add($"Unexpected runtime endpoint: '{key}'.");
                continue;
            }

            ValidateSecurityMetadata(contract, key, expected, violations);
        }

        var keyedContracts = platformContracts
            .Where(contract =>
                contract.HttpMethods.Count == 1
                && !string.IsNullOrWhiteSpace(contract.HttpMethods[0]))
            .GroupBy(
                contract => Key(contract.HttpMethods[0], contract.Route),
                EndpointKeyComparer.Instance)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray(),
                EndpointKeyComparer.Instance);

        foreach (var duplicate in keyedContracts.Where(pair => pair.Value.Length != 1))
        {
            violations.Add(
                $"Duplicate platform endpoint '{duplicate.Key}' was registered {duplicate.Value.Length} times.");
        }

        foreach (var expected in ReviewedPlatformEndpoints.Keys)
        {
            if (!keyedContracts.ContainsKey(expected))
            {
                violations.Add($"Missing reviewed platform endpoint: '{expected}'.");
            }
        }

        var globalContracts = platformContracts
            .Where(contract => !contract.RequiresCompanyContext)
            .ToArray();
        if (globalContracts.Length != ReviewedAuthenticatedOnlyGlobals.Count)
        {
            violations.Add(
                $"Expected exactly {ReviewedAuthenticatedOnlyGlobals.Count} authenticated-only global endpoints, found {globalContracts.Length}.");
        }

        foreach (var globalKey in ReviewedAuthenticatedOnlyGlobals)
        {
            var count = globalContracts.Count(contract => Matches(contract, globalKey));
            if (count != 1)
            {
                violations.Add(
                    $"Authenticated-only global exception '{globalKey}' must occur exactly once; found {count}.");
            }
        }

        return violations;
    }

    private static void ValidateSecurityMetadata(
        EndpointContract contract,
        EndpointKey key,
        ExpectedEndpointContract expected,
        ICollection<string> violations)
    {
        if (contract.Authorization.Count == 0)
        {
            violations.Add($"{key}: authorization metadata is missing.");
        }

        if (contract.AllowsAnonymous)
        {
            violations.Add($"{key}: AllowAnonymous metadata is forbidden.");
        }

        if (contract.RequiresCompanyContext != expected.RequiresCompanyContext)
        {
            violations.Add(
                $"{key}: company context metadata must be {expected.RequiresCompanyContext}.");
        }

        var policies = contract.Authorization
            .Where(metadata => !string.IsNullOrWhiteSpace(metadata.Policy))
            .Select(metadata => metadata.Policy!)
            .ToArray();
        if (contract.Authorization.Any(metadata =>
                metadata.Policy is not null
                && string.IsNullOrWhiteSpace(metadata.Policy)))
        {
            violations.Add($"{key}: blank authorization policy metadata is forbidden.");
        }

        if (expected.PermissionPolicy is null)
        {
            if (policies.Length != 0)
            {
                violations.Add(
                    $"{key}: authenticated-only endpoint must not declare permission policies; found [{string.Join(", ", policies)}].");
            }
        }
        else if (policies.Length != 1
                 || !policies[0].Equals(expected.PermissionPolicy, StringComparison.Ordinal))
        {
            violations.Add(
                $"{key}: expected permission policy '{expected.PermissionPolicy}' exactly once; found [{string.Join(", ", policies)}].");
        }

        if (contract.Authorization.Any(metadata => metadata.Roles is not null))
        {
            violations.Add($"{key}: role authorization metadata is unexpected.");
        }

        if (contract.Authorization.Any(metadata => metadata.AuthenticationSchemes is not null))
        {
            violations.Add($"{key}: authentication scheme metadata is unexpected.");
        }

        if (contract.Tags.Count != 1
            || !contract.Tags[0].Equals(expected.Tag, StringComparison.Ordinal))
        {
            violations.Add(
                $"{key}: endpoint tags must equal [{expected.Tag}]; found [{string.Join(", ", contract.Tags)}].");
        }
    }

    private static EndpointContract ToContract(RouteEndpoint endpoint)
    {
        return new EndpointContract(
            NormalizeRoute(endpoint.RoutePattern.RawText),
            endpoint.DisplayName ?? "<unnamed endpoint>",
            endpoint.Metadata
                .OfType<IHttpMethodMetadata>()
                .SelectMany(metadata => metadata.HttpMethods)
                .Select(NormalizeMethod)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(method => method, StringComparer.Ordinal)
                .ToArray(),
            endpoint.Metadata
                .OfType<IAuthorizeData>()
                .Select(metadata => new AuthorizationContract(
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

    private static EndpointContract[] BuildReviewedSyntheticInventory()
    {
        return ReviewedPlatformEndpoints
            .Select(pair => CreateSafeSyntheticContract(pair.Key, pair.Value))
            .Append(new EndpointContract(
                HealthRoute,
                $"GET {HealthRoute}",
                [HttpMethods.Get],
                Authorization: [],
                AllowsAnonymous: true,
                RequiresCompanyContext: false,
                Tags: []))
            .ToArray();
    }

    private static EndpointContract CreateSafeSyntheticContract(
        EndpointKey key,
        ExpectedEndpointContract expected)
    {
        AuthorizationContract[] authorization = expected.PermissionPolicy is null
            ? [new(Policy: null, Roles: null, AuthenticationSchemes: null)]
            : [new(expected.PermissionPolicy, Roles: null, AuthenticationSchemes: null)];

        return new EndpointContract(
            key.Route,
            key.ToString(),
            [key.Method],
            authorization,
            AllowsAnonymous: false,
            expected.RequiresCompanyContext,
            [expected.Tag]);
    }

    private static void AssertMutationRejected(
        EndpointContract[] safeInventory,
        EndpointKey target,
        Func<EndpointContract, EndpointContract> mutation,
        string expectedViolation)
    {
        var mutated = safeInventory
            .Select(contract => Matches(contract, target) ? mutation(contract) : contract)
            .ToArray();

        Assert.Contains(
            FindViolations(mutated),
            violation => violation.Contains(expectedViolation, StringComparison.Ordinal));
    }

    private static void AssertRouteMutationRejected(
        EndpointContract[] safeInventory,
        EndpointKey target,
        string mutatedRoute)
    {
        AssertMutationRejected(
            safeInventory,
            target,
            contract => contract with { Route = mutatedRoute },
            mutatedRoute);
    }

    private static EndpointContract FindContract(
        IEnumerable<EndpointContract> contracts,
        EndpointKey key)
    {
        return contracts.Single(contract => Matches(contract, key));
    }

    private static bool Matches(EndpointContract contract, EndpointKey key)
    {
        return contract.HttpMethods.Count == 1
            && contract.HttpMethods[0].Equals(key.Method, StringComparison.Ordinal)
            && contract.Route.Equals(key.Route, StringComparison.Ordinal);
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

        return normalized.Length > 1
            ? normalized.TrimEnd('/')
            : normalized;
    }

    private readonly record struct EndpointKey(string Method, string Route)
    {
        public override string ToString() => $"{Method} {Route}";
    }

    private sealed class EndpointKeyComparer : IEqualityComparer<EndpointKey>
    {
        public static EndpointKeyComparer Instance { get; } = new();

        public bool Equals(EndpointKey left, EndpointKey right)
        {
            return left.Method.Equals(right.Method, StringComparison.Ordinal)
                && left.Route.Equals(right.Route, StringComparison.Ordinal);
        }

        public int GetHashCode(EndpointKey key)
        {
            return HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(key.Method),
                StringComparer.Ordinal.GetHashCode(key.Route));
        }
    }

    private sealed record ExpectedEndpointContract(
        string? PermissionPolicy,
        bool RequiresCompanyContext,
        string Tag);

    private sealed record AuthorizationContract(
        string? Policy,
        string? Roles,
        string? AuthenticationSchemes);

    private sealed record EndpointContract(
        string Route,
        string DisplayName,
        IReadOnlyList<string> HttpMethods,
        IReadOnlyList<AuthorizationContract> Authorization,
        bool AllowsAnonymous,
        bool RequiresCompanyContext,
        IReadOnlyList<string> Tags);
}
