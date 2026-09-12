using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Product.Domain.Products;

namespace GoymenERP.Modules.Product.Application.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateProductDraftRequest(
    [property: JsonRequired] string Name,
    [property: JsonRequired] Guid GroupId,
    Guid? ClassId = null,
    Guid? FamilyId = null,
    ProductRoleInputs? Roles = null,
    ProductReferences? References = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EditProductDraftRequest(
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] ProductDraftChanges Changes,
    [property: JsonRequired] string Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProductDraftChanges
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Name { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<Guid?> ClassId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<Guid?> FamilyId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ProductRoleInputs?> Roles { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ProductReferenceChanges?> References { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProductReferenceChanges
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<Guid?> TemplateId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> TemplateVersion { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, JsonElement>?> Attributes { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ProductUnitChanges?> Units { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<Guid?> TrackingPolicyId { get; init; }

    public ProductReferences ApplyTo(ProductReferences current)
    {
        if (TemplateId.IsSpecified != TemplateVersion.IsSpecified)
        {
            throw new ProductValidationException([new("references.templateId", "template_pair_required")]);
        }

        var templateId = TemplateId.IsSpecified ? TemplateId.Value : current.TemplateId;
        var templateVersion = TemplateVersion.IsSpecified ? TemplateVersion.Value : current.TemplateVersion;
        if ((templateId != current.TemplateId || templateVersion != current.TemplateVersion)
            && current.Attributes?.Count > 0 && !Attributes.IsSpecified)
        {
            throw new ProductValidationException([new("references.attributes", "explicit_attribute_change_required")]);
        }

        if ((Attributes.IsSpecified && Attributes.Value is null) || (Units.IsSpecified && Units.Value is null))
        {
            throw new ProductValidationException([new("references", "null_not_allowed")]);
        }

        return new(templateId, templateVersion,
            Attributes.IsSpecified ? Attributes.Value : current.Attributes,
            Units.IsSpecified ? Units.Value!.ApplyTo(current.Units ?? new()) : current.Units,
            TrackingPolicyId.IsSpecified ? TrackingPolicyId.Value : current.TrackingPolicyId);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProductUnitChanges
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<Guid?> Stock { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<Guid?> Purchase { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<Guid?> Sales { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<Guid?> Price { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<Guid?> Production { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<Guid?> Package { get; init; }

    public ProductUnitReferences ApplyTo(ProductUnitReferences current) => new(
        Stock.IsSpecified ? Stock.Value : current.Stock,
        Purchase.IsSpecified ? Purchase.Value : current.Purchase,
        Sales.IsSpecified ? Sales.Value : current.Sales,
        Price.IsSpecified ? Price.Value : current.Price,
        Production.IsSpecified ? Production.Value : current.Production,
        Package.IsSpecified ? Package.Value : current.Package);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProductRoleInput(
    [property: JsonRequired] bool? Value,
    [property: JsonRequired] string Provenance)
{
    public ProductRoleState ToManualState() => (Provenance, Value) switch
    {
        ("not-evaluated", null) => ProductRoleState.NotEvaluated,
        ("manual", bool value) => ProductRoleState.Manual(value),
        _ => throw new ArgumentException("Rol değeri/kaynağı geçersiz; türetilmiş rol istemciden yazılamaz."),
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProductRoleInputs(
    ProductRoleInput? Purchasable = null,
    ProductRoleInput? Sellable = null,
    ProductRoleInput? ProductionInput = null,
    ProductRoleInput? ProductionOutput = null)
{
    public ProductRoles ApplyTo(ProductRoles current) => new(
        Purchasable?.ToManualState() ?? current.Purchasable,
        Sellable?.ToManualState() ?? current.Sellable,
        ProductionInput?.ToManualState() ?? current.ProductionInput,
        ProductionOutput?.ToManualState() ?? current.ProductionOutput);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProductReferences(
    Guid? TemplateId = null,
    string? TemplateVersion = null,
    IReadOnlyDictionary<string, JsonElement>? Attributes = null,
    ProductUnitReferences? Units = null,
    Guid? TrackingPolicyId = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProductUnitReferences(
    Guid? Stock = null, Guid? Purchase = null, Guid? Sales = null,
    Guid? Price = null, Guid? Production = null, Guid? Package = null);

public sealed record ProductRoleView(bool? Value, string Provenance)
{
    public static ProductRoleView FromState(ProductRoleState state) => new(state.Value,
        state.Provenance switch
        {
            ProductRoleProvenance.NotEvaluated => "not-evaluated",
            ProductRoleProvenance.Manual => "manual",
            ProductRoleProvenance.Derived => "derived",
            _ => throw new InvalidOperationException("Ürün rol kaynağı geçersiz."),
        });
}

public sealed record ProductRolesView(ProductRoleView Purchasable, ProductRoleView Sellable,
    ProductRoleView ProductionInput, ProductRoleView ProductionOutput);

public sealed record ProductDraftView(
    Guid Id, string Code, string Name, Guid GroupId, Guid? ClassId, Guid? FamilyId,
    ProductRolesView Roles, string Lifecycle, string Version,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    ProductReferenceLabels Labels, ProductCompletenessView Completeness)
{
    public static ProductDraftView FromProduct(Domain.Products.Product product,
        ProductReferenceLabels? labels = null, ProductCompletenessView? completeness = null)
    {
        ArgumentNullException.ThrowIfNull(product);
        var code = ProductCode.Parse(product.Code.Value);
        var lifecycle = product.Lifecycle switch
        {
            ProductLifecycle.Draft => "Draft",
            ProductLifecycle.Active => "Active",
            ProductLifecycle.Inactive => "Inactive",
            _ => throw new InvalidOperationException("Ürün yaşam döngüsü geçersiz."),
        };
        return new ProductDraftView(product.Id, code.Value, product.Name,
            product.GroupId, product.ClassId, product.FamilyId,
            new(ProductRoleView.FromState(product.Roles.Purchasable),
                ProductRoleView.FromState(product.Roles.Sellable),
                ProductRoleView.FromState(product.Roles.ProductionInput),
                ProductRoleView.FromState(product.Roles.ProductionOutput)),
            lifecycle, product.Version.ToString(CultureInfo.InvariantCulture),
            product.CreatedAtUtc, product.UpdatedAtUtc,
            labels ?? new(null, null, null, null), completeness ?? new("NotEvaluated", []));
    }
}

public sealed record ProductCardView(ProductDraftView Product, ProductReferences References)
{
    public ProductCompletenessView Completeness => Product.Completeness;
}

public sealed record ProductCompletenessView(string Evaluation, IReadOnlyList<string> MissingFields);
public sealed record ProductReferenceLabels(string? Group, string? Class, string? Family, string? PrimaryStockUnit);

public static class ProductVersion
{
    public static long Parse(string value)
    {
        if (string.IsNullOrEmpty(value) || value[0] == '0'
            || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            || version <= 0)
        {
            throw new ArgumentException("Ürün sürümü pozitif ve kayıpsız ondalık metin olmalıdır.", nameof(value));
        }

        return version;
    }
}
