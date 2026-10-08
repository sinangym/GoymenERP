using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Product.Infrastructure.Fixtures;

/// <summary>
/// Test-only materialization of one sales candidate whose stock display is backed by the released
/// Product technical definition. Registered exclusively by the complete disposable fixture gate.
/// </summary>
public interface IProductStockDisplayQualifiedFixtureStore
{
    Task<ProductStockDisplayQualifiedFixtureOutput> EnsureAsync(CancellationToken cancellationToken);
}

public sealed record ProductStockDisplayQualifiedFixtureOutput(
    Guid ProductId,
    string ProductVersion,
    Guid UnitId,
    Guid ProfileId,
    string ProfileVersion,
    string Code,
    string Name);

public static class ProductStockDisplayQualifiedFixtureStoreRegistration
{
    public static IServiceCollection AddProductStockDisplayQualifiedFixtureStore(
        this IServiceCollection services,
        Func<IServiceProvider, ProductUnitProfileAdapter>? fixtureUnitProfiles = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IProductStockDisplayQualifiedFixtureStore>(provider =>
            new ProductStockDisplayQualifiedFixtureStore(
                provider.GetRequiredService<ProductDbContext>(),
                fixtureUnitProfiles?.Invoke(provider) ?? provider.GetRequiredService<ProductUnitProfileAdapter>()));
        return services;
    }
}

/// <summary>
/// Fixed disposable s04 Product card. The Product row is synthetic test data, but every technical
/// reference resolves to the reviewed non-synthetic Product release. This does not release master
/// data, assign a production default, grant authority or create a business event.
/// </summary>
public static class ProductStockDisplayQualifiedFixture
{
    public static readonly Guid ProductId = Guid.Parse("b6b00000-0000-4000-8000-000000000401");
    public static readonly Guid GroupId = Guid.Parse("b6c00000-0000-4000-8000-000000000401");
    public static readonly DateOnly EffectiveOn = new(2026, 9, 18);
    public const string Prefix = "SYNS04G";
    public const string Name = "SENTETIK browser-suite:s04 stock-display-qualified sales candidate";

    internal static ProductCodeGroupRow Group() => ProductCodeGroupRow.Define(GroupId, Name, Prefix);

    internal static ProductStoredRecord Record()
    {
        var now = new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.Zero);
        var product = Domain.Products.Product.CreateDraft(
            ProductId,
            ProductCode.FromSequence(Prefix, 1),
            Name,
            GroupId,
            null,
            ProductInitialTechnicalDefinitionV1.HotRolledSlitCoilFamilyId,
            ProductRoles.NotEvaluated with { Sellable = ProductRoleState.Manual(true) },
            now);
        product.Activate(product.Version, now);
        var unit = ApprovedUnitProfileCatalog.GdcKilogramV1.UnitId;
        return ProductStoredRecord.Capture(product, new ProductReferences(
            ProductInitialTechnicalDefinitionV1.TemplateId,
            "1",
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["thickness-mm"] = JsonSerializer.SerializeToElement(1.000m),
                ["width-mm"] = JsonSerializer.SerializeToElement(1000.000m),
                ["steel-grade"] = JsonSerializer.SerializeToElement("S235JR"),
                ["coating"] = JsonSerializer.SerializeToElement("uncoated"),
            },
            new ProductUnitReferences(Stock: unit, Sales: unit)));
    }
}

internal sealed class ProductStockDisplayQualifiedFixtureStore(
    ProductDbContext context,
    ProductUnitProfileAdapter unitProfiles) : IProductStockDisplayQualifiedFixtureStore
{
    public async Task<ProductStockDisplayQualifiedFixtureOutput> EnsureAsync(
        CancellationToken cancellationToken)
    {
        // The host must materialize the released definition and existing synthetic technical fixture
        // in a prior clean scope. This store only validates that exact state; it never creates or
        // repairs registry rows.
        if (context.ChangeTracker.Entries().Any()) throw Conflict();
        if (!await context.Families.AnyAsync(cancellationToken)) throw Conflict();

        var expected = ProductStockDisplayQualifiedFixture.Record();
        var card = new ProductCardView(
            ProductDraftView.FromProduct(expected.RestoreProduct()),
            expected.RestoreReferences());
        var definition = await new SqlProductFamilyDefinitionSource(context).GetAsync(cancellationToken);
        ProductFamilyDefinitionRulesV2.RequireWellFormed(definition);
        if (!MatchesPreparedRegistry(definition)) throw Conflict();
        var family = definition.Families.SingleOrDefault(candidate =>
            candidate.Id == ProductInitialTechnicalDefinitionV1.HotRolledSlitCoilFamilyId);
        var template = family is null ? null : ProductFamilyDefinitionRulesV2.TemplateOf(definition, family.Id);
        if (family is null || template is null
            || !string.Equals(family.SourceRef, ProductInitialTechnicalDefinitionV1.SourceRef,
                StringComparison.Ordinal)
            || template.Id != ProductInitialTechnicalDefinitionV1.TemplateId
            || !string.Equals(template.Version, "1", StringComparison.Ordinal)
            || !string.Equals(template.SourceRef, ProductInitialTechnicalDefinitionV1.SourceRef,
                StringComparison.Ordinal)
            || ProductManufacturingTechnicalSnapshotService.IsSynthetic(family.SourceRef)
            || ProductManufacturingTechnicalSnapshotService.IsSynthetic(template.SourceRef))
        {
            throw Conflict();
        }

        var errors = await new ProductFamilyRequirementPolicyV2(
                new SqlProductFamilyDefinitionSource(context))
            .ValidateAsync(card, cancellationToken);
        if (errors.Count != 0
            || ProductManufacturingTechnicalSnapshotService
                .ValidateTechnicalReferences(card.References, definition, template).Count != 0
            || ProductManufacturingTechnicalSnapshotService
                .UsesSyntheticDictionaryValue(card.References, definition, template))
        {
            throw Conflict();
        }

        // Read, never seed or invent, the approved Settings tuple under the server company.
        var approved = ApprovedUnitProfileCatalog.GdcKilogramV1;
        var read = await unitProfiles.ReadAsync(
            approved.UnitId,
            ProductStockDisplayQualifiedFixture.EffectiveOn,
            requiresKilogram: true,
            cancellationToken);
        var profile = read.Profile;
        if (!read.IsUsable || profile.CompanyId != approved.CompanyId
            || profile.Profile.ProfileId != approved.ProfileId || profile.Profile.Version != approved.Version
            || profile.UnitCode != approved.UnitCode || profile.DimensionCode != approved.DimensionCode
            || profile.Scale != approved.Scale || profile.EffectiveFrom != approved.EffectiveFrom
            || profile.EffectiveThrough != approved.EffectiveThrough)
        {
            throw Conflict();
        }

        var products = await context.Products.AsNoTracking().ToListAsync(cancellationToken);
        var groups = await context.CodeGroups.AsNoTracking().ToListAsync(cancellationToken);
        var registrations = await context.CodeRegistrations.AsNoTracking().ToListAsync(cancellationToken);
        var counters = await context.CodeCounters.AsNoTracking().ToListAsync(cancellationToken);
        if (products.Count + registrations.Count + counters.Count == 0
            && ProductCandidateFixtureCodeGroupState.IsEmptyOrExactReleasedBaseline(groups))
        {
            await context.AddStockDisplayQualifiedFixtureAsync(cancellationToken);
        }
        else if (products.Count != 1 || products[0].ToRecord() != expected
            || !ProductCandidateFixtureCodeGroupState.MatchesFixtureWithOptionalReleasedBaseline(
                groups, ProductStockDisplayQualifiedFixture.Group())
            || registrations.Count != 1 || registrations[0].ProductId != expected.Id
            || registrations[0].Code != expected.Code || !registrations[0].IsCurrent
            || counters.Count != 1 || counters[0].Prefix != ProductStockDisplayQualifiedFixture.Prefix
            || counters[0].LastSequence != 1)
        {
            throw Conflict();
        }

        var persisted = (await context.Products.AsNoTracking().SingleAsync(cancellationToken)).ToRecord();
        if (persisted != expected) throw Conflict();
        return new(
            persisted.Id,
            persisted.Version.ToString(CultureInfo.InvariantCulture),
            profile.UnitId,
            profile.Profile.ProfileId,
            profile.Profile.Version.ToString(CultureInfo.InvariantCulture),
            persisted.Code,
            persisted.Name);
    }

    private static bool MatchesPreparedRegistry(ProductFamilyDefinitionV2 actual)
    {
        var genuine = ProductInitialTechnicalDefinitionV1.Definition();
        var synthetic = ProductTechnicalCardFixture.Definition();
        var expected = new ProductFamilyDefinitionV2(
            [.. genuine.Families, .. synthetic.Families],
            [.. genuine.Templates, .. synthetic.Templates],
            [.. genuine.Entries, .. synthetic.Entries]);
        if (!actual.Families.OrderBy(row => row.Id)
                .SequenceEqual(expected.Families.OrderBy(row => row.Id))
            || !actual.Entries.OrderBy(row => row.Dictionary, StringComparer.Ordinal)
                .ThenBy(row => row.Code, StringComparer.Ordinal)
                .SequenceEqual(expected.Entries.OrderBy(row => row.Dictionary, StringComparer.Ordinal)
                    .ThenBy(row => row.Code, StringComparer.Ordinal)))
        {
            return false;
        }

        var actualTemplates = actual.Templates.OrderBy(row => row.Id)
            .ThenBy(row => row.Version, StringComparer.Ordinal).ToArray();
        var expectedTemplates = expected.Templates.OrderBy(row => row.Id)
            .ThenBy(row => row.Version, StringComparer.Ordinal).ToArray();
        if (actualTemplates.Length != expectedTemplates.Length) return false;
        return actualTemplates.Zip(expectedTemplates).All(pair =>
            pair.First.Id == pair.Second.Id
            && pair.First.Version == pair.Second.Version
            && pair.First.Code == pair.Second.Code
            && pair.First.Name == pair.Second.Name
            && pair.First.SourceRef == pair.Second.SourceRef
            && pair.First.FamilyIds.SetEquals(pair.Second.FamilyIds)
            && pair.First.Fields.SequenceEqual(pair.Second.Fields));
    }

    private static InvalidOperationException Conflict() => new(
        "The Product store/registry/profile is not the exact isolated stock-display-qualified fixture prerequisite.");
}
