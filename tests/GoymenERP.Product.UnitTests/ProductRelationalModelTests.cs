using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Product.UnitTests;

// SQL provider translation/model only: no connection, schema or real SQL claim.
public sealed class ProductRelationalModelTests
{
    [Fact]
    public void Code_and_aliases_share_one_unique_namespace_and_product_version_is_concurrent()
    {
        using var context = Context();
        var code = context.Model.FindEntityType(typeof(ProductCodeRegistration))!;
        Assert.Equal(nameof(ProductCodeRegistration.Code), Assert.Single(code.FindPrimaryKey()!.Properties).Name);
        Assert.DoesNotContain(code.GetProperties(), property => property.Name == "CompanyId");
        var product = context.Model.FindEntityType(typeof(ProductRow))!;
        Assert.True(product.FindProperty(nameof(ProductRow.Version))!.IsConcurrencyToken);
        Assert.DoesNotContain(product.GetProperties(), property => property.Name == "CompanyId");
    }

    [Fact]
    public void Counter_is_shared_by_prefix_and_uses_an_optimistic_concurrency_predicate()
    {
        using var context = Context();
        var counter = context.Model.FindEntityType(typeof(ProductCodeCounter))!;
        Assert.Equal(nameof(ProductCodeCounter.Prefix), Assert.Single(counter.FindPrimaryKey()!.Properties).Name);
        Assert.True(counter.FindProperty(nameof(ProductCodeCounter.LastSequence))!.IsConcurrencyToken);
        Assert.DoesNotContain(counter.GetProperties(), property => property.Name is "CompanyId" or "GroupId");
        var groups = context.Model.FindEntityType(typeof(ProductCodeGroupRow))!;
        Assert.DoesNotContain(groups.GetIndexes(), index => index.IsUnique && index.Properties.Any(property => property.Name == "Prefix"));
    }

    [Fact]
    public void Query_translation_uses_global_product_rows_known_true_roles_and_stable_order()
    {
        using var context = Context();
        var sql = context.Products.AsNoTracking().Where(product => product.SellableValue == true)
            .OrderBy(product => product.Code).ThenBy(product => product.Id).Take(51).ToQueryString();
        Assert.Contains("[product].[Products]", sql);
        Assert.Contains("[SellableValue]", sql);
        Assert.Contains("ORDER BY", sql);
        Assert.DoesNotContain("CompanyId", sql);
    }

    [Fact]
    public async Task Standalone_save_is_closed_until_owner_supported_atomic_enrollment_exists()
    {
        await using var context = Context();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains("atomic writer enrollment", exception.Message);
    }

    [Theory]
    [InlineData("code", "asc")]
    [InlineData("code", "desc")]
    [InlineData("name", "asc")]
    [InlineData("name", "desc")]
    [InlineData("updatedAtUtc", "asc")]
    [InlineData("updatedAtUtc", "desc")]
    public void Every_cursor_sort_translates_with_alias_search_and_known_true_roles(string sort, string direction)
    {
        using var context = Context();
        var store = new ProductReadStore(context);
        var query = new ProductListQuery(Q: "çelik", Roles: "sellable,purchasable", RoleMatch: "all", Sort: sort, Direction: direction);
        var position = new ProductListPosition(Guid.NewGuid(), sort == "updatedAtUtc" ? null : sort == "name" ? "Çelik" : "RM-1",
            sort == "updatedAtUtc" ? DateTimeOffset.UtcNow : null);
        var sql = store.BuildQuery(query, position).Take(51).ToQueryString();
        Assert.Contains("EXISTS", sql);
        Assert.Contains("[product].[CodeRegistrations]", sql);
        Assert.Contains("ORDER BY", sql);
        Assert.Contains("[Id]", sql);
        Assert.DoesNotContain("OFFSET", sql);
        Assert.DoesNotContain("CompanyId", sql);
    }

    private static ProductDbContext Context() => new(new DbContextOptionsBuilder<ProductDbContext>()
        .UseSqlServer("Server=127.0.0.1,1;Database=ProductModelOnly;Integrated Security=true;Encrypt=false")
        .Options);
}
