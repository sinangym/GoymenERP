using System.Text.Json;
using GoymenERP.Api.Shell;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Fixtures;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.BrowserSuiteFixtureTool.Suites.R14;

/// <summary>
/// R14 creates, replays, reopens and activates one genuine SAC Product through its public owner API, then resolves one
/// active non-external GDC location through Organization's real public read. The opening count itself
/// is never seeded: the browser records it through the real Inventory command.
/// <para>
/// R19 split/merge (user decision 2026-09-27): <c>inventory.label-transformation.record</c> is held ONLY by the
/// disposable <see cref="TransformerActor"/> of this per-run database (its role comes from
/// infra/browser-suites/r14.override.yml); no real user and no seeded role holds it. This seed writes nothing for
/// it: the R19 spec arranges its source label through the recorder's real opening command. The read-back pins the
/// capability split: only the transformer is offered the label-detail split/merge control (the recorder, the denied
/// actor and the arranger are not), and it is not offered the opening workspace.
/// </para>
/// <para>
/// AT-037 stock count (user decision 2026-09-27): <c>inventory.stock-count.record</c> is held ONLY by the disposable
/// <see cref="CounterActor"/> and <c>inventory.stock-count.approve</c> ONLY by the disposable
/// <see cref="CountApproverActor"/> — two different actors, so maker ≠ checker is a fact of the grants as well as
/// of the command. This seed writes nothing for them; the read-back pins that each is offered exactly its own
/// control beside the label detail and no other r14 actor is offered either.
/// </para>
/// <para>
/// ST-02 scrap write-off (the AT-037 pattern): <c>inventory.stock-scrap.record</c> is held ONLY by the disposable
/// <see cref="ScrapperActor"/> and <c>inventory.stock-scrap.approve</c> ONLY by the disposable
/// <see cref="ScrapApproverActor"/>; the read-back pins the same split, and that no other r14 actor (the stock
/// counters included) is offered either scrap control.
/// </para>
/// </summary>
public sealed class R14BrowserSeed : IBrowserSuiteFixture
{
    public const string RecorderActor = "r14-recorder";
    public const string DeniedActor = "r14-denied";
    public const string ArrangerActor = "r14-arranger";
    public const string TransformerActor = "r14-transformer";
    public const string CounterActor = "r14-counter";
    public const string CountApproverActor = "r14-count-approver";
    public const string ScrapperActor = "r14-scrapper";
    public const string ScrapApproverActor = "r14-scrap-approver";

    public static readonly Guid CompanyGdc = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid CompanyOther = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid RecorderUserId = Guid.Parse("b2000000-0000-4000-8000-000000000701");

    /// <summary>
    /// Product master assignments bind the canonical SYSTEM_ADMINISTRATOR role. The recorder's normal
    /// company role remains the source of its Inventory permissions and is not replaced by this fixture.
    /// </summary>
    public static readonly Guid ProductReadRoleId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    public static readonly Guid ProductIssuerUserId = Guid.Parse("b2000000-0000-4000-8000-000000000204");
    public static readonly Guid ProductIssuerExternalIdentityId = Guid.Parse("b2100000-0000-4000-8000-000000000204");

    private static readonly Guid ProductReadAssignmentId = Guid.Parse("b5000000-0000-4000-8000-000000000701");
    private static readonly Guid ProductReadDecisionId = Guid.Parse("b5100000-0000-4000-8000-000000000701");
    private static readonly Guid ProductCreateAssignmentId = Guid.Parse("b5000000-0000-4000-8000-000000000702");
    private static readonly Guid ProductCreateDecisionId = Guid.Parse("b5100000-0000-4000-8000-000000000702");
    private static readonly Guid ProductActivateAssignmentId = Guid.Parse("b5000000-0000-4000-8000-000000000703");
    private static readonly Guid ProductActivateDecisionId = Guid.Parse("b5100000-0000-4000-8000-000000000703");
    private static readonly DateTimeOffset ProductReadValidFromUtc =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly ProductEffectiveOn = new(2026, 9, 18);

    private const string LocationsPath = "/api/v1/organization/locations?page=1&pageSize=100";
    private const string ProductCandidatesPath = "/api/v1/inventory/references/products";
    private const string LocationCandidatesPath = "/api/v1/inventory/references/locations";
    private const string CapabilitiesPath = "/api/v1/shell/capabilities";
    private const string OpeningCapability = "inventory.opening-stock.record";
    private const string LabelDetailCapability = "inventory.stock-positions.list";
    private const string ProductDetailCapability = "product.items.detail";
    private const string LabelClassificationCapability = "inventory.label-classification.write";
    private const string LabelTransformationCapability = "inventory.label-transformation.record";
    private const string StockCountRecordCapability = "inventory.stock-count.record";
    private const string StockCountApproveCapability = "inventory.stock-count.approve";
    private const string StockScrapRecordCapability = "inventory.stock-scrap.record";
    private const string StockScrapApproveCapability = "inventory.stock-scrap.approve";

    public string Suite => "r14";

    public async Task<IReadOnlyDictionary<string, string>> SeedAsync(
        BrowserSuiteFixtureContext context,
        CancellationToken cancellationToken)
    {
        await using (var authorityScope = context.Services.CreateAsyncScope())
        {
            var assignments = authorityScope.ServiceProvider
                .GetRequiredService<C01E2eProductMasterAssignmentFixtureWriter>();
            var grants = new[]
            {
                (ProductMasterOperation.Read, ProductReadAssignmentId, ProductReadDecisionId),
                (ProductMasterOperation.DraftCreate, ProductCreateAssignmentId, ProductCreateDecisionId),
                (ProductMasterOperation.Activate, ProductActivateAssignmentId, ProductActivateDecisionId),
            };
            foreach (var (operation, assignmentId, decisionId) in grants)
            {
                await assignments.EnsureAsync(new ProductMasterAssignmentFixtureRow(
                    assignmentId,
                    RecorderUserId,
                    ProductReadRoleId,
                    operation,
                    ProductIssuerUserId,
                    ProductIssuerExternalIdentityId,
                    decisionId,
                    context.Label($"R14 genuine Product {operation}"),
                    ProductReadValidFromUtc), cancellationToken);
            }
        }

        GenuineProduct product = await EnsureGenuineProductAsync(context, cancellationToken);

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
            + $"&effectiveOn={ProductEffectiveOn:yyyy-MM-dd}&limit=25",
            cancellationToken);
        JsonElement[] products = productCandidates.GetProperty("items").EnumerateArray().ToArray();
        if (products.Length != 1
            || products[0].GetProperty("productId").GetGuid() != product.ProductId
            || products[0].GetProperty("productVersion").GetString() != product.ProductVersion
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

        string[] transformerCapabilities = await CapabilitiesAsync(context, TransformerActor, cancellationToken);
        string[] arrangerCapabilities = await CapabilitiesAsync(context, ArrangerActor, cancellationToken);
        if (!transformerCapabilities.Contains(LabelDetailCapability, StringComparer.Ordinal)
            || !transformerCapabilities.Contains(LabelTransformationCapability, StringComparer.Ordinal)
            || transformerCapabilities.Contains(OpeningCapability, StringComparer.Ordinal)
            || recorderCapabilities.Contains(LabelTransformationCapability, StringComparer.Ordinal)
            || deniedCapabilities.Contains(LabelTransformationCapability, StringComparer.Ordinal)
            || arrangerCapabilities.Contains(LabelTransformationCapability, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "Only the disposable R19 transformer may hold the label split/merge control, beside the label detail.");
        }

        string[] counterCapabilities = await CapabilitiesAsync(context, CounterActor, cancellationToken);
        string[] approverCapabilities = await CapabilitiesAsync(context, CountApproverActor, cancellationToken);
        string[][] othersCapabilities =
            [recorderCapabilities, deniedCapabilities, arrangerCapabilities, transformerCapabilities];
        if (!counterCapabilities.Contains(LabelDetailCapability, StringComparer.Ordinal)
            || !counterCapabilities.Contains(StockCountRecordCapability, StringComparer.Ordinal)
            || counterCapabilities.Contains(StockCountApproveCapability, StringComparer.Ordinal)
            || !approverCapabilities.Contains(LabelDetailCapability, StringComparer.Ordinal)
            || !approverCapabilities.Contains(StockCountApproveCapability, StringComparer.Ordinal)
            || approverCapabilities.Contains(StockCountRecordCapability, StringComparer.Ordinal)
            || othersCapabilities.Any(capabilities =>
                capabilities.Contains(StockCountRecordCapability, StringComparer.Ordinal)
                || capabilities.Contains(StockCountApproveCapability, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException(
                "Only the disposable AT-037 counter may record and only the disposable count approver may decide a count.");
        }

        string[] scrapperCapabilities = await CapabilitiesAsync(context, ScrapperActor, cancellationToken);
        string[] scrapApproverCapabilities = await CapabilitiesAsync(context, ScrapApproverActor, cancellationToken);
        string[][] nonClassificationCapabilities =
        [
            deniedCapabilities, arrangerCapabilities, transformerCapabilities, counterCapabilities,
            approverCapabilities, scrapperCapabilities, scrapApproverCapabilities,
        ];
        if (!recorderCapabilities.Contains(LabelClassificationCapability, StringComparer.Ordinal)
            || nonClassificationCapabilities.Any(capabilities =>
                capabilities.Contains(LabelClassificationCapability, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException(
                "Only the disposable R14 recorder may hold the label classification control.");
        }

        string[][] nonScrapCapabilities =
        [
            recorderCapabilities, deniedCapabilities, arrangerCapabilities, transformerCapabilities,
            counterCapabilities, approverCapabilities,
        ];
        if (!scrapperCapabilities.Contains(LabelDetailCapability, StringComparer.Ordinal)
            || !scrapperCapabilities.Contains(StockScrapRecordCapability, StringComparer.Ordinal)
            || scrapperCapabilities.Contains(StockScrapApproveCapability, StringComparer.Ordinal)
            || !scrapApproverCapabilities.Contains(LabelDetailCapability, StringComparer.Ordinal)
            || !scrapApproverCapabilities.Contains(StockScrapApproveCapability, StringComparer.Ordinal)
            || scrapApproverCapabilities.Contains(StockScrapRecordCapability, StringComparer.Ordinal)
            || nonScrapCapabilities.Any(capabilities =>
                capabilities.Contains(StockScrapRecordCapability, StringComparer.Ordinal)
                || capabilities.Contains(StockScrapApproveCapability, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException(
                "Only the disposable scrapper may record and only the disposable scrap approver may decide a scrap.");
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["recorderActor"] = RecorderActor,
            ["deniedActor"] = DeniedActor,
            ["arrangerActor"] = ArrangerActor,
            ["transformerActor"] = TransformerActor,
            ["counterActor"] = CounterActor,
            ["countApproverActor"] = CountApproverActor,
            ["scrapperActor"] = ScrapperActor,
            ["scrapApproverActor"] = ScrapApproverActor,
            ["companyId"] = CompanyGdc.ToString("D"),
            ["otherCompanyId"] = CompanyOther.ToString("D"),
            ["locationId"] = locationId.ToString("D"),
            ["productId"] = product.ProductId.ToString("D"),
            ["productVersion"] = product.ProductVersion,
            ["productUnitId"] = products[0].GetProperty("stockUnitId").GetGuid().ToString("D"),
            ["productUnitProfileId"] = products[0].GetProperty("profileId").GetGuid().ToString("D"),
            ["productUnitProfileVersion"] = products[0].GetProperty("profileVersion").GetString()!,
            ["productCode"] = product.Code,
            ["productName"] = product.Name,
        };
    }

    private static async Task<GenuineProduct> EnsureGenuineProductAsync(
        BrowserSuiteFixtureContext context,
        CancellationToken cancellationToken)
    {
        const string name = "SENTETIK browser-suite:r14 genuine S235JR 3 mm x 248 mm kaplamasiz sac";
        var draft = new CreateProductDraftRequest(
            name,
            ProductInitialCodeGroupV1.SacGroupId,
            FamilyId: ProductInitialTechnicalDefinitionV1.HotRolledSlitCoilFamilyId,
            References: new ProductReferences(
                ProductInitialTechnicalDefinitionV1.TemplateId,
                "1",
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["thickness-mm"] = JsonSerializer.SerializeToElement(3m),
                    ["width-mm"] = JsonSerializer.SerializeToElement(248m),
                    ["steel-grade"] = JsonSerializer.SerializeToElement("S235JR"),
                    ["coating"] = JsonSerializer.SerializeToElement("uncoated"),
                },
                new ProductUnitReferences(
                    Stock: ApprovedUnitProfileCatalog.GdcKilogramV1.UnitId,
                    Sales: ApprovedUnitProfileCatalog.GdcKilogramV1.UnitId)));

        var created = await context.PostJsonAsync(
            RecorderActor, CompanyGdc, ProductApiV1.BasePath, draft, "genuine-product-create", cancellationToken);
        if (created.GetProperty("isReplay").GetBoolean())
        {
            throw new InvalidOperationException("The first R14 genuine Product create unexpectedly replayed.");
        }

        var createdProduct = created.GetProperty("card").GetProperty("product");
        var productId = createdProduct.GetProperty("id").GetGuid();
        if (productId == Guid.Empty
            || createdProduct.GetProperty("code").GetString() != "SAC-1"
            || createdProduct.GetProperty("name").GetString() != name
            || createdProduct.GetProperty("lifecycle").GetString() != "Draft"
            || createdProduct.GetProperty("version").GetString() != "1")
        {
            throw new InvalidOperationException("The R14 genuine Product draft answer is not the exact SAC-1 card.");
        }

        var replay = await context.PostJsonAsync(
            RecorderActor, CompanyGdc, ProductApiV1.BasePath, draft, "genuine-product-create", cancellationToken);
        if (!replay.GetProperty("isReplay").GetBoolean()
            || replay.GetProperty("card").GetProperty("product").GetProperty("id").GetGuid() != productId)
        {
            throw new InvalidOperationException("The R14 genuine Product create did not replay its exact card.");
        }

        var reopenedDraft = await context.GetJsonAsync(
            RecorderActor, CompanyGdc, $"{ProductApiV1.BasePath}/{productId:D}", cancellationToken);
        if (reopenedDraft.GetProperty("product").GetProperty("version").GetString() != "1")
        {
            throw new InvalidOperationException("The R14 genuine Product draft did not reopen at version 1.");
        }

        var activated = await context.PostJsonAsync(
            RecorderActor,
            CompanyGdc,
            $"{ProductApiV1.BasePath}/{productId:D}/activation",
            new ActivateProductRequest("1"),
            "genuine-product-activate",
            cancellationToken);
        var activeProduct = activated.GetProperty("card").GetProperty("product");
        if (activeProduct.GetProperty("id").GetGuid() != productId
            || activeProduct.GetProperty("name").GetString() != name
            || activeProduct.GetProperty("lifecycle").GetString() != "Active"
            || activeProduct.GetProperty("version").GetString() != "2")
        {
            throw new InvalidOperationException("The R14 genuine Product did not activate at version 2.");
        }

        var reopenedActive = await context.GetJsonAsync(
            RecorderActor, CompanyGdc, $"{ProductApiV1.BasePath}/{productId:D}", cancellationToken);
        if (reopenedActive.GetProperty("product").GetProperty("lifecycle").GetString() != "Active"
            || reopenedActive.GetProperty("product").GetProperty("version").GetString() != "2"
            || reopenedActive.GetProperty("references").GetProperty("templateId").GetGuid()
                != ProductInitialTechnicalDefinitionV1.TemplateId)
        {
            throw new InvalidOperationException("The R14 genuine active Product did not reopen with its template.");
        }

        return new GenuineProduct(productId, "2", "SAC-1", name);
    }

    private sealed record GenuineProduct(Guid ProductId, string ProductVersion, string Code, string Name);

    private static async Task<string[]> CapabilitiesAsync(
        BrowserSuiteFixtureContext context,
        string actor,
        CancellationToken cancellationToken)
    {
        JsonElement snapshot = await context.GetJsonAsync(actor, CompanyGdc, CapabilitiesPath, cancellationToken);
        return [.. snapshot.GetProperty("capabilities").EnumerateArray().Select(item => item.GetString()!)];
    }
}
