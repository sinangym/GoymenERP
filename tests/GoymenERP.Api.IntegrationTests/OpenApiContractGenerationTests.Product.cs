using System.Globalization;
using System.Text.Json;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.Security;

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
        // DEC-PRD-010 (spec PRODUCT-U01-U02-JIT-001 §10.2): the same-name duplicate warning, a read that never blocks.
        new("get", "/api/v1/products/duplicate-candidates", "GetProductDuplicateCandidatesV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json"),
            ParameterValues: ["query:name:false:type:string", "query:excludeProductId:false:type:string;format:uuid"]),
        new("get", "/api/v1/products/{id}", "GetProductV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "404:application/problem+json"),
            ParameterValues: ["path:id:true:type:string;format:uuid"]),
        new("get", "/api/v1/products/{id}/history", "GetProductHistoryV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "404:application/problem+json"),
            ParameterValues: ["path:id:true:type:string;format:uuid"]),
        new("post", "/api/v1/products", "CreateProductDraftV1",
            AuthenticatedResponses("201:application/json", "400:application/problem+json", "409:application/problem+json", "415:", "503:application/problem+json"),
            "#/components/schemas/CreateProductDraftRequest"),
        new("post", "/api/v1/products/imports/preview", "PreviewProductImportV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "413:", "415:"),
            "#/components/schemas/ProductImportPreviewFormV1", RequestMediaType: "multipart/form-data"),
        new("post", "/api/v1/products/imports", "ApplyProductImportV1",
            AuthenticatedResponses("201:application/json", "400:application/problem+json", "409:application/problem+json", "413:", "415:", "503:application/problem+json"),
            "#/components/schemas/ProductImportApplyFormV1", RequestMediaType: "multipart/form-data"),
        new("patch", "/api/v1/products/{id}/draft", "EditProductDraftV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "404:application/problem+json", "409:application/problem+json", "415:", "503:application/problem+json"),
            "#/components/schemas/EditProductDraftRequest", ["path:id:true:type:string;format:uuid"]),
        // DEC-PRD-010 (#329): the Draft -> Active card approval.
        new("post", "/api/v1/products/{id}/activation", "ActivateProductV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "404:application/problem+json", "409:application/problem+json", "415:", "422:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/ActivateProductRequest", ["path:id:true:type:string;format:uuid"]),
        // DEC-CAR-003 / PRD-PERM-001: Active -> Inactive and back, each under its own global operation and a reason.
        new("post", "/api/v1/products/{id}/deactivation", "DeactivateProductV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "404:application/problem+json", "409:application/problem+json", "415:", "503:application/problem+json"),
            "#/components/schemas/ChangeProductLifecycleRequest", ["path:id:true:type:string;format:uuid"]),
        new("post", "/api/v1/products/{id}/reactivation", "ReactivateProductV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "404:application/problem+json", "409:application/problem+json", "415:", "503:application/problem+json"),
            "#/components/schemas/ChangeProductLifecycleRequest", ["path:id:true:type:string;format:uuid"]),
    ];

    private static readonly IReadOnlyDictionary<string, string> ProductSuccessSchemas = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["GetProductsV1"] = "ref:ProductPage",
        ["GetProductCodeGroupsV1"] = "array:ProductCodeGroup",
        ["GetProductReferenceOptionsV1"] = "ref:ProductReferenceOptions",
        ["GetProductDuplicateCandidatesV1"] = "ref:ProductDuplicateCandidates",
        ["GetProductV1"] = "ref:ProductCardView",
        ["GetProductHistoryV1"] = "ref:ProductHistoryView",
        ["CreateProductDraftV1"] = "ref:ProductMutationResponse",
        ["PreviewProductImportV1"] = "ref:ProductImportPreviewV1",
        ["ApplyProductImportV1"] = "ref:ProductImportResultV1",
        ["EditProductDraftV1"] = "ref:ProductMutationResponse",
        ["ActivateProductV1"] = "ref:ProductMutationResponse",
        ["DeactivateProductV1"] = "ref:ProductMutationResponse",
        ["ReactivateProductV1"] = "ref:ProductMutationResponse",
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
        var mutation = expected.Method is "post" or "patch" && expected.OperationId != "PreviewProductImportV1";
        var multipart = expected.OperationId is "PreviewProductImportV1" or "ApplyProductImportV1";
        if (expected.Method is "post" or "patch")
        {
            var body = actual.Value.GetProperty("requestBody");
            Assert.True(body.GetProperty("required").GetBoolean());
            var mediaType = multipart ? "multipart/form-data" : "application/json";
            Assert.Equal([mediaType], body.GetProperty("content").EnumerateObject().Select(property => property.Name));
            Assert.Equal(expected.RequestSchemaReference, body.GetProperty("content").GetProperty(mediaType)
                .GetProperty("schema").GetProperty("$ref").GetString());
        }
        else Assert.False(actual.Value.TryGetProperty("requestBody", out _));
        var parameters = actual.Value.GetProperty("parameters").EnumerateArray().ToArray();
        var names = new List<string> { "X-Correlation-Id", "X-Causation-Id", "X-Company-Id" };
        if (mutation) names.Add("Idempotency-Key");
        // KIM-D31: every Product write (the import preview too) documents the optional antiforgery header. Its 400
        // codes stay the Product runtime set: the generator adds antiforgery_token_invalid only to the company V1 shape.
        var write = LocalAccountAntiforgery.IsWrite(expected.Method);
        if (write) names.Add(LocalAccountAntiforgery.HeaderName);
        Assert.Equal(names.Order(StringComparer.Ordinal), parameters.Where(parameter => parameter.GetProperty("in").GetString() == "header")
            .Select(parameter => parameter.GetProperty("name").GetString()).Order(StringComparer.Ordinal));
        foreach (var name in new[] { "X-Correlation-Id", "X-Causation-Id" })
            AssertHeaderSchema(parameters, name, false, "string", 1, 128, "^[A-Za-z0-9._:-]+$");
        AssertHeaderSchema(parameters, "X-Company-Id", true, "string", format: "uuid", nonEmptyUuid: true);
        Assert.Contains("origin", parameters.Single(parameter => parameter.GetProperty("name").GetString() == "X-Company-Id")
            .GetProperty("description").GetString(), StringComparison.OrdinalIgnoreCase);
        if (mutation) AssertHeaderSchema(parameters, "Idempotency-Key", true, "string", 1, 128, "^[!-+\\--~]{1,128}$");
        if (write) AssertHeaderSchema(parameters, LocalAccountAntiforgery.HeaderName, false, "string", 1);
        foreach (var response in actual.Value.GetProperty("responses").EnumerateObject())
        {
            var status = int.Parse(response.Name, CultureInfo.InvariantCulture);
            if (status >= 400 && status is not (413 or 415))
            {
                var expectedProblemSchema = status switch
                {
                    500 => "#/components/schemas/ErpProblemDetailsContract",
                    409 when !mutation => "#/components/schemas/ErpProblemDetailsContract",
                    503 when !mutation => "#/components/schemas/ErpProblemDetailsContract",
                    _ => "#/components/schemas/ProductRuntimeProblemContract",
                };
                Assert.Equal(expectedProblemSchema,
                    response.Value.GetProperty("content").GetProperty("application/problem+json").GetProperty("schema").GetProperty("$ref").GetString());
            }
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
                409 => !mutation ? ["organization.company.setup_pending"]
                    : expected.OperationId is "EditProductDraftV1" or "ActivateProductV1" or "DeactivateProductV1" or "ReactivateProductV1"
                        ? ["idempotency_conflict", "lifecycle_conflict", "organization.company.setup_pending", "version_conflict"]
                        : ["idempotency_conflict", "organization.company.setup_pending"],
                422 => ["validation_failed"],
                500 => ["internal_error"], 503 => mutation ? ["product_write_contention", "service_unavailable"] : ["service_unavailable"], _ => [],
            };
            Assert.Equal(codes.Length > 0, response.Value.TryGetProperty("x-erp-problem-codes", out var actualCodes));
            if (codes.Length > 0) Assert.Equal(codes, actualCodes.EnumerateArray().Select(value => value.GetString()));
            if (status == 400)
                Assert.Equal(mutation || expected.OperationId is "GetProductsV1" or "GetProductReferenceOptionsV1"
                        or "GetProductDuplicateCandidatesV1" or "PreviewProductImportV1",
                    response.Value.TryGetProperty(ErpOpenApiContract.EmptyBodyPossibleExtensionName, out var empty) && empty.GetBoolean());
        }
    }

    private static void AssertProductSchemas(JsonElement schemas)
    {
        var expected = new Dictionary<string, ComponentSchemaExpectation>(StringComparer.Ordinal)
        {
            ["ProductCardView"] = ObjectSchema(["product", "references"],
                ("completeness", "ref:ProductCompletenessView"),
                ("product", "ref:ProductDraftView"),
                ("references", "ref:ProductReferences")),
            ["ProductCodeGroup"] = ObjectSchema(["id", "name", "prefix"],
                ("id", "type:string;format:uuid"),
                ("name", "type:string"),
                ("prefix", "type:string")),
            ["ProductCompletenessView"] = ObjectSchema(["evaluation", "missingFields"],
                ("evaluation", "type:string"),
                ("missingFields", "type:array;items:type:string")),
            ["ProductDictionaryEntryOption"] = ObjectSchema(["code", "displayName", "status", "parentCode", "isExplicitNone", "massGm2"],
                ("code", "type:string"),
                ("displayName", "type:string"),
                ("isExplicitNone", "type:boolean"),
                ("massGm2", "type:null|number|string;format:double"),
                ("parentCode", "type:null|string"),
                ("status", "type:string")),
            ["ProductDictionaryOption"] = ObjectSchema(["name", "entries"],
                ("entries", "type:array;items:ref:ProductDictionaryEntryOption"),
                ("name", "type:string")),
            ["ProductDraftView"] = ObjectSchema(["id", "code", "name", "groupId", "classId", "familyId", "roles", "lifecycle", "version", "createdAtUtc", "updatedAtUtc", "labels", "completeness"],
                ("classId", "type:null|string;format:uuid"),
                ("code", "type:string"),
                ("completeness", "ref:ProductCompletenessView"),
                ("createdAtUtc", "type:string;format:date-time"),
                ("familyId", "type:null|string;format:uuid"),
                ("groupId", "type:string;format:uuid"),
                ("id", "type:string;format:uuid"),
                ("labels", "ref:ProductReferenceLabels"),
                ("lifecycle", "type:string"),
                ("name", "type:string"),
                ("roles", "ref:ProductRolesView"),
                ("updatedAtUtc", "type:string;format:date-time"),
                ("version", "type:string")),
            ["ProductDuplicateCandidates"] = ObjectSchema(["rule", "name", "exactName", "hasMore"],
                ("exactName", "type:array;items:ref:ProductDraftView"),
                ("hasMore", "type:boolean"),
                ("name", "type:string"),
                ("rule", "type:string")),
            ["ProductFamilyFactsLabels"] = ObjectSchema(["cins", "tur", "tip"],
                ("cins", "type:string"),
                ("tip", "type:string"),
                ("tur", "type:string")),
            ["ProductFamilyFactsOption"] = ObjectSchema(["id", "code", "displayName", "cins", "tur", "tip", "templateId", "templateVersion"],
                ("cins", "type:string"),
                ("code", "type:string"),
                ("displayName", "type:string"),
                ("id", "type:string;format:uuid"),
                ("templateId", "type:null|string;format:uuid"),
                ("templateVersion", "type:null|string"),
                ("tip", "type:string"),
                ("tur", "type:string")),
            ["ProductHistoryEntryView"] = ObjectSchema(["eventId", "operation", "occurredAtUtc", "versionBefore", "versionAfter", "reason", "changedFields"],
                ("changedFields", "type:array;items:type:string"),
                ("eventId", "type:string;format:uuid"),
                ("occurredAtUtc", "type:string;format:date-time"),
                ("operation", "type:string"),
                ("reason", "type:string"),
                ("versionAfter", "type:string"),
                ("versionBefore", "type:null|string")),
            ["ProductHistoryView"] = ObjectSchema(["productId", "entries", "truncated"],
                ("entries", "type:array;items:ref:ProductHistoryEntryView"),
                ("productId", "type:string;format:uuid"),
                ("truncated", "type:boolean")),
            ["ProductImportCreatedV1"] = ObjectSchema(["productId", "code", "legacyCode", "name", "groupId"],
                ("code", "type:string"),
                ("groupId", "type:string;format:uuid"),
                ("legacyCode", "type:string"),
                ("name", "type:string"),
                ("productId", "type:string;format:uuid")),
            ["ProductImportErrorV1"] = ObjectSchema(["rowNumber", "field", "code", "message"],
                ("code", "type:string"),
                ("field", "type:string"),
                ("message", "type:string"),
                ("rowNumber", "type:integer|null|string;format:int32")),
            ["ProductImportPreviewV1"] = ObjectSchema(["fileSha256", "previewToken", "sourceRowCount", "rows", "errors"],
                ("errors", "type:array;items:ref:ProductImportErrorV1"),
                ("fileSha256", "type:string"),
                ("previewToken", "type:string"),
                ("ready", "type:boolean"),
                ("rows", "type:array;items:ref:ProductImportSourceRowV1"),
                ("sourceRowCount", "type:integer|string;format:int32")),
            ["ProductImportResultV1"] = ObjectSchema(["products", "isReplay", "originalCorrelationId"],
                ("isReplay", "type:boolean"),
                ("originalCorrelationId", "type:string"),
                ("products", "type:array;items:ref:ProductImportCreatedV1")),
            ["ProductImportSourceRowV1"] = ObjectSchema(["rowNumber", "legacyCode", "name", "groupId"],
                ("groupId", "type:string;format:uuid"),
                ("legacyCode", "type:string"),
                ("name", "type:string"),
                ("rowNumber", "type:integer|string;format:int32")),
            ["ProductMutationResponse"] = ObjectSchema(["card", "isReplay"],
                ("card", "ref:ProductCardView"),
                ("isReplay", "type:boolean")),
            ["ProductPage"] = ObjectSchema(["items", "nextCursor"],
                ("items", "type:array;items:ref:ProductDraftView"),
                ("nextCursor", "type:null|string")),
            ["ProductReferenceLabels"] = ObjectSchema(["group", "class", "family", "primaryStockUnit"],
                ("attributes", "type:null|object"),
                ("class", "type:null|string"),
                ("family", "type:null|string"),
                ("familyFacts", "oneOf:ref:ProductFamilyFactsLabels|type:null"),
                ("group", "type:null|string"),
                ("primaryStockUnit", "type:null|string"),
                ("template", "type:null|string"),
                ("units", "type:null|object")),
            ["ProductReferenceOption"] = ObjectSchema(["id", "displayName"],
                ("displayName", "type:string"),
                ("id", "type:string;format:uuid")),
            ["ProductReferenceOptions"] = ObjectSchema(["groupId", "classId", "classes", "families", "units", "trackingPolicies", "templates", "unavailableCatalogs"],
                ("classId", "type:null|string;format:uuid"),
                ("classes", "type:array;items:ref:ProductReferenceOption"),
                ("dictionaries", "type:array|null;items:ref:ProductDictionaryOption"),
                ("families", "type:array;items:ref:ProductReferenceOption"),
                ("familyFacts", "type:array|null;items:ref:ProductFamilyFactsOption"),
                ("groupId", "type:string;format:uuid"),
                ("templates", "type:array;items:ref:ProductTemplateOption"),
                ("trackingPolicies", "type:array;items:ref:ProductReferenceOption"),
                ("unavailableCatalogs", "type:array;items:type:string"),
                ("units", "type:array;items:ref:ProductUnitOption")),
            ["ProductReferences"] = ObjectSchema([],
                ("attributes", "type:null|object"),
                ("templateId", "type:null|string;format:uuid"),
                ("templateVersion", "type:null|string"),
                ("trackingPolicyId", "type:null|string;format:uuid"),
                ("units", "oneOf:ref:ProductUnitReferences|type:null")),
            ["ProductRoleView"] = ObjectSchema(["value", "provenance"],
                ("provenance", "type:string"),
                ("value", "type:boolean|null")),
            ["ProductRolesView"] = ObjectSchema(["purchasable", "sellable", "productionInput", "productionOutput"],
                ("productionInput", "ref:ProductRoleView"),
                ("productionOutput", "ref:ProductRoleView"),
                ("purchasable", "ref:ProductRoleView"),
                ("sellable", "ref:ProductRoleView")),
            ["ProductTemplateFieldOption"] = ObjectSchema(["key", "kind", "role", "required", "dictionary", "allowsCandidate", "appliesWhenField", "appliesWhenCode", "parentField"],
                ("allowsCandidate", "type:boolean"),
                ("appliesWhenCode", "type:null|string"),
                ("appliesWhenField", "type:null|string"),
                ("dictionary", "type:null|string"),
                ("key", "type:string"),
                ("kind", "type:string"),
                ("parentField", "type:null|string"),
                ("required", "type:boolean"),
                ("role", "type:null|string")),
            ["ProductTemplateOption"] = ObjectSchema(["id", "version", "displayName"],
                ("displayName", "type:string"),
                ("fields", "type:array|null;items:ref:ProductTemplateFieldOption"),
                ("id", "type:string;format:uuid"),
                ("version", "type:string")),
            ["ProductUnitOption"] = ObjectSchema(["id", "displayName", "symbol", "isKilogram"],
                ("displayName", "type:string"),
                ("id", "type:string;format:uuid"),
                ("isKilogram", "type:boolean"),
                ("symbol", "type:string")),
            ["ProductUnitReferences"] = ObjectSchema([],
                ("package", "type:null|string;format:uuid"),
                ("price", "type:null|string;format:uuid"),
                ("production", "type:null|string;format:uuid"),
                ("purchase", "type:null|string;format:uuid"),
                ("sales", "type:null|string;format:uuid"),
                ("stock", "type:null|string;format:uuid")),
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
        var historyEntry = schemas.GetProperty("ProductHistoryEntryView").GetProperty("properties");
        foreach (var field in new[] { "versionBefore", "versionAfter" })
        {
            var version = historyEntry.GetProperty(field);
            Assert.Equal(1, version.GetProperty("minLength").GetInt32());
            Assert.Equal(19, version.GetProperty("maxLength").GetInt32());
            Assert.Equal(ErpOpenApiContract.PositiveIntegerStringPattern, version.GetProperty("pattern").GetString());
        }
        AssertRequestSchema(schemas.GetProperty("CreateProductDraftRequest"), ["name", "groupId"], ["classId", "familyId", "roles", "references"],
            ["name", "groupId", "classId", "familyId", "roles", "references"]);
        AssertRequestSchema(schemas.GetProperty("EditProductDraftRequest"), ["expectedVersion", "changes", "reason"], [], ["expectedVersion", "changes", "reason"]);
        AssertRequestSchema(schemas.GetProperty("ActivateProductRequest"), ["expectedVersion"], [], ["expectedVersion"]);
        AssertRequestSchema(schemas.GetProperty("ChangeProductLifecycleRequest"), ["expectedVersion", "reason"], [], ["expectedVersion", "reason"]);
        AssertRequestSchema(schemas.GetProperty("ProductImportPreviewFormV1"),
            ["file", "legacyCodeColumn", "nameColumn", "groupIdColumn"], [],
            ["file", "legacyCodeColumn", "nameColumn", "groupIdColumn"]);
        AssertRequestSchema(schemas.GetProperty("ProductImportApplyFormV1"),
            ["file", "legacyCodeColumn", "nameColumn", "groupIdColumn", "fileSha256", "previewToken"], [],
            ["file", "legacyCodeColumn", "nameColumn", "groupIdColumn", "fileSha256", "previewToken"]);
        foreach (var name in new[] { "CreateProductDraftRequest", "EditProductDraftRequest", "ActivateProductRequest", "ChangeProductLifecycleRequest", "ProductDraftChanges", "ProductReferenceChanges", "ProductUnitChanges", "ProductRoleInputs", "ProductRoleInput" })
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
        foreach (var request in new[] { "EditProductDraftRequest", "ActivateProductRequest", "ChangeProductLifecycleRequest" })
        {
            var version = schemas.GetProperty(request).GetProperty("properties").GetProperty("expectedVersion");
            Assert.Equal("string", version.GetProperty("type").GetString());
            Assert.Equal(1, version.GetProperty("minLength").GetInt32());
            Assert.Equal(19, version.GetProperty("maxLength").GetInt32());
            Assert.Equal(ErpOpenApiContract.PositiveIntegerStringPattern, version.GetProperty("pattern").GetString());
        }
        var problem = schemas.GetProperty("ProductRuntimeProblemContract");
        Assert.Equal(["status", "title", "type"], problem.GetProperty("required").EnumerateArray().Select(value => value.GetString()).Order(StringComparer.Ordinal));
        Assert.False(problem.GetProperty("properties").TryGetProperty("conflict", out _));
        foreach (var field in new[] { "expectedVersion", "currentVersion" })
            Assert.Equal("string", problem.GetProperty("properties").GetProperty(field).GetProperty("type").GetString());
    }
}
