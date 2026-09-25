using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Product.Infrastructure.Fixtures;

/// <summary>Explicit test-only port. The host must register it only behind its existing complete fixture gate.</summary>
public interface IProductTechnicalCardFixtureStore
{
    Task EnsureAsync(CancellationToken cancellationToken);
}

public static class ProductTechnicalCardFixtureStoreRegistration
{
    public static IServiceCollection AddProductTechnicalCardFixtureStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IProductTechnicalCardFixtureStore, ProductTechnicalCardFixtureStore>();
        return services;
    }
}

/// <summary>Fixed synthetic u02 data, not a Q6 release, grant, unit seed or business activity.</summary>
public static class ProductTechnicalCardFixture
{
    public static readonly Guid FamilyId = Guid.Parse("b6100000-0000-4000-8000-000000000201");
    public static readonly Guid TemplateId = Guid.Parse("b6200000-0000-4000-8000-000000000201");
    public const string SourceRef = "SYNTHETIC-browser-suite-u02";

    // A fresh value prevents callers from changing the data used by the store.
    public static ProductFamilyDefinitionV2 Definition() => new(
        [new(FamilyId, "syn-u02-coil", "SENTETIK browser-suite:u02 family", "SENTETIK coil",
            "SENTETIK process", "SENTETIK form", ProductFamilyRuleSetsV2.GdcBandCoil, SourceRef)],
        [new(TemplateId, "1", "syn-u02-template", "SENTETIK browser-suite:u02 template", new HashSet<Guid> { FamilyId },
            [new("syn-thickness-mm", ProductTemplateFieldKindV2.MeasureMillimetre, ProductTemplateFieldRolesV2.Thickness, true, null, false, null, null, null),
             new("syn-width-mm", ProductTemplateFieldKindV2.MeasureMillimetre, ProductTemplateFieldRolesV2.Width, true, null, false, null, null, null),
             new("syn-grade", ProductTemplateFieldKindV2.DictionaryReference, ProductTemplateFieldRolesV2.Quality, true, "syn-u02-grades", false, null, null, null),
             new("syn-coating", ProductTemplateFieldKindV2.DictionaryReference, ProductTemplateFieldRolesV2.Coating, true, "syn-u02-coatings", false, null, null, null)], SourceRef)],
        [new("syn-u02-grades", "SYN-A", "SENTETIK grade A", ProductDefinitionEntryStatusV2.Approved, SourceRef, null, null, false),
         new("syn-u02-grades", "SYN-C", "SENTETIK candidate", ProductDefinitionEntryStatusV2.Candidate, SourceRef, null, null, false),
         new("syn-u02-coatings", "syn-none", "SENTETIK kaplamasız", ProductDefinitionEntryStatusV2.Approved, SourceRef, null, null, true)]);
}

internal sealed class ProductTechnicalCardFixtureStore(ProductDbContext context) : IProductTechnicalCardFixtureStore
{
    public async Task EnsureAsync(CancellationToken cancellationToken)
    {
        if (context.ChangeTracker.Entries().Any())
            throw new InvalidOperationException("A technical fixture requires a clean Product context.");
        var expected = ProductTechnicalCardFixture.Definition();
        ProductFamilyDefinitionRulesV2.RequireWellFormed(expected);
        var families = await context.Families.CountAsync(cancellationToken);
        var templates = await context.Templates.CountAsync(cancellationToken);
        var links = await context.TemplateFamilies.CountAsync(cancellationToken);
        var fields = await context.TemplateFields.CountAsync(cancellationToken);
        var entries = await context.DictionaryEntries.CountAsync(cancellationToken);
        if (families + templates + links + fields + entries == 0)
        {
            await context.AddTechnicalCardFixtureAsync(cancellationToken);
            return;
        }

        // Never merge, repair or replace any existing release, including a partial fixture.
        if (families != 1 || templates != 1 || links != 1 || fields != 4 || entries != 3)
            throw Conflict();
        var actual = await new SqlProductFamilyDefinitionSource(context).GetAsync(cancellationToken);
        var template = actual.Templates.Single();
        var wanted = expected.Templates.Single();
        if (!actual.Families.SequenceEqual(expected.Families)
            || template.Id != wanted.Id || template.Version != wanted.Version || template.Code != wanted.Code
            || template.Name != wanted.Name || template.SourceRef != wanted.SourceRef
            || !template.FamilyIds.SetEquals(wanted.FamilyIds) || !template.Fields.SequenceEqual(wanted.Fields)
            || !actual.Entries.OrderBy(row => row.Dictionary, StringComparer.Ordinal).ThenBy(row => row.Code, StringComparer.Ordinal)
                .SequenceEqual(expected.Entries.OrderBy(row => row.Dictionary, StringComparer.Ordinal).ThenBy(row => row.Code, StringComparer.Ordinal)))
            throw Conflict();
    }

    private static InvalidOperationException Conflict() => new("The Product registry is not the identical synthetic u02 fixture.");
}
