using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Api.OpenApi;
using Microsoft.OpenApi;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class OpenApiContractGenerationTests
{
    [Fact]
    public void ComponentSerialization_IsOrdinalAcrossCulturesAndPreservesContentAndSource()
    {
        // Check the actual MSBuild producer too: a helper that is not registered
        // cannot fix the culture-dependent ordering applied after transformers.
        using var artifact = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(
            FindRepositoryRoot(), "artifacts", CanonicalFileName)));
        var generatedNames = artifact.RootElement.GetProperty("components")
            .GetProperty("schemas").EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(generatedNames.Order(StringComparer.Ordinal), generatedNames);

        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var version in new[] { OpenApiSpecVersion.OpenApi3_0, OpenApiSpecVersion.OpenApi3_1 })
            {
                byte[]? first = null;
                foreach (var culture in new[]
                         {
                             CultureInfo.GetCultureInfo("tr-TR"),
                             CultureInfo.GetCultureInfo("en-US"),
                             CultureInfo.InvariantCulture,
                         })
                {
                    CultureInfo.CurrentCulture = culture;
                    CultureInfo.CurrentUICulture = culture;
                    var source = ComponentsInCultureOrder(culture);
                    var sourceNames = source.Schemas!.Keys.ToArray();
                    var sourceBytes = SerializeComponents(source, version);
                    var canonical = new ErpCanonicalOpenApiComponents(source);
                    // Reproduce the framework's post-transform schema assignment.
                    canonical.Schemas = new Dictionary<string, IOpenApiSchema>(
                        canonical.Schemas!.OrderBy(pair => pair.Key), StringComparer.Ordinal);
                    var canonicalNames = canonical.Schemas.Keys.ToArray();

                    var bytes = SerializeComponents(canonical, version);

                    first ??= bytes;
                    Assert.Equal(first, bytes);
                    using var originalJson = JsonDocument.Parse(sourceBytes);
                    using var canonicalJson = JsonDocument.Parse(bytes);
                    Assert.True(JsonElement.DeepEquals(originalJson.RootElement, canonicalJson.RootElement));
                    var schemas = canonicalJson.RootElement.GetProperty("schemas");
                    Assert.Equal(sourceNames.Order(StringComparer.Ordinal),
                        schemas.EnumerateObject().Select(property => property.Name));
                    Assert.Equal("bearer", canonicalJson.RootElement.GetProperty("securitySchemes")
                        .GetProperty("Bearer").GetProperty("scheme").GetString());
                    Assert.Equal("source-preserved", canonicalJson.RootElement.GetProperty("x-erp-order-proof").GetString());
                    Assert.Equal(sourceNames, source.Schemas.Keys);
                    Assert.Equal(canonicalNames, canonical.Schemas.Keys);
                    Assert.Equal(sourceBytes, SerializeComponents(source, version));
                    Assert.Equal(bytes, SerializeComponents(canonical, version));
                    Assert.Same(source.Schemas[sourceNames[0]], canonical.Schemas[sourceNames[0]]);
                    Assert.Same(culture, CultureInfo.CurrentCulture);
                    Assert.Same(culture, CultureInfo.CurrentUICulture);
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static OpenApiComponents ComponentsInCultureOrder(CultureInfo culture)
    {
        string[] names =
        [
            "FinanceOpenItemViewV1", "FinanceOpeningPeriodViewV1",
            "ProductItemTypeViewV1", "ProductionCostViewV1",
        ];
        return new OpenApiComponents
        {
            Schemas = names.Order(StringComparer.Create(culture, ignoreCase: false)).ToDictionary(
                name => name,
                name => (IOpenApiSchema)new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Description = name,
                    Required = new HashSet<string>(StringComparer.Ordinal) { "companyId" },
                    Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
                    {
                        ["companyId"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
                    },
                },
                StringComparer.Ordinal),
            SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal)
            {
                ["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
                },
            },
            Responses = new Dictionary<string, IOpenApiResponse>(StringComparer.Ordinal)
            {
                ["Unavailable"] = new OpenApiResponse { Description = "Kaynak kullanılamıyor." },
            },
            Extensions = new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal)
            {
                ["x-erp-order-proof"] = new JsonNodeExtension(JsonValue.Create("source-preserved")!),
            },
        };
    }

    private static byte[] SerializeComponents(OpenApiComponents components, OpenApiSpecVersion version)
    {
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        var writer = new OpenApiJsonWriter(text);
        if (version == OpenApiSpecVersion.OpenApi3_1)
        {
            components.SerializeAsV31(writer);
        }
        else
        {
            components.SerializeAsV3(writer);
        }

        return Encoding.UTF8.GetBytes(text.ToString());
    }
}
