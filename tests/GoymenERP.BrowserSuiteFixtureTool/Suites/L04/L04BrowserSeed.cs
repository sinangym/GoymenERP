using System.Text.Json;
using GoymenERP.Api.Shell;
using GoymenERP.BrowserSuiteFixtureTool.Shared;
using GoymenERP.Modules.Logistics.Infrastructure.Fixtures;
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

    // Synthetic owner snapshots the Sales fixture stores (no Product or Settings money answer is asked for them).
    private static readonly Guid CurrencyId = Guid.Parse("e4000000-0000-4000-8000-000000000401");
    private static readonly Guid MoneyProfileId = Guid.Parse("e5000000-0000-4000-8000-000000000401");
    private static readonly Guid CoilProductId = Guid.Parse("e8000000-0000-4000-8000-000000000401");
    // The REAL Settings kilogram unit (ApprovedUnitProfileCatalog.GdcKilogramV1): the Logistics create verifies it against the real profile.
    private static readonly Guid KgUnitId = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid KgProfileId = Guid.Parse("4a790fdc-2a6b-5126-835f-d3a13d28b71c");

    private static readonly DateOnly BusinessDate = new DateOnly(2026, 9, 19);
    private static readonly DateOnly EffectiveFrom = new DateOnly(2026, 9, 18);
    private static readonly DateTimeOffset At = new DateTimeOffset(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);

    private const int CustomerRole = 1;
    private const string DraftsPath = "/api/v1/sales/drafts";
    private const string InstructionsPath = "/api/v1/logistics/loading-instructions";
    private const string ApprovedLinesPath = InstructionsPath + "/approved-order-lines";
    private const string CapabilitiesPath = "/api/v1/shell/capabilities";
    private const string ListCapability = "logistics.instructions.list";
    private const string CreateCapability = "logistics.instructions.create";
    private const string DetailCapability = "logistics.instructions.detail";

    public string Suite => "l04";

    public async Task<IReadOnlyDictionary<string, string>> SeedAsync(
        BrowserSuiteFixtureContext context,
        CancellationToken cancellationToken)
    {
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
                        new SalesDraftFixtureLine(LineId, CoilProductId, "L04-COIL", ProductName,
                            KgUnitId, KgProfileId, "kg", "weight", IsKilogram: true, Scale: 3, "6", 6m),
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
        Guid instructionId = await EnsureInstructionAsync(context, customer, cancellationToken);
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
                || published.Contains(CreateCapability, StringComparer.Ordinal))
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
            ["customerName"] = CustomerName,
            ["instructionId"] = instructionId.ToString("D"),
            ["blockedLineId"] = BlockedLineId.ToString("D"),
            ["openLineId"] = OpenLineId.ToString("D"),
        };
    }

    /// <summary>The seeded instruction through the real API: its create key replays on a re-run, so the id is stable.</summary>
    private static async Task<Guid> EnsureInstructionAsync(BrowserSuiteFixtureContext context, CariBrowserAccount customer, CancellationToken cancellationToken)
    {
        JsonElement created = await context.PostJsonAsync(PreparerActor, CompanyGdc, InstructionsPath,
            new
            {
                customerPartyId = customer.PartyId,
                companyAccountId = customer.AccountId,
                effectiveOn = "2026-09-19",
                lines = new[]
                {
                    new { lineId = OpenLineId, sourceOrderId = DraftId, sourceOrderLineId = LineId, unitId = KgUnitId, quantity = "4" },
                    new { lineId = BlockedLineId, sourceOrderId = DraftId, sourceOrderLineId = LineId, unitId = KgUnitId, quantity = "2" },
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
