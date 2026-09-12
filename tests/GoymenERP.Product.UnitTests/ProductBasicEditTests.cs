using System.Text.Json;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using ProductCard = GoymenERP.Modules.Product.Domain.Products.Product;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductBasicEditTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static ProductCard Create(ProductRoles? roles = null) => ProductCard.CreateDraft(
        Guid.NewGuid(), ProductCode.FromSequence("SAC", 1), "Sac", Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), roles ?? ProductRoles.NotEvaluated, DateTimeOffset.UtcNow);

    [Fact]
    public void Basic_edit_advances_version_and_stale_second_editor_cannot_overwrite()
    {
        var product = Create();
        var classId = product.ClassId;
        var groupId = product.GroupId;
        var roles = new ProductRoleInputs(Sellable: new(true, "manual")).ApplyTo(product.Roles);
        product.EditBasic(1, "Düzeltilen sac", classId, null, roles, "Ad düzeltildi", DateTimeOffset.UtcNow);

        Assert.Equal(2, product.Version);
        Assert.Equal(groupId, product.GroupId);
        Assert.Equal(classId, product.ClassId);
        Assert.Null(product.FamilyId);
        Assert.Equal(ProductLifecycle.Draft, product.Lifecycle);
        var conflict = Assert.Throws<ProductVersionConflictException>(() => product.EditBasic(
            1, "Eski editör", null, null, ProductRoles.NotEvaluated, "Düzeltme", DateTimeOffset.UtcNow));
        Assert.Equal(2, conflict.CurrentVersion);
        Assert.Equal("Düzeltilen sac", product.Name);
        Assert.Equal("2", ProductDraftView.FromProduct(product).Version);
    }

    [Fact]
    public void Classification_and_multiple_manual_roles_are_independent()
    {
        var product = Create();
        var classId = product.ClassId;
        var roles = new ProductRoleInputs(new(true, "manual"), new(true, "manual")).ApplyTo(product.Roles);
        product.EditBasic(1, product.Name, classId, product.FamilyId, roles, "Ticari roller", DateTimeOffset.UtcNow);
        Assert.Equal(classId, product.ClassId);
        Assert.True(product.Roles.Purchasable.Value);
        Assert.True(product.Roles.Sellable.Value);
        Assert.Null(product.Roles.ProductionInput.Value);
        Assert.Equal(ProductRoleProvenance.NotEvaluated, product.Roles.ProductionInput.Provenance);
    }

    [Fact]
    public void Derived_role_cannot_be_overridden_and_failure_is_atomic()
    {
        var roles = ProductRoles.NotEvaluated with { ProductionInput = ProductRoleState.Derived(true) };
        var product = Create(roles);
        Assert.Throws<InvalidOperationException>(() => product.EditBasic(1, "Değişmesin", null, null,
            roles with { ProductionInput = ProductRoleState.Manual(false) }, "Yanlış", DateTimeOffset.UtcNow));
        Assert.Equal("Sac", product.Name);
        Assert.Equal(1, product.Version);
        Assert.Equal(roles, product.Roles);
    }

    [Theory]
    [InlineData(null, "manual")]
    [InlineData(true, "not-evaluated")]
    [InlineData(true, "derived")]
    [InlineData(false, "unknown")]
    public void Invalid_or_forged_role_provenance_is_rejected(bool? value, string provenance)
    {
        Assert.Throws<ArgumentException>(() => new ProductRoleInput(value, provenance).ToManualState());
    }

    [Fact]
    public void Unknown_and_explicit_false_survive_projection_as_distinct_values()
    {
        Assert.Equal(new ProductRoleView(null, "not-evaluated"),
            ProductRoleView.FromState(ProductRoleState.NotEvaluated));
        Assert.Equal(new ProductRoleView(false, "manual"),
            ProductRoleView.FromState(ProductRoleState.Manual(false)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("01")]
    [InlineData("1.0")]
    [InlineData("+1")]
    [InlineData(" 1")]
    [InlineData("9223372036854775808")]
    public void Noncanonical_or_overflow_version_is_rejected(string value)
    {
        Assert.Throws<ArgumentException>(() => ProductVersion.Parse(value));
    }

    [Fact]
    public void Version_is_lossless_above_javascript_integer_range()
    {
        Assert.Equal(9007199254740993L, ProductVersion.Parse("9007199254740993"));
        Assert.Equal(long.MaxValue, ProductVersion.Parse("9223372036854775807"));
    }

    [Fact]
    public void Missing_change_and_explicit_null_are_distinct()
    {
        var edit = JsonSerializer.Deserialize<EditProductDraftRequest>(
            """{"expectedVersion":"1","reason":"Aile temizleme","changes":{"familyId":null}}""", JsonOptions)!;
        Assert.False(edit.Changes.Name.IsSpecified);
        Assert.False(edit.Changes.ClassId.IsSpecified);
        Assert.True(edit.Changes.FamilyId.IsSpecified);
        Assert.Null(edit.Changes.FamilyId.Value);
        Assert.False(edit.Changes.References.IsSpecified);
    }

    [Fact]
    public void Nil_group_cannot_create_product()
    {
        Assert.Throws<ArgumentException>(() => ProductCard.CreateDraft(Guid.NewGuid(),
            ProductCode.FromSequence("SAC", 1), "Sac", Guid.Empty, null, null,
            ProductRoles.NotEvaluated, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Name_length_and_control_characters_are_validated_without_truncation()
    {
        Assert.Equal("Çelik sac", ProductCard.NormalizeName(" Çelik sac "));
        Assert.Throws<ArgumentException>(() => ProductCard.NormalizeName(new string('a', 251)));
        Assert.Throws<ArgumentException>(() => ProductCard.NormalizeName("Sac\nçelik"));
    }

    [Fact]
    public void Code_roundtrip_is_canonical_and_prefix_alphabet_is_explicit_ascii()
    {
        Assert.Equal("SAC-9223372036854775807", ProductCode.Parse("SAC-9223372036854775807").Value);
        Assert.Throws<ArgumentException>(() => ProductCode.Parse("SAC-01"));
        Assert.Throws<ArgumentException>(() => ProductCode.Parse("sac-1"));
        Assert.Throws<ArgumentException>(() => ProductCode.FromSequence("ÇELİK", 1));
    }
}
