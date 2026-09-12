using GoymenERP.Modules.Product.Infrastructure.Persistence;
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

    private static ProductDbContext Context() => new(new DbContextOptionsBuilder<ProductDbContext>()
        .UseSqlServer("Server=127.0.0.1,1;Database=ProductModelOnly;Integrated Security=true;Encrypt=false")
        .Options);
}
