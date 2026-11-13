using System.Text.Json;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D318 (owner decision of 2026-11-13): the input issue's 409 names the issue Inventory refused for physical
/// stock in an optional <c>stockShortfall</c> member. The documented body is the shared coded problem plus that one
/// optional member, and the member carries identities and the requested kilograms only.
/// </summary>
public sealed partial class OpenApiContractGenerationTests
{
    private const string InputIssueConflictProblem = "ManufacturingInputIssueConflictProblemContract";
    private const string StockShortfallView = "ManufacturingStockShortfallViewV1";

    private static readonly IReadOnlyDictionary<string, string> EndpointProblemResponses =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["IssueManufacturingInputV1:409"] = $"#/components/schemas/{InputIssueConflictProblem}",
        };

    private static readonly IReadOnlyDictionary<string, ComponentSchemaExpectation>
        ExpectedManufacturingStockShortfallSchemas =
            new Dictionary<string, ComponentSchemaExpectation>(StringComparer.Ordinal)
            {
                [InputIssueConflictProblem] = ObjectSchema(
                    ["type", "title", "status", "detail", "code"],
                    ("type", "type:string"),
                    ("title", "type:string"),
                    ("status", "type:integer;format:int32"),
                    ("detail", "type:string"),
                    ("code", "type:string"),
                    ("correlationId", "type:null|string"),
                    ("conflict", "oneOf:ref:ErpVersionConflictContract|type:null"),
                    ("stockShortfall", $"oneOf:ref:{StockShortfallView}|type:null")),
                [StockShortfallView] = ObjectSchema(
                    ["movementKind", "sourceKind", "sourceId", "sourceLineId", "labelId", "productId", "locationId",
                        "requestedKg"],
                    ("movementKind", "type:string"),
                    ("sourceKind", "type:string"),
                    ("sourceId", "type:string;format:uuid"),
                    ("sourceLineId", "type:null|string;format:uuid"),
                    ("labelId", "type:string;format:uuid"),
                    ("productId", "type:string;format:uuid"),
                    ("locationId", "type:null|string;format:uuid"),
                    ("requestedKg", "type:string")),
            };

    private static void AssertManufacturingStockShortfallSchemas(JsonElement schemas)
    {
        AssertComponentSchemaMatrix(schemas, ExpectedManufacturingStockShortfallSchemas);

        // A superset of the shared coded problem: every shared member unchanged, the same required set, and only
        // the optional stockShortfall added. Old clients read the same answer they read before.
        var shared = schemas.GetProperty("ErpProblemDetailsContract");
        var inputIssue = schemas.GetProperty(InputIssueConflictProblem);
        Assert.Equal(RequiredNames(shared), RequiredNames(inputIssue));
        var sharedProperties = PropertyDescriptors(shared);
        var inputIssueProperties = PropertyDescriptors(inputIssue);
        Assert.Equal(
            sharedProperties.Keys.Append("stockShortfall").Order(StringComparer.Ordinal),
            inputIssueProperties.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, descriptor) in sharedProperties)
        {
            Assert.Equal(descriptor, inputIssueProperties[name]);
        }

        // Disclosure (NEEDS-SINAN): the record-facts caller sees no label stock elsewhere in the input-issue flow,
        // so the view names identities and the request only; balances and other demands stay out of the contract.
        var viewProperties = PropertyDescriptors(schemas.GetProperty(StockShortfallView));
        Assert.DoesNotContain(viewProperties.Keys, name =>
            name.Contains("available", StringComparison.OrdinalIgnoreCase)
            || name.Contains("reserved", StringComparison.OrdinalIgnoreCase)
            || name.Contains("balance", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string?> RequiredNames(JsonElement schema) =>
        schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()).Order(StringComparer.Ordinal);

    private static Dictionary<string, string> PropertyDescriptors(JsonElement schema) =>
        schema.GetProperty("properties").EnumerateObject()
            .ToDictionary(property => property.Name, property => DescribeSchema(property.Value), StringComparer.Ordinal);
}
