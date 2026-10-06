namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>
/// The first source-stamped Product code group approved for normal authoring. The technical id is
/// UUIDv5(namespace, <c>sac</c>), where namespace is UUIDv5(URL,
/// <c>https://github.com/sinangym/GoymenERP/product/code-groups</c>). The id is assigned once; it is
/// not a business code and must never be reused for another group.
/// </summary>
public static class ProductInitialCodeGroupV1
{
    public const string SourceRef = "issue66-5979389135";
    public static readonly Guid NamespaceId = Guid.Parse("4a97dc56-91c7-57f3-bf78-0652ab82b266");
    public static readonly Guid SacGroupId = Guid.Parse("829e576d-c635-502a-97b8-9d5074c9d6a0");

    public static ProductCodeGroupRow Sac() => ProductCodeGroupRow.Define(SacGroupId, "Sac", "SAC");
}
