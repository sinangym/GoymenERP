using System.Text.Json;
using GoymenERP.Api.Shell;
using GoymenERP.BrowserSuiteFixtureTool.Shared;
using GoymenERP.Modules.Purchasing.Infrastructure.Fixtures;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.BrowserSuiteFixtureTool.Suites.PUR01;

/// <summary>
/// pur01 writes one synthetic Draft order as the approval scenario precondition and opens one separate
/// active Supplier account through the real Cari API for the P-04 create path. Approval, create,
/// close/reopen, replay and every permission/company negative run through the real host and browser.
/// </summary>
public sealed class Pur01BrowserSeed : IBrowserSuiteFixture
{
    public const string CreatorActor = "pur01-creator";
    public const string ApproverActor = "pur01-approver";
    public const string ReaderActor = "pur01-reader";
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
            || creator.Contains(ApproveCapability, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The creator needs list/detail/create and must not receive approve.");
        }

        string[] approver = await CapabilitiesAsync(context, ApproverActor, cancellationToken);
        if (!approver.Contains(ListCapability, StringComparer.Ordinal)
            || !approver.Contains(DetailCapability, StringComparer.Ordinal)
            || !approver.Contains(ApproveCapability, StringComparer.Ordinal)
            || approver.Contains(CreateCapability, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The approver needs list/detail/approve and must not receive the staged create screen.");
        }

        string[] reader = await CapabilitiesAsync(context, ReaderActor, cancellationToken);
        if (!reader.Contains(ListCapability, StringComparer.Ordinal)
            || !reader.Contains(DetailCapability, StringComparer.Ordinal)
            || reader.Contains(ApproveCapability, StringComparer.Ordinal)
            || reader.Contains(CreateCapability, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The reader needs list/detail only and must not receive approve/create.");
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["creatorActor"] = CreatorActor,
            ["approverActor"] = ApproverActor,
            ["readerActor"] = ReaderActor,
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
