using System.Globalization;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Product.Domain.Products;

namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// Narrow Draft contract candidate. No HTTP route or persistence is enabled by
/// this DTO. Group catalogue and group-shared authorization require integration.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateProductDraftRequest(
    [property: JsonRequired] string Name,
    [property: JsonRequired] Guid GroupId);

public sealed record ProductDraftView(
    Guid Id,
    string Code,
    string Name,
    string Lifecycle,
    string Version,
    DateTimeOffset CreatedAtUtc)
{
    public static ProductDraftView FromProduct(Domain.Products.Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        return new ProductDraftView(
            product.Id,
            product.Code.Value,
            product.Name,
            product.Lifecycle switch
            {
                ProductLifecycle.Draft => "draft",
                ProductLifecycle.Active => "active",
                ProductLifecycle.Inactive => "inactive",
                _ => throw new ArgumentOutOfRangeException(nameof(product)),
            },
            product.Version.ToString(CultureInfo.InvariantCulture),
            product.CreatedAtUtc);
    }
}
