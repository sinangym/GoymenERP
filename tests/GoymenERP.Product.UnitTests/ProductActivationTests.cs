using System.Text.Json;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using ProductCard = GoymenERP.Modules.Product.Domain.Products.Product;

namespace GoymenERP.Product.UnitTests;

/// <summary>DEC-PRD-010: approving a Draft card (Draft -> Active). Synthetic cards only.</summary>
public sealed class ProductActivationTests
{
    private static ProductCard Create() => ProductCard.CreateDraft(
        Guid.NewGuid(), ProductCode.FromSequence("SAC", 1), "Sac", Guid.NewGuid(),
        null, null, ProductRoles.NotEvaluated, DateTimeOffset.UtcNow);

    [Fact]
    public void A_draft_becomes_active_at_the_version_the_approver_read_and_keeps_every_card_value()
    {
        var product = Create();
        var before = (product.Id, product.Code, product.Name, product.GroupId, product.ClassId, product.FamilyId, product.Roles);

        product.Activate(1, DateTimeOffset.UtcNow);

        Assert.Equal((ProductLifecycle.Active, 2L), (product.Lifecycle, product.Version));
        Assert.Equal(before, (product.Id, product.Code, product.Name, product.GroupId, product.ClassId, product.FamilyId, product.Roles));
        Assert.Equal("Active", ProductDraftView.FromProduct(product).Lifecycle);
    }

    [Fact]
    public void A_stale_version_changes_nothing_and_only_a_draft_is_approved()
    {
        var product = Create();

        var stale = Assert.Throws<ProductVersionConflictException>(() => product.Activate(7, DateTimeOffset.UtcNow));
        Assert.Equal((7L, 1L), (stale.ExpectedVersion, stale.CurrentVersion));
        Assert.Equal((ProductLifecycle.Draft, 1L), (product.Lifecycle, product.Version));

        product.Activate(1, DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => product.Activate(2, DateTimeOffset.UtcNow));
        // An Active card is not a draft any more: its basic fields are not edited as one.
        Assert.Throws<InvalidOperationException>(() => product.EditBasic(2, "Sac", null, null, ProductRoles.NotEvaluated,
            "Düzeltme", DateTimeOffset.UtcNow));
        Assert.Equal((ProductLifecycle.Active, 2L), (product.Lifecycle, product.Version));
    }

    [Fact]
    public void The_stored_row_accepts_only_the_activation_of_the_same_card()
    {
        var product = Create();
        var references = new ProductReferences();
        var row = ProductRow.FromRecord(ProductStoredRecord.Capture(product, references));
        var draft = row.ToRecord();

        product.Activate(1, DateTimeOffset.UtcNow);
        row.ApplyActivation(ProductStoredRecord.Capture(product, references));
        Assert.Equal(((int)ProductLifecycle.Active, 2L), (row.Lifecycle, row.Version));

        // Not twice, and never with another value slipped in.
        Assert.Throws<InvalidOperationException>(() => row.ApplyActivation(row.ToRecord()));
        var fresh = ProductRow.FromRecord(draft);
        var renamed = draft with { Name = "Başka ad", Lifecycle = (int)ProductLifecycle.Active, Version = 2 };
        Assert.Throws<InvalidOperationException>(() => fresh.ApplyActivation(renamed));
    }

    [Fact]
    public void The_activation_intent_is_versioned_canonical_and_never_keeps_the_raw_key()
    {
        var id = Guid.NewGuid();
        var first = ProductCommandIntent.Activate("activation-key", id, new ActivateProductRequest("1"));
        var again = ProductCommandIntent.Activate("activation-key", id, new ActivateProductRequest("1"));
        var otherVersion = ProductCommandIntent.Activate("activation-key", id, new ActivateProductRequest("2"));

        Assert.Equal((first.KeyHash, first.RequestHash), (again.KeyHash, again.RequestHash));
        Assert.NotEqual(first.RequestHash, otherVersion.RequestHash);
        Assert.DoesNotContain("activation-key", first.CanonicalRequestJson, StringComparison.Ordinal);
        using var envelope = JsonDocument.Parse(first.CanonicalRequestJson);
        Assert.Equal("product.card.activate.v1", envelope.RootElement.GetProperty("operation").GetString());
        Assert.Equal(id, envelope.RootElement.GetProperty("productId").GetGuid());

        Assert.Throws<ProductValidationException>(() => ProductCommandIntent.Activate("activation-key", Guid.Empty, new ActivateProductRequest("1")));
        Assert.Throws<ProductValidationException>(() => ProductCommandIntent.Activate("activation-key", id, new ActivateProductRequest("0")));
    }
}
