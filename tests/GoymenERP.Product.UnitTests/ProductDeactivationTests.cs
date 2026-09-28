using System.Text.Json;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using ProductCard = GoymenERP.Modules.Product.Domain.Products.Product;

namespace GoymenERP.Product.UnitTests;

/// <summary>
/// DEC-CAR-003 / PRD-SCOPE-001 (#98) §7.9 / PRD-PERM-001 (#99): an Active card is taken out of new use
/// (Active -> Inactive) and may be returned (Inactive -> Active). Nothing is deleted, every card value is kept,
/// and new operational use is refused by the owner's own eligibility reads. Synthetic cards only.
/// </summary>
public sealed class ProductDeactivationTests
{
    private static ProductCard Active()
    {
        var product = ProductCard.CreateDraft(Guid.NewGuid(), ProductCode.FromSequence("SAC", 1), "Sac", Guid.NewGuid(),
            null, null, ProductRoles.NotEvaluated, DateTimeOffset.UtcNow);
        product.Activate(1, DateTimeOffset.UtcNow);
        return product;
    }

    private static (Guid, ProductCode, string, Guid, Guid?, Guid?, ProductRoles, DateTimeOffset) Values(ProductCard product) =>
        (product.Id, product.Code, product.Name, product.GroupId, product.ClassId, product.FamilyId, product.Roles, product.CreatedAtUtc);

    [Fact]
    public void An_active_card_becomes_inactive_at_the_version_read_and_keeps_every_value_then_returns_to_active()
    {
        var product = Active();
        var values = Values(product);

        product.Deactivate(2, DateTimeOffset.UtcNow);
        Assert.Equal((ProductLifecycle.Inactive, 3L), (product.Lifecycle, product.Version));
        Assert.Equal(values, Values(product));
        Assert.Equal("Inactive", ProductDraftView.FromProduct(product).Lifecycle);

        product.Reactivate(3, DateTimeOffset.UtcNow);
        Assert.Equal((ProductLifecycle.Active, 4L), (product.Lifecycle, product.Version));
        Assert.Equal(values, Values(product));
    }

    [Fact]
    public void A_stale_version_changes_nothing_and_each_move_accepts_only_its_own_source_state()
    {
        var draft = ProductCard.CreateDraft(Guid.NewGuid(), ProductCode.FromSequence("SAC", 1), "Sac", Guid.NewGuid(),
            null, null, ProductRoles.NotEvaluated, DateTimeOffset.UtcNow);
        // A Draft is neither deactivated (it was never in use) nor "reactivated" into use.
        Assert.Throws<InvalidOperationException>(() => draft.Deactivate(1, DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => draft.Reactivate(1, DateTimeOffset.UtcNow));
        Assert.Equal((ProductLifecycle.Draft, 1L), (draft.Lifecycle, draft.Version));

        var product = Active();
        var stale = Assert.Throws<ProductVersionConflictException>(() => product.Deactivate(1, DateTimeOffset.UtcNow));
        Assert.Equal((1L, 2L), (stale.ExpectedVersion, stale.CurrentVersion));
        Assert.Throws<InvalidOperationException>(() => product.Reactivate(2, DateTimeOffset.UtcNow));
        Assert.Equal((ProductLifecycle.Active, 2L), (product.Lifecycle, product.Version));

        product.Deactivate(2, DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => product.Deactivate(3, DateTimeOffset.UtcNow));
        // An Inactive card is not approved (Draft -> Active) and not edited as a draft.
        Assert.Throws<InvalidOperationException>(() => product.Activate(3, DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => product.EditBasic(3, "Sac", null, null, ProductRoles.NotEvaluated,
            "Düzeltme", DateTimeOffset.UtcNow));
        Assert.Equal((ProductLifecycle.Inactive, 3L), (product.Lifecycle, product.Version));
    }

    [Fact]
    public void The_stored_row_accepts_only_the_exact_lifecycle_move_of_the_same_card()
    {
        var product = Active();
        var references = new ProductReferences();
        var row = ProductRow.FromRecord(ProductStoredRecord.Capture(product, references));
        var active = row.ToRecord();

        product.Deactivate(2, DateTimeOffset.UtcNow);
        row.ApplyLifecycleChange(ProductStoredRecord.Capture(product, references), ProductLifecycle.Active, ProductLifecycle.Inactive);
        Assert.Equal(((int)ProductLifecycle.Inactive, 3L), (row.Lifecycle, row.Version));

        // Not twice, never another value slipped in, never to or from Draft, never the wrong direction.
        Assert.Throws<InvalidOperationException>(() =>
            row.ApplyLifecycleChange(row.ToRecord(), ProductLifecycle.Active, ProductLifecycle.Inactive));
        var fresh = ProductRow.FromRecord(active);
        var renamed = active with { Name = "Başka ad", Lifecycle = (int)ProductLifecycle.Inactive, Version = 3 };
        Assert.Throws<InvalidOperationException>(() =>
            fresh.ApplyLifecycleChange(renamed, ProductLifecycle.Active, ProductLifecycle.Inactive));
        var toDraft = active with { Lifecycle = (int)ProductLifecycle.Draft, Version = 3 };
        Assert.Throws<InvalidOperationException>(() =>
            fresh.ApplyLifecycleChange(toDraft, ProductLifecycle.Active, ProductLifecycle.Draft));
        var inactive = active with { Lifecycle = (int)ProductLifecycle.Inactive, Version = 3 };
        Assert.Throws<InvalidOperationException>(() =>
            fresh.ApplyLifecycleChange(inactive, ProductLifecycle.Inactive, ProductLifecycle.Active));
        Assert.Equal(((int)ProductLifecycle.Active, 2L), (fresh.Lifecycle, fresh.Version));
    }

    [Fact]
    public void The_lifecycle_intents_are_versioned_canonical_distinct_and_never_keep_the_raw_key()
    {
        var id = Guid.NewGuid();
        var first = ProductCommandIntent.Deactivate("lifecycle-key", id, new ChangeProductLifecycleRequest("2", "Üretimden kalktı"));
        // Surrounding blanks are one logical command: the reason is normalised before hashing.
        var again = ProductCommandIntent.Deactivate("lifecycle-key", id, new ChangeProductLifecycleRequest("2", "  Üretimden kalktı "));
        var otherReason = ProductCommandIntent.Deactivate("lifecycle-key", id, new ChangeProductLifecycleRequest("2", "Başka gerekçe"));
        var reactivate = ProductCommandIntent.Reactivate("lifecycle-key", id, new ChangeProductLifecycleRequest("2", "Üretimden kalktı"));

        Assert.Equal((first.KeyHash, first.RequestHash), (again.KeyHash, again.RequestHash));
        Assert.NotEqual(first.RequestHash, otherReason.RequestHash);
        Assert.NotEqual(first.RequestHash, reactivate.RequestHash);
        Assert.DoesNotContain("lifecycle-key", first.CanonicalRequestJson, StringComparison.Ordinal);
        using var envelope = JsonDocument.Parse(first.CanonicalRequestJson);
        Assert.Equal("product.card.deactivate.v1", envelope.RootElement.GetProperty("operation").GetString());
        Assert.Equal(id, envelope.RootElement.GetProperty("productId").GetGuid());
        Assert.Equal("Üretimden kalktı", envelope.RootElement.GetProperty("request").GetProperty("reason").GetString());
        using var back = JsonDocument.Parse(reactivate.CanonicalRequestJson);
        Assert.Equal("product.card.reactivate.v1", back.RootElement.GetProperty("operation").GetString());
    }

    [Theory]
    [InlineData("0", "Gerekçe", "expectedVersion", "invalid_version")]
    [InlineData("x", "Gerekçe", "expectedVersion", "invalid_version")]
    [InlineData("2", "", "reason", "invalid_reason")]
    [InlineData("2", "   ", "reason", "invalid_reason")]
    [InlineData("2", "satır\nsonu", "reason", "invalid_reason")]
    public void An_invalid_version_or_reason_is_a_field_error_before_anything_is_resolved(string version, string reason,
        string field, string code)
    {
        foreach (var create in new Func<ProductCommandIntent>[]
                 {
                     () => ProductCommandIntent.Deactivate("key", Guid.NewGuid(), new(version, reason)),
                     () => ProductCommandIntent.Reactivate("key", Guid.NewGuid(), new(version, reason)),
                 })
        {
            var error = Assert.Single(Assert.Throws<ProductValidationException>(create).Errors);
            Assert.Equal((field, code), (error.Field, error.Code));
        }

        Assert.Throws<ProductValidationException>(() =>
            ProductCommandIntent.Deactivate("key", Guid.Empty, new("2", "Gerekçe")));
        Assert.Throws<ProductValidationException>(() =>
            ProductCommandIntent.Deactivate("key", Guid.NewGuid(), new("2", new string('a', 1001))));
    }

    [Fact]
    public void An_inactive_card_is_refused_by_every_operational_eligibility_read_but_stays_a_readable_card()
    {
        var product = Active();
        product.Deactivate(2, DateTimeOffset.UtcNow);
        var card = new ProductCardView(ProductDraftView.FromProduct(product), new ProductReferences());

        // The card itself is unchanged and readable (existing orders, stock and snapshots still show it) ...
        Assert.Equal(("Inactive", "3", "Sac"), (card.Product.Lifecycle, card.Product.Version, card.Product.Name));
        // ... but a new sale, purchase or stock use is refused by the owner's own rule, whatever else the card holds.
        var on = new DateOnly(2026, 9, 28);
        var lifecycle = new ProductFieldError("productId", ProductApiV1.LifecycleConflict);
        Assert.Contains(lifecycle, ProductSalesPreconditions.Check(card, new(product.Id, "3", Guid.NewGuid(), on)));
        Assert.Contains(lifecycle, ProductStockPreconditions.Check(card, new(product.Id, "3", on)));
        Assert.Contains(lifecycle, ProductPurchasePreconditions.Check(card, new(product.Id, "3", Guid.NewGuid(), on)));
    }
}
