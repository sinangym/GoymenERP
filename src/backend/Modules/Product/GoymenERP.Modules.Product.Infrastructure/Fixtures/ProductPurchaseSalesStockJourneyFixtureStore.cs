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
/// Test-only materialization of one Product tuple shared by the purchase, stock and sales readers.
/// Registered exclusively by the existing complete disposable fixture gate.
/// </summary>
public interface IProductPurchaseSalesStockJourneyFixtureStore
{
    Task<ProductPurchaseSalesStockJourneyFixtureOutput> EnsureAsync(CancellationToken cancellationToken);
}

public sealed record ProductPurchaseSalesStockJourneyFixtureOutput(
    Guid CompanyId,
    Guid ProductId,
    string ProductVersion,
    Guid UnitId,
    Guid ProfileId,
    string ProfileVersion,
    DateOnly EffectiveOn,
    string Code,
    string Name);

public static class ProductPurchaseSalesStockJourneyFixtureStoreRegistration
{
    public static IServiceCollection AddProductPurchaseSalesStockJourneyFixtureStore(
        this IServiceCollection services,
        Func<IServiceProvider, ProductUnitProfileAdapter>? fixtureUnitProfiles = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IProductPurchaseSalesStockJourneyFixtureStore>(provider =>
            new ProductPurchaseSalesStockJourneyFixtureStore(
                provider.GetRequiredService<ProductDbContext>(),
                fixtureUnitProfiles?.Invoke(provider) ?? provider.GetRequiredService<ProductUnitProfileAdapter>()));
        return services;
    }
}

/// <summary>
/// Fixed synthetic purchase-to-delivery Product card. It is not a real master release, activation
/// command, grant or business event, and it never upgrades one of the existing single-purpose cards.
/// </summary>
public static class ProductPurchaseSalesStockJourneyFixture
{
    public static readonly Guid ProductId = Guid.Parse("b6900000-0000-4000-8000-000000000401");
    public static readonly Guid GroupId = Guid.Parse("b6a00000-0000-4000-8000-000000000401");
    public static readonly DateOnly EffectiveOn = new(2026, 9, 18);
    public const string Prefix = "SYNPL04";
    public const string Name = "SENTETIK browser-suite:purchase-to-delivery candidate";

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
            ProductTechnicalCardFixture.FamilyId,
            ProductRoles.NotEvaluated with
            {
                Purchasable = ProductRoleState.Manual(true),
                Sellable = ProductRoleState.Manual(true),
            },
            now);
        product.Activate(product.Version, now);
        var unit = ApprovedUnitProfileCatalog.GdcKilogramV1.UnitId;
        return ProductStoredRecord.Capture(product, new ProductReferences(
            ProductTechnicalCardFixture.TemplateId,
            "1",
            new Dictionary<string, JsonElement>
            {
                ["syn-thickness-mm"] = JsonSerializer.SerializeToElement(1.000m),
                ["syn-width-mm"] = JsonSerializer.SerializeToElement(1000.000m),
                ["syn-grade"] = JsonSerializer.SerializeToElement("SYN-A"),
                ["syn-coating"] = JsonSerializer.SerializeToElement("syn-none"),
            },
            new ProductUnitReferences(Stock: unit, Sales: unit, Purchase: unit)));
    }
}

internal sealed class ProductPurchaseSalesStockJourneyFixtureStore(
    ProductDbContext context,
    ProductUnitProfileAdapter unitProfiles) : IProductPurchaseSalesStockJourneyFixtureStore
{
    public async Task<ProductPurchaseSalesStockJourneyFixtureOutput> EnsureAsync(
        CancellationToken cancellationToken)
    {
        // Host must ensure the existing fixed technical registry in a separate scope first.
        if (context.ChangeTracker.Entries().Any()) throw Conflict();
        if (!await context.Families.AnyAsync(cancellationToken)) throw Conflict();
        await new ProductTechnicalCardFixtureStore(context).EnsureAsync(cancellationToken);

        var expected = ProductPurchaseSalesStockJourneyFixture.Record();
        var card = new ProductCardView(
            ProductDraftView.FromProduct(expected.RestoreProduct()),
            expected.RestoreReferences());
        var purchaseErrors = await new ProductPurchaseFamilyRequirementPolicyV1(
                new SqlProductFamilyDefinitionSource(context))
            .ValidateAsync(card, cancellationToken);
        var salesErrors = await new ProductFamilyRequirementPolicyV2(
                new SqlProductFamilyDefinitionSource(context))
            .ValidateAsync(card, cancellationToken);
        if (purchaseErrors.Count != 0 || salesErrors.Count != 0) throw Conflict();

        // Read, never seed or invent, the approved Settings tuple under the server company.
        var approved = ApprovedUnitProfileCatalog.GdcKilogramV1;
        var read = await unitProfiles.ReadAsync(
            approved.UnitId,
            ProductPurchaseSalesStockJourneyFixture.EffectiveOn,
            requiresKilogram: true,
            cancellationToken);
        var profile = read.Profile;
        if (!read.IsUsable || profile.CompanyId != approved.CompanyId
            || profile.Profile.ProfileId != approved.ProfileId || profile.Profile.Version != approved.Version
            || profile.UnitCode != approved.UnitCode || profile.DimensionCode != approved.DimensionCode
            || profile.Scale != approved.Scale || profile.EffectiveFrom != approved.EffectiveFrom
            || profile.EffectiveThrough != approved.EffectiveThrough) throw Conflict();

        var products = await context.Products.AsNoTracking().ToListAsync(cancellationToken);
        var groups = await context.CodeGroups.AsNoTracking().ToListAsync(cancellationToken);
        var registrations = await context.CodeRegistrations.AsNoTracking().ToListAsync(cancellationToken);
        var counters = await context.CodeCounters.AsNoTracking().ToListAsync(cancellationToken);
        if (products.Count + groups.Count + registrations.Count + counters.Count == 0)
        {
            await context.AddPurchaseSalesStockJourneyFixtureAsync(cancellationToken);
        }
        else if (products.Count != 1 || products[0].ToRecord() != expected
            || groups.Count != 1
            || groups[0].ToContract() != ProductPurchaseSalesStockJourneyFixture.Group().ToContract()
            || registrations.Count != 1 || registrations[0].ProductId != expected.Id
            || registrations[0].Code != expected.Code || !registrations[0].IsCurrent
            || counters.Count != 1 || counters[0].Prefix != ProductPurchaseSalesStockJourneyFixture.Prefix
            || counters[0].LastSequence != 1)
        {
            // No repair/overwrite of partial, changed, or single-purpose fixture data.
            throw Conflict();
        }

        var persisted = (await context.Products.AsNoTracking().SingleAsync(cancellationToken)).ToRecord();
        if (persisted != expected) throw Conflict();
        return new(
            profile.CompanyId,
            persisted.Id,
            persisted.Version.ToString(CultureInfo.InvariantCulture),
            profile.UnitId,
            profile.Profile.ProfileId,
            profile.Profile.Version.ToString(CultureInfo.InvariantCulture),
            ProductPurchaseSalesStockJourneyFixture.EffectiveOn,
            persisted.Code,
            persisted.Name);
    }

    private static InvalidOperationException Conflict() =>
        new("The Product store/profile is not the exact isolated synthetic purchase-to-delivery fixture prerequisite.");
}
