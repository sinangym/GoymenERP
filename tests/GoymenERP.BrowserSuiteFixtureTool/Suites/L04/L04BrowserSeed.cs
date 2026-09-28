using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Shell;
using GoymenERP.BrowserSuiteFixtureTool.Shared;
using GoymenERP.Modules.Logistics.Infrastructure.Fixtures;
using GoymenERP.Modules.Product.Infrastructure.Fixtures;
using GoymenERP.Modules.Purchasing.Infrastructure.Fixtures;
using GoymenERP.Modules.Sales.Infrastructure.Fixtures;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.BrowserSuiteFixtureTool.Suites.L04;

/// <summary>
/// The l04 seed (LOG-4b, the loading-instruction user path). Fixture scope, exactly this and nothing else:
/// <list type="number">
/// <item>ONE Cari customer (party + company account, Customer role) through the REAL Counterparty API as the
/// bootstrap admin (the fin01 way).</item>
/// <item>ONE Sales draft written by the gated Sales fixture writer (decision W1/C: synthetic product / money
/// snapshots, no attempt, no audit) with the REAL customer ids and the REAL Settings kilogram unit, then
/// SUBMITTED and APPROVED through the REAL Sales API by two Sales actors (Ö4: never the same one).</item>
/// <item>ONE loading instruction (two lines on that approved line) created and ISSUED through the REAL
/// Logistics API by the preparer; its second line is then BLOCKED by the gated Logistics fixture writer —
/// the only suite state no API produces in 4a (D2's block source arrives in LOG-5).</item>
/// </list>
/// <item>AT-025 (L04-08): ONE Cari supplier through the REAL Counterparty API and ONE purchase order written by the gated
/// Purchasing fixture writer (Draft, the pur01 way) with that supplier, then APPROVED through the REAL Purchasing API by the
/// seed-only purchase approver — so the operations work list has one purchase line waiting for goods receipt beside the
/// approved sales line, and the suite can prove that neither shows in the other's section.</item>
/// Everything the suite does afterwards is real UI + real API: the positive path creates ANOTHER instruction
/// from a real selection of the approved line in the normal screens. The read-backs below fail the run here,
/// not in the browser, when a grant, a port or a mount does not resolve.
/// </summary>
public sealed class L04BrowserSeed : IBrowserSuiteFixture
{
    public const string PreparerActor = "l04-preparer";
    public const string LoaderActor = "l04-loader";
    public const string ReaderActor = "l04-reader";
    public const string SalesPreparerActor = "l04-sales-preparer";
    public const string SalesApproverActor = "l04-sales-approver";
    // AT-025: the operations work list's actor (both section names + the owner screens' reads) and the seed-only purchase approver.
    public const string OperationsActor = "l04-operations";
    public const string PurchaseApproverActor = "l04-purchase-approver";
    public const string CariActor = "group-bootstrap-admin";

    public static readonly Guid CompanyGdc = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid CompanyOther = Guid.Parse("10000000-0000-0000-0000-000000000002");

    public static readonly Guid DraftId = Guid.Parse("e1000000-0000-4000-8000-000000000401");
    public static readonly Guid LineId = Guid.Parse("e9000000-0000-4000-8000-000000000401");
    public static readonly Guid OpenLineId = Guid.Parse("e3000000-0000-4000-8000-000000000401");
    public static readonly Guid BlockedLineId = Guid.Parse("e3000000-0000-4000-8000-000000000402");
    public const string DocumentNo = "SENTETIK browser-suite:l04 SO-0001";
    public const string ProductName = "SENTETIK browser-suite:l04 rulo 1,25 × 1240";
    // Business 5745076917: the customer select shows the Cari NAME (trade name, else legal name). The seed's party has
    // no trade name, so the price-free read's customerName is its legal name = context.Label("Yükleme müşterisi").
    public const string CustomerName = "SENTETIK browser-suite:l04 Yükleme müşterisi";

    // AT-025: the purchase order waiting for goods receipt (deterministic ids, the pur01 way), and its supplier's Cari name.
    public static readonly Guid PurchaseOrderId = Guid.Parse("e7000000-0000-4000-8000-000000000401");
    public static readonly Guid PurchaseLineId = Guid.Parse("e7100000-0000-4000-8000-000000000401");
    private static readonly Guid PurchaseProductId = Guid.Parse("e7200000-0000-4000-8000-000000000401");
    public const string SupplierName = "SENTETIK browser-suite:l04 Operasyon tedarikçisi";

    // Synthetic owner snapshots the Sales fixture stores (no Product or Settings money answer is asked for them).
    private static readonly Guid CurrencyId = Guid.Parse("e4000000-0000-4000-8000-000000000401");
    private static readonly Guid MoneyProfileId = Guid.Parse("e5000000-0000-4000-8000-000000000401");
    private static readonly Guid OpeningCountId = Guid.Parse("e6000000-0000-4000-8000-000000000401");
    private static readonly Guid OpeningLineId = Guid.Parse("e6100000-0000-4000-8000-000000000401");
    private static readonly Guid ShipmentLabelId = Guid.Parse("e6200000-0000-4000-8000-000000000401");
    private static readonly Guid ShipmentLotId = Guid.Parse("e6300000-0000-4000-8000-000000000401");
    private static readonly Guid ShipmentAllocationId = Guid.Parse("e6400000-0000-4000-8000-000000000401");

    private static readonly DateOnly BusinessDate = new DateOnly(2026, 9, 19);
    private static readonly DateOnly EffectiveFrom = new DateOnly(2026, 9, 18);
    private static readonly DateTimeOffset At = new DateTimeOffset(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);

    private const int CustomerRole = 1;
    private const int SupplierRole = 2;
    private const string PurchaseOrdersPath = "/api/v1/purchasing/orders";
    private const string SalesWorkPath = "/api/v1/logistics/operations-work/sales";
    private const string PurchaseWorkPath = "/api/v1/logistics/operations-work/purchase";
    private const string SalesWorkCapability = "logistics.operations-work.sales";
    private const string PurchaseWorkCapability = "logistics.operations-work.purchase";
    private const string DraftsPath = "/api/v1/sales/drafts";
    private const string InstructionsPath = "/api/v1/logistics/loading-instructions";
    private const string ApprovedLinesPath = InstructionsPath + "/approved-order-lines";
    private const string OpeningCountsPath = "/api/v1/inventory/opening-counts";
    private const string PhysicalAllocationsPath = "/api/v1/inventory/physical-allocations";
    private const string OrganizationLocationsPath = "/api/v1/organization/locations?page=1&pageSize=100";
    private const string CapabilitiesPath = "/api/v1/shell/capabilities";
    private const string ListCapability = "logistics.instructions.list";
    private const string CreateCapability = "logistics.instructions.create";
    private const string DetailCapability = "logistics.instructions.detail";

    public string Suite => "l04";

    public async Task<IReadOnlyDictionary<string, string>> SeedAsync(
        BrowserSuiteFixtureContext context,
        CancellationToken cancellationToken)
    {
        await using (var registryScope = context.Services.CreateAsyncScope())
        {
            await registryScope.ServiceProvider.GetRequiredService<C01E2eProductTechnicalCardFixtureWriter>()
                .EnsureAsync(cancellationToken);
        }

        ProductSalesCandidateFixtureOutput product;
        await using (var productScope = context.Services.CreateAsyncScope())
        {
            product = await productScope.ServiceProvider.GetRequiredService<C01E2eProductSalesCandidateFixtureWriter>()
                .EnsureAsync(cancellationToken);
        }

        string shipmentLabelVersion = await EnsureShipmentLabelAsync(context, product, cancellationToken);

        // 1) The customer, through the real Cari API.
        CariBrowserAccount customer = await CariBrowserSeed.OpenPartyWithAccountAsync(
            context, CariActor, CompanyGdc, "customer", "Yükleme müşterisi", CustomerRole, cancellationToken);

        // 2) The draft: fixture-written (Preparing), then submitted and approved through the real Sales API.
        long draftVersion;
        await using (AsyncServiceScope scope = context.Services.CreateAsyncScope())
        {
            SalesDraftFixtureOutcome outcome = await scope.ServiceProvider.GetRequiredService<C01E2eSalesDraftFixtureWriter>().EnsureAsync(
                new SalesDraftFixtureRequest(
                    DraftId,
                    CompanyGdc,
                    DocumentNo,
                    context.Label(string.Empty).TrimEnd(),
                    BusinessDate,
                    new SalesDraftFixtureOwners(customer.PartyId, customer.AccountId, CurrencyId, MoneyProfileId, EffectiveFrom),
                    [
                        new SalesDraftFixtureLine(LineId, product.ProductId, "L04-COIL", ProductName,
                            product.UnitId, product.ProfileId, "kg", "weight", IsKilogram: true, Scale: 3, "6", 6m),
                    ],
                    context.Label("seed"),
                    context.IdempotencyKey("draft"),
                    At),
                cancellationToken);
            if (outcome.DraftId != DraftId)
            {
                throw new InvalidOperationException("The Sales fixture writer answered another draft id.");
            }

            draftVersion = outcome.Version;
        }

        JsonElement current = await context.GetJsonAsync(SalesPreparerActor, CompanyGdc, $"{DraftsPath}/{DraftId:D}", cancellationToken);
        string approvalState = current.GetProperty("draft").GetProperty("approvalState").GetString() ?? string.Empty;
        if (approvalState == "Preparing")
        {
            JsonElement submitted = await context.PostJsonAsync(SalesPreparerActor, CompanyGdc, $"{DraftsPath}/{DraftId:D}/submission",
                new { expectedVersion = draftVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) }, "submit", cancellationToken);
            draftVersion = long.Parse(submitted.GetProperty("detail").GetProperty("draft").GetProperty("version").GetString()!,
                System.Globalization.CultureInfo.InvariantCulture);
            approvalState = "PendingApproval";
        }

        if (approvalState == "PendingApproval")
        {
            await context.PostJsonAsync(SalesApproverActor, CompanyGdc, $"{DraftsPath}/{DraftId:D}/approval",
                new { expectedVersion = draftVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) }, "approve", cancellationToken);
        }

        await EnsureShipmentAllocationAsync(context, shipmentLabelVersion, cancellationToken);

        JsonElement approved = await context.GetJsonAsync(SalesApproverActor, CompanyGdc, $"{DraftsPath}/{DraftId:D}", cancellationToken);
        JsonElement approvedDraft = approved.GetProperty("draft");
        if (approvedDraft.GetProperty("approvalState").GetString() != "Approved"
            || approvedDraft.GetProperty("partyId").GetGuid() != customer.PartyId
            || approvedDraft.GetProperty("companyAccountId").GetGuid() != customer.AccountId
            || approved.GetProperty("lines").GetArrayLength() != 1
            || approved.GetProperty("lines")[0].GetProperty("lineId").GetGuid() != LineId)
        {
            throw new InvalidOperationException("The seeded draft is not an APPROVED one-line draft of the seeded customer on the real detail route.");
        }

        // Deny-first (Sinan §4 #66 5738113623): the Logistics preparer holds no Sales grant — the Sales detail refuses it (403) —
        // and picks the line through the PRICE-FREE Logistics read, which answers the seeded approved line without a price key.
        bool salesRefused = false;
        try
        {
            await context.GetJsonAsync(PreparerActor, CompanyGdc, $"{DraftsPath}/{DraftId:D}", cancellationToken);
        }
        catch (InvalidOperationException refusal) when (refusal.Message.Contains("answered 403", StringComparison.Ordinal))
        {
            salesRefused = true;
        }

        if (!salesRefused)
        {
            throw new InvalidOperationException("The Sales detail route must refuse the Logistics preparer (no Sales grant).");
        }

        JsonElement approvedLines = await context.GetJsonAsync(PreparerActor, CompanyGdc, ApprovedLinesPath, cancellationToken);
        string approvedLinesJson = approvedLines.GetRawText();
        if (!approvedLinesJson.Contains(DocumentNo, StringComparison.Ordinal) || !approvedLinesJson.Contains(LineId.ToString("D"), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The price-free approved-order-lines read does not answer the seeded approved line to the preparer.");
        }

        if (!CarriesCustomerName(approvedLines, CustomerName))
        {
            throw new InvalidOperationException($"The price-free approved-order-lines read has no item whose customerName is '{CustomerName}'.");
        }

        foreach (string priceKey in new[] { "unitPrice", "discountPercent", "paymentTerms", "priceUnitId", "currencyId" })
        {
            if (approvedLinesJson.Contains($"\"{priceKey}\"", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The price-free approved-order-lines read carries a price key: {priceKey}.");
            }
        }

        // 3) The negatives' instruction: created and issued through the real Logistics API, then one line blocked by the fixture.
        Guid instructionId = await EnsureInstructionAsync(context, customer, product.UnitId, cancellationToken);
        await using (AsyncServiceScope scope = context.Services.CreateAsyncScope())
        {
            LogisticsLineBlockFixtureOutcome block = await scope.ServiceProvider.GetRequiredService<C01E2eLogisticsLineBlockFixtureWriter>().BlockAsync(
                new LogisticsLineBlockFixtureRequest(instructionId, BlockedLineId, CompanyGdc, context.Label("blok"), At), cancellationToken);
            if (block.LineId != BlockedLineId)
            {
                throw new InvalidOperationException("The Logistics fixture writer blocked another line.");
            }
        }

        // Read back through the real host, as each actor.
        JsonElement detail = await context.GetJsonAsync(PreparerActor, CompanyGdc, $"{InstructionsPath}/{instructionId:D}", cancellationToken);
        JsonElement instruction = detail.GetProperty("instruction");
        JsonElement[] lines = [.. detail.GetProperty("lines").EnumerateArray()];
        if (instruction.GetProperty("id").GetGuid() != instructionId
            || instruction.GetProperty("status").GetString() != "Issued"
            || lines.Length != 2
            || !lines.Any(line => line.GetProperty("lineId").GetGuid() == OpenLineId && !line.GetProperty("isBlocked").GetBoolean())
            || !lines.Any(line => line.GetProperty("lineId").GetGuid() == BlockedLineId && line.GetProperty("isBlocked").GetBoolean()))
        {
            throw new InvalidOperationException("The seeded instruction is not an Issued two-line instruction with its second line blocked on the real detail route.");
        }

        JsonElement list = await context.GetJsonAsync(ReaderActor, CompanyGdc, $"{InstructionsPath}?status=Issued", cancellationToken);
        if (!list.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("id").GetGuid() == instructionId))
        {
            throw new InvalidOperationException("The reader does not see the seeded instruction in the real list.");
        }

        // 4) AT-025: one approved purchase order waiting for goods receipt, and the operations actor's two separate sections.
        await EnsureApprovedPurchaseOrderAsync(context, cancellationToken);
        JsonElement salesWork = await context.GetJsonAsync(OperationsActor, CompanyGdc, SalesWorkPath, cancellationToken);
        JsonElement purchaseWork = await context.GetJsonAsync(OperationsActor, CompanyGdc, PurchaseWorkPath, cancellationToken);
        string salesWorkJson = salesWork.GetRawText();
        string purchaseWorkJson = purchaseWork.GetRawText();
        if (salesWork.GetProperty("section").GetString() != "Sales"
            || !salesWorkJson.Contains(LineId.ToString("D"), StringComparison.OrdinalIgnoreCase)
            || salesWorkJson.Contains(PurchaseOrderId.ToString("D"), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The sales section must answer the seeded approved sales line and nothing of the purchase order.");
        }

        if (purchaseWork.GetProperty("section").GetString() != "Purchase"
            || !purchaseWorkJson.Contains(PurchaseLineId.ToString("D"), StringComparison.OrdinalIgnoreCase)
            || purchaseWorkJson.Contains(DraftId.ToString("D"), StringComparison.OrdinalIgnoreCase)
            || !purchaseWorkJson.Contains(SupplierName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The purchase section must answer the seeded purchase line by the supplier's name and nothing of the sales order.");
        }

        string[] operations = await CapabilitiesAsync(context, OperationsActor, cancellationToken);
        if (!operations.Contains(SalesWorkCapability, StringComparer.Ordinal) || !operations.Contains(PurchaseWorkCapability, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The two operations sections are not published to the operations actor.");
        }

        string[] preparer = await CapabilitiesAsync(context, PreparerActor, cancellationToken);
        if (!preparer.Contains(ListCapability, StringComparer.Ordinal) || !preparer.Contains(CreateCapability, StringComparer.Ordinal)
            || !preparer.Contains(DetailCapability, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The three Logistics screens are not published to the preparer.");
        }

        foreach (string actor in new[] { LoaderActor, ReaderActor })
        {
            string[] published = await CapabilitiesAsync(context, actor, cancellationToken);
            if (!published.Contains(ListCapability, StringComparer.Ordinal) || !published.Contains(DetailCapability, StringComparer.Ordinal)
                || published.Contains(CreateCapability, StringComparer.Ordinal)
                || published.Contains(SalesWorkCapability, StringComparer.Ordinal) || published.Contains(PurchaseWorkCapability, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"{actor} must see the list and the detail and not the create screen.");
            }
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["preparerActor"] = PreparerActor,
            ["loaderActor"] = LoaderActor,
            ["readerActor"] = ReaderActor,
            ["companyId"] = CompanyGdc.ToString("D"),
            ["otherCompanyId"] = CompanyOther.ToString("D"),
            ["customerPartyId"] = customer.PartyId.ToString("D"),
            ["companyAccountId"] = customer.AccountId.ToString("D"),
            ["draftId"] = DraftId.ToString("D"),
            ["approvedLineId"] = LineId.ToString("D"),
            ["instructionId"] = instructionId.ToString("D"),
            ["blockedLineId"] = BlockedLineId.ToString("D"),
            ["openLineId"] = OpenLineId.ToString("D"),
            ["shipmentAllocationId"] = ShipmentAllocationId.ToString("D"),
            ["shipmentLabelId"] = ShipmentLabelId.ToString("D"),
        };
    }

    /// <summary>
    /// AT-025: the supplier through the real Cari API, the order through the gated Purchasing fixture writer (Draft), then the
    /// approval through the real Purchasing API by the seed-only approver; a re-run finds it Approved and leaves it.
    /// </summary>
    private static async Task EnsureApprovedPurchaseOrderAsync(BrowserSuiteFixtureContext context, CancellationToken cancellationToken)
    {
        CariBrowserAccount supplier = await CariBrowserSeed.OpenPartyWithAccountAsync(
            context, CariActor, CompanyGdc, "supplier", "Operasyon tedarikçisi", SupplierRole, cancellationToken);
        long version;
        await using (AsyncServiceScope scope = context.Services.CreateAsyncScope())
        {
            PurchaseOrderFixtureOutcome outcome = await scope.ServiceProvider.GetRequiredService<C01E2ePurchaseOrderFixtureWriter>().EnsureAsync(
                new PurchaseOrderFixtureRequest(
                    PurchaseOrderId,
                    CompanyGdc,
                    supplier.PartyId,
                    supplier.AccountId,
                    BusinessDate,
                    new PurchaseOrderFixtureLine(PurchaseLineId, PurchaseProductId, "1", SettingsUnitIdentitiesV1.Kilogram, IsKilogram: true, Quantity: 7m),
                    context.Label(string.Empty).TrimEnd(),
                    context.Label("purchase-seed"),
                    context.IdempotencyKey("purchase-order"),
                    At),
                cancellationToken);
            if (outcome.OrderId != PurchaseOrderId)
            {
                throw new InvalidOperationException("The Purchasing fixture writer answered another order id.");
            }

            version = outcome.Version;
        }

        JsonElement detail = await context.GetJsonAsync(PurchaseApproverActor, CompanyGdc, $"{PurchaseOrdersPath}/{PurchaseOrderId:D}", cancellationToken);
        if (detail.GetProperty("order").GetProperty("status").GetString() == "Draft")
        {
            await context.PostJsonAsync(PurchaseApproverActor, CompanyGdc, $"{PurchaseOrdersPath}/{PurchaseOrderId:D}/approval",
                new { expectedVersion = version.ToString(System.Globalization.CultureInfo.InvariantCulture) }, "purchase-approve", cancellationToken);
            detail = await context.GetJsonAsync(PurchaseApproverActor, CompanyGdc, $"{PurchaseOrdersPath}/{PurchaseOrderId:D}", cancellationToken);
        }

        if (detail.GetProperty("order").GetProperty("status").GetString() != "Approved"
            || detail.GetProperty("lines").GetArrayLength() != 1
            || detail.GetProperty("lines")[0].GetProperty("lineId").GetGuid() != PurchaseLineId
            || detail.GetProperty("lines")[0].GetProperty("remaining").GetString() != "7")
        {
            throw new InvalidOperationException("The seeded purchase order is not an APPROVED one-line order with 7 kg waiting for receipt.");
        }
    }

    /// <summary>The seeded instruction through the real API: its create key replays on a re-run, so the id is stable.</summary>
    private static async Task<Guid> EnsureInstructionAsync(BrowserSuiteFixtureContext context, CariBrowserAccount customer,
        Guid unitId, CancellationToken cancellationToken)
    {
        JsonElement created = await context.PostJsonAsync(PreparerActor, CompanyGdc, InstructionsPath,
            new
            {
                customerPartyId = customer.PartyId,
                companyAccountId = customer.AccountId,
                effectiveOn = "2026-09-19",
                lines = new[]
                {
                    new { lineId = OpenLineId, sourceOrderId = DraftId, sourceOrderLineId = LineId, unitId, quantity = "4" },
                    new { lineId = BlockedLineId, sourceOrderId = DraftId, sourceOrderLineId = LineId, unitId, quantity = "2" },
                },
            },
            "instruction",
            cancellationToken);
        JsonElement summary = created.GetProperty("detail").GetProperty("instruction");
        Guid instructionId = summary.GetProperty("id").GetGuid();
        if (summary.GetProperty("status").GetString() == "Draft")
        {
            await context.PostJsonAsync(PreparerActor, CompanyGdc, $"{InstructionsPath}/{instructionId:D}/issue",
                new { expectedVersion = summary.GetProperty("version").GetString() }, "issue", cancellationToken);
        }

        return instructionId;
    }

    private static async Task<string> EnsureShipmentLabelAsync(BrowserSuiteFixtureContext context,
        ProductSalesCandidateFixtureOutput product, CancellationToken cancellationToken)
    {
        JsonElement locations = await context.GetJsonAsync(CariActor, CompanyGdc, OrganizationLocationsPath, cancellationToken);
        Guid locationId = locations.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("isActive").GetBoolean() && !item.GetProperty("isExternal").GetBoolean())
            .OrderBy(item => item.GetProperty("code").GetString(), StringComparer.Ordinal)
            .Select(item => item.GetProperty("id").GetGuid())
            .FirstOrDefault();
        if (locationId == Guid.Empty)
        {
            throw new InvalidOperationException("The l04 shipment prerequisite has no active non-external GDC location.");
        }

        JsonElement opening = await context.PostJsonAsync(LoaderActor, CompanyGdc, OpeningCountsPath, new
        {
            countId = OpeningCountId,
            locationId,
            countedOn = ProductSalesCandidateFixture.EffectiveOn,
            sheetReference = context.Label("shipment opening"),
            lines = new[]
            {
                new
                {
                    lineId = OpeningLineId,
                    labelId = ShipmentLabelId,
                    labelCode = "SYNL04-SHIP-LABEL-1",
                    lotId = ShipmentLotId,
                    heatNumber = (string?)null,
                    coilNumber = (string?)null,
                    productId = product.ProductId,
                    productVersion = product.ProductVersion,
                    quantityKg = "2",
                },
            },
        }, "shipment-opening", cancellationToken);
        JsonElement opened = opening.GetProperty("lines").EnumerateArray().Single();
        if (opened.GetProperty("labelId").GetGuid() != ShipmentLabelId
            || opened.GetProperty("productId").GetGuid() != product.ProductId
            || opened.GetProperty("quantityKg").GetString() != "2")
        {
            throw new InvalidOperationException("The real opening command returned another l04 shipment prerequisite.");
        }

        JsonElement label = await context.GetJsonAsync(
            LoaderActor, CompanyGdc, $"/api/v1/inventory/labels/{ShipmentLabelId:D}/detail", cancellationToken);
        return label.GetProperty("labelVersion").GetString()
            ?? throw new InvalidOperationException("The l04 shipment label version is missing.");
    }

    private static async Task EnsureShipmentAllocationAsync(BrowserSuiteFixtureContext context,
        string labelVersion, CancellationToken cancellationToken)
    {
        JsonElement allocation = await context.PostJsonAsync(LoaderActor, CompanyGdc, PhysicalAllocationsPath, new
        {
            allocationId = ShipmentAllocationId,
            salesOrderId = DraftId,
            salesOrderLineId = LineId,
            labelId = ShipmentLabelId,
            expectedLabelVersion = labelVersion,
            quantityKg = "2",
            reason = context.Label("definite shipment allocation"),
            reservedUntilUtc = (DateTimeOffset?)null,
        }, "shipment-allocation", cancellationToken);
        if (allocation.GetProperty("allocationId").GetGuid() != ShipmentAllocationId
            || allocation.GetProperty("status").GetString() != "Active"
            || allocation.GetProperty("remainingKg").GetString() != "2")
        {
            throw new InvalidOperationException("The real allocation command did not create the exact l04 shipment allocation.");
        }
    }

    /// <summary>True when any object anywhere in the JSON tree has a customerName equal to the expected text.</summary>
    private static bool CarriesCustomerName(JsonElement element, string expected)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("customerName", out JsonElement name) && name.ValueKind == JsonValueKind.String
                    && string.Equals(name.GetString(), expected, StringComparison.Ordinal))
                {
                    return true;
                }

                return element.EnumerateObject().Any(property => CarriesCustomerName(property.Value, expected));
            case JsonValueKind.Array:
                return element.EnumerateArray().Any(item => CarriesCustomerName(item, expected));
            default:
                return false;
        }
    }

    private static async Task<string[]> CapabilitiesAsync(BrowserSuiteFixtureContext context, string actor, CancellationToken cancellationToken)
    {
        JsonElement snapshot = await context.GetJsonAsync(actor, CompanyGdc, CapabilitiesPath, cancellationToken);
        return [.. snapshot.GetProperty("capabilities").EnumerateArray().Select(capability => capability.GetString()!)];
    }
}
