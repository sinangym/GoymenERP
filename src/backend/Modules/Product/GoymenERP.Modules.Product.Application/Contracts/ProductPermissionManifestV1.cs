using System.Collections.Frozen;

namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// Product-owned naming input for the platform's versioned manifest/security review.
/// This declares names only: it registers no permission, role, assignment or scope.
/// The locked grantee is defined by PRD-PERM-001; global eligibility remains in Identity.
/// </summary>
public static class ProductPermissionManifestV1
{
    public const int Version = 1;
    public const string Subject = "Product";
    public const string Read = "product.master.read";
    public const string CreateDraft = "product.draft.create";
    public const string EditDraft = "product.draft.edit";
    public static IReadOnlySet<string> Names { get; } = new[] { Read, CreateDraft, EditDraft }
        .ToFrozenSet(StringComparer.Ordinal);
}
