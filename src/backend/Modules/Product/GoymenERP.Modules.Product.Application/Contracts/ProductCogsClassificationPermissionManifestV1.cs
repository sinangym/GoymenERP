using System.Collections.Frozen;
namespace GoymenERP.Modules.Product.Application.Contracts;
/// <summary>Only registers the explicit global owner operation's audit/capability name. No role, grant or Critical decision.</summary>
public static class ProductCogsClassificationPermissionManifestV1
{
    public const int Version = 1;
    public static IReadOnlySet<string> Names { get; } = new[] { ProductCogsClassificationContractsV1.Manage }.ToFrozenSet(StringComparer.Ordinal);
}
