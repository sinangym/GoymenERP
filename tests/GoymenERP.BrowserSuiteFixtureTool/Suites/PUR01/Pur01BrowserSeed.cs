using System.Text.Json;
using GoymenERP.Api.Shell;
using GoymenERP.BrowserSuiteFixtureTool.Shared;
using GoymenERP.Modules.Product.Infrastructure.Fixtures;
using GoymenERP.Modules.Purchasing.Infrastructure.Fixtures;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.BrowserSuiteFixtureTool.Suites.PUR01;

/// <summary>
/// pur01 writes one synthetic Draft order as the approval scenario precondition and opens one separate
/// active Supplier account through the real Cari API for the P-04 create path. Approval, create,
/// close/reopen, replay and every permission/company negative run through the real host and browser.
/// The goods-receipt journey writes nothing here: its approved order is created and approved by the spec
/// through the real commands. The seed only proves the capability split of the disposable receiver (the
/// ONLY actor holding the receipt name) and the Inventory stock reader before the browser starts.
/// </summary>
public sealed class Pur01BrowserSeed : IBrowserSuiteFixture
{
    public const string CreatorActor = "pur01-creator";
    public const string ApproverActor = "pur01-approver";
    public const string ReaderActor = "pur01-reader";
    public const string ReceiverActor = "pur01-receiver";
    public const string StockReaderActor = "pur01-stock-reader";
    public const string CariActor = "group-bootstrap-admin";

    public static readonly Guid CompanyGdc = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid CompanyOther = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid OrderId = Guid.Parse("ea000000-0000-4000-8000-000000000301");
    public static readonly Guid LineId = Guid.Parse("ea000000-0000-4000-8000-000000000302");

    private static readonly Guid SeededSupplierPartyId = Guid.Parse("ea000000-0000-4000-8000-000000000303");
    private static readonly Guid SeededCompanyAccountId = Guid.Parse("ea000000-0000-4000-8000-000000000304");
    private static readonly Guid ProductId = Guid.Parse("ea000000-0000-4000-8000-000000000305");
    private static readonly Guid UnitId = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly DateOnly EffectiveOn = new(2026, 9, 23);
    private static readonly DateTimeOffset At = new(2026, 9, 23, 9, 0, 0, TimeSpan.Zero);

    private const string OrdersPath = "/api/v1/purchasing/orders";
    private const string ProductCandidatesPath = "/api/v1/purchasing/references/products";
    private const string CapabilitiesPath = "/api/v1/shell/capabilities";
    private const string ListCapability = "purchasing.orders.list";
    private const string DetailCapability = "purchasing.orders.detail";
    private const string ApproveCapability = "purchasing.orders.approve";
    private const string CreateCapability = "purchasing.orders.create";
    private const string GoodsReceiptCapability = "purchasing.goods-receipts.record";
    private const string StockLabelCapability = "inventory.stock-positions.list";
    private const int SupplierRole = 2;

    public string Suite => "pur01";

    public async Task<IReadOnlyDictionary<string, string>> SeedAsync(
        BrowserSuiteFixtureContext context,
        CancellationToken cancellationToken)
    {
        var supplier = await CariBrowserSeed.OpenPartyWithAccountAsync(
            context,
            CariActor,
            CompanyGdc,
            "supplier",
            "P-04 tedarikçisi",
            SupplierRole,
            cancellationToken);

        await using (var registryScope = context.Services.CreateAsyncScope())
        {
            await registryScope.ServiceProvider.GetRequiredService<C01E2eProductTechnicalCardFixtureWriter>()
                .EnsureAsync(cancellationToken);
        }

        ProductPurchaseCandidateFixtureOutput productCandidate;
        await using (var productScope = context.Services.CreateAsyncScope())
        {
            productCandidate = await productScope.ServiceProvider
                .GetRequiredService<C01E2eProductPurchaseCandidateFixtureWriter>()
                .EnsureAsync(cancellationToken);
        }

        await using (AsyncServiceScope scope = context.Services.CreateAsyncScope())
        {
            PurchaseOrderFixtureOutcome outcome = await scope.ServiceProvider
                .GetRequiredService<C01E2ePurchaseOrderFixtureWriter>()
                .EnsureAsync(
                    new PurchaseOrderFixtureRequest(
                        OrderId,
                        CompanyGdc,
                        SeededSupplierPartyId,
                        SeededCompanyAccountId,
                        EffectiveOn,
                        new PurchaseOrderFixtureLine(LineId, ProductId, "1", UnitId, IsKilogram: true, Quantity: 5m),
                        context.Label(string.Empty).TrimEnd(),
                        context.Label("seed"),
                        context.IdempotencyKey("order"),
                        At),
                    cancellationToken);
            if (outcome.OrderId != OrderId)
            {
                throw new InvalidOperationException("The Purchasing fixture writer answered another order id.");
            }
        }

        JsonElement detail = await context.GetJsonAsync(ApproverActor, CompanyGdc, $"{OrdersPath}/{OrderId:D}", cancellationToken);
        JsonElement order = detail.GetProperty("order");
        if (order.GetProperty("id").GetGuid() != OrderId
            || order.GetProperty("status").GetString() is not ("Draft" or "Approved")
            || detail.GetProperty("lines").GetArrayLength() != 1
            || detail.GetProperty("lines")[0].GetProperty("lineId").GetGuid() != LineId)
        {
            throw new InvalidOperationException("The real Purchasing detail does not answer the seeded one-line order.");
        }

        JsonElement list = await context.GetJsonAsync(ReaderActor, CompanyGdc, OrdersPath, cancellationToken);
        if (!list.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("id").GetGuid() == OrderId))
        {
            throw new InvalidOperationException("The reader does not see the seeded order in the real list.");
        }

        JsonElement suppliers = await context.GetJsonAsync(
            CreatorActor,
            CompanyGdc,
            $"{CariBrowserSeed.CounterpartiesPath}?role=Supplier&partyState=active&accountState=active&pageSize=200",
            cancellationToken);
        JsonElement[] parties = suppliers.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("id").GetGuid() == supplier.PartyId)
            .ToArray();
        if (parties.Length != 1 || !parties[0].GetProperty("isActive").GetBoolean())
        {
            throw new InvalidOperationException("The creator cannot resolve the real active Supplier party.");
        }

        JsonElement[] accounts = parties[0].GetProperty("companyAccounts").EnumerateArray()
            .Where(account => account.GetProperty("id").GetGuid() == supplier.AccountId
                && account.GetProperty("companyId").GetGuid() == CompanyGdc)
            .ToArray();
        if (accounts.Length != 1 || !accounts[0].GetProperty("isActive").GetBoolean()
            || (accounts[0].GetProperty("roles").GetInt32() & SupplierRole) == 0)
        {
            throw new InvalidOperationException("The creator cannot resolve the real active Supplier company account.");
        }

        JsonElement candidates = await context.GetJsonAsync(
            CreatorActor,
            CompanyGdc,
            $"{ProductCandidatesPath}?q={Uri.EscapeDataString(productCandidate.Code)}"
            + $"&effectiveOn={ProductPurchaseCandidateFixture.EffectiveOn:yyyy-MM-dd}&limit=25",
            cancellationToken);
        JsonElement[] products = candidates.GetProperty("items").EnumerateArray().ToArray();
        if (products.Length != 1
            || products[0].GetProperty("productId").GetGuid() != productCandidate.ProductId
            || products[0].GetProperty("productVersion").GetString() != productCandidate.ProductVersion
            || products[0].GetProperty("unitId").GetGuid() != productCandidate.UnitId
            || products[0].GetProperty("profileId").GetGuid() != productCandidate.ProfileId
            || products[0].GetProperty("profileVersion").GetString() != productCandidate.ProfileVersion
            || products[0].GetProperty("code").GetString() != productCandidate.Code
            || products[0].GetProperty("name").GetString() != productCandidate.Name)
        {
            throw new InvalidOperationException("The real Purchasing reference read does not answer the exact Product owner tuple.");
        }

        string[] creator = await CapabilitiesAsync(context, CreatorActor, cancellationToken);
        if (!creator.Contains(ListCapability, StringComparer.Ordinal)
            || !creator.Contains(DetailCapability, StringComparer.Ordinal)
            || !creator.Contains(CreateCapability, StringComparer.Ordinal)
            || creator.Contains(ApproveCapability, StringComparer.Ordinal)
            || creator.Contains(GoodsReceiptCapability, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The creator needs list/detail/create and must not receive approve or goods receipt.");
        }

        string[] approver = await CapabilitiesAsync(context, ApproverActor, cancellationToken);
        if (!approver.Contains(ListCapability, StringComparer.Ordinal)
            || !approver.Contains(DetailCapability, StringComparer.Ordinal)
            || !approver.Contains(ApproveCapability, StringComparer.Ordinal)
            || approver.Contains(CreateCapability, StringComparer.Ordinal)
            || approver.Contains(GoodsReceiptCapability, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The approver needs list/detail/approve and must not receive the staged create screen or goods receipt.");
        }

        string[] reader = await CapabilitiesAsync(context, ReaderActor, cancellationToken);
        if (!reader.Contains(ListCapability, StringComparer.Ordinal)
            || !reader.Contains(DetailCapability, StringComparer.Ordinal)
            || reader.Contains(ApproveCapability, StringComparer.Ordinal)
            || reader.Contains(CreateCapability, StringComparer.Ordinal)
            || reader.Contains(GoodsReceiptCapability, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The reader needs list/detail only and must not receive approve/create/goods receipt.");
        }

        // The receipt command needs purchasing.goods-receipts.record; its detail screen and receipt history
        // need purchasing.orders.read. Nothing else: no approve, no create, no Inventory screen.
        string[] receiver = await CapabilitiesAsync(context, ReceiverActor, cancellationToken);
        if (!receiver.Contains(ListCapability, StringComparer.Ordinal)
            || !receiver.Contains(DetailCapability, StringComparer.Ordinal)
            || !receiver.Contains(GoodsReceiptCapability, StringComparer.Ordinal)
            || receiver.Contains(ApproveCapability, StringComparer.Ordinal)
            || receiver.Contains(CreateCapability, StringComparer.Ordinal)
            || receiver.Contains(StockLabelCapability, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The receiver needs list/detail/goods receipt only and must not receive approve, create or the Inventory label screen.");
        }

        // The Inventory label detail (inventory.labels.read + inventory.balances.read) with no Purchasing name at all.
        string[] stockReader = await CapabilitiesAsync(context, StockReaderActor, cancellationToken);
        if (!stockReader.Contains(StockLabelCapability, StringComparer.Ordinal)
            || stockReader.Any(capability => capability.StartsWith("purchasing.", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("The stock reader needs the Inventory label screen only and no Purchasing capability.");
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["creatorActor"] = CreatorActor,
            ["approverActor"] = ApproverActor,
            ["readerActor"] = ReaderActor,
            ["receiverActor"] = ReceiverActor,
            ["stockReaderActor"] = StockReaderActor,
            ["companyId"] = CompanyGdc.ToString("D"),
            ["otherCompanyId"] = CompanyOther.ToString("D"),
            ["orderId"] = OrderId.ToString("D"),
            ["lineId"] = LineId.ToString("D"),
            ["supplierPartyId"] = supplier.PartyId.ToString("D"),
            ["companyAccountId"] = supplier.AccountId.ToString("D"),
            ["productId"] = productCandidate.ProductId.ToString("D"),
            ["productVersion"] = productCandidate.ProductVersion,
            ["productUnitId"] = productCandidate.UnitId.ToString("D"),
            ["productUnitProfileId"] = productCandidate.ProfileId.ToString("D"),
            ["productUnitProfileVersion"] = productCandidate.ProfileVersion,
            ["productCode"] = productCandidate.Code,
            ["productName"] = productCandidate.Name,
        };
    }

    private static async Task<string[]> CapabilitiesAsync(
        BrowserSuiteFixtureContext context,
        string actor,
        CancellationToken cancellationToken)
    {
        JsonElement snapshot = await context.GetJsonAsync(actor, CompanyGdc, CapabilitiesPath, cancellationToken);
        return [.. snapshot.GetProperty("capabilities").EnumerateArray().Select(capability => capability.GetString()!)];
    }
}
