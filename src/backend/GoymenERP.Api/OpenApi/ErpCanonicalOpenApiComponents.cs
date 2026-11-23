using Microsoft.OpenApi;

namespace GoymenERP.Api.OpenApi;

// ASP.NET Core sorts schemas with the current culture after document transformers.
// Apply the ordinal wire order at serialization without changing the document or culture.
internal sealed class ErpCanonicalOpenApiComponents(OpenApiComponents? source)
    : OpenApiComponents(source)
{
    public override void SerializeAsV31(IOpenApiWriter writer) =>
        CanonicalCopy().SerializeAsV31(writer);

    public override void SerializeAsV3(IOpenApiWriter writer) =>
        CanonicalCopy().SerializeAsV3(writer);

    private OpenApiComponents CanonicalCopy()
    {
        var copy = new OpenApiComponents(this);
        if (Schemas is not null)
        {
            copy.Schemas = new Dictionary<string, IOpenApiSchema>(
                Schemas.OrderBy(pair => pair.Key, StringComparer.Ordinal),
                StringComparer.Ordinal);
        }

        return copy;
    }
}
