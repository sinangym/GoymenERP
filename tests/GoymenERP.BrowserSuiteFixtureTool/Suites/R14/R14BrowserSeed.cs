using System.Text.Json;
using GoymenERP.Api.Shell;
using GoymenERP.Modules.Product.Infrastructure.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.BrowserSuiteFixtureTool.Suites.R14;

/// <summary>
/// R14 materializes Product's fixed stock candidate through its gated owner port, then resolves one
/// active non-external GDC location through Organization's real public read. The opening count itself
/// is never seeded: the browser records it through the real Inventory command.
/// </summary>
public sealed class R14BrowserSeed : IBrowserSuiteFixture
{
    public const string RecorderActor = "r14-recorder";
    public const string DeniedActor = "r14-denied";
    public const string ArrangerActor = "r14-arranger";

    public static readonly Guid CompanyGdc = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid CompanyOther = Guid.Parse("10000000-0000-0000-0000-000000000002");

    private const string LocationsPath = "/api/v1/organization/locations?page=1&pageSize=100";
    private const string ProductCandidatesPath = "/api/v1/inventory/references/products";
    private const string LocationCandidatesPath = "/api/v1/inventory/references/locations";
    private const string CapabilitiesPath = "/api/v1/shell/capabilities";
    private const string OpeningCapability = "inventory.opening-stock.record";
    private const string LabelDetailCapability = "inventory.stock-positions.list";
    private const string ProductDetailCapability = "product.items.detail";

    public string Suite => "r14";

    public async Task<IReadOnlyDictionary<string, string>> SeedAsync(
        BrowserSuiteFixtureContext context,
        CancellationToken cancellationToken)
    {
        await using (var registryScope = context.Services.CreateAsyncScope())
        {
            await registryScope.ServiceProvider.GetRequiredService<C01E2eProductTechnicalCardFixtureWriter>()
                .EnsureAsync(cancellationToken);
        }

        ProductStockCandidateFixtureOutput product;
        await using (var productScope = context.Services.CreateAsyncScope())
        {
            product = await productScope.ServiceProvider
                .GetRequiredService<C01E2eProductStockCandidateFixtureWriter>()
                .EnsureAsync(cancellationToken);
        }

        JsonElement locationList = await context.GetJsonAsync(
            ArrangerActor, CompanyGdc, LocationsPath, cancellationToken);
        JsonElement[] activeLocations = locationList.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("isActive").GetBoolean()
                           && !item.GetProperty("isExternal").GetBoolean())
            .OrderBy(item => item.GetProperty("code").GetString(), StringComparer.Ordinal)
            .ToArray();
        if (activeLocations.Length == 0)
        {
            throw new InvalidOperationException("The seeded GDC company has no active non-external location for R14.");
        }

        JsonElement location = activeLocations[0];
        Guid locationId = location.GetProperty("id").GetGuid();
        string locationCode = location.GetProperty("code").GetString()!;

        JsonElement productCandidates = await context.GetJsonAsync(
            RecorderActor,
            CompanyGdc,
            $"{ProductCandidatesPath}?q={Uri.EscapeDataString(product.Code)}"
            + $"&effectiveOn={ProductStockCandidateFixture.EffectiveOn:yyyy-MM-dd}&limit=25",
            cancellationToken);
        JsonElement[] products = productCandidates.GetProperty("items").EnumerateArray().ToArray();
        if (products.Length != 1
            || products[0].GetProperty("productId").GetGuid() != product.ProductId
            || products[0].GetProperty("productVersion").GetString() != product.ProductVersion
            || products[0].GetProperty("stockUnitId").GetGuid() != product.UnitId
            || products[0].GetProperty("profileId").GetGuid() != product.ProfileId
            || products[0].GetProperty("profileVersion").GetString() != product.ProfileVersion
            || products[0].GetProperty("code").GetString() != product.Code
            || products[0].GetProperty("name").GetString() != product.Name)
        {
            throw new InvalidOperationException("The real Inventory Product reference does not answer the exact owner tuple.");
        }

        JsonElement locationCandidates = await context.GetJsonAsync(
            RecorderActor,
            CompanyGdc,
            $"{LocationCandidatesPath}?q={Uri.EscapeDataString(locationCode)}&limit=25",
            cancellationToken);
        JsonElement[] locations = locationCandidates.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("locationId").GetGuid() == locationId)
            .ToArray();
        if (locations.Length != 1)
        {
            throw new InvalidOperationException("The real Inventory location reference does not answer the active GDC location.");
        }

        string[] recorderCapabilities = await CapabilitiesAsync(context, RecorderActor, cancellationToken);
        string[] deniedCapabilities = await CapabilitiesAsync(context, DeniedActor, cancellationToken);
        string[] requiredCapabilities = [OpeningCapability, LabelDetailCapability, ProductDetailCapability];
        if (requiredCapabilities.Any(capability =>
                !recorderCapabilities.Contains(capability, StringComparer.Ordinal)
                || deniedCapabilities.Contains(capability, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException("The R14 capability split does not match recorder and denied actors.");
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["recorderActor"] = RecorderActor,
            ["deniedActor"] = DeniedActor,
            ["arrangerActor"] = ArrangerActor,
            ["companyId"] = CompanyGdc.ToString("D"),
            ["otherCompanyId"] = CompanyOther.ToString("D"),
            ["locationId"] = locationId.ToString("D"),
            ["productId"] = product.ProductId.ToString("D"),
            ["productVersion"] = product.ProductVersion,
            ["productUnitId"] = product.UnitId.ToString("D"),
            ["productUnitProfileId"] = product.ProfileId.ToString("D"),
            ["productUnitProfileVersion"] = product.ProfileVersion,
            ["productCode"] = product.Code,
            ["productName"] = product.Name,
        };
    }

    private static async Task<string[]> CapabilitiesAsync(
        BrowserSuiteFixtureContext context,
        string actor,
        CancellationToken cancellationToken)
    {
        JsonElement snapshot = await context.GetJsonAsync(actor, CompanyGdc, CapabilitiesPath, cancellationToken);
        return [.. snapshot.GetProperty("capabilities").EnumerateArray().Select(item => item.GetString()!)];
    }
}
