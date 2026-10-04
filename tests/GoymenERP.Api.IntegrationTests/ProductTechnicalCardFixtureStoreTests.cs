using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Fixtures;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

// Owner-port behavior only; InMemory is not SQL/browser acceptance or proof of the host gate.
public sealed class ProductTechnicalCardFixtureStoreTests
{
    [Fact]
    public void Genuine_release_and_unchanged_synthetic_fixture_are_disjoint_and_jointly_well_formed()
    {
        var genuine = ProductInitialTechnicalDefinitionV1.Definition();
        var synthetic = ProductTechnicalCardFixture.Definition();
        Assert.Empty(genuine.Families.Select(row => row.Id).Intersect(synthetic.Families.Select(row => row.Id)));
        Assert.Empty(genuine.Templates.Select(row => (row.Id, row.Version))
            .Intersect(synthetic.Templates.Select(row => (row.Id, row.Version))));
        Assert.Empty(genuine.Entries.Select(row => (row.Dictionary, row.Code))
            .Intersect(synthetic.Entries.Select(row => (row.Dictionary, row.Code))));

        ProductFamilyDefinitionRulesV2.RequireWellFormed(new(
            [.. genuine.Families, .. synthetic.Families],
            [.. genuine.Templates, .. synthetic.Templates],
            [.. genuine.Entries, .. synthetic.Entries]));
    }

    [Fact]
    public async Task Fixed_fixture_roundtrips_and_identical_rerun_does_not_add_business_effects()
    {
        await using var provider = Services();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IProductTechnicalCardFixtureStore>().EnsureAsync(default);
        }
        await using var read = provider.CreateAsyncScope();
        var db = read.ServiceProvider.GetRequiredService<ProductDbContext>();
        var definition = await new SqlProductFamilyDefinitionSource(db).GetAsync(default);
        ProductFamilyDefinitionRulesV2.RequireWellFormed(definition);
        Assert.Equal(ProductTechnicalCardFixture.FamilyId, Assert.Single(definition.Families).Id);
        Assert.All(definition.Families, row => Assert.Equal(ProductTechnicalCardFixture.SourceRef, row.SourceRef));
        Assert.Equal(4, Assert.Single(definition.Templates).Fields.Count);
        Assert.Equal(ProductDefinitionEntryStatusV2.Candidate, definition.Entries.Single(row => row.Code == "SYN-C").Status);
        Assert.True(definition.Entries.Single(row => row.Code == "syn-none").IsExplicitNone);
        Assert.Empty(await db.CodeGroups.ToListAsync());
        Assert.Empty(await db.Products.ToListAsync());
        Assert.Empty(await db.CodeRegistrations.ToListAsync());
        Assert.Empty(await db.CodeCounters.ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pending_foreign_changes_are_refused_without_flush(bool partial)
    {
        await using var provider = Services();
        await using (var seed = provider.CreateAsyncScope())
            await seed.ServiceProvider.GetRequiredService<IProductTechnicalCardFixtureStore>().EnsureAsync(default);
        // Pending changes must never be included in a fixture flush.
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
            var family = await db.Families.SingleAsync();
            db.Entry(family).Property(row => row.SourceRef).CurrentValue = "REAL-RELEASE-MUST-NOT-BE-OVERWRITTEN";
            if (partial) db.Remove(await db.DictionaryEntries.FirstAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<IProductTechnicalCardFixtureStore>().EnsureAsync(default));
            Assert.Equal("REAL-RELEASE-MUST-NOT-BE-OVERWRITTEN", family.SourceRef);
        }
    }

    [Fact]
    public void Normal_service_collection_does_not_register_fixture_port()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        Assert.Null(provider.GetService<IProductTechnicalCardFixtureStore>());
    }

    private static ServiceProvider Services()
    {
        var database = Guid.NewGuid().ToString();
        return new ServiceCollection()
            .AddDbContext<ProductDbContext>(options => options.UseInMemoryDatabase(database))
            .AddProductTechnicalCardFixtureStore().BuildServiceProvider();
    }
}
