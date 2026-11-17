using System.Collections.Frozen;
namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>BS-10 membership maintenance names only; no assignment, role, seed or Critical classification.</summary>
public static class ProductPaintMembershipPermissionManifestV1
{
    public const int Version = 1;
    public static IReadOnlySet<string> Names { get; } = new[] { ProductPaintMembershipPermissionsV1.Manage }.ToFrozenSet(StringComparer.Ordinal);
}
