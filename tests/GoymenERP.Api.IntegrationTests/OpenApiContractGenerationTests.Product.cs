using System.Globalization;
using System.Text.Json;
using GoymenERP.Api.OpenApi;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class OpenApiContractGenerationTests
{
    private static IReadOnlyList<OperationExpectation> ProductOperations =>
    [
        new("get", "/api/v1/products", "GetProductsV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json"), ParameterValues:
            ["query:q:false:type:string", "query:lifecycle:false:type:string", "query:groupId:false:type:string;format:uuid",
                "query:classId:false:type:string;format:uuid", "query:familyId:false:type:string;format:uuid",
                "query:roles:false:type:string", "query:roleMatch:false:type:string", "query:sort:false:type:string",
                "query:direction:false:type:string", "query:completeness:false:type:string",
                "query:pageSize:false:type:integer|string;format:int32;pattern:^-?(?:0|[1-9]\\d*)$", "query:cursor:false:type:string"]),
        new("get", "/api/v1/products/code-groups", "GetProductCodeGroupsV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json")),
        new("get", "/api/v1/products/reference-options", "GetProductReferenceOptionsV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "404:application/problem+json"),
            ParameterValues: ["query:groupId:true:type:string;format:uuid", "query:classId:false:type:string;format:uuid"]),
        new("get", "/api/v1/products/{id}", "GetProductV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "404:application/problem+json"),
            ParameterValues: ["path:id:true:type:string;format:uuid"]),
        new("post", "/api/v1/products", "CreateProductDraftV1",
            AuthenticatedResponses("201:application/json", "400:application/problem+json", "409:application/problem+json", "415:", "503:application/problem+json"),
            "#/components/schemas/CreateProductDraftRequest"),
        new("patch", "/api/v1/products/{id}/draft", "EditProductDraftV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "404:application/problem+json", "409:application/problem+json", "415:", "503:application/problem+json"),
            "#/components/schemas/EditProductDraftRequest", ["path:id:true:type:string;format:uuid"]),
    ];

    private static readonly IReadOnlyDictionary<string, string> ProductSuccessSchemas = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["GetProductsV1"] = "ref:ProductPage",
        ["GetProductCodeGroupsV1"] = "array:ProductCodeGroup",
        ["GetProductReferenceOptionsV1"] = "ref:ProductReferenceOptions",
        ["GetProductV1"] = "ref:ProductCardView",
        ["CreateProductDraftV1"] = "ref:ProductMutationResponse",
        ["EditProductDraftV1"] = "ref:ProductMutationResponse",
    };

    private static bool IsProductOperation(string id) => ProductSuccessSchemas.ContainsKey(id);

    private static void AssertProductOperation(OperationExpectation expected, OpenApiOperationSnapshot actual)
    {
        Assert.Equal(expected.Method, actual.Method);
        Assert.Equal(expected.Path, actual.Path);
        Assert.Equal(expected.Responses.Order(StringComparer.Ordinal), SnapshotResponses(actual.Value).Order(StringComparer.Ordinal));
        Assert.Equal(ProductSuccessSchemas[expected.OperationId], SnapshotSuccessSchema(actual.Value));
        Assert.Contains("global Product authority", actual.Value.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Equal(expected.Parameters, ReadParameters(actual.Value).Where(parameter => parameter.Location != "header")
            .Select(parameter => $"{parameter.Location}:{parameter.Name}:{parameter.Required.ToString().ToLowerInvariant()}:{parameter.Schema}"));
        var mutation = expected.Method is "post" or "patch";
        if (mutation)
        {
            var body = actual.Value.GetProperty("requestBody");
            Assert.True(body.GetProperty("required").GetBoolean());
            Assert.Equal(["application/json"], body.GetProperty("content").EnumerateObject().Select(property => property.Name));
            Assert.Equal(expected.RequestSchemaReference, body.GetProperty("content").GetProperty("application/json")
                .GetProperty("schema").GetProperty("$ref").GetString());
        }
        else Assert.False(actual.Value.TryGetProperty("requestBody", out _));
        var parameters = actual.Value.GetProperty("parameters").EnumerateArray().ToArray();
        var names = new List<string> { "X-Correlation-Id", "X-Causation-Id", "X-Company-Id" };
        if (mutation) names.Add("Idempotency-Key");
        Assert.Equal(names.Order(StringComparer.Ordinal), parameters.Where(parameter => parameter.GetProperty("in").GetString() == "header")
            .Select(parameter => parameter.GetProperty("name").GetString()).Order(StringComparer.Ordinal));
        foreach (var name in new[] { "X-Correlation-Id", "X-Causation-Id" })
            AssertHeaderSchema(parameters, name, false, "string", 1, 128, "^[A-Za-z0-9._:-]+$");
        AssertHeaderSchema(parameters, "X-Company-Id", true, "string", format: "uuid", nonEmptyUuid: true);
        Assert.Contains("origin", parameters.Single(parameter => parameter.GetProperty("name").GetString() == "X-Company-Id")
            .GetProperty("description").GetString(), StringComparison.OrdinalIgnoreCase);
        if (mutation) AssertHeaderSchema(parameters, "Idempotency-Key", true, "string", 1, 128, "^[!-+\\--~]{1,128}$");
        foreach (var response in actual.Value.GetProperty("responses").EnumerateObject())
        {
            var status = int.Parse(response.Name, CultureInfo.InvariantCulture);
            if (status >= 400 && status != 415)
                Assert.Equal(status == 500 ? "#/components/schemas/ErpProblemDetailsContract" : "#/components/schemas/ProductRuntimeProblemContract",
                    response.Value.GetProperty("content").GetProperty("application/problem+json").GetProperty("schema").GetProperty("$ref").GetString());
            var headers = response.Value.GetProperty("headers");
            var replay = mutation && status is 200 or 201;
            Assert.Equal(replay ? ["Idempotency-Original-Correlation-Id", "Idempotency-Replayed", "X-Correlation-Id"] : ["X-Correlation-Id"],
                headers.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
            AssertResponseHeaderSchema(headers, "X-Correlation-Id", "string", 1, 128, "^[A-Za-z0-9._:-]+$");
            if (replay)
            {
                AssertResponseHeaderSchema(headers, "Idempotency-Replayed", "boolean");
                AssertResponseHeaderSchema(headers, "Idempotency-Original-Correlation-Id", "string", 1, 128, "^[A-Za-z0-9._:-]+$");
            }
            string[] codes = status switch
            {
                400 => expected.OperationId == "GetProductsV1"
                    ? ["company_context_invalid", "correlation_metadata_invalid", "invalid_cursor", "validation_failed"]
                    : ["company_context_invalid", "correlation_metadata_invalid", "validation_failed"],
                401 => ["authentication_required"], 404 => ["not_found"],
                409 => expected.Method == "patch" ? ["idempotency_conflict", "lifecycle_conflict", "version_conflict"] : ["idempotency_conflict"],
                500 => ["internal_error"], 503 => ["product_write_contention"], _ => [],
            };
            Assert.Equal(codes.Length > 0, response.Value.TryGetProperty("x-erp-problem-codes", out var actualCodes));
            if (codes.Length > 0) Assert.Equal(codes, actualCodes.EnumerateArray().Select(value => value.GetString()));
            if (status == 400)
                Assert.Equal(mutation || expected.OperationId is "GetProductsV1" or "GetProductReferenceOptionsV1",
                    response.Value.TryGetProperty(ErpOpenApiContract.EmptyBodyPossibleExtensionName, out var empty) && empty.GetBoolean());
        }
    }

    private static void AssertProductSchemas(JsonElement schemas)
    {
        var expected = new Dictionary<string, ComponentSchemaExpectation>(StringComparer.Ordinal)
        {
            ["ProductPage"] = ObjectSchema(["items", "nextCursor"], ("items", "type:array;items:ref:ProductDraftView"), ("nextCursor", "type:null|string")),
            ["ProductCodeGroup"] = ObjectSchema(["id", "name", "prefix"], ("id", "type:string;format:uuid"), ("name", "type:string"), ("prefix", "type:string")),
            ["ProductMutationResponse"] = ObjectSchema(["card", "isReplay"], ("card", "ref:ProductCardView"), ("isReplay", "type:boolean")),
            ["ProductCardView"] = ObjectSchema(["product", "references"], ("product", "ref:ProductDraftView"), ("references", "ref:ProductReferences"), ("completeness", "ref:ProductCompletenessView")),
            ["ProductDraftView"] = ObjectSchema(["id", "code", "name", "groupId", "classId", "familyId", "roles", "lifecycle", "version", "createdAtUtc", "updatedAtUtc", "labels", "completeness"],
                ("id", "type:string;format:uuid"), ("code", "type:string"), ("name", "type:string"), ("groupId", "type:string;format:uuid"),
                ("classId", "type:null|string;format:uuid"), ("familyId", "type:null|string;format:uuid"), ("roles", "ref:ProductRolesView"),
                ("lifecycle", "type:string"), ("version", "type:string"), ("createdAtUtc", "type:string;format:date-time"), ("updatedAtUtc", "type:string;format:date-time"),
                ("labels", "ref:ProductReferenceLabels"), ("completeness", "ref:ProductCompletenessView")),
            ["ProductRolesView"] = ObjectSchema(["purchasable", "sellable", "productionInput", "productionOutput"],
                ("purchasable", "ref:ProductRoleView"), ("sellable", "ref:ProductRoleView"), ("productionInput", "ref:ProductRoleView"), ("productionOutput", "ref:ProductRoleView")),
            ["ProductRoleView"] = ObjectSchema(["value", "provenance"], ("value", "type:boolean|null"), ("provenance", "type:string")),
            ["ProductCompletenessView"] = ObjectSchema(["evaluation", "missingFields"], ("evaluation", "type:string"), ("missingFields", "type:array;items:type:string")),
            ["ProductReferenceLabels"] = ObjectSchema(["group", "class", "family", "primaryStockUnit"],
                ("group", "type:null|string"), ("class", "type:null|string"), ("family", "type:null|string"), ("primaryStockUnit", "type:null|string")),
            ["ProductReferences"] = ObjectSchema([], ("templateId", "type:null|string;format:uuid"), ("templateVersion", "type:null|string"),
                ("attributes", "type:null|object"), ("units", "oneOf:ref:ProductUnitReferences|type:null"), ("trackingPolicyId", "type:null|string;format:uuid")),
            ["ProductUnitReferences"] = ObjectSchema([], new[] { "stock", "purchase", "sales", "price", "production", "package" }.Select(name => (name, "type:null|string;format:uuid")).ToArray()),
            ["ProductReferenceOptions"] = ObjectSchema(["groupId", "classId", "classes", "families", "units", "trackingPolicies", "templates", "unavailableCatalogs"],
                ("groupId", "type:string;format:uuid"), ("classId", "type:null|string;format:uuid"),
                ("classes", "type:array;items:ref:ProductReferenceOption"), ("families", "type:array;items:ref:ProductReferenceOption"),
                ("units", "type:array;items:ref:ProductUnitOption"), ("trackingPolicies", "type:array;items:ref:ProductReferenceOption"),
                ("templates", "type:array;items:ref:ProductTemplateOption"), ("unavailableCatalogs", "type:array;items:type:string")),
            ["ProductReferenceOption"] = ObjectSchema(["id", "displayName"], ("id", "type:string;format:uuid"), ("displayName", "type:string")),
            ["ProductUnitOption"] = ObjectSchema(["id", "displayName", "symbol", "isKilogram"], ("id", "type:string;format:uuid"), ("displayName", "type:string"), ("symbol", "type:string"), ("isKilogram", "type:boolean")),
            ["ProductTemplateOption"] = ObjectSchema(["id", "version", "displayName"], ("id", "type:string;format:uuid"), ("version", "type:string"), ("displayName", "type:string")),
        };
        AssertComponentSchemaMatrix(schemas, expected);
        var queue = new Queue<string>(ProductSuccessSchemas.Values.Select(value => value[(value.IndexOf(':') + 1)..]));
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        while (queue.TryDequeue(out var name))
        {
            if (!reachable.Add(name)) continue;
            foreach (var referenced in ReadSchemaReferences(schemas.GetProperty(name))) queue.Enqueue(referenced);
        }
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), reachable.Order(StringComparer.Ordinal));
        AssertPositiveIntegerStringVersion(schemas, "ProductDraftView");
        AssertRequestSchema(schemas.GetProperty("CreateProductDraftRequest"), ["name", "groupId"], ["classId", "familyId", "roles", "references"],
            ["name", "groupId", "classId", "familyId", "roles", "references"]);
        AssertRequestSchema(schemas.GetProperty("EditProductDraftRequest"), ["expectedVersion", "changes", "reason"], [], ["expectedVersion", "changes", "reason"]);
        foreach (var name in new[] { "CreateProductDraftRequest", "EditProductDraftRequest", "ProductDraftChanges", "ProductReferenceChanges", "ProductUnitChanges", "ProductRoleInputs", "ProductRoleInput" })
            Assert.False(schemas.GetProperty(name).GetProperty("additionalProperties").GetBoolean());
        var changes = schemas.GetProperty("ProductDraftChanges");
        Assert.Equal(1, changes.GetProperty("minProperties").GetInt32());
        Assert.Equal("string", changes.GetProperty("properties").GetProperty("name").GetProperty("type").GetString());
        var optionalGuid = schemas.GetProperty("OptionalOfGuid");
        Assert.True(IsNullable(optionalGuid));
        Assert.Equal("uuid", optionalGuid.GetProperty("format").GetString());
        foreach (var (optional, target) in new[] { ("OptionalOfProductRoleInputs", "ProductRoleInputs"),
            ("OptionalOfProductReferenceChanges", "ProductReferenceChanges"), ("OptionalOfProductUnitChanges", "ProductUnitChanges") })
            Assert.Equal("#/components/schemas/" + target, Assert.Single(schemas.GetProperty(optional).GetProperty("allOf").EnumerateArray()).GetProperty("$ref").GetString());
        Assert.Equal("object", Assert.Single(schemas.GetProperty("OptionalOfIReadOnlyDictionaryOfstringAndJsonElement").GetProperty("allOf").EnumerateArray()).GetProperty("type").GetString());
        Assert.Equal(2, schemas.GetProperty("ProductRoleInput").GetProperty("oneOf").GetArrayLength());
        var version = schemas.GetProperty("EditProductDraftRequest").GetProperty("properties").GetProperty("expectedVersion");
        Assert.Equal("string", version.GetProperty("type").GetString());
        Assert.Equal(19, version.GetProperty("maxLength").GetInt32());
        Assert.Equal(ErpOpenApiContract.PositiveIntegerStringPattern, version.GetProperty("pattern").GetString());
        var problem = schemas.GetProperty("ProductRuntimeProblemContract");
        Assert.Equal(["status", "title", "type"], problem.GetProperty("required").EnumerateArray().Select(value => value.GetString()).Order(StringComparer.Ordinal));
        Assert.False(problem.GetProperty("properties").TryGetProperty("conflict", out _));
        foreach (var field in new[] { "expectedVersion", "currentVersion" })
            Assert.Equal("string", problem.GetProperty("properties").GetProperty(field).GetProperty("type").GetString());
    }
}
