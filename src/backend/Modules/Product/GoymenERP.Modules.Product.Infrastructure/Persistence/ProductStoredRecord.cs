using System.Text.Json;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>
/// Product-owned storage value. No owner CompanyId or calculated completeness is
/// persisted here. Mapping, concurrency predicates and global uniqueness still
/// belong to the forthcoming SQL adapter, not to this serialization boundary.
/// </summary>
public sealed record ProductStoredRecord(
    Guid Id, string Code, string Name, Guid GroupId, Guid? ClassId, Guid? FamilyId,
    ProductStoredRole Purchasable, ProductStoredRole Sellable,
    ProductStoredRole ProductionInput, ProductStoredRole ProductionOutput,
    int Lifecycle, long Version, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    string ReferencesJson)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ProductStoredRecord Capture(Domain.Products.Product product, ProductReferences references)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(references);
        return new(product.Id, product.Code.Value, product.Name, product.GroupId, product.ClassId, product.FamilyId,
            ProductStoredRole.Capture(product.Roles.Purchasable), ProductStoredRole.Capture(product.Roles.Sellable),
            ProductStoredRole.Capture(product.Roles.ProductionInput), ProductStoredRole.Capture(product.Roles.ProductionOutput),
            (int)product.Lifecycle, product.Version, product.CreatedAtUtc, product.UpdatedAtUtc,
            JsonSerializer.Serialize(references, Json));
    }

    public Domain.Products.Product RestoreProduct() => Domain.Products.Product.Restore(
        Id, Code, Name, GroupId, ClassId, FamilyId,
        new(RestoreRole(Purchasable), RestoreRole(Sellable), RestoreRole(ProductionInput), RestoreRole(ProductionOutput)),
        (ProductLifecycle)Lifecycle, Version, CreatedAtUtc, UpdatedAtUtc);

    public ProductReferences RestoreReferences()
    {
        // Fail closed on corrupt storage: "null" cannot be turned into an empty
        // reference set, which would silently erase template/unit assignments.
        if (ReferencesJson is null)
            throw new InvalidOperationException("Stored Product references are missing.");
        return JsonSerializer.Deserialize<ProductReferences>(ReferencesJson, Json)
            ?? throw new InvalidOperationException("Stored Product references are missing.");
    }

    private static ProductRoleState RestoreRole(ProductStoredRole? role) => role?.Restore()
        ?? throw new InvalidOperationException("Stored Product role is missing.");

    public override string ToString() => nameof(ProductStoredRecord);
}

public sealed record ProductStoredRole(bool? Value, int Provenance)
{
    public static ProductStoredRole Capture(ProductRoleState role) => new(role.Value, (int)role.Provenance);

    public ProductRoleState Restore() => (Provenance, Value) switch
    {
        ((int)ProductRoleProvenance.NotEvaluated, null) => ProductRoleState.NotEvaluated,
        ((int)ProductRoleProvenance.Manual, bool value) => ProductRoleState.Manual(value),
        ((int)ProductRoleProvenance.Derived, bool value) => ProductRoleState.Derived(value),
        _ => throw new InvalidOperationException("Stored Product role value/provenance is invalid."),
    };
}
