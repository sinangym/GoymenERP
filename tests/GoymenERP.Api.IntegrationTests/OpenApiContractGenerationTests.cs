using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.OpenApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class OpenApiContractGenerationTests
{
    private const string DocumentName = "v1";
    private const string CanonicalFileName = "openapi.json";

    private static readonly IReadOnlyList<OperationExpectation> ExpectedOperations =
    [
        new("get", "/health/live", "GetLivenessV1",
            ["200:text/plain", "400:application/problem+json", "500:application/problem+json", "503:application/problem+json"]),
        new("get", "/api/v1/audit/events", "GetAuditEventsV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json"),
            ParameterValues:
            [
                "query:pageSize:false:type:integer|string;format:int32;pattern:^-?(?:0|[1-9]\\d*)$;normalized-minimum:1;normalized-maximum:100",
                "query:cursor:false:type:string;canonicalization:trim;canonical-max-length:128",
                "query:correlationId:false:type:string;canonicalization:trim;canonical-max-length:128",
                "query:entityType:false:type:string;canonicalization:trim;canonical-max-length:160",
                "query:entityId:false:type:string;canonicalization:trim;canonical-max-length:128",
            ]),
        new("get", "/api/v1/organization/companies", "GetAuthorizedCompaniesV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json")),
        new("get", "/api/v1/organization/context", "GetOrganizationContextV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json")),
        new("get", "/api/v1/organization/sites", "GetOrganizationSitesV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json")),
        new("get", "/api/v1/organization/locations", "GetOrganizationLocationsV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json"),
            ParameterValues:
            [
                "query:page:false:type:integer|string;format:int32;pattern:^-?(?:0|[1-9]\\d*)$",
                "query:pageSize:false:type:integer|string;format:int32;pattern:^-?(?:0|[1-9]\\d*)$",
            ]),
        new("get", "/api/v1/organization/locations/{id}", "GetOrganizationLocationV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "404:application/problem+json"),
            ParameterValues: ["path:id:true:type:string;format:uuid"]),
        new("put", "/api/v1/organization/locations/{id}/active-state", "SetOrganizationLocationActiveStateV1",
            AuthenticatedResponses("204:", "400:application/problem+json", "404:application/problem+json", "409:application/problem+json", "415:"),
            "#/components/schemas/SetLocationActiveState",
            ["path:id:true:type:string;format:uuid"]),
        new("get", "/api/v1/organization/work-centers", "GetOrganizationWorkCentersV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json")),
        new("get", "/api/v1/identity/me", "GetCurrentIdentityV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json")),
        new("get", "/api/v1/counterparties", "GetCounterpartiesV1",
            AuthenticatedResponses(
                "200:application/json",
                "400:application/problem+json",
                "503:application/problem+json"),
            ParameterValues:
            [
                "query:q:false:type:string;canonicalization:unicode-trim+nfc",
                "query:kind:false:type:string;pattern:^(LegalEntity|NaturalPerson)$",
                "query:role:false:type:array;items:type:string",
                "query:roleMatch:false:type:string;pattern:^(any|all)$",
                "query:partyState:false:type:string;pattern:^(active|inactive|all)$",
                "query:accountState:false:type:string;pattern:^(active|inactive|all)$",
                "query:sort:false:type:string;pattern:^(code|legalName|updatedAt)$",
                "query:direction:false:type:string;pattern:^(asc|desc)$",
                "query:pageSize:false:type:integer",
                "query:cursor:false:type:string",
                "query:includeCount:false:type:boolean",
            ]),
        new("get", "/api/v1/counterparties/company-account-link-candidates",
            "GetCounterpartyCompanyAccountLinkCandidatesV1",
            AuthenticatedResponses(
                "200:application/json",
                "400:application/problem+json",
                "503:application/problem+json"),
            ParameterValues:
            [
                "query:q:false:type:string;canonicalization:unicode-trim+nfc",
                "query:pageSize:false:type:integer",
                "query:cursor:false:type:string",
            ]),
        new("post", "/api/v1/counterparties", "CreateCounterpartyV1",
            AuthenticatedResponses("201:application/json", "400:application/problem+json", "409:application/problem+json", "415:"),
            "#/components/schemas/CreateCounterparty"),
        new("get", "/api/v1/counterparties/{partyId}", "GetCounterpartyV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "404:application/problem+json"),
            ParameterValues:
            [
                "path:partyId:true:type:string;format:uuid",
            ]),
        new("post", "/api/v1/counterparties/{partyId}/company-account", "CreateCounterpartyCompanyAccountV1",
            AuthenticatedResponses("201:application/json", "400:application/problem+json", "404:application/problem+json", "409:application/problem+json", "415:"),
            "#/components/schemas/CreateCompanyPartyAccount",
            ["path:partyId:true:type:string;format:uuid;non-empty-uuid:true"]),
        new("put", "/api/v1/counterparties/{partyId}/company-account/active-state", "SetCounterpartyCompanyAccountActiveStateV1",
            AuthenticatedResponses("204:", "400:application/problem+json", "404:application/problem+json", "409:application/problem+json", "415:"),
            "#/components/schemas/SetCompanyPartyAccountActiveState",
            ["path:partyId:true:type:string;format:uuid;non-empty-uuid:true"]),
        new("get", "/api/v1/shell/capabilities", "GetShellCapabilitiesV1",
            AuthenticatedResponses(
                "200:application/json",
                "400:application/problem+json",
                "503:application/problem+json")),
    ];

    private static readonly IReadOnlyDictionary<string, string> ExpectedSuccessSchemas =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GetLivenessV1"] = "type:string",
            ["GetAuditEventsV1"] = "ref:AuditEventPage",
            ["GetAuthorizedCompaniesV1"] = "array:CompanySummary",
            ["GetOrganizationContextV1"] = "ref:CompanyContextResponse",
            ["GetOrganizationSitesV1"] = "array:SiteSummary",
            ["GetOrganizationLocationsV1"] = "ref:PageResultOfLocationSummary",
            ["GetOrganizationLocationV1"] = "ref:LocationSummary",
            ["SetOrganizationLocationActiveStateV1"] = "none",
            ["GetOrganizationWorkCentersV1"] = "array:WorkCenterSummary",
            ["GetCurrentIdentityV1"] = "ref:IdentityProfile",
            ["GetCounterpartiesV1"] = "ref:CounterpartyPage",
            ["GetCounterpartyCompanyAccountLinkCandidatesV1"] =
                "ref:CounterpartyLinkCandidatePage",
            ["CreateCounterpartyV1"] = "ref:CounterpartyMutationView",
            ["GetCounterpartyV1"] = "ref:CounterpartyDetailView",
            ["CreateCounterpartyCompanyAccountV1"] = "ref:CounterpartyMutationView",
            ["SetCounterpartyCompanyAccountActiveStateV1"] = "none",
            ["GetShellCapabilitiesV1"] = "ref:ShellCapabilitySnapshot",
        };

    private static readonly HashSet<string> RequiredCompanyOperations =
    [
        "GetAuditEventsV1",
        "GetOrganizationContextV1",
        "GetOrganizationSitesV1",
        "GetOrganizationLocationsV1",
        "GetOrganizationLocationV1",
        "SetOrganizationLocationActiveStateV1",
        "GetOrganizationWorkCentersV1",
        "GetCounterpartyCompanyAccountLinkCandidatesV1",
        "CreateCounterpartyV1",
        "CreateCounterpartyCompanyAccountV1",
        "SetCounterpartyCompanyAccountActiveStateV1",
    ];

    private static readonly HashSet<string> OptionalCompanyOperations =
    [
        "GetCounterpartiesV1",
        "GetCounterpartyV1",
        "GetShellCapabilitiesV1",
    ];

    private static readonly HashSet<string> IdempotentOperations =
    [
        "SetOrganizationLocationActiveStateV1",
        "CreateCounterpartyV1",
        "CreateCounterpartyCompanyAccountV1",
        "SetCounterpartyCompanyAccountActiveStateV1",
    ];

    private static readonly IReadOnlyDictionary<string, HashSet<int>>
        ReplayableResponseStatuses =
            new Dictionary<string, HashSet<int>>(StringComparer.Ordinal)
            {
                ["SetOrganizationLocationActiveStateV1"] =
                [
                    StatusCodes.Status204NoContent,
                    StatusCodes.Status404NotFound,
                    StatusCodes.Status409Conflict,
                ],
                ["CreateCounterpartyV1"] = [StatusCodes.Status201Created],
                ["CreateCounterpartyCompanyAccountV1"] =
                [
                    StatusCodes.Status201Created,
                    StatusCodes.Status404NotFound,
                    StatusCodes.Status409Conflict,
                ],
                ["SetCounterpartyCompanyAccountActiveStateV1"] =
                [
                    StatusCodes.Status204NoContent,
                    StatusCodes.Status404NotFound,
                    StatusCodes.Status409Conflict,
                ],
            };

    private static readonly HashSet<string> EmptyBodyPossible400Operations =
    [
        "GetAuditEventsV1",
        "GetOrganizationLocationsV1",
        "SetOrganizationLocationActiveStateV1",
        "CreateCounterpartyV1",
        "CreateCounterpartyCompanyAccountV1",
        "SetCounterpartyCompanyAccountActiveStateV1",
    ];

    private static readonly HashSet<string> CodeLessProblemResponses =
    [
        "GetOrganizationLocationV1:404",
        "SetOrganizationLocationActiveStateV1:404",
        "SetOrganizationLocationActiveStateV1:409",
    ];

    private static readonly HashSet<string> MixedProblemResponses =
    [
        "GetAuditEventsV1:400",
        "SetOrganizationLocationActiveStateV1:400",
    ];

    private static readonly IReadOnlyDictionary<string, string[]> AdditionalProblemCodes =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["GetCounterpartiesV1:400"] = ["invalid_cursor", "validation_failed"],
            ["GetCounterpartyCompanyAccountLinkCandidatesV1:400"] =
                ["invalid_cursor", "validation_failed"],
            ["GetCounterpartyV1:400"] = ["validation_failed"],
            ["GetCounterpartyV1:404"] = ["not_found"],
            ["CreateCounterpartyV1:400"] = ["command_metadata_invalid", "validation_failed"],
            ["CreateCounterpartyV1:409"] = ["idempotency_conflict"],
            ["CreateCounterpartyCompanyAccountV1:400"] =
                ["command_metadata_invalid", "validation_failed"],
            ["CreateCounterpartyCompanyAccountV1:404"] = ["not_found"],
            ["CreateCounterpartyCompanyAccountV1:409"] =
                ["already_linked", "idempotency_conflict"],
            ["SetCounterpartyCompanyAccountActiveStateV1:400"] =
                ["command_metadata_invalid", "validation_failed"],
            ["SetCounterpartyCompanyAccountActiveStateV1:404"] = ["not_found"],
            ["SetCounterpartyCompanyAccountActiveStateV1:409"] =
                ["idempotency_conflict", "version_conflict"],
            ["GetShellCapabilitiesV1:400"] = ["company_context_invalid"],
        };

    private static readonly IReadOnlyDictionary<string, ComponentSchemaExpectation>
        ExpectedSuccessComponentSchemas =
            new Dictionary<string, ComponentSchemaExpectation>(StringComparer.Ordinal)
            {
                ["AuditEventItem"] = ObjectSchema(
                [
                    "id", "companyId", "occurredAtUtc", "eventType", "entityType",
                    "entityId", "operation", "entityVersionBefore", "entityVersionAfter",
                    "actorType", "actorId", "permissionCode", "roleCodes", "beforeJson",
                    "afterJson", "reason", "approvalContextJson", "aiInvolvement",
                    "aiContextJson", "sourceChannel", "clientIpAddress", "userAgent",
                    "correlationId", "causationId", "outcome", "schemaVersion",
                    "integrityHash",
                ],
                ("id", "type:string;format:uuid"),
                ("companyId", "type:string;format:uuid"),
                ("occurredAtUtc", "type:string;format:date-time"),
                ("eventType", "type:string"),
                ("entityType", "type:string"),
                ("entityId", "type:string"),
                ("operation", "type:string"),
                ("entityVersionBefore", "type:integer|null;format:int64"),
                ("entityVersionAfter", "type:integer|null;format:int64"),
                ("actorType", "type:string"),
                ("actorId", "type:string"),
                ("permissionCode", "type:string"),
                ("roleCodes", "type:array;items:type:string"),
                ("beforeJson", "type:string"),
                ("afterJson", "type:string"),
                ("reason", "type:string"),
                ("approvalContextJson", "type:null|string"),
                ("aiInvolvement", "type:string"),
                ("aiContextJson", "type:null|string"),
                ("sourceChannel", "type:string"),
                ("clientIpAddress", "type:null|string"),
                ("userAgent", "type:null|string"),
                ("correlationId", "type:string"),
                ("causationId", "type:null|string"),
                ("outcome", "type:string"),
                ("schemaVersion", "type:integer;format:int32"),
                ("integrityHash", "type:string")),
                ["AuditEventPage"] = ObjectSchema(
                    ["items", "pageSize", "nextCursor"],
                    ("items", "type:array;items:ref:AuditEventItem"),
                    ("pageSize", "type:integer;format:int32"),
                    ("nextCursor", "type:null|string")),
                ["CompanyContextResponse"] = ObjectSchema(
                    ["companyId"],
                    ("companyId", "type:string;format:uuid")),
                ["CounterpartyDetailAccountView"] = ObjectSchema(
                    ["id", "companyId", "roles", "isActive", "version"],
                    ("id", "type:string;format:uuid"),
                    ("companyId", "type:string;format:uuid"),
                    ("roles", "ref:PartyRole"),
                    ("isActive", "type:boolean"),
                    ("version", "type:string")),
                ["CounterpartyDetailView"] = ObjectSchema(
                    ["id", "code", "kind", "legalName", "tradeName", "isActive", "version", "companyAccounts"],
                    ("id", "type:string;format:uuid"),
                    ("code", "type:string"),
                    ("kind", "ref:PartyKind"),
                    ("legalName", "type:string"),
                    ("tradeName", "type:null|string"),
                    ("isActive", "type:boolean"),
                    ("version", "type:string"),
                    ("companyAccounts", "type:array;items:ref:CounterpartyDetailAccountView")),
                ["CounterpartyListAccount"] = ObjectSchema(
                    ["id", "companyId", "roles", "isActive", "version"],
                    ("id", "type:string;format:uuid"),
                    ("companyId", "type:string;format:uuid"),
                    ("roles", "ref:PartyRole"),
                    ("isActive", "type:boolean"),
                    ("version", "type:integer;format:int64")),
                ["CounterpartyListItem"] = ObjectSchema(
                    ["id", "code", "kind", "legalName", "tradeName", "isActive", "version", "companyAccounts"],
                    ("id", "type:string;format:uuid"),
                    ("code", "type:string"),
                    ("kind", "ref:PartyKind"),
                    ("legalName", "type:string"),
                    ("tradeName", "type:null|string"),
                    ("isActive", "type:boolean"),
                    ("version", "type:integer;format:int64"),
                    ("companyAccounts", "type:array;items:ref:CounterpartyListAccount")),
                ["CounterpartyLinkCandidateSourceAccount"] = ObjectSchema(
                    ["id", "companyId", "roles", "isActive", "version"],
                    ("id", "type:string;format:uuid"),
                    ("companyId", "type:string;format:uuid"),
                    ("roles", "ref:PartyRole"),
                    ("isActive", "type:boolean"),
                    ("version", "type:string")),
                ["CounterpartyLinkCandidateItem"] = ObjectSchema(
                    ["id", "code", "kind", "legalName", "tradeName", "isActive", "visibleSourceAccounts"],
                    ("id", "type:string;format:uuid"),
                    ("code", "type:string"),
                    ("kind", "ref:PartyKind"),
                    ("legalName", "type:string"),
                    ("tradeName", "type:null|string"),
                    ("isActive", "type:boolean"),
                    ("visibleSourceAccounts", "type:array;items:ref:CounterpartyLinkCandidateSourceAccount")),
                ["CounterpartyLinkCandidatePage"] = ObjectSchema(
                    ["items", "nextCursor", "hasMore"],
                    ("items", "type:array;items:ref:CounterpartyLinkCandidateItem"),
                    ("nextCursor", "type:null|string"),
                    ("hasMore", "type:boolean")),
                ["CounterpartyMutationAccountView"] = ObjectSchema(
                    ["id", "companyId", "roles", "isActive", "version"],
                    ("id", "type:string;format:uuid"),
                    ("companyId", "type:string;format:uuid"),
                    ("roles", "ref:PartyRole"),
                    ("isActive", "type:boolean"),
                    ("version", "type:string")),
                ["CounterpartyMutationView"] = ObjectSchema(
                    ["id", "code", "kind", "legalName", "tradeName", "isActive", "version", "companyAccounts"],
                    ("id", "type:string;format:uuid"),
                    ("code", "type:string"),
                    ("kind", "ref:PartyKind"),
                    ("legalName", "type:string"),
                    ("tradeName", "type:null|string"),
                    ("isActive", "type:boolean"),
                    ("version", "type:string"),
                    ("companyAccounts", "type:array;items:ref:CounterpartyMutationAccountView")),
                ["CounterpartyPage"] = ObjectSchema(
                    ["items", "nextCursor", "hasMore", "totalCount", "countScope"],
                    ("items", "type:array;items:ref:CounterpartyListItem"),
                    ("nextCursor", "type:null|string"),
                    ("hasMore", "type:boolean"),
                    ("totalCount", "type:integer|null|string;format:int64"),
                    ("countScope", "type:null|string")),
                ["CompanySummary"] = ObjectSchema(
                    ["id", "code", "legalName", "isPlaceholder"],
                    ("id", "type:string;format:uuid"),
                    ("code", "type:string"),
                    ("legalName", "type:string"),
                    ("isPlaceholder", "type:boolean")),
                ["IdentityProfile"] = ObjectSchema(
                    ["userId", "displayName", "email", "allowedCompanyIds"],
                    ("userId", "type:string;format:uuid"),
                    ("displayName", "type:string"),
                    ("email", "type:null|string"),
                    ("allowedCompanyIds", "type:array;items:type:string;format:uuid")),
                ["LocationSummary"] = ObjectSchema(
                    ["id", "siteId", "code", "name", "isExternal", "isActive", "version"],
                    ("id", "type:string;format:uuid"),
                    ("siteId", "type:string;format:uuid"),
                    ("code", "type:string"),
                    ("name", "type:string"),
                    ("isExternal", "type:boolean"),
                    ("isActive", "type:boolean"),
                    ("version", "type:integer;format:int64")),
                ["PageResultOfLocationSummary"] = ObjectSchema(
                    ["items", "page", "pageSize", "totalCount"],
                    ("items", "type:array;items:ref:LocationSummary"),
                    ("page", "type:integer;format:int32"),
                    ("pageSize", "type:integer;format:int32"),
                    ("totalCount", "type:integer;format:int32")),
                ["PartyKind"] = ScalarSchema("type:integer"),
                ["PartyRole"] = ScalarSchema("type:integer"),
                ["ShellCapabilitySnapshot"] = ObjectSchema(
                    ["contractVersion", "generation", "capabilities"],
                    ("contractVersion", "type:string"),
                    ("generation", "type:string"),
                    ("capabilities", "type:array;items:type:string")),
                ["SiteSummary"] = ObjectSchema(
                    ["id", "code", "name", "type", "isActive"],
                    ("id", "type:string;format:uuid"),
                    ("code", "type:string"),
                    ("name", "type:string"),
                    ("type", "type:string"),
                    ("isActive", "type:boolean")),
                ["WorkCenterSummary"] = ObjectSchema(
                    ["id", "locationId", "code", "name", "kind", "isActive"],
                    ("id", "type:string;format:uuid"),
                    ("locationId", "type:string;format:uuid"),
                    ("code", "type:string"),
                    ("name", "type:string"),
                    ("kind", "type:string"),
                    ("isActive", "type:boolean")),
            };

    private static readonly IReadOnlyDictionary<string, ComponentSchemaExpectation>
        ExpectedRequestComponentSchemas =
            new Dictionary<string, ComponentSchemaExpectation>(StringComparer.Ordinal)
            {
                ["CreateCounterparty"] = ObjectSchema(
                    ["kind", "legalName"],
                    ("kind", "ref:PartyKind"),
                    ("legalName", "type:string"),
                    ("tradeName", "type:null|string"),
                    ("roles", "ref:PartyRole")),
                ["CreateCompanyPartyAccount"] = ObjectSchema(
                    [],
                    ("roles", "ref:PartyRole")),
                ["SetCompanyPartyAccountActiveState"] = ObjectSchema(
                    ["isActive", "expectedVersion", "reason"],
                    ("isActive", "type:boolean"),
                    ("expectedVersion", "type:integer|string;format:int64"),
                    ("reason", "type:string")),
                ["SetLocationActiveState"] = ObjectSchema(
                    ["isActive", "expectedVersion", "reason"],
                    ("isActive", "type:boolean"),
                    ("expectedVersion", "type:integer|string;format:int64"),
                    ("reason", "type:string")),
            };

    private static readonly IReadOnlyDictionary<string, ComponentSchemaExpectation>
        ExpectedProblemComponentSchemas =
            new Dictionary<string, ComponentSchemaExpectation>(StringComparer.Ordinal)
            {
                ["ErpProblemDetailsContract"] = ObjectSchema(
                    ["type", "title", "status", "detail", "code"],
                    ("type", "type:string"),
                    ("title", "type:string"),
                    ("status", "type:integer;format:int32"),
                    ("detail", "type:string"),
                    ("code", "type:string"),
                    ("correlationId", "type:null|string"),
                    ("conflict", "oneOf:ref:ErpVersionConflictContract|type:null")),
                ["ErpCodeLessProblemDetailsContract"] = ObjectSchema(
                    ["type", "title", "status", "detail"],
                    ("type", "type:string"),
                    ("title", "type:string"),
                    ("status", "type:integer;format:int32"),
                    ("detail", "type:string"),
                    ("conflict", "oneOf:ref:ErpVersionConflictContract|type:null")),
                ["ErpVersionConflictContract"] = ObjectSchema(
                    ["expectedVersion", "currentVersion"],
                    ("expectedVersion", "type:integer;format:int64"),
                    ("currentVersion", "type:integer;format:int64")),
            };

    [Fact]
    public async Task BuildTimeContract_IsDeterministicSecretFreeAndDoesNotContactConfiguredDependencies()
    {
        var repositoryRoot = FindRepositoryRoot();
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-erp-openapi-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);

        try
        {
            var first = await GenerateAsync(
                repositoryRoot,
                temporaryRoot,
                "Production",
                "first");
            var second = await GenerateAsync(
                repositoryRoot,
                temporaryRoot,
                "Production",
                "second");
            var development = await GenerateAsync(
                repositoryRoot,
                temporaryRoot,
                "Development",
                "development");

            Assert.Equal(first.Bytes, second.Bytes);
            Assert.Equal(first.Bytes, development.Bytes);
            Assert.Equal(0, first.AcceptedConnectionCount);
            Assert.Equal(0, second.AcceptedConnectionCount);
            Assert.Equal(0, development.AcceptedConnectionCount);

            var canonicalArtifactPath = Path.Combine(
                repositoryRoot,
                "artifacts",
                CanonicalFileName);
            Assert.True(
                File.Exists(canonicalArtifactPath),
                $"MSBuild canonical artifact was not emitted at {canonicalArtifactPath}.");
            Assert.Equal(
                first.Bytes,
                await File.ReadAllBytesAsync(canonicalArtifactPath));

            var contractText = Encoding.UTF8.GetString(first.Bytes);
            foreach (var forbiddenValue in new[]
                     {
                         first.SecretMarker,
                         second.SecretMarker,
                         development.SecretMarker,
                         repositoryRoot,
                         repositoryRoot.Replace('\\', '/'),
                         temporaryRoot,
                         temporaryRoot.Replace('\\', '/'),
                         Environment.MachineName,
                         Environment.UserName,
                         "openapi.invalid",
                         "127.0.0.1",
                         "DB_CONNECTION_STRING",
                         "Password=",
                         "SELECT ",
                     })
            {
                Assert.DoesNotContain(
                    forbiddenValue,
                    contractText,
                    StringComparison.OrdinalIgnoreCase);
            }

            AssertContractShape(first.Bytes);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task EndpointDataSource_MatchesTheGeneratedContractExactly()
    {
        await using var factory = new GoymenErpApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/live");
        response.EnsureSuccessStatusCode();

        var routeEndpoints = factory.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
        Assert.NotEmpty(routeEndpoints);
        var runtimeOperations = routeEndpoints
            .SelectMany(endpoint => SnapshotRuntimeEndpoint(endpoint))
            .OrderBy(operation => operation.Path, StringComparer.Ordinal)
            .ThenBy(operation => operation.Method, StringComparer.Ordinal)
            .ToArray();
        var expected = ExpectedOperations
            .Select(operation => new EndpointSnapshot(
                operation.Method,
                operation.Path,
                operation.OperationId))
            .OrderBy(operation => operation.Path, StringComparer.Ordinal)
            .ThenBy(operation => operation.Method, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, runtimeOperations);
    }

    [Fact]
    public async Task JsonCommands_RejectUnsupportedAndMalformedBodiesAtTheAdvertisedBoundary()
    {
        using var factory = new GoymenErpApiFactory();
        using var client = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyA);
        var partyId = Guid.Parse("30000000-0000-0000-0000-000000000001");
        var locationId = Guid.Parse("20000000-0000-0000-0000-000000000101");
        var commands = new[]
        {
            (HttpMethod.Post, "/api/v1/counterparties"),
            (HttpMethod.Post, $"/api/v1/counterparties/{partyId}/company-account"),
            (HttpMethod.Put, $"/api/v1/counterparties/{partyId}/company-account/active-state"),
            (HttpMethod.Put, $"/api/v1/organization/locations/{locationId}/active-state"),
        };

        foreach (var (method, path) in commands)
        {
            using var unsupportedRequest = new HttpRequestMessage(method, path)
            {
                Content = new StringContent("{}", Encoding.UTF8, "text/plain"),
            };
            using var unsupportedResponse = await client.SendAsync(unsupportedRequest);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, unsupportedResponse.StatusCode);
            Assert.Null(unsupportedResponse.Content.Headers.ContentType);
            Assert.Empty(await unsupportedResponse.Content.ReadAsByteArrayAsync());

            using var malformedRequest = new HttpRequestMessage(method, path)
            {
                Content = new StringContent("{", Encoding.UTF8, "application/json"),
            };
            using var malformedResponse = await client.SendAsync(malformedRequest);
            Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
            Assert.Null(malformedResponse.Content.Headers.ContentType);
            Assert.Empty(await malformedResponse.Content.ReadAsByteArrayAsync());
        }
    }

    [Fact]
    public async Task PrimitiveQueryBindingFailures_UseTheAdvertisedEmptyBadRequestVariant()
    {
        using var factory = new GoymenErpApiFactory();
        using var scopedAdminClient = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyA);
        var probes = new[]
        {
            (Client: scopedAdminClient, Path: "/api/v1/audit/events?pageSize=not-an-integer"),
            (Client: scopedAdminClient, Path: "/api/v1/organization/locations?page=not-an-integer"),
            (Client: scopedAdminClient, Path: "/api/v1/organization/locations?pageSize=not-an-integer"),
        };

        foreach (var (client, path) in probes)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Null(response.Content.Headers.ContentType);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        }
    }

    [Fact]
    public async Task RequiredStateFieldsAndNonEmptyCompanyBoundaries_FailClosedBeforeDurableExecution()
    {
        using var factory = new GoymenErpApiFactory();
        using var organizationClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        using var counterpartyClient = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyA);
        var location = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyA,
            "OSM-H6");
        var locationId = location.Id;
        var locationVersion = location.Version;

        var invalidLocationCommands = new[]
        {
            $"{{\"expectedVersion\":{locationVersion},\"reason\":\"isActive omission probe\"}}",
            "{\"isActive\":true,\"reason\":\"expectedVersion omission probe\"}",
            "{\"isActive\":true,\"expectedVersion\":0,\"reason\":\"zero version probe\"}",
            "{\"isActive\":true,\"expectedVersion\":\"-1\",\"reason\":\"quoted negative version probe\"}",
        };
        foreach (var json in invalidLocationCommands)
        {
            var key = $"openapi-invalid-org-{Guid.NewGuid():N}";
            using var response = await SendJsonCommandAsync(
                organizationClient,
                HttpMethod.Put,
                $"/api/v1/organization/locations/{locationId}/active-state",
                json,
                key);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await factory.GetCommandExecutionsAsync(key));
        }

        using var partyResponse = await SendJsonCommandAsync(
            counterpartyClient,
            HttpMethod.Post,
            "/api/v1/counterparties",
            "{\"kind\":1,\"legalName\":\"OpenAPI omission probe\",\"roles\":1}");
        Assert.Equal(HttpStatusCode.Created, partyResponse.StatusCode);
        using var partyDocument = JsonDocument.Parse(
            await partyResponse.Content.ReadAsStringAsync());
        var partyId = partyDocument.RootElement.GetProperty("id").GetGuid();
        var companyAccount = Assert.Single(
            partyDocument.RootElement.GetProperty("companyAccounts")
                .EnumerateArray(),
            account => account.GetProperty("companyId").GetGuid()
                == TestCompanyIds.CompanyA);
        var accountVersion = long.Parse(
            companyAccount.GetProperty("version").GetString()!,
            CultureInfo.InvariantCulture);

        var invalidCounterpartyCommands = new[]
        {
            $"{{\"expectedVersion\":{accountVersion},\"reason\":\"isActive omission probe\"}}",
            "{\"isActive\":true,\"reason\":\"expectedVersion omission probe\"}",
            "{\"isActive\":true,\"expectedVersion\":0,\"reason\":\"zero version probe\"}",
            "{\"isActive\":true,\"expectedVersion\":\"-1\",\"reason\":\"quoted negative version probe\"}",
        };
        foreach (var json in invalidCounterpartyCommands)
        {
            var key = $"openapi-invalid-cp-{Guid.NewGuid():N}";
            using var response = await SendJsonCommandAsync(
                counterpartyClient,
                HttpMethod.Put,
                $"/api/v1/counterparties/{partyId}/company-account/active-state",
                json,
                key);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await factory.GetCommandExecutionsAsync(key));
        }

        using var nilLink = await SendJsonCommandAsync(
            counterpartyClient,
            HttpMethod.Post,
            $"/api/v1/counterparties/{Guid.Empty}/company-account",
            "{\"roles\":1}");
        Assert.Equal(HttpStatusCode.BadRequest, nilLink.StatusCode);

        using var nilStatus = await SendJsonCommandAsync(
            counterpartyClient,
            HttpMethod.Put,
            $"/api/v1/counterparties/{Guid.Empty}/company-account/active-state",
            "{\"isActive\":true,\"expectedVersion\":1,\"reason\":\"nil guid probe\"}");
        Assert.Equal(HttpStatusCode.BadRequest, nilStatus.StatusCode);

        using var requiredEmptyCompanyClient = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            Guid.Empty);
        using var requiredEmptyCompany = await requiredEmptyCompanyClient.GetAsync(
            "/api/v1/organization/context");
        Assert.Equal(HttpStatusCode.BadRequest, requiredEmptyCompany.StatusCode);

        using var optionalEmptyCompanyClient = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            Guid.Empty);
        using var optionalEmptyCompany = await optionalEmptyCompanyClient.GetAsync(
            "/api/v1/counterparties");
        Assert.Equal(HttpStatusCode.BadRequest, optionalEmptyCompany.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendJsonCommandAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string json,
        string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(
            "Idempotency-Key",
            idempotencyKey ?? $"openapi-probe-{Guid.NewGuid():N}");
        return await client.SendAsync(request);
    }

    [Fact]
    public void RuntimeProcess_CannotEnableBuildTimeModeThroughConfiguration()
    {
        Assert.False(OpenApiDocumentGenerationMode.IsActive);
        Assert.Equal("GetDocument.Insider", OpenApiDocumentGenerationMode.EntryAssemblyName);
    }

    [Fact]
    public void RestoredGraph_UsesTheExactPatchedOpenApiDependency()
    {
        var repositoryRoot = FindRepositoryRoot();
        var assetsPath = ApiAssetsPath(repositoryRoot);
        using var assets = JsonDocument.Parse(File.ReadAllBytes(assetsPath));
        var libraries = assets.RootElement.GetProperty("libraries");
        var openApiVersions = libraries.EnumerateObject()
            .Where(property => property.Name.StartsWith(
                "Microsoft.OpenApi/",
                StringComparison.OrdinalIgnoreCase))
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(["Microsoft.OpenApi/2.7.5"], openApiVersions);
        Assert.True(libraries.TryGetProperty(
            "Microsoft.AspNetCore.OpenApi/10.0.8",
            out _));
        Assert.True(libraries.TryGetProperty(
            "Microsoft.Extensions.ApiDescription.Server/10.0.11",
            out _));

        var packageRoot = assets.RootElement.GetProperty("packageFolders")
            .EnumerateObject()
            .Select(property => property.Name)
            .Single(path => Directory.Exists(Path.Combine(
                path,
                "microsoft.extensions.apidescription.server",
                "10.0.11")));
        var embeddedOpenApiAssembly = Path.Combine(
            packageRoot,
            "microsoft.extensions.apidescription.server",
            "10.0.11",
            "tools",
            "net10.0",
            "Microsoft.OpenApi.dll");
        Assert.True(
            File.Exists(embeddedOpenApiAssembly),
            $"Build tool OpenAPI assembly was not found: {embeddedOpenApiAssembly}");
        Assert.Equal(
            new Version(2, 7, 5, 0),
            AssemblyName.GetAssemblyName(embeddedOpenApiAssembly).Version);
    }

    private static async Task<GenerationResult> GenerateAsync(
        string repositoryRoot,
        string temporaryRoot,
        string environmentName,
        string runName)
    {
        var outputDirectory = Path.Combine(temporaryRoot, runName);
        Directory.CreateDirectory(outputDirectory);
        var secretMarker = $"openapi-secret-{runName}-{Guid.NewGuid():N}";

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var acceptedConnectionCount = 0;
        using var listenerCancellation = new CancellationTokenSource();
        var acceptLoop = Task.Run(async () =>
        {
            while (!listenerCancellation.IsCancellationRequested)
            {
                try
                {
                    using var client = await listener.AcceptTcpClientAsync(
                        listenerCancellation.Token);
                    Interlocked.Increment(ref acceptedConnectionCount);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (SocketException) when (listenerCancellation.IsCancellationRequested)
                {
                    return;
                }
            }
        });

        var assetsPath = ApiAssetsPath(repositoryRoot);
        using var assets = JsonDocument.Parse(File.ReadAllBytes(assetsPath));
        var packageRoot = assets.RootElement.GetProperty("packageFolders")
            .EnumerateObject()
            .Select(property => property.Name)
            .Single(path => Directory.Exists(Path.Combine(
                path,
                "microsoft.extensions.apidescription.server",
                "10.0.11")));
        var toolPath = Path.Combine(
            packageRoot,
            "microsoft.extensions.apidescription.server",
            "10.0.11",
            "tools",
            "dotnet-getdocument.dll");
        var configuration = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        var apiAssembly = Path.Combine(
            repositoryRoot,
            "src",
            "backend",
            "GoymenERP.Api",
            "bin",
            configuration,
            "net10.0",
            "GoymenERP.Api.dll");
        Assert.True(File.Exists(toolPath), $"OpenAPI build tool was not found: {toolPath}");
        Assert.True(File.Exists(apiAssembly), $"API assembly was not found: {apiAssembly}");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.Combine(
                repositoryRoot,
                "src",
                "backend",
                "GoymenERP.Api"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[]
                 {
                     toolPath,
                     "--assembly", apiAssembly,
                     "--file-list", Path.Combine(outputDirectory, "documents.cache"),
                     "--framework", ".NETCoreApp,Version=v10.0",
                     "--output", outputDirectory,
                     "--project", "GoymenERP.Api",
                     "--assets-file", assetsPath,
                     "--platform", "AnyCPU",
                     "--document-name", DocumentName,
                     "--file-name", Path.GetFileNameWithoutExtension(CanonicalFileName),
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = environmentName;
        startInfo.Environment["DOTNET_ENVIRONMENT"] = environmentName;
        startInfo.Environment["DB_CONNECTION_STRING"] =
            $"Server=127.0.0.1,{endpoint.Port};Database={secretMarker};"
            + $"User Id={secretMarker};Password={secretMarker};Connect Timeout=1;";
        startInfo.Environment["ConnectionStrings__GoymenErp"] =
            startInfo.Environment["DB_CONNECTION_STRING"];
        startInfo.Environment["Authentication__Mode"] = "OidcJwtBearer";
        startInfo.Environment["Authentication__Oidc__Authority"] =
            $"https://127.0.0.1:{endpoint.Port}/{secretMarker}";
        startInfo.Environment["Authentication__Oidc__Audience"] = secretMarker;
        startInfo.Environment["OpenApi__DocumentGenerationMode"] = "false";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("OpenAPI generator process could not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var processTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            await process.WaitForExitAsync(processTimeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("OpenAPI generation exceeded five minutes.");
        }
        finally
        {
            await Task.Delay(100);
            listenerCancellation.Cancel();
            listener.Stop();
            await acceptLoop;
        }

        var output = await standardOutput;
        var error = await standardError;
        Assert.True(
            process.ExitCode == 0,
            $"OpenAPI generation failed ({process.ExitCode}).\nOUT:\n{output}\nERR:\n{error}");
        var documentPath = Path.Combine(outputDirectory, CanonicalFileName);
        Assert.True(
            File.Exists(documentPath),
            $"Canonical OpenAPI artifact was not emitted at {documentPath}.");
        Assert.Equal(
            [documentPath],
            Directory.GetFiles(outputDirectory, "*.json", SearchOption.TopDirectoryOnly));
        return new GenerationResult(
            await File.ReadAllBytesAsync(documentPath),
            secretMarker,
            acceptedConnectionCount);
    }

    private static void AssertContractShape(byte[] contractBytes)
    {
        using var contract = JsonDocument.Parse(contractBytes);
        var root = contract.RootElement;
        Assert.Equal("3.1.1", root.GetProperty("openapi").GetString());
        Assert.Equal("Göymen ERP API", root.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("v1", root.GetProperty("info").GetProperty("version").GetString());
        Assert.False(root.TryGetProperty("servers", out var servers)
            && servers.GetArrayLength() > 0);
        var securitySchemes = root.GetProperty("components")
            .GetProperty("securitySchemes");
        var bearer = Assert.Single(securitySchemes.EnumerateObject());
        Assert.Equal("Bearer", bearer.Name);
        Assert.Equal("http", bearer.Value.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.Value.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.Value.GetProperty("bearerFormat").GetString());

        var operations = ReadOperations(root.GetProperty("paths"));
        Assert.Equal(17, operations.Count);
        Assert.Equal(
            17,
            operations.Select(operation => operation.OperationId)
                .Distinct(StringComparer.Ordinal)
                .Count());
        var operationsById = operations.ToDictionary(
            operation => operation.OperationId,
            StringComparer.Ordinal);
        Assert.Equal(
            ExpectedOperations.Select(operation => operation.OperationId).Order(StringComparer.Ordinal),
            operationsById.Keys.Order(StringComparer.Ordinal));
        foreach (var expected in ExpectedOperations)
        {
            var actual = operationsById[expected.OperationId];
            Assert.Equal(expected.Method, actual.Method);
            Assert.Equal(expected.Path, actual.Path);
            Assert.Equal(
                expected.Responses.Order(StringComparer.Ordinal),
                SnapshotResponses(actual.Value).Order(StringComparer.Ordinal));
            Assert.Equal(
                ExpectedSuccessSchemas[expected.OperationId],
                SnapshotSuccessSchema(actual.Value));
            foreach (var response in actual.Value.GetProperty("responses").EnumerateObject())
            {
                if (response.Value.TryGetProperty("content", out var responseContent)
                    && responseContent.TryGetProperty(
                        "application/problem+json",
                        out var problemContent))
                {
                    AssertProblemResponseSchema(
                        expected.OperationId,
                        int.Parse(response.Name, CultureInfo.InvariantCulture),
                        problemContent.GetProperty("schema"));
                }
            }

            if (expected.OperationId == "CreateCounterpartyCompanyAccountV1")
            {
                Assert.Equal(
                    "#/components/schemas/CounterpartyMutationView",
                    actual.Value.GetProperty("responses")
                        .GetProperty("201")
                        .GetProperty("content")
                        .GetProperty("application/json")
                        .GetProperty("schema")
                        .GetProperty("$ref")
                        .GetString());
            }

            if (expected.RequestSchemaReference is null)
            {
                Assert.False(actual.Value.TryGetProperty("requestBody", out _));
            }
            else
            {
                var requestBody = actual.Value.GetProperty("requestBody");
                Assert.True(requestBody.GetProperty("required").GetBoolean());
                var requestContent = requestBody.GetProperty("content");
                Assert.Equal(["application/json"], requestContent.EnumerateObject()
                    .Select(content => content.Name));
                Assert.Equal(
                    expected.RequestSchemaReference,
                    requestContent.GetProperty("application/json")
                        .GetProperty("schema")
                        .GetProperty("$ref")
                        .GetString());
            }

            var nonHeaderParameters = ReadParameters(actual.Value)
                .Where(parameter => parameter.Location != "header")
                .Select(parameter => $"{parameter.Location}:{parameter.Name}:"
                    + parameter.Required.ToString().ToLowerInvariant()
                    + $":{parameter.Schema}")
                .ToArray();
            Assert.Equal(expected.Parameters, nonHeaderParameters);
            if (expected.OperationId == "GetCounterpartiesV1")
            {
                AssertCounterpartyPageQueryContract(actual.Value);
            }

            if (expected.OperationId == "GetCounterpartyCompanyAccountLinkCandidatesV1")
            {
                AssertCounterpartyLinkCandidateQueryContract(actual.Value);
            }
        }

        var anonymousOperation = Assert.Single(operations, operation =>
            operation.Path == "/health/live" && operation.Method == "get");
        Assert.False(anonymousOperation.Value.TryGetProperty("security", out _));

        foreach (var operation in operations.Where(operation => operation != anonymousOperation))
        {
            var security = operation.Value.GetProperty("security");
            var requirement = Assert.Single(security.EnumerateArray());
            var bearerRequirement = Assert.Single(requirement.EnumerateObject());
            Assert.Equal("Bearer", bearerRequirement.Name);
            Assert.Empty(bearerRequirement.Value.EnumerateArray());
            Assert.Contains("401", ResponseCodes(operation.Value));
            Assert.Contains("403", ResponseCodes(operation.Value));
        }

        foreach (var operation in operations)
        {
            var parameters = operation.Value.TryGetProperty("parameters", out var value)
                ? value.EnumerateArray().ToArray()
                : [];
            var expectedHeaderNames = new List<string>
            {
                "X-Correlation-Id",
                "X-Causation-Id",
            };
            if (RequiredCompanyOperations.Contains(operation.OperationId)
                || OptionalCompanyOperations.Contains(operation.OperationId))
            {
                expectedHeaderNames.Add("X-Company-Id");
            }

            if (IdempotentOperations.Contains(operation.OperationId))
            {
                expectedHeaderNames.Add("Idempotency-Key");
            }

            Assert.Equal(
                expectedHeaderNames.Order(StringComparer.Ordinal),
                parameters.Where(parameter =>
                        parameter.GetProperty("in").GetString() == "header")
                    .Select(parameter => parameter.GetProperty("name").GetString())
                    .Order(StringComparer.Ordinal));
            Assert.Contains(parameters, parameter =>
                parameter.GetProperty("in").GetString() == "header"
                && parameter.GetProperty("name").GetString() == "X-Correlation-Id");
            AssertHeaderSchema(
                parameters,
                "X-Correlation-Id",
                required: false,
                type: "string",
                minimumLength: 1,
                maximumLength: 128,
                pattern: "^[A-Za-z0-9._:-]+$");
            AssertHeaderSchema(
                parameters,
                "X-Causation-Id",
                required: false,
                type: "string",
                minimumLength: 1,
                maximumLength: 128,
                pattern: "^[A-Za-z0-9._:-]+$");

            var companyHeader = parameters.SingleOrDefault(parameter =>
                parameter.GetProperty("in").GetString() == "header"
                && parameter.GetProperty("name").GetString() == "X-Company-Id");
            if (RequiredCompanyOperations.Contains(operation.OperationId))
            {
                Assert.Equal(JsonValueKind.Object, companyHeader.ValueKind);
                AssertHeaderSchema(
                    parameters,
                    "X-Company-Id",
                    required: true,
                    type: "string",
                    format: "uuid",
                    nonEmptyUuid: true);
            }
            else if (OptionalCompanyOperations.Contains(operation.OperationId))
            {
                Assert.Equal(JsonValueKind.Object, companyHeader.ValueKind);
                AssertHeaderSchema(
                    parameters,
                    "X-Company-Id",
                    required: false,
                    type: "string",
                    format: "uuid",
                    nonEmptyUuid: true);
            }
            else
            {
                Assert.Equal(JsonValueKind.Undefined, companyHeader.ValueKind);
            }

            var idempotencyHeader = parameters.SingleOrDefault(parameter =>
                parameter.GetProperty("in").GetString() == "header"
                && parameter.GetProperty("name").GetString() == "Idempotency-Key");
            Assert.Equal(
                IdempotentOperations.Contains(operation.OperationId),
                idempotencyHeader.ValueKind == JsonValueKind.Object);
            if (idempotencyHeader.ValueKind == JsonValueKind.Object)
            {
                AssertHeaderSchema(
                    parameters,
                    "Idempotency-Key",
                    required: true,
                    type: "string",
                    minimumLength: 1,
                    maximumLength: null,
                    pattern: "^(?!.*[,])(?=.*\\S).+$",
                    canonicalMaximumLength: 128);
            }

            foreach (var response in operation.Value.GetProperty("responses").EnumerateObject())
            {
                var responseHeaders = response.Value.GetProperty("headers");
                AssertResponseHeaderSchema(
                    responseHeaders,
                    "X-Correlation-Id",
                    "string",
                    minimumLength: 1,
                    maximumLength: 128,
                    pattern: "^[A-Za-z0-9._:-]+$");
                var numericStatusCode = int.Parse(
                    response.Name,
                    CultureInfo.InvariantCulture);
                var replayHeadersExpected = ReplayableResponseStatuses
                    .GetValueOrDefault(operation.OperationId, [])
                    .Contains(numericStatusCode);
                Assert.Equal(
                    replayHeadersExpected,
                    responseHeaders.TryGetProperty(
                        "Idempotency-Replayed",
                        out _));
                Assert.Equal(
                    replayHeadersExpected,
                    responseHeaders.TryGetProperty(
                        "Idempotency-Original-Correlation-Id",
                        out _));
                if (replayHeadersExpected)
                {
                    AssertResponseHeaderSchema(
                        responseHeaders,
                        "Idempotency-Replayed",
                        "boolean");
                    AssertResponseHeaderSchema(
                        responseHeaders,
                        "Idempotency-Original-Correlation-Id",
                        "string",
                        minimumLength: 1,
                        maximumLength: 128,
                        pattern: "^[A-Za-z0-9._:-]+$");
                }

                if (numericStatusCode >= StatusCodes.Status400BadRequest)
                {
                    var expectedProblemCodes = ExpectedProblemCodes(
                        operation.OperationId,
                        numericStatusCode);
                    var hasProblemCodes = response.Value.TryGetProperty(
                        "x-erp-problem-codes",
                        out var stableCodes);
                    Assert.Equal(expectedProblemCodes.Count > 0, hasProblemCodes);
                    if (hasProblemCodes)
                    {
                        Assert.Equal(
                            expectedProblemCodes,
                            stableCodes.EnumerateArray()
                                .Select(code => code.GetString())
                                .Order(StringComparer.Ordinal));
                    }
                }

                if (numericStatusCode == StatusCodes.Status400BadRequest)
                {
                    var expectedEmptyBodyVariant =
                        EmptyBodyPossible400Operations.Contains(operation.OperationId);
                    Assert.Equal(
                        expectedEmptyBodyVariant,
                        response.Value.TryGetProperty(
                            ErpOpenApiContract.EmptyBodyPossibleExtensionName,
                            out var emptyBodyPossible)
                        && emptyBodyPossible.ValueKind == JsonValueKind.True);
                }
            }
        }

        var schemas = root.GetProperty("components").GetProperty("schemas");
        AssertSchemaSemantics(schemas);
        AssertSuccessComponentSchemas(schemas);
        var mutationAccounts = schemas
            .GetProperty("CounterpartyMutationView")
            .GetProperty("properties")
            .GetProperty("companyAccounts");
        Assert.Equal(1, mutationAccounts.GetProperty("minItems").GetInt32());
        Assert.Equal(1, mutationAccounts.GetProperty("maxItems").GetInt32());
    }

    private static IReadOnlyList<OpenApiOperationSnapshot> ReadOperations(JsonElement paths)
    {
        var operations = new List<OpenApiOperationSnapshot>();
        foreach (var path in paths.EnumerateObject())
        {
            foreach (var method in path.Value.EnumerateObject().Where(property =>
                         property.Name is "get" or "post" or "put" or "patch" or "delete"))
            {
                operations.Add(new OpenApiOperationSnapshot(
                    path.Name,
                    method.Name,
                    method.Value.GetProperty("operationId").GetString()
                        ?? throw new InvalidOperationException("OperationId is missing."),
                    method.Value));
            }
        }

        return operations;
    }

    private static IReadOnlyList<EndpointSnapshot> SnapshotRuntimeEndpoint(
        RouteEndpoint endpoint)
    {
        var name = endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;
        Assert.False(
            string.IsNullOrWhiteSpace(name),
            $"HTTP RouteEndpoint '{endpoint.DisplayName}' has no stable endpoint name.");
        var methodMetadata = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>();
        Assert.NotNull(methodMetadata);
        Assert.NotEmpty(methodMetadata.HttpMethods);
        var path = NormalizeRoute(endpoint.RoutePattern.RawText);
        Assert.False(
            string.IsNullOrWhiteSpace(path),
            $"HTTP RouteEndpoint '{endpoint.DisplayName}' has no canonical route path.");
        return methodMetadata.HttpMethods
            .Select(method => new EndpointSnapshot(
                method.ToLowerInvariant(),
                path!,
                name!))
            .ToArray();
    }

    private static IReadOnlyList<ParameterSnapshot> ReadParameters(JsonElement operation)
    {
        if (!operation.TryGetProperty("parameters", out var parameters))
        {
            return [];
        }

        return parameters.EnumerateArray()
            .Select(parameter => new ParameterSnapshot(
                parameter.GetProperty("in").GetString()
                    ?? throw new InvalidOperationException("Parameter location is missing."),
                parameter.GetProperty("name").GetString()
                    ?? throw new InvalidOperationException("Parameter name is missing."),
                parameter.TryGetProperty("required", out var required)
                && required.GetBoolean(),
                DescribeParameterSchema(parameter.GetProperty("schema"))))
            .ToArray();
    }

    private static IReadOnlyList<string> SnapshotResponses(JsonElement operation)
    {
        return operation.GetProperty("responses")
            .EnumerateObject()
            .Select(response =>
            {
                IEnumerable<string> mediaTypes = response.Value.TryGetProperty(
                    "content",
                    out var content)
                    ? content.EnumerateObject()
                        .Select(mediaType => mediaType.Name)
                        .Order(StringComparer.Ordinal)
                    : [];
                return $"{response.Name}:{string.Join(',', mediaTypes)}";
            })
            .ToArray();
    }

    private static string SnapshotSuccessSchema(JsonElement operation)
    {
        var success = Assert.Single(
            operation.GetProperty("responses").EnumerateObject(),
            response => response.Name.StartsWith('2'));
        if (!success.Value.TryGetProperty("content", out var content))
        {
            return "none";
        }

        var mediaType = Assert.Single(content.EnumerateObject());
        var schema = mediaType.Value.GetProperty("schema");
        if (schema.TryGetProperty("$ref", out var reference))
        {
            return $"ref:{reference.GetString()?.Split('/').Last()}";
        }

        if (schema.TryGetProperty("items", out var items)
            && items.TryGetProperty("$ref", out var itemReference))
        {
            return $"array:{itemReference.GetString()?.Split('/').Last()}";
        }

        return $"type:{schema.GetProperty("type").GetString()}";
    }

    private static void AssertHeaderSchema(
        JsonElement[] parameters,
        string name,
        bool required,
        string type,
        int? minimumLength = null,
        int? maximumLength = null,
        string? pattern = null,
        string? format = null,
        int? canonicalMaximumLength = null,
        bool nonEmptyUuid = false)
    {
        var parameter = Assert.Single(parameters, candidate =>
            candidate.GetProperty("in").GetString() == "header"
            && candidate.GetProperty("name").GetString() == name);
        Assert.Equal(
            required,
            parameter.TryGetProperty("required", out var requiredValue)
            && requiredValue.GetBoolean());
        AssertStringSchema(
            parameter.GetProperty("schema"),
            type,
            minimumLength,
            maximumLength,
            pattern,
            format,
            canonicalMaximumLength);
        Assert.Equal(
            nonEmptyUuid,
            parameter.GetProperty("schema").TryGetProperty(
                ErpOpenApiContract.NonEmptyUuidExtensionName,
                out var nonEmptyValue)
            && nonEmptyValue.ValueKind == JsonValueKind.True);
    }

    private static void AssertResponseHeaderSchema(
        JsonElement headers,
        string name,
        string type,
        int? minimumLength = null,
        int? maximumLength = null,
        string? pattern = null)
    {
        Assert.True(headers.TryGetProperty(name, out var header));
        AssertStringSchema(
            header.GetProperty("schema"),
            type,
            minimumLength,
            maximumLength,
            pattern,
            format: null,
            canonicalMaximumLength: null);
    }

    private static void AssertCounterpartyPageQueryContract(JsonElement operation)
    {
        var parameters = operation.GetProperty("parameters")
            .EnumerateArray()
            .Where(parameter => parameter.GetProperty("in").GetString() == "query")
            .ToDictionary(
                parameter => parameter.GetProperty("name").GetString()!,
                parameter => parameter.GetProperty("schema"),
                StringComparer.Ordinal);

        Assert.Equal(11, parameters.Count);
        var search = parameters["q"];
        Assert.Equal(200, search.GetProperty("maxLength").GetInt32());
        Assert.Equal(
            "unicode-trim+nfc",
            search.GetProperty(ErpOpenApiContract.CanonicalizationExtensionName).GetString());

        var role = parameters["role"];
        Assert.Equal("array", role.GetProperty("type").GetString());
        Assert.Equal(
            "^(Customer|Supplier|Tolling|Service)$",
            role.GetProperty("items").GetProperty("pattern").GetString());

        var pageSize = parameters["pageSize"];
        Assert.Equal(1, pageSize.GetProperty("minimum").GetInt32());
        Assert.Equal(200, pageSize.GetProperty("maximum").GetInt32());
        Assert.Equal(1, parameters["cursor"].GetProperty("minLength").GetInt32());
        Assert.Equal("boolean", parameters["includeCount"].GetProperty("type").GetString());
        Assert.DoesNotContain("companyId", parameters.Keys);
    }

    private static void AssertCounterpartyLinkCandidateQueryContract(JsonElement operation)
    {
        var parameters = operation.GetProperty("parameters")
            .EnumerateArray()
            .Where(parameter => parameter.GetProperty("in").GetString() == "query")
            .ToDictionary(
                parameter => parameter.GetProperty("name").GetString()!,
                parameter => parameter.GetProperty("schema"),
                StringComparer.Ordinal);

        Assert.Equal(3, parameters.Count);
        var search = parameters["q"];
        Assert.Equal(200, search.GetProperty("maxLength").GetInt32());
        Assert.Equal(
            "unicode-trim+nfc",
            search.GetProperty(ErpOpenApiContract.CanonicalizationExtensionName).GetString());

        var pageSize = parameters["pageSize"];
        Assert.Equal(1, pageSize.GetProperty("minimum").GetInt32());
        Assert.Equal(100, pageSize.GetProperty("maximum").GetInt32());
        Assert.Equal(1, parameters["cursor"].GetProperty("minLength").GetInt32());
        Assert.DoesNotContain("companyId", parameters.Keys);
    }

    private static void AssertStringSchema(
        JsonElement schema,
        string type,
        int? minimumLength,
        int? maximumLength,
        string? pattern,
        string? format,
        int? canonicalMaximumLength)
    {
        Assert.Equal(type, schema.GetProperty("type").GetString());
        Assert.Equal(
            minimumLength,
            schema.TryGetProperty("minLength", out var minLength)
                ? minLength.GetInt32()
                : null);
        Assert.Equal(
            maximumLength,
            schema.TryGetProperty("maxLength", out var maxLength)
                ? maxLength.GetInt32()
                : null);
        Assert.Equal(
            pattern,
            schema.TryGetProperty("pattern", out var patternValue)
                ? patternValue.GetString()
                : null);
        Assert.Equal(
            format,
            schema.TryGetProperty("format", out var formatValue)
                ? formatValue.GetString()
                : null);
        Assert.Equal(
            canonicalMaximumLength,
            schema.TryGetProperty(
                ErpOpenApiContract.CanonicalMaximumLengthExtensionName,
                out var canonicalMaximum)
                ? canonicalMaximum.GetInt32()
                : null);
        Assert.Equal(
            canonicalMaximumLength is not null,
            schema.TryGetProperty(
                ErpOpenApiContract.CanonicalizationExtensionName,
                out var canonicalization)
            && canonicalization.GetString() == "trim");
    }

    private static string DescribeSchema(JsonElement schema)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            return $"ref:{reference.GetString()?.Split('/').Last()}";
        }

        if (schema.TryGetProperty("oneOf", out var oneOf))
        {
            return "oneOf:"
                + string.Join('|', oneOf.EnumerateArray()
                    .Select(DescribeSchema)
                    .Order(StringComparer.Ordinal));
        }

        var type = schema.GetProperty("type");
        IEnumerable<string?> types = type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray()
                .Select(value => value.GetString())
                .Order(StringComparer.Ordinal)
            : new[] { type.GetString() };
        var descriptor = $"type:{string.Join('|', types)}";
        if (schema.TryGetProperty("format", out var format))
        {
            descriptor += $";format:{format.GetString()}";
        }

        if (schema.TryGetProperty("items", out var items))
        {
            descriptor += $";items:{DescribeSchema(items)}";
        }

        if (schema.TryGetProperty(
                ErpOpenApiContract.NonEmptyUuidExtensionName,
                out var nonEmptyUuid))
        {
            descriptor += $";non-empty-uuid:{nonEmptyUuid.GetBoolean().ToString().ToLowerInvariant()}";
        }

        return descriptor;
    }

    private static string DescribeParameterSchema(JsonElement schema)
    {
        var descriptor = DescribeSchema(schema);
        if (schema.TryGetProperty("pattern", out var pattern))
        {
            descriptor += $";pattern:{pattern.GetString()}";
        }

        if (schema.TryGetProperty(
                ErpOpenApiContract.CanonicalizationExtensionName,
                out var canonicalization))
        {
            descriptor += $";canonicalization:{canonicalization.GetString()}";
        }

        if (schema.TryGetProperty(
                ErpOpenApiContract.CanonicalMaximumLengthExtensionName,
                out var canonicalMaximum))
        {
            descriptor += $";canonical-max-length:{canonicalMaximum.GetInt32()}";
        }

        if (schema.TryGetProperty(
                ErpOpenApiContract.NormalizedMinimumExtensionName,
                out var normalizedMinimum))
        {
            descriptor += $";normalized-minimum:{normalizedMinimum.GetInt32()}";
        }

        if (schema.TryGetProperty(
                ErpOpenApiContract.NormalizedMaximumExtensionName,
                out var normalizedMaximum))
        {
            descriptor += $";normalized-maximum:{normalizedMaximum.GetInt32()}";
        }

        return descriptor;
    }

    private static void AssertSchemaSemantics(JsonElement schemas)
    {
        AssertComponentSchemaMatrix(schemas, ExpectedRequestComponentSchemas);
        AssertComponentSchemaMatrix(schemas, ExpectedProblemComponentSchemas);
        AssertPositiveIntegerStringVersion(schemas, "CounterpartyMutationAccountView");
        AssertPositiveIntegerStringVersion(schemas, "CounterpartyMutationView");
        AssertPositiveIntegerStringVersion(schemas, "CounterpartyDetailAccountView");
        AssertPositiveIntegerStringVersion(schemas, "CounterpartyDetailView");
        AssertPositiveIntegerStringVersion(
            schemas,
            "CounterpartyLinkCandidateSourceAccount");

        var problem = schemas.GetProperty("ErpProblemDetailsContract");
        Assert.Equal(
            ["code", "detail", "status", "title", "type"],
            problem.GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString())
                .Order(StringComparer.Ordinal));
        var properties = problem.GetProperty("properties");
        Assert.Equal(
            ["code", "conflict", "correlationId", "detail", "status", "title", "type"],
            properties.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.False(IsNullable(properties.GetProperty("code")));
        Assert.Equal("string", properties.GetProperty("code").GetProperty("type").GetString());
        Assert.True(IsNullable(properties.GetProperty("correlationId")));
        Assert.True(IsNullable(properties.GetProperty("conflict")));
        Assert.Equal(
            ErpOpenApiContract.KnownProblemCodePattern,
            properties.GetProperty("code").GetProperty("pattern").GetString());
        Assert.Contains(
            "#/components/schemas/ErpVersionConflictContract",
            properties.GetProperty("conflict").GetRawText(),
            StringComparison.Ordinal);

        var codeLessProblem = schemas.GetProperty("ErpCodeLessProblemDetailsContract");
        Assert.Equal(
            ["detail", "status", "title", "type"],
            codeLessProblem.GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString())
                .Order(StringComparer.Ordinal));
        var codeLessProperties = codeLessProblem.GetProperty("properties");
        Assert.Equal(
            ["conflict", "detail", "status", "title", "type"],
            codeLessProperties.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.False(codeLessProperties.TryGetProperty("code", out _));
        Assert.False(codeLessProperties.TryGetProperty("correlationId", out _));
        Assert.True(IsNullable(codeLessProperties.GetProperty("conflict")));
        Assert.Equal(
            ["code"],
            codeLessProblem.GetProperty("not")
                .GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString()));

        var versionConflict = schemas.GetProperty("ErpVersionConflictContract");
        Assert.Equal(
            ["currentVersion", "expectedVersion"],
            versionConflict.GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString())
                .Order(StringComparer.Ordinal));

        AssertRequestSchema(
            schemas.GetProperty("CreateCounterparty"),
            ["kind", "legalName"],
            ["tradeName"],
            ["kind", "legalName", "tradeName", "roles"]);
        AssertRequestSchema(
            schemas.GetProperty("CreateCompanyPartyAccount"),
            [],
            [],
            ["roles"]);
        AssertRequestSchema(
            schemas.GetProperty("SetCompanyPartyAccountActiveState"),
            ["isActive", "expectedVersion", "reason"],
            [],
            ["isActive", "expectedVersion", "reason"]);
        AssertRequestSchema(
            schemas.GetProperty("SetLocationActiveState"),
            ["isActive", "expectedVersion", "reason"],
            [],
            ["isActive", "expectedVersion", "reason"]);

        var partyKind = schemas.GetProperty("PartyKind");
        Assert.Equal(1, partyKind.GetProperty("minimum").GetInt32());
        Assert.Equal(2, partyKind.GetProperty("maximum").GetInt32());
        Assert.Equal(
            [1, 2],
            partyKind.GetProperty(ErpOpenApiContract.AllowedValuesExtensionName)
                .EnumerateArray()
                .Select(value => value.GetInt32()));
        var partyRole = schemas.GetProperty("PartyRole");
        Assert.Equal(0, partyRole.GetProperty("minimum").GetInt32());
        Assert.Equal(15, partyRole.GetProperty("maximum").GetInt32());
        Assert.Equal(
            [1, 2, 4, 8],
            partyRole.GetProperty(ErpOpenApiContract.AllowedFlagsExtensionName)
                .EnumerateArray()
                .Select(value => value.GetInt32()));

        var createCounterparty = schemas.GetProperty("CreateCounterparty")
            .GetProperty("properties");
        AssertCanonicalStringContract(
            createCounterparty.GetProperty("legalName"),
            minimumLength: 1,
            maximumLength: 250);
        AssertCanonicalStringContract(
            createCounterparty.GetProperty("tradeName"),
            minimumLength: null,
            maximumLength: 200);
        AssertStatusRequestConstraints(
            schemas.GetProperty("SetCompanyPartyAccountActiveState"),
            expectedVersionMinimum: 1);
        AssertStatusRequestConstraints(
            schemas.GetProperty("SetLocationActiveState"),
            expectedVersionMinimum: 1);
    }

    private static void AssertPositiveIntegerStringVersion(
        JsonElement schemas,
        string schemaName)
    {
        var version = schemas
            .GetProperty(schemaName)
            .GetProperty("properties")
            .GetProperty("version");
        Assert.Equal("string", version.GetProperty("type").GetString());
        Assert.False(IsNullable(version));
        Assert.False(version.TryGetProperty("format", out _));
        Assert.False(version.TryGetProperty("minimum", out _));
        Assert.False(version.TryGetProperty("maximum", out _));
        Assert.Equal(1, version.GetProperty("minLength").GetInt32());
        Assert.Equal(19, version.GetProperty("maxLength").GetInt32());
        Assert.Equal(
            ErpOpenApiContract.PositiveIntegerStringPattern,
            version.GetProperty("pattern").GetString());
    }

    private static void AssertStatusRequestConstraints(
        JsonElement schema,
        int? expectedVersionMinimum)
    {
        var properties = schema.GetProperty("properties");
        Assert.Equal(
            expectedVersionMinimum,
            properties.GetProperty("expectedVersion")
                .TryGetProperty("minimum", out var minimum)
                ? minimum.GetInt32()
                : null);
        Assert.Equal(
            ErpOpenApiContract.PositiveIntegerStringPattern,
            properties.GetProperty("expectedVersion").GetProperty("pattern").GetString());
        AssertCanonicalStringContract(
            properties.GetProperty("reason"),
            minimumLength: 1,
            maximumLength: 1000);
    }

    private static void AssertCanonicalStringContract(
        JsonElement schema,
        int? minimumLength,
        int? maximumLength)
    {
        Assert.False(schema.TryGetProperty("minLength", out _));
        Assert.False(schema.TryGetProperty("maxLength", out _));
        Assert.Equal(
            "trim",
            schema.GetProperty(ErpOpenApiContract.CanonicalizationExtensionName)
                .GetString());
        Assert.Equal(
            minimumLength,
            schema.TryGetProperty(
                ErpOpenApiContract.CanonicalMinimumLengthExtensionName,
                out var canonicalMinimum)
                ? canonicalMinimum.GetInt32()
                : null);
        Assert.Equal(
            maximumLength,
            schema.TryGetProperty(
                ErpOpenApiContract.CanonicalMaximumLengthExtensionName,
                out var canonicalMaximum)
                ? canonicalMaximum.GetInt32()
                : null);
    }

    private static void AssertRequestSchema(
        JsonElement schema,
        IReadOnlyList<string> requiredProperties,
        IReadOnlyList<string> nullableProperties,
        IReadOnlyList<string> expectedProperties)
    {
        var actualRequiredProperties = schema.TryGetProperty("required", out var required)
            ? required.EnumerateArray().Select(item => item.GetString()!).ToArray()
            : [];
        Assert.Equal(
            requiredProperties.Order(StringComparer.Ordinal),
            actualRequiredProperties.Order(StringComparer.Ordinal));
        var properties = schema.GetProperty("properties");
        Assert.Equal(
            expectedProperties.Order(StringComparer.Ordinal),
            properties.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        foreach (var propertyName in nullableProperties)
        {
            Assert.True(IsNullable(properties.GetProperty(propertyName)));
        }
    }

    private static void AssertComponentSchemaMatrix(
        JsonElement schemas,
        IReadOnlyDictionary<string, ComponentSchemaExpectation> expectations)
    {
        foreach (var (schemaName, expected) in expectations)
        {
            var schema = schemas.GetProperty(schemaName);
            Assert.Equal(expected.RootDescriptor, DescribeSchema(schema));
            IEnumerable<string?> required = schema.TryGetProperty(
                    "required",
                    out var requiredValues)
                ? requiredValues.EnumerateArray()
                    .Select(value => value.GetString())
                    .Order(StringComparer.Ordinal)
                : Array.Empty<string?>();
            Assert.Equal(
                expected.RequiredProperties.Order(StringComparer.Ordinal),
                required);
            var properties = schema.GetProperty("properties")
                .EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => DescribeSchema(property.Value),
                    StringComparer.Ordinal);
            Assert.Equal(
                expected.Properties.OrderBy(pair => pair.Key, StringComparer.Ordinal),
                properties.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        }
    }

    private static void AssertSuccessComponentSchemas(JsonElement schemas)
    {
        var reachableComponents = CollectReachableSuccessComponents(schemas);
        Assert.Equal(
            ExpectedSuccessComponentSchemas.Keys.Order(StringComparer.Ordinal),
            reachableComponents.Order(StringComparer.Ordinal));

        foreach (var (schemaName, expected) in ExpectedSuccessComponentSchemas)
        {
            var schema = schemas.GetProperty(schemaName);
            Assert.Equal(expected.RootDescriptor, DescribeSchema(schema));
            IEnumerable<string?> required = schema.TryGetProperty(
                    "required",
                    out var requiredValues)
                ? requiredValues.EnumerateArray()
                    .Select(value => value.GetString())
                    .Order(StringComparer.Ordinal)
                : Array.Empty<string?>();
            Assert.Equal(
                expected.RequiredProperties.Order(StringComparer.Ordinal),
                required);
            var properties = schema.TryGetProperty("properties", out var propertyValues)
                ? propertyValues.EnumerateObject()
                    .ToDictionary(
                        property => property.Name,
                        property => DescribeSchema(property.Value),
                        StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);
            Assert.Equal(
                expected.Properties.OrderBy(pair => pair.Key, StringComparer.Ordinal),
                properties.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        }
    }

    private static HashSet<string> CollectReachableSuccessComponents(JsonElement schemas)
    {
        var queue = new Queue<string>(ExpectedSuccessSchemas.Values
            .Where(value => value.StartsWith("ref:", StringComparison.Ordinal)
                || value.StartsWith("array:", StringComparison.Ordinal))
            .Select(value => value[(value.IndexOf(':') + 1)..]));
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        while (queue.TryDequeue(out var schemaName))
        {
            if (!reachable.Add(schemaName))
            {
                continue;
            }

            foreach (var reference in ReadSchemaReferences(schemas.GetProperty(schemaName)))
            {
                queue.Enqueue(reference);
            }
        }

        return reachable;
    }

    private static IEnumerable<string> ReadSchemaReferences(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("$ref"))
                {
                    yield return property.Value.GetString()?.Split('/').Last()
                        ?? throw new InvalidOperationException("Schema reference is empty.");
                    continue;
                }

                foreach (var reference in ReadSchemaReferences(property.Value))
                {
                    yield return reference;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var reference in ReadSchemaReferences(item))
                {
                    yield return reference;
                }
            }
        }
    }

    private static bool IsNullable(JsonElement schema)
    {
        if (schema.TryGetProperty("type", out var type))
        {
            return type.ValueKind == JsonValueKind.Array
                && type.EnumerateArray().Any(value => value.GetString() == "null");
        }

        var alternatives = schema.TryGetProperty("anyOf", out var anyOf)
            ? anyOf
            : schema.TryGetProperty("oneOf", out var oneOf)
                ? oneOf
                : default;
        return alternatives.ValueKind == JsonValueKind.Array
            && alternatives.EnumerateArray().Any(option =>
                option.TryGetProperty("type", out var optionType)
                && optionType.GetString() == "null");
    }

    private static IReadOnlyList<string> AuthenticatedResponses(params string[] responses)
    {
        return responses
            .Concat(
            [
                "401:application/problem+json",
                "403:application/problem+json",
                "500:application/problem+json",
            ])
            .ToArray();
    }

    private static IReadOnlyList<string> ExpectedProblemCodes(
        string operationId,
        int statusCode)
    {
        var codes = statusCode switch
        {
            StatusCodes.Status400BadRequest => new List<string>
            {
                "correlation_metadata_invalid",
            },
            StatusCodes.Status401Unauthorized => ["authentication_required"],
            StatusCodes.Status403Forbidden => ["forbidden"],
            StatusCodes.Status500InternalServerError => ["internal_error"],
            StatusCodes.Status503ServiceUnavailable => ["service_unavailable"],
            _ => [],
        };
        if (statusCode == StatusCodes.Status400BadRequest
            && RequiredCompanyOperations.Contains(operationId))
        {
            codes.Add("company_context_invalid");
        }

        if (AdditionalProblemCodes.TryGetValue(
                $"{operationId}:{statusCode}",
                out var additionalCodes))
        {
            codes.AddRange(additionalCodes);
        }

        return codes.Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static void AssertProblemResponseSchema(
        string operationId,
        int statusCode,
        JsonElement schema)
    {
        var responseKey = $"{operationId}:{statusCode}";
        if (MixedProblemResponses.Contains(responseKey))
        {
            Assert.False(schema.TryGetProperty("$ref", out _));
            Assert.Equal(
                [
                    "#/components/schemas/ErpCodeLessProblemDetailsContract",
                    "#/components/schemas/ErpProblemDetailsContract",
                ],
                schema.GetProperty("oneOf")
                    .EnumerateArray()
                    .Select(option => option.GetProperty("$ref").GetString())
                    .Order(StringComparer.Ordinal));
            return;
        }

        var expectedReference = CodeLessProblemResponses.Contains(responseKey)
            ? "#/components/schemas/ErpCodeLessProblemDetailsContract"
            : "#/components/schemas/ErpProblemDetailsContract";
        Assert.Equal(expectedReference, schema.GetProperty("$ref").GetString());
        Assert.False(schema.TryGetProperty("oneOf", out _));
    }

    private static string? NormalizeRoute(string? rawText)
    {
        if (rawText is null)
        {
            return null;
        }

        var normalized = rawText.Length > 1
            ? $"/{rawText.Trim('/')}"
            : rawText;
        return Regex.Replace(
            normalized,
            "\\{([^}:]+):[^}]+\\}",
            "{$1}",
            RegexOptions.CultureInvariant);
    }

    private static IReadOnlyList<string> ResponseCodes(JsonElement operation)
    {
        return operation.GetProperty("responses")
            .EnumerateObject()
            .Select(response => response.Name)
            .ToArray();
    }

    private static string ApiAssetsPath(string repositoryRoot)
    {
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "backend",
            "GoymenERP.Api",
            "obj",
            "project.assets.json");
        Assert.True(File.Exists(path), $"API assets file was not found: {path}");
        return path;
    }

    private static ComponentSchemaExpectation ObjectSchema(
        IReadOnlyList<string> requiredProperties,
        params (string Name, string Descriptor)[] properties)
    {
        return new ComponentSchemaExpectation(
            "type:object",
            requiredProperties,
            properties.ToDictionary(
                property => property.Name,
                property => property.Descriptor,
                StringComparer.Ordinal));
    }

    private static ComponentSchemaExpectation ScalarSchema(string descriptor)
    {
        return new ComponentSchemaExpectation(
            descriptor,
            [],
            new Dictionary<string, string>(StringComparer.Ordinal));
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

        throw new DirectoryNotFoundException("GoymenERP repository root was not found.");
    }

    private sealed record GenerationResult(
        byte[] Bytes,
        string SecretMarker,
        int AcceptedConnectionCount);

    private sealed record OpenApiOperationSnapshot(
        string Path,
        string Method,
        string OperationId,
        JsonElement Value);

    private sealed record OperationExpectation(
        string Method,
        string Path,
        string OperationId,
        IReadOnlyList<string> Responses,
        string? RequestSchemaReference = null,
        IReadOnlyList<string>? ParameterValues = null)
    {
        public IReadOnlyList<string> Parameters { get; } = ParameterValues ?? [];
    }

    private sealed record ParameterSnapshot(
        string Location,
        string Name,
        bool Required,
        string Schema);

    private sealed record ComponentSchemaExpectation(
        string RootDescriptor,
        IReadOnlyList<string> RequiredProperties,
        IReadOnlyDictionary<string, string> Properties);

    private sealed record EndpointSnapshot(
        string Method,
        string Path,
        string OperationId);
}
