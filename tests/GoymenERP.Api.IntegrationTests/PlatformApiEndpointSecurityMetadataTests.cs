using GoymenERP.Api.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

public sealed class PlatformApiEndpointSecurityMetadataTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private const string HealthRoute = "/health/live";

    private static readonly IReadOnlyDictionary<EndpointKey, ExpectedEndpointContract>
        ReviewedPlatformEndpoints =
            new Dictionary<EndpointKey, ExpectedEndpointContract>(EndpointKeyComparer.Instance)
            {
                [Key(HttpMethods.Get, "/api/v1/organization/companies")] =
                    ExpectedEndpointContract.AuthenticatedOnly("Organization"),
                [Key(HttpMethods.Get, "/api/v1/organization/context")] =
                    ExpectedEndpointContract.EndpointPermission(
                        ErpPermissions.OrganizationContextRead,
                        "Organization"),
                [Key(HttpMethods.Get, "/api/v1/organization/sites")] =
                    ExpectedEndpointContract.EndpointPermission(
                        ErpPermissions.OrganizationSitesRead,
                        "Organization"),
                [Key(HttpMethods.Get, "/api/v1/organization/locations")] =
                    ExpectedEndpointContract.EndpointPermission(
                        ErpPermissions.OrganizationLocationsRead,
                        "Organization"),
                [Key(HttpMethods.Get, "/api/v1/organization/locations/{id:guid}")] =
                    ExpectedEndpointContract.EndpointPermission(
                        ErpPermissions.OrganizationLocationsRead,
                        "Organization"),
                [Key(HttpMethods.Put, "/api/v1/organization/locations/{id:guid}/active-state")] =
                    ExpectedEndpointContract.EndpointPermission(
                        ErpPermissions.OrganizationLocationsStatusUpdate,
                        "Organization"),
                [Key(HttpMethods.Get, "/api/v1/organization/work-centers")] =
                    ExpectedEndpointContract.EndpointPermission(
                        ErpPermissions.OrganizationWorkCentersRead,
                        "Organization"),
                [Key(HttpMethods.Get, "/api/v1/identity/me")] =
                    ExpectedEndpointContract.AuthenticatedOnly("Identity"),
                [Key(HttpMethods.Get, "/api/v1/audit/events")] =
                    ExpectedEndpointContract.EndpointPermission(
                        ErpPermissions.IdentityAuditRead,
                        "Audit",
                        includesInheritedDefaultPolicy: false),
                [Key(HttpMethods.Get, "/api/v1/counterparties")] =
                    ExpectedEndpointContract.DynamicAuthorizedCompanySet(
                        ErpPermissions.CounterpartyPartiesRead,
                        "Counterparty"),
                [Key(HttpMethods.Get, "/api/v1/counterparties/{partyId:guid}")] =
                    ExpectedEndpointContract.DynamicAuthorizedCompanySet(
                        ErpPermissions.CounterpartyPartiesRead,
                        "Counterparty"),
                [Key(HttpMethods.Post, "/api/v1/counterparties")] =
                    ExpectedEndpointContract.EndpointPermission(
                        ErpPermissions.CounterpartyPartiesCreate,
                        "Counterparty"),
                [Key(
                    HttpMethods.Put,
                    "/api/v1/counterparties/{partyId:guid}/company-account/active-state")] =
                    ExpectedEndpointContract.EndpointPermission(
                        ErpPermissions.CounterpartyAccountsStatusUpdate,
                        "Counterparty"),
            };

    private static readonly IReadOnlySet<EndpointKey> ReviewedNoRequiredCompanyContextEndpoints =
        new HashSet<EndpointKey>(EndpointKeyComparer.Instance)
        {
            Key(HttpMethods.Get, "/api/v1/organization/companies"),
            Key(HttpMethods.Get, "/api/v1/identity/me"),
            Key(HttpMethods.Get, "/api/v1/counterparties"),
            Key(HttpMethods.Get, "/api/v1/counterparties/{partyId:guid}"),
        };

    [Fact]
    public void PlatformApiEndpoints_MatchExactReviewedInventoryInTestingAndProductionLike()
    {
        var testingInventory = ReadRuntimeInventory(factory.Services);
        AssertNoViolations("Testing", factory.Services, testingInventory);

        using var productionLikeFactory = new ProductionLikeApiFactory();
        var productionLikeInventory = ReadRuntimeInventory(productionLikeFactory.Services);
        AssertNoViolations(
            "Production-like",
            productionLikeFactory.Services,
            productionLikeInventory);

        Assert.Equal(
            ToStableInventory(testingInventory),
            ToStableInventory(productionLikeInventory));
    }

    [Fact]
    public void Guard_DetectsAuthorizationAndCompanyContextDowngrades()
    {
        var safeInventory = BuildReviewedSyntheticInventory();
        Assert.Empty(FindViolations(safeInventory));

        var tenantKey = Key(HttpMethods.Get, "/api/v1/organization/context");
        var counterpartyReadKey = Key(HttpMethods.Get, "/api/v1/counterparties");
        var counterpartyWriteKey = Key(HttpMethods.Post, "/api/v1/counterparties");

        AssertMutationRejected(
            safeInventory,
            tenantKey,
            contract => contract with { Authorization = [] },
            "authorization metadata is missing");
        AssertMutationRejected(
            safeInventory,
            tenantKey,
            contract => contract with
            {
                Authorization =
                [
                    new(Policy: null, Roles: null, AuthenticationSchemes: null),
                    new("wrong.permission", Roles: null, AuthenticationSchemes: null),
                ],
            },
            "expected endpoint permission");
        AssertMutationRejected(
            safeInventory,
            tenantKey,
            contract => contract with
            {
                Authorization =
                [
                    new(Policy: null, Roles: null, AuthenticationSchemes: null),
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
                    new(Policy: null, Roles: null, AuthenticationSchemes: null),
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

        AssertMutationRejected(
            safeInventory,
            counterpartyReadKey,
            contract => contract with
            {
                Authorization =
                [
                    new(
                        ErpPermissions.CounterpartyPartiesRead,
                        Roles: null,
                        AuthenticationSchemes: null),
                ],
            },
            "dynamic authorized-company-set endpoint must not declare permission policies");
        AssertMutationRejected(
            safeInventory,
            counterpartyReadKey,
            contract => contract with { RequiresCompanyContext = true },
            "company context metadata");
        AssertMutationRejected(
            safeInventory,
            counterpartyWriteKey,
            contract => contract with
            {
                Authorization =
                [
                    new(Policy: null, Roles: null, AuthenticationSchemes: null),
                    new(
                        ErpPermissions.CounterpartyAccountsStatusUpdate,
                        Roles: null,
                        AuthenticationSchemes: null),
                ],
            },
            "expected endpoint permission");
        AssertMutationRejected(
            safeInventory,
            counterpartyWriteKey,
            contract => contract with { RequiresCompanyContext = false },
            "company context metadata");
    }

    [Fact]
    public void Guard_DetectsHealthAndAnonymousRouteDowngrades()
    {
        var safeInventory = BuildReviewedSyntheticInventory();
        Assert.Empty(FindViolations(safeInventory));

        AssertHealthMutationRejected(
            safeInventory,
            contract => contract with { HttpMethods = [HttpMethods.Post] },
            "must declare exactly GET");
        AssertHealthMutationRejected(
            safeInventory,
            contract => contract with { HttpMethods = [] },
            "must declare exactly GET");
        AssertHealthMutationRejected(
            safeInventory,
            contract => contract with { AllowsAnonymous = false },
            "must declare AllowAnonymous");
        AssertHealthMutationRejected(
            safeInventory,
            contract => contract with
            {
                Authorization = [new(Policy: null, Roles: null, AuthenticationSchemes: null)],
            },
            "must not declare authorization metadata");
        AssertHealthMutationRejected(
            safeInventory,
            contract => contract with { RequiresCompanyContext = true },
            "must not require company context");

        var anonymousExpansion = safeInventory
            .Append(new EndpointContract(
                "/diagnostics/anonymous",
                "GET /diagnostics/anonymous",
                [HttpMethods.Get],
                Authorization: [],
                AllowsAnonymous: true,
                RequiresCompanyContext: false,
                Tags: []))
            .ToArray();
        Assert.Contains(
            FindViolations(anonymousExpansion),
            violation => violation.Contains(
                "AllowAnonymous is permitted only on exact GET /health/live",
                StringComparison.Ordinal));
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
            contract => contract with { Tags = ["Organization", "Organization"] },
            "endpoint tags");

        var missingInventory = safeInventory
            .Where(contract => !Matches(contract, tenantKey))
            .ToArray();
        Assert.Contains(
            FindViolations(missingInventory),
            violation => violation.Contains("Missing reviewed platform endpoint", StringComparison.Ordinal));

        var extraInventory = safeInventory
            .Append(CreateSafeSyntheticContract(
                Key(HttpMethods.Get, "/api/v1/other/new-endpoint"),
                ExpectedEndpointContract.EndpointPermission("other.read", "Other")))
            .ToArray();
        Assert.Contains(
            FindViolations(extraInventory),
            violation => violation.Contains("Unexpected runtime endpoint", StringComparison.Ordinal));
    }

    private static void AssertNoViolations(
        string environmentName,
        IServiceProvider services,
        EndpointContract[] contracts)
    {
        var violations = FindViolations(contracts).ToList();
        violations.AddRange(FindAuthorizationPolicyViolations(services));

        Assert.True(
            violations.Count == 0,
            $"{environmentName} platform API security metadata contract violations:"
            + $"{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static EndpointContract[] ReadRuntimeInventory(IServiceProvider services)
    {
        var endpointDataSource = services.GetRequiredService<EndpointDataSource>();
        return endpointDataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Select(ToContract)
            .OrderBy(contract => contract.Route, StringComparer.Ordinal)
            .ThenBy(contract => contract.DisplayName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> FindAuthorizationPolicyViolations(
        IServiceProvider services)
    {
        var violations = new List<string>();
        var options = services.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        ValidateAuthenticatedErpUserPolicy("DefaultPolicy", options.DefaultPolicy, violations);
        ValidateAuthenticatedErpUserPolicy("FallbackPolicy", options.FallbackPolicy, violations);
        return violations;
    }

    private static void ValidateAuthenticatedErpUserPolicy(
        string name,
        AuthorizationPolicy? policy,
        ICollection<string> violations)
    {
        if (policy is null)
        {
            violations.Add($"{name}: policy is missing.");
            return;
        }

        var denyAnonymousCount = policy.Requirements
            .OfType<DenyAnonymousAuthorizationRequirement>()
            .Count();
        var erpUserClaims = policy.Requirements
            .OfType<ClaimsAuthorizationRequirement>()
            .Where(requirement => string.Equals(
                requirement.ClaimType,
                GoymenClaimTypes.UserId,
                StringComparison.Ordinal))
            .ToArray();

        if (denyAnonymousCount != 1)
        {
            violations.Add(
                $"{name}: authenticated-user requirement must occur exactly once; found {denyAnonymousCount}.");
        }

        if (erpUserClaims.Length != 1
            || erpUserClaims[0].AllowedValues is not null)
        {
            violations.Add(
                $"{name}: '{GoymenClaimTypes.UserId}' presence requirement must occur exactly once.");
        }

        var unexpectedRequirements = policy.Requirements
            .Where(requirement => requirement switch
            {
                DenyAnonymousAuthorizationRequirement => false,
                ClaimsAuthorizationRequirement claimsRequirement => !string.Equals(
                    claimsRequirement.ClaimType,
                    GoymenClaimTypes.UserId,
                    StringComparison.Ordinal),
                _ => true,
            })
            .ToArray();
        if (unexpectedRequirements.Length != 0)
        {
            violations.Add(
                $"{name}: unexpected authorization requirements found: "
                + string.Join(", ", unexpectedRequirements.Select(x => x.GetType().Name)));
        }
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
        ValidateHealthContract(healthEndpoints, violations);

        foreach (var anonymousEndpoint in allContracts.Where(contract => contract.AllowsAnonymous))
        {
            if (!IsExactHealthContractKey(anonymousEndpoint))
            {
                violations.Add(
                    $"{anonymousEndpoint.DisplayName}: AllowAnonymous is permitted only on exact GET {HealthRoute}.");
            }
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
                    $"{contract.DisplayName}: platform endpoint must declare exactly one HTTP method; "
                    + $"found [{string.Join(", ", contract.HttpMethods)}].");
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

        var noRequiredCompanyContextContracts = platformContracts
            .Where(contract => !contract.RequiresCompanyContext)
            .Where(contract => contract.HttpMethods.Count == 1)
            .Select(contract => Key(contract.HttpMethods[0], contract.Route))
            .ToArray();
        if (noRequiredCompanyContextContracts.Length
            != ReviewedNoRequiredCompanyContextEndpoints.Count)
        {
            violations.Add(
                $"Expected exactly {ReviewedNoRequiredCompanyContextEndpoints.Count} reviewed "
                + "endpoints without mandatory CompanyContext; found "
                + $"{noRequiredCompanyContextContracts.Length}.");
        }

        foreach (var expected in ReviewedNoRequiredCompanyContextEndpoints)
        {
            var count = noRequiredCompanyContextContracts.Count(actual =>
                EndpointKeyComparer.Instance.Equals(actual, expected));
            if (count != 1)
            {
                violations.Add(
                    $"No-required-CompanyContext allowlist entry '{expected}' must occur exactly once; "
                    + $"found {count}.");
            }
        }

        foreach (var actual in noRequiredCompanyContextContracts)
        {
            if (!ReviewedNoRequiredCompanyContextEndpoints.Contains(actual))
            {
                violations.Add(
                    $"Unexpected no-required-CompanyContext endpoint: '{actual}'.");
            }
        }

        return violations;
    }

    private static void ValidateHealthContract(
        IReadOnlyCollection<EndpointContract> healthEndpoints,
        ICollection<string> violations)
    {
        if (healthEndpoints.Count != 1)
        {
            violations.Add(
                $"Expected exactly one health endpoint '{HealthRoute}', found {healthEndpoints.Count}.");
            return;
        }

        var health = healthEndpoints.Single();
        if (health.HttpMethods.Count != 1
            || !health.HttpMethods[0].Equals(HttpMethods.Get, StringComparison.Ordinal))
        {
            violations.Add(
                $"{HealthRoute}: health endpoint must declare exactly GET; found "
                + $"[{string.Join(", ", health.HttpMethods)}].");
        }

        if (!health.AllowsAnonymous)
        {
            violations.Add($"GET {HealthRoute}: health endpoint must declare AllowAnonymous.");
        }

        if (health.Authorization.Count != 0)
        {
            violations.Add($"GET {HealthRoute}: health endpoint must not declare authorization metadata.");
        }

        if (health.RequiresCompanyContext)
        {
            violations.Add($"GET {HealthRoute}: health endpoint must not require company context.");
        }
    }

    private static bool IsExactHealthContractKey(EndpointContract contract)
    {
        return contract.Route.Equals(HealthRoute, StringComparison.Ordinal)
            && contract.HttpMethods.Count == 1
            && contract.HttpMethods[0].Equals(HttpMethods.Get, StringComparison.Ordinal);
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

        if (contract.Authorization.Any(metadata =>
                metadata.Policy is not null
                && string.IsNullOrWhiteSpace(metadata.Policy)))
        {
            violations.Add($"{key}: blank authorization policy metadata is forbidden.");
        }

        if (contract.Authorization.Any(metadata => metadata.Roles is not null))
        {
            violations.Add($"{key}: role authorization metadata is unexpected.");
        }

        if (contract.Authorization.Any(metadata => metadata.AuthenticationSchemes is not null))
        {
            violations.Add($"{key}: authentication scheme metadata is unexpected.");
        }

        var policies = contract.Authorization
            .Where(metadata => !string.IsNullOrWhiteSpace(metadata.Policy))
            .Select(metadata => metadata.Policy!)
            .ToArray();
        switch (expected.AuthorizationMode)
        {
            case AuthorizationMode.AuthenticatedOnly:
                if (contract.Authorization.Count != 1 || policies.Length != 0)
                {
                    violations.Add(
                        $"{key}: authenticated-only endpoint must have exactly one default-policy "
                        + $"authorization entry; found {contract.Authorization.Count} entries and "
                        + $"policies [{string.Join(", ", policies)}].");
                }
                break;
            case AuthorizationMode.DynamicAuthorizedCompanySet:
                if (contract.Authorization.Count != 1 || policies.Length != 0)
                {
                    violations.Add(
                        $"{key}: dynamic authorized-company-set endpoint must not declare permission "
                        + $"policies and must inherit default authorization exactly once; found "
                        + $"{contract.Authorization.Count} entries and policies "
                        + $"[{string.Join(", ", policies)}].");
                }

                if (!string.Equals(
                        expected.Permission,
                        ErpPermissions.CounterpartyPartiesRead,
                        StringComparison.Ordinal))
                {
                    violations.Add(
                        $"{key}: dynamic read contract must retain effective permission "
                        + $"'{ErpPermissions.CounterpartyPartiesRead}'.");
                }
                break;
            case AuthorizationMode.EndpointPermission:
                if (contract.Authorization.Count != expected.AuthorizationEntryCount
                    || policies.Length != 1
                    || !policies[0].Equals(expected.Permission, StringComparison.Ordinal))
                {
                    violations.Add(
                        $"{key}: expected endpoint permission '{expected.Permission}' exactly once "
                        + $"within exactly {expected.AuthorizationEntryCount} authorization entries; found "
                        + $"{contract.Authorization.Count} entries and policies "
                        + $"[{string.Join(", ", policies)}].");
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(expected),
                    expected.AuthorizationMode,
                    "Unsupported authorization mode.");
        }

        if (contract.Tags.Count != 1
            || !contract.Tags[0].Equals(expected.Tag, StringComparison.Ordinal))
        {
            violations.Add(
                $"{key}: endpoint tags must equal [{expected.Tag}]; found "
                + $"[{string.Join(", ", contract.Tags)}].");
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
        AuthorizationContract[] authorization = expected.AuthorizationMode switch
        {
            AuthorizationMode.AuthenticatedOnly or AuthorizationMode.DynamicAuthorizedCompanySet =>
                [new(Policy: null, Roles: null, AuthenticationSchemes: null)],
            AuthorizationMode.EndpointPermission when expected.AuthorizationEntryCount == 2 =>
                [
                    new(Policy: null, Roles: null, AuthenticationSchemes: null),
                    new(expected.Permission, Roles: null, AuthenticationSchemes: null),
                ],
            AuthorizationMode.EndpointPermission when expected.AuthorizationEntryCount == 1 =>
                [new(expected.Permission, Roles: null, AuthenticationSchemes: null)],
            _ => throw new ArgumentOutOfRangeException(
                nameof(expected),
                expected.AuthorizationMode,
                "Unsupported authorization mode."),
        };

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

    private static void AssertHealthMutationRejected(
        EndpointContract[] safeInventory,
        Func<EndpointContract, EndpointContract> mutation,
        string expectedViolation)
    {
        var mutated = safeInventory
            .Select(contract =>
                contract.Route.Equals(HealthRoute, StringComparison.Ordinal)
                    ? mutation(contract)
                    : contract)
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

    private static string[] ToStableInventory(IEnumerable<EndpointContract> contracts)
    {
        return contracts
            .Select(contract =>
                $"{string.Join('+', contract.HttpMethods)} {contract.Route} | "
                + $"auth={string.Join(',', contract.Authorization.Select(x => x.Policy ?? "<default>"))} | "
                + $"anonymous={contract.AllowsAnonymous} | company={contract.RequiresCompanyContext} | "
                + $"tags={string.Join(',', contract.Tags)}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
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

    private enum AuthorizationMode
    {
        AuthenticatedOnly,
        EndpointPermission,
        DynamicAuthorizedCompanySet,
    }

    private sealed record ExpectedEndpointContract(
        AuthorizationMode AuthorizationMode,
        string? Permission,
        bool RequiresCompanyContext,
        string Tag,
        int AuthorizationEntryCount)
    {
        public static ExpectedEndpointContract AuthenticatedOnly(string tag)
        {
            return new(
                AuthorizationMode.AuthenticatedOnly,
                Permission: null,
                RequiresCompanyContext: false,
                tag,
                AuthorizationEntryCount: 1);
        }

        public static ExpectedEndpointContract EndpointPermission(
            string permission,
            string tag,
            bool includesInheritedDefaultPolicy = true)
        {
            return new(
                AuthorizationMode.EndpointPermission,
                permission,
                RequiresCompanyContext: true,
                tag,
                includesInheritedDefaultPolicy ? 2 : 1);
        }

        public static ExpectedEndpointContract DynamicAuthorizedCompanySet(
            string permission,
            string tag)
        {
            return new(
                AuthorizationMode.DynamicAuthorizedCompanySet,
                permission,
                RequiresCompanyContext: false,
                tag,
                AuthorizationEntryCount: 1);
        }
    }

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

    private sealed class ProductionLikeApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting(
                "DB_CONNECTION_STRING",
                "Server=production-like;Database=production-like;User Id=test;Password=test;");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Authentication:Mode"] = "OidcJwtBearer",
                    ["Authentication:Oidc:Authority"] = "https://identity.example.test",
                    ["Authentication:Oidc:Audience"] = "goymen-erp-production-like-tests",
                    ["Authentication:Oidc:RequireHttpsMetadata"] = "true",
                });
            });
        }
    }
}
