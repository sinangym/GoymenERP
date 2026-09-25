using System.Text.Json;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using ProductCard = GoymenERP.Modules.Product.Domain.Products.Product;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductReferenceValidationTests
{
    [Fact]
    public void Serializing_partial_edit_does_not_turn_omitted_fields_into_explicit_nulls()
    {
        var patch = new ProductDraftChanges
        {
            References = new(new ProductReferenceChanges
            {
                Units = new(new ProductUnitChanges { Stock = new((Guid?)null) }),
            }),
        };
        var json = JsonSerializer.Serialize(patch, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("{\"references\":{\"units\":{\"stock\":null}}}", json);
    }

    [Fact]
    public async Task Draft_with_real_group_may_omit_activation_references()
    {
        var catalog = new Catalog();
        var result = await new ProductDraftValidator(catalog).ValidateCreateAsync(new(" Sac ", catalog.Group.Id));
        Assert.Equal("Sac", result.Name);
        Assert.Equal(catalog.Group, result.Group);
        Assert.Equal(ProductRoles.NotEvaluated, result.Roles);
        Assert.Null(result.References.TemplateId);
        Assert.Empty(catalog.CheckedUnits);
    }

    [Fact]
    public async Task Well_formed_but_unknown_group_is_not_a_valid_reference()
    {
        var exception = await Assert.ThrowsAsync<ProductValidationException>(() =>
            new ProductDraftValidator(new Catalog()).ValidateCreateAsync(new("Sac", Guid.NewGuid())));
        Assert.Contains(new ProductFieldError("groupId", "invalid_reference"), exception.Errors);
    }

    [Fact]
    public async Task Nil_group_is_rejected_before_catalog_read()
    {
        var catalog = new Catalog();
        await Assert.ThrowsAsync<ProductValidationException>(() =>
            new ProductDraftValidator(catalog).ValidateCreateAsync(new("Sac", Guid.Empty)));
        Assert.Equal(0, catalog.GroupReads);
    }

    [Fact]
    public async Task Unknown_class_or_family_is_not_treated_as_missing()
    {
        var catalog = new Catalog();
        var exception = await Assert.ThrowsAsync<ProductValidationException>(() =>
            new ProductDraftValidator(catalog).ValidateCreateAsync(new("Sac", catalog.Group.Id, FamilyId: Guid.NewGuid())));
        Assert.Contains(new ProductFieldError("classification", "invalid_reference"), exception.Errors);
    }

    [Fact]
    public async Task Supplied_units_are_checked_and_stock_requires_kilogram_semantics()
    {
        var catalog = new Catalog();
        var invalid = Guid.NewGuid();
        var exception = await Assert.ThrowsAsync<ProductValidationException>(() => new ProductDraftValidator(catalog)
            .ValidateCreateAsync(new("Sac", catalog.Group.Id, References: new(Units: new(Stock: catalog.UnitId, Sales: invalid)))));
        Assert.Contains((catalog.UnitId, true), catalog.CheckedUnits);
        Assert.Contains((invalid, false), catalog.CheckedUnits);
        Assert.Contains(new ProductFieldError("references.units.sales", "invalid_reference"), exception.Errors);
    }

    [Fact]
    public async Task Only_the_stock_purchase_and_sales_unit_roles_are_open_and_the_others_never_reach_the_catalogue()
    {
        // DEC-PRD-006 purchase identity authoring is open; price, production and package stay closed.
        var catalog = new Catalog();
        var exception = await Assert.ThrowsAsync<ProductValidationException>(() => new ProductDraftValidator(catalog)
            .ValidateCreateAsync(new("Sac", catalog.Group.Id, References: new(Units: new(Stock: catalog.UnitId, Sales: catalog.UnitId,
                Purchase: catalog.UnitId, Price: catalog.UnitId, Production: catalog.UnitId, Package: catalog.UnitId)))));

        Assert.Equal([(catalog.UnitId, true), (catalog.UnitId, false), (catalog.UnitId, false)], catalog.CheckedUnits);
        Assert.Equal(
            [
                new ProductFieldError("references.units.price", "invalid_reference"),
                new ProductFieldError("references.units.production", "invalid_reference"),
                new ProductFieldError("references.units.package", "invalid_reference"),
            ],
            exception.Errors);
    }

    [Fact]
    public async Task Purchase_identity_is_optional_independent_and_validated_on_create_and_edit()
    {
        var catalog = new Catalog();
        var validator = new ProductDraftValidator(catalog);
        var created = await validator.ValidateCreateAsync(new("Sac", catalog.Group.Id,
            References: new(Units: new(Purchase: catalog.UnitId))));
        Assert.Equal(catalog.UnitId, created.References.Units!.Purchase);
        Assert.Null(created.References.Units.Stock);
        Assert.Null(created.References.Units.Sales);
        Assert.Equal([(catalog.UnitId, false)], catalog.CheckedUnits);
        var product = ProductCard.CreateDraft(Guid.NewGuid(), ProductCode.FromSequence("SAC", 1), "Sac",
            catalog.Group.Id, null, null, ProductRoles.NotEvaluated, DateTimeOffset.UtcNow);
        var clear = JsonSerializer.Deserialize<EditProductDraftRequest>(
            """{"expectedVersion":"1","reason":"Birim düzeltme","changes":{"references":{"units":{"purchase":null}}}}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var edited = await validator.ValidateEditAsync(product, created.References, clear);
        Assert.Null(edited.References.Units!.Purchase);
        var invalid = await Assert.ThrowsAsync<ProductValidationException>(() => validator.ValidateCreateAsync(
            new("Sac", catalog.Group.Id, References: new(Units: new(Purchase: Guid.NewGuid())))));
        Assert.Equal([new ProductFieldError("references.units.purchase", "invalid_reference")], invalid.Errors);
    }

    [Fact]
    public async Task Supplied_template_fields_must_pass_the_catalog_profile()
    {
        var catalog = new Catalog();
        var exception = await Assert.ThrowsAsync<ProductValidationException>(() => new ProductDraftValidator(catalog)
            .ValidateCreateAsync(new("Sac", catalog.Group.Id, References: new(TemplateId: Guid.NewGuid(), TemplateVersion: "1"))));
        Assert.Contains(new ProductFieldError("references.templateId", "invalid_reference"), exception.Errors);
    }

    [Fact]
    public async Task Partial_edit_preserves_omitted_roles_attributes_and_units()
    {
        var catalog = new Catalog();
        var roles = ProductRoles.NotEvaluated with { Sellable = ProductRoleState.Manual(false) };
        var product = ProductCard.CreateDraft(Guid.NewGuid(), ProductCode.FromSequence("SAC", 1), "Sac",
            catalog.Group.Id, null, null, roles, DateTimeOffset.UtcNow);
        var refs = new ProductReferences(Units: new(Sales: catalog.UnitId));
        var edit = JsonSerializer.Deserialize<EditProductDraftRequest>(
            """{"expectedVersion":"1","reason":"Ad düzeltme","changes":{"name":"Yeni ad"}}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var result = await new ProductDraftValidator(catalog).ValidateEditAsync(product, refs, edit);
        Assert.Equal("Yeni ad", result.Name);
        Assert.Equal(roles, result.Roles);
        Assert.Equal(refs, result.References);
        Assert.Equal("Sac", product.Name);
    }

    [Fact]
    public void Unit_patch_preserves_omitted_values_and_explicit_null_clears_only_target()
    {
        var sales = Guid.NewGuid();
        var units = new ProductUnitReferences(Stock: Guid.NewGuid(), Sales: sales);
        var patch = JsonSerializer.Deserialize<ProductUnitChanges>("""{"stock":null}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var result = patch.ApplyTo(units);
        Assert.Null(result.Stock);
        Assert.Equal(sales, result.Sales);
    }

    [Fact]
    public void Template_change_cannot_silently_drop_existing_attributes()
    {
        var refs = new ProductReferences(Guid.NewGuid(), "1", new Dictionary<string, JsonElement>
        {
            ["thickness"] = JsonSerializer.SerializeToElement(2),
        });
        var patch = new ProductReferenceChanges { TemplateId = new(Guid.NewGuid()), TemplateVersion = new("2") };
        Assert.Throws<ProductValidationException>(() => patch.ApplyTo(refs));
    }

    private sealed class Catalog : IProductReferenceCatalog
    {
        public ProductCodeGroup Group { get; } = new(Guid.NewGuid(), "Sentetik test grubu", "SAC");
        public Guid UnitId { get; } = Guid.NewGuid();
        public int GroupReads { get; private set; }
        public List<(Guid, bool)> CheckedUnits { get; } = [];
        public Task<ProductCodeGroup?> GetCodeGroupAsync(Guid id, CancellationToken cancellationToken)
        {
            GroupReads++;
            return Task.FromResult(id == Group.Id ? Group : null);
        }
        public Task<bool> IsClassificationValidAsync(Guid groupId, Guid? classId, Guid? familyId,
            CancellationToken cancellationToken) => Task.FromResult(classId is null && familyId is null);
        public Task<bool> IsUnitValidAsync(Guid id, bool requiresKilogram, CancellationToken cancellationToken)
        {
            CheckedUnits.Add((id, requiresKilogram));
            return Task.FromResult(id == UnitId);
        }
        public Task<bool> IsTrackingPolicyValidAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<IReadOnlyList<ProductFieldError>> ValidateTemplateAsync(Guid? familyId, ProductReferences references,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProductFieldError>>(
                [new("references.templateId", "invalid_reference")]);
    }
}
