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
    /// <summary>
    /// K-1(a) (Sinan's decision, Business #66 5719251344): a SEPARATE, NARROW operational
    /// product-selection read, never the master read. It carries no master management,
    /// cost/price history, audit or unbounded product listing, and grants nothing on its
    /// own: default-deny, no automatic assignment, and the consuming module still needs
    /// its own permission. Registering the name centrally and granting it are the
    /// Identity/central owner's and a human's steps, not this module's.
    /// </summary>
    public const string OperationalSelectionRead = "product.operational-selection.read";
    public const string CreateDraft = "product.draft.create";
    public const string EditDraft = "product.draft.edit";
    public static IReadOnlySet<string> Names { get; } = new[] { Read, OperationalSelectionRead, CreateDraft, EditDraft }
        .ToFrozenSet(StringComparer.Ordinal);
}
