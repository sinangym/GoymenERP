using System.Globalization;
using GoymenERP.Modules.Documents.Domain;
using Microsoft.Extensions.Configuration;

namespace GoymenERP.Modules.Documents.Infrastructure;

/// <summary>
/// KLN-D33: admission may narrow the existing closed PDF/PNG/JPEG set. No configuration means the existing set;
/// an explicitly empty, malformed or expanded set is unavailable. This never reclassifies an immutable old asset.
/// The vault's existing DocumentsServiceBindings remains the sole byte-limit source.
/// </summary>
public sealed class DocumentBrandingFilePolicy(IConfiguration configuration)
{
    public const string AllowedContentTypesKey = "Documents:Branding:AllowedContentTypes";

    public DocumentBrandingFilePolicyRead Read()
    {
        var section = configuration.GetSection(AllowedContentTypesKey);
        var configured = configuration.GetSection("Documents:Branding").GetChildren()
            .Any(child => string.Equals(child.Key, "AllowedContentTypes", StringComparison.OrdinalIgnoreCase));
        if (!configured) return new(true, DocumentContentPolicy.StandardKinds);
        var children = section.GetChildren().ToArray();
        if (section.Value is not null || children.Length < 1 || children.Length > DocumentContentPolicy.StandardKinds.Count) return Closed();
        var kinds = new HashSet<DocumentFileKind>();
        for (var index = 0; index < children.Length; index++)
        {
            var child = children[index];
            if (child.Key != index.ToString(CultureInfo.InvariantCulture) || child.GetChildren().Any()) return Closed();
            var known = DocumentContentPolicy.InfosOf(DocumentContentPolicy.StandardKinds)
                .SingleOrDefault(info => string.Equals(info.ContentType, child.Value, StringComparison.Ordinal));
            if (known is null || !kinds.Add(known.Kind)) return Closed();
        }
        // A stable closed order, independent of how the operator ordered the selected subset.
        return new(true, Array.AsReadOnly(DocumentContentPolicy.StandardKinds.Where(kinds.Contains).ToArray()));
    }

    private static DocumentBrandingFilePolicyRead Closed() => new(false, []);
}

public sealed record DocumentBrandingFilePolicyRead(bool Available, IReadOnlyList<DocumentFileKind> Kinds);
