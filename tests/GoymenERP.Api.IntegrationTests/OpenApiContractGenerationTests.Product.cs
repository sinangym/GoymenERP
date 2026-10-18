using System.Globalization;
using System.Text.Json;
using GoymenERP.Api.OpenApi;
using GoymenERP.Modules.Product.Application.Contracts;

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
        new("get", "/api/v1/products/{id}/manufacturing-technical-snapshot",
            ProductManufacturingTechnicalHttpApiV2.OperationId,
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["path:id:true:type:string;format:uuid",
                "query:expectedProductVersion:true:type:string;pattern:^[1-9]\\d{0,18}$",
                "query:effectiveOn:true:type:string;format:date"]),
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
        [ProductManufacturingTechnicalHttpApiV2.OperationId] = "ref:ProductManufacturingTechnicalHttpResultV2",
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
        Assert.Contains(expected.OperationId == ProductManufacturingTechnicalHttpApiV2.OperationId
                ? "product.operational-selection.read"
                : "global Product authority",
            actual.Value.GetProperty("description").GetString(), StringComparison.Ordinal);
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
            if (status >= 400 && status is not (413 or 415))
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
                409 => expected.OperationId is "EditProductDraftV1" or "ActivateProductV1" or "DeactivateProductV1" or "ReactivateProductV1"
                    ? ["idempotency_conflict", "lifecycle_conflict", "version_conflict"] : ["idempotency_conflict"],
                422 => ["validation_failed"],
                500 => ["internal_error"],
                503 => expected.OperationId == ProductManufacturingTechnicalHttpApiV2.OperationId
                    ? ["product_runtime_unconfigured", "product_family_policy_unavailable",
                        "product_family_policy_invalid", "product_manufacturing_technical_snapshot_unavailable",
                        "product_manufacturing_measure_undefined", "settings_provider_unavailable",
                        "settings_profile_invalid", "settings_unit_profile_not_found"]
                    : ["product_write_contention"],
            };
            Assert.Equal(codes.Length > 0, response.Value.TryGetProperty("x-erp-problem-codes", out var actualCodes));
            if (codes.Length > 0) Assert.Equal(codes, actualCodes.EnumerateArray().Select(value => value.GetString()));
            if (status == 400)
            {
                var emptyBodyPossible = mutation
                    || (expected.OperationId is "GetProductsV1" or "GetProductReferenceOptionsV1"
                        or "GetProductDuplicateCandidatesV1" or "PreviewProductImportV1")
                    || expected.OperationId == ProductManufacturingTechnicalHttpApiV2.OperationId;
                Assert.Equal(emptyBodyPossible,
                    response.Value.TryGetProperty(ErpOpenApiContract.EmptyBodyPossibleExtensionName, out var empty) && empty.GetBoolean());
            }
        }
    }

    private static void AssertProductSchemas(JsonElement schemas)
    {
        var expected = new Dictionary<string, ComponentSchemaExpectation>(StringComparer.Ordinal)
        {
            ["ProductPage"] = ObjectSchema(["items", "nextCursor"], ("items", "type:array;items:ref:ProductDraftView"), ("nextCursor", "type:null|string")),
            ["ProductDuplicateCandidates"] = ObjectSchema(["rule", "name", "exactName", "hasMore"],
                ("rule", "type:string"), ("name", "type:string"), ("exactName", "type:array;items:ref:ProductDraftView"),
                ("hasMore", "type:boolean")),
            ["ProductCodeGroup"] = ObjectSchema(["id", "name", "prefix"], ("id", "type:string;format:uuid"), ("name", "type:string"), ("prefix", "type:string")),
            ["ProductMutationResponse"] = ObjectSchema(["card", "isReplay"], ("card", "ref:ProductCardView"), ("isReplay", "type:boolean")),
            ["ProductImportPreviewV1"] = ObjectSchema(["fileSha256", "previewToken", "sourceRowCount", "rows", "errors"],
                ("fileSha256", "type:string"), ("previewToken", "type:string"),
                ("sourceRowCount", "type:integer|string;format:int32"),
                ("rows", "type:array;items:ref:ProductImportSourceRowV1"),
                ("errors", "type:array;items:ref:ProductImportErrorV1"), ("ready", "type:boolean")),
            ["ProductImportSourceRowV1"] = ObjectSchema(["rowNumber", "legacyCode", "name", "groupId"],
                ("rowNumber", "type:integer|string;format:int32"),
                ("legacyCode", "type:string"), ("name", "type:string"),
                ("groupId", "type:string;format:uuid")),
            ["ProductImportErrorV1"] = ObjectSchema(["rowNumber", "field", "code", "message"],
                ("rowNumber", "type:integer|null|string;format:int32"),
                ("field", "type:string"), ("code", "type:string"),
                ("message", "type:string")),
            ["ProductImportResultV1"] = ObjectSchema(["products", "isReplay", "originalCorrelationId"],
                ("products", "type:array;items:ref:ProductImportCreatedV1"), ("isReplay", "type:boolean"),
                ("originalCorrelationId", "type:string")),
            ["ProductImportCreatedV1"] = ObjectSchema(["productId", "code", "legacyCode", "name", "groupId"],
                ("productId", "type:string;format:uuid"), ("code", "type:string"), ("legacyCode", "type:string"),
                ("name", "type:string"), ("groupId", "type:string;format:uuid")),
            ["ProductHistoryView"] = ObjectSchema(["productId", "entries", "truncated"],
                ("productId", "type:string;format:uuid"), ("entries", "type:array;items:ref:ProductHistoryEntryView"),
                ("truncated", "type:boolean")),
            ["ProductHistoryEntryView"] = ObjectSchema(["eventId", "operation", "occurredAtUtc", "versionBefore", "versionAfter", "reason", "changedFields"],
                ("eventId", "type:string;format:uuid"), ("operation", "type:string"),
                ("occurredAtUtc", "type:string;format:date-time"), ("versionBefore", "type:null|string"),
                ("versionAfter", "type:string"), ("reason", "type:string"),
                ("changedFields", "type:array;items:type:string")),
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
                ("group", "type:null|string"), ("class", "type:null|string"), ("family", "type:null|string"), ("primaryStockUnit", "type:null|string"),
                ("template", "type:null|string"), ("familyFacts", "oneOf:ref:ProductFamilyFactsLabels|type:null"),
                ("attributes", "type:null|object"), ("units", "type:null|object")),
            ["ProductFamilyFactsLabels"] = ObjectSchema(["cins", "tur", "tip"],
                ("cins", "type:string"), ("tur", "type:string"), ("tip", "type:string")),
            ["ProductReferences"] = ObjectSchema([], ("templateId", "type:null|string;format:uuid"), ("templateVersion", "type:null|string"),
                ("attributes", "type:null|object"), ("units", "oneOf:ref:ProductUnitReferences|type:null"), ("trackingPolicyId", "type:null|string;format:uuid")),
            ["ProductUnitReferences"] = ObjectSchema([], new[] { "stock", "purchase", "sales", "price", "production", "package" }.Select(name => (name, "type:null|string;format:uuid")).ToArray()),
            ["ProductReferenceOptions"] = ObjectSchema(["groupId", "classId", "classes", "families", "units", "trackingPolicies", "templates", "unavailableCatalogs"],
                ("groupId", "type:string;format:uuid"), ("classId", "type:null|string;format:uuid"),
                ("classes", "type:array;items:ref:ProductReferenceOption"), ("families", "type:array;items:ref:ProductReferenceOption"),
                ("units", "type:array;items:ref:ProductUnitOption"), ("trackingPolicies", "type:array;items:ref:ProductReferenceOption"),
                ("templates", "type:array;items:ref:ProductTemplateOption"), ("unavailableCatalogs", "type:array;items:type:string"),
                ("familyFacts", "type:array|null;items:ref:ProductFamilyFactsOption"),
                ("dictionaries", "type:array|null;items:ref:ProductDictionaryOption")),
            ["ProductReferenceOption"] = ObjectSchema(["id", "displayName"], ("id", "type:string;format:uuid"), ("displayName", "type:string")),
            ["ProductUnitOption"] = ObjectSchema(["id", "displayName", "symbol", "isKilogram"], ("id", "type:string;format:uuid"), ("displayName", "type:string"), ("symbol", "type:string"), ("isKilogram", "type:boolean")),
            ["ProductTemplateOption"] = ObjectSchema(["id", "version", "displayName"], ("id", "type:string;format:uuid"), ("version", "type:string"), ("displayName", "type:string"),
                ("fields", "type:array|null;items:ref:ProductTemplateFieldOption")),
            ["ProductTemplateFieldOption"] = ObjectSchema(["key", "kind", "role", "required", "dictionary", "allowsCandidate", "appliesWhenField", "appliesWhenCode", "parentField"],
                ("key", "type:string"), ("kind", "type:string"), ("role", "type:null|string"), ("required", "type:boolean"),
                ("dictionary", "type:null|string"), ("allowsCandidate", "type:boolean"), ("appliesWhenField", "type:null|string"),
                ("appliesWhenCode", "type:null|string"), ("parentField", "type:null|string")),
            ["ProductDictionaryOption"] = ObjectSchema(["name", "entries"], ("name", "type:string"),
                ("entries", "type:array;items:ref:ProductDictionaryEntryOption")),
            ["ProductDictionaryEntryOption"] = ObjectSchema(["code", "displayName", "status", "parentCode", "isExplicitNone", "massGm2"],
                ("code", "type:string"), ("displayName", "type:string"), ("status", "type:string"), ("parentCode", "type:null|string"),
                ("isExplicitNone", "type:boolean"), ("massGm2", "type:null|number|string;format:double")),
            ["ProductFamilyFactsOption"] = ObjectSchema(["id", "code", "displayName", "cins", "tur", "tip", "templateId", "templateVersion"],
                ("id", "type:string;format:uuid"), ("code", "type:string"), ("displayName", "type:string"), ("cins", "type:string"),
                ("tur", "type:string"), ("tip", "type:string"), ("templateId", "type:null|string;format:uuid"), ("templateVersion", "type:null|string")),
            ["ProductManufacturingTechnicalHttpResultV2"] = ObjectSchema(["snapshot", "errors"],
                ("snapshot", "oneOf:ref:ProductManufacturingTechnicalHttpSnapshotV2|type:null"),
                ("errors", "type:array;items:ref:ProductFieldError")),
            ["ProductManufacturingTechnicalHttpSnapshotV2"] = ObjectSchema(
                ["productId", "productVersion", "family", "template", "geometry", "unitKg", "effectiveOn"],
                ("productId", "type:string;format:uuid"), ("productVersion", "type:string"),
                ("family", "ref:ProductManufacturingFamilyRevisionV1"),
                ("template", "ref:ProductManufacturingTemplateRevisionV1"),
                ("geometry", "ref:ProductManufacturingHttpGeometryV2"),
                ("unitKg", "ref:ProductManufacturingKilogramUnitV1"),
                ("effectiveOn", "type:string;format:date")),
            ["ProductManufacturingFamilyRevisionV1"] = ObjectSchema(["familyId", "familyCode", "sourceRevision"],
                ("familyId", "type:string;format:uuid"), ("familyCode", "type:string"), ("sourceRevision", "type:string")),
            ["ProductManufacturingTemplateRevisionV1"] = ObjectSchema(
                ["templateId", "templateVersion", "templateCode", "sourceRevision"],
                ("templateId", "type:string;format:uuid"), ("templateVersion", "type:string"),
                ("templateCode", "type:string"), ("sourceRevision", "type:string")),
            ["ProductManufacturingKilogramUnitV1"] = ObjectSchema(
                ["companyId", "unitId", "unitCode", "profileId", "profileVersion"],
                ("companyId", "type:string;format:uuid"), ("unitId", "type:string;format:uuid"),
                ("unitCode", "type:string"), ("profileId", "type:string;format:uuid"), ("profileVersion", "type:string")),
            ["ProductFieldError"] = ObjectSchema(["field", "code"], ("field", "type:string"), ("code", "type:string")),
            ["ProductManufacturingHttpGeometryV2"] = ObjectSchema(["kind"]),
            ["ProductManufacturingHttpGeometryV2ProductManufacturingFlatHttpGeometryV2"] = ObjectSchema(
                ["thicknessMm", "widthMm", "lengthMm"],
                ("kind", "enum:flat"), ("thicknessMm", "type:number|string;format:double"),
                ("widthMm", "type:number|string;format:double"), ("lengthMm", "type:null|number|string;format:double")),
            ["ProductManufacturingHttpGeometryV2ProductManufacturingRoundPipeHttpGeometryV2"] = ObjectSchema(
                ["outsideDiameterMm", "wallThicknessMm", "lengthMm"],
                ("kind", "enum:roundPipe"), ("outsideDiameterMm", "type:number|string;format:double"),
                ("wallThicknessMm", "type:number|string;format:double"),
                ("lengthMm", "type:number|string;format:double")),
            ["ProductManufacturingHttpGeometryV2ProductManufacturingProfileHttpGeometryV2"] = ObjectSchema(
                ["sectionWidthMm", "sectionHeightMm", "wallThicknessMm", "lengthMm"],
                ("kind", "enum:profile"), ("sectionWidthMm", "type:number|string;format:double"),
                ("sectionHeightMm", "type:number|string;format:double"),
                ("wallThicknessMm", "type:number|string;format:double"),
                ("lengthMm", "type:number|string;format:double")),
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
        var productVersion = schemas.GetProperty("ProductManufacturingTechnicalHttpSnapshotV2")
            .GetProperty("properties").GetProperty("productVersion");
        Assert.Equal("string", productVersion.GetProperty("type").GetString());
        Assert.Equal(1, productVersion.GetProperty("minLength").GetInt32());
        Assert.Equal(19, productVersion.GetProperty("maxLength").GetInt32());
        Assert.Equal(ErpOpenApiContract.PositiveIntegerStringPattern, productVersion.GetProperty("pattern").GetString());
        var geometry = schemas.GetProperty("ProductManufacturingHttpGeometryV2");
        Assert.Equal("kind", geometry.GetProperty("discriminator").GetProperty("propertyName").GetString());
        Assert.Equal(["flat", "profile", "roundPipe"], geometry.GetProperty("discriminator").GetProperty("mapping")
            .EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
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
