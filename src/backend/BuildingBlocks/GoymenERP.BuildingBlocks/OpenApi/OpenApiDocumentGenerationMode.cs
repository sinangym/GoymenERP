using System.Reflection;

namespace GoymenERP.BuildingBlocks.OpenApi;

public static class OpenApiDocumentGenerationMode
{
    public const string EntryAssemblyName = "GetDocument.Insider";

    public static bool IsActive => string.Equals(
        Assembly.GetEntryAssembly()?.GetName().Name,
        EntryAssemblyName,
        StringComparison.Ordinal);
}
