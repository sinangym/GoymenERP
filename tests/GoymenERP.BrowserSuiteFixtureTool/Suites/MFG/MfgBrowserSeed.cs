using System.Text.Json;
using GoymenERP.Api.Shell;
using GoymenERP.BrowserSuiteFixtureTool.Shared;
using GoymenERP.Modules.Product.Infrastructure.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.BrowserSuiteFixtureTool.Suites.Mfg;

/// <summary>
/// The `mfg` suite's seed (ORGANİZASYON, Manufacturing owner): one approved slitting plan and its open
/// order in company GDC, written through the real Manufacturing API as the suite's recorder actor, so
/// authorization (fresh company-wide grant), the company context, the approved kilogram profile,
/// ADR-011 idempotency and the audit envelope apply exactly as in the product. No SQL.
/// <list type="bullet">
/// <item>The work center is read through the real Organization endpoint as the canonical
/// <c>company-a-user</c> (its seeded ORGANIZATION_OPERATOR role reads work centers in GDC) and chosen by
/// its seeded code; no id is invented.</item>
/// <item>The plan, order and parent roll ids are fixed synthetic ids and every step's idempotency key is
/// deterministic, so a re-run of the fixture is a replay, never a second plan or order.</item>
/// <item>The parent roll is recorded through Inventory's real opening-count command from Product's gated
/// browser candidate and the work centre's real Organization location. The plan carries the resulting
/// label version; no SQL or invented Inventory reference is used.</item>
/// </list>
/// The browser suite (tests/e2e/manufacturing-mfg) arranges its own orders for the write cases; this
/// seeded order serves the reader case and proves the seed path end to end.
/// </summary>
public sealed class MfgBrowserSeed : IBrowserSuiteFixture
{
    public const string RecorderActor = "mfg-recorder";
    public const string ReaderActor = "mfg-reader";
    /// <summary>MFG-ORD: the close is its own authority — read + close, and no fact permission.</summary>
    public const string CloserActor = "mfg-closer";
    public const string OrganizationActor = "company-a-user";
    public const string CariActor = "group-bootstrap-admin";
    private const int CustomerRole = 1;
    public const string WorkCenterCode = "WC-SLIT";

    public static readonly Guid CompanyGdc = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid PlanId = Guid.Parse("c1000000-0000-4000-8000-000000000001");
    public static readonly Guid OrderId = Guid.Parse("c2000000-0000-4000-8000-000000000001");
    public static readonly Guid ParentRollId = Guid.Parse("c3000000-0000-4000-8000-000000000001");
    public static readonly Guid OpeningCountId = Guid.Parse("c3100000-0000-4000-8000-000000000001");
    public static readonly Guid CaseRollOpeningCountId = Guid.Parse("c3100000-0000-4000-8000-000000000002");
    public static readonly Guid CustomerOpeningCountId = Guid.Parse("c3100000-0000-4000-8000-000000000003");
    public static readonly Guid CustomerPlanId = Guid.Parse("c1000000-0000-4000-8000-000000000002");
    public static readonly Guid CustomerOrderId = Guid.Parse("c2000000-0000-4000-8000-000000000002");
    public static readonly Guid OpeningLineId = Guid.Parse("c3200000-0000-4000-8000-000000000001");
    public static readonly Guid ParentRollLotId = Guid.Parse("c3300000-0000-4000-8000-000000000001");
    public const string ParentRollCode = "SYN-MFG-ROLL-1";

    private sealed record ParentRollFixture(Guid LabelId, Guid LineId, Guid LotId, string Code, string CoilNumber);

    private static readonly ParentRollFixture ReaderRoll =
        new(ParentRollId, OpeningLineId, ParentRollLotId, ParentRollCode, "SYN-MFG-COIL-1");
    private static readonly ParentRollFixture IssueRoll = Roll(2);
    private static readonly ParentRollFixture ReplayRoll = Roll(3);
    private static readonly ParentRollFixture ConflictRoll = Roll(4);
    private static readonly ParentRollFixture ListRoll = Roll(5);
    private static readonly ParentRollFixture CloseRoll = Roll(6);
    private static readonly ParentRollFixture PlanCreateRoll = Roll(7);
    private static readonly ParentRollFixture PlanReplayRoll = Roll(8);
    private static readonly ParentRollFixture PlanRefusalRoll = Roll(9);
    private static readonly ParentRollFixture OutputUiRoll = Roll(10);
    private static readonly ParentRollFixture ProcessSlittingRoll = Roll(11);
    private static readonly ParentRollFixture ProcessColdRollingRoll = Roll(12);
    private static readonly ParentRollFixture ProcessTubeProfileRoll = Roll(13);
    private static readonly ParentRollFixture ProcessPaintRoll = Roll(14);
    private static readonly ParentRollFixture ProcessCutToLengthRoll = Roll(15);
    private static readonly ParentRollFixture CustomerOwnedRoll = Roll(16);
    private static readonly ParentRollFixture[] CaseRolls =
    [
        IssueRoll, ReplayRoll, ConflictRoll, ListRoll, CloseRoll,
        PlanCreateRoll, PlanReplayRoll, PlanRefusalRoll, OutputUiRoll,
        ProcessSlittingRoll, ProcessColdRollingRoll, ProcessTubeProfileRoll, ProcessPaintRoll,
        ProcessCutToLengthRoll,
    ];

    private const string ManufacturingPath = "/api/v1/manufacturing";
    private const string OpeningCountsPath = "/api/v1/inventory/opening-counts";
    private const string ParentRollCandidatesPath = "/api/v1/inventory/references/parent-rolls";

    public string Suite => "mfg";

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
            product = await productScope.ServiceProvider
                .GetRequiredService<C01E2eProductSalesCandidateFixtureWriter>()
                .EnsureAsync(cancellationToken);
        }

        var workCenters = await context.GetJsonAsync(
            OrganizationActor, CompanyGdc, "/api/v1/organization/work-centers", cancellationToken);
        var slitting = workCenters.EnumerateArray()
            .Where(workCenter => workCenter.GetProperty("code").GetString() == WorkCenterCode)
            .ToArray();
        if (slitting.Length != 1)
        {
            throw new InvalidOperationException($"GDC must have exactly one seeded {WorkCenterCode} work center.");
        }

        var workCenterId = slitting[0].GetProperty("id").GetGuid();
        var locationId = slitting[0].GetProperty("locationId").GetGuid();

        var customer = await CariBrowserSeed.OpenPartyWithAccountAsync(
            context, CariActor, CompanyGdc, "customer-owned-party", "Fason uretim musterisi",
            CustomerRole, cancellationToken);
        JsonElement customerDetail;
        using (var client = context.Api(CariActor, CompanyGdc))
        using (var request = new HttpRequestMessage(
                   HttpMethod.Get, $"{CariBrowserSeed.CounterpartiesPath}/{customer.PartyId:D}"))
        {
            request.Headers.Add(CariBrowserSeed.ClientContractHeader, CariBrowserSeed.ClientContractVersion);
            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            customerDetail = document.RootElement.Clone();
        }
        var customerAccount = customerDetail.GetProperty("companyAccounts").EnumerateArray()
            .SingleOrDefault(account => account.GetProperty("id").GetGuid() == customer.AccountId);
        var partyVersion = customerDetail.GetProperty("version").GetString();
        var accountVersion = customerAccount.ValueKind == JsonValueKind.Object
            ? customerAccount.GetProperty("version").GetString()
            : null;
        if (customerDetail.GetProperty("id").GetGuid() != customer.PartyId
            || customerAccount.ValueKind != JsonValueKind.Object
            || customerAccount.GetProperty("companyId").GetGuid() != CompanyGdc
            || customerAccount.GetProperty("roles").GetInt32() != CustomerRole
            || customerAccount.GetProperty("isActive").GetBoolean() is false
            || string.IsNullOrWhiteSpace(partyVersion)
            || string.IsNullOrWhiteSpace(accountVersion)
            || !string.Equals(accountVersion, customer.AccountVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The fresh Cari customer/account read differs from the opened customer.");
        }

        var opening = await context.PostJsonAsync(
            RecorderActor,
            CompanyGdc,
            OpeningCountsPath,
            new
            {
                countId = OpeningCountId,
                locationId,
                countedOn = ProductSalesCandidateFixture.EffectiveOn,
                sheetReference = "SYN-MFG-OPENING-1",
                lines = new[]
                {
                    new
                    {
                        lineId = ReaderRoll.LineId,
                        labelId = ReaderRoll.LabelId,
                        labelCode = ReaderRoll.Code,
                        lotId = ReaderRoll.LotId,
                        heatNumber = (string?)null,
                        coilNumber = ReaderRoll.CoilNumber,
                        productId = product.ProductId,
                        productVersion = product.ProductVersion,
                        quantityKg = "1000",
                    },
                },
            },
            "opening-count",
            cancellationToken);
        var openedLine = opening.GetProperty("lines")[0];
        if (openedLine.GetProperty("labelId").GetGuid() != ParentRollId
            || openedLine.GetProperty("labelCode").GetString() != ParentRollCode
            || openedLine.GetProperty("productId").GetGuid() != product.ProductId
            || openedLine.GetProperty("quantityKg").GetString() != "1000")
        {
            throw new InvalidOperationException("Inventory opening count did not answer the exact MFG parent roll.");
        }

        var caseOpening = await context.PostJsonAsync(
            RecorderActor,
            CompanyGdc,
            OpeningCountsPath,
            new
            {
                countId = CaseRollOpeningCountId,
                locationId,
                countedOn = ProductSalesCandidateFixture.EffectiveOn,
                sheetReference = "SYN-MFG-CASE-ROLLS-1",
                lines = CaseRolls.Select(roll => new
                {
                    lineId = roll.LineId,
                    labelId = roll.LabelId,
                    labelCode = roll.Code,
                    lotId = roll.LotId,
                    heatNumber = (string?)null,
                    coilNumber = roll.CoilNumber,
                    productId = product.ProductId,
                    productVersion = product.ProductVersion,
                    quantityKg = "1000",
                }).ToArray(),
            },
            "case-rolls-opening-count",
            cancellationToken);
        var openedLines = caseOpening.GetProperty("lines").EnumerateArray()
            .ToDictionary(line => line.GetProperty("labelId").GetGuid());
        if (openedLines.Count != CaseRolls.Length || CaseRolls.Any(roll =>
                !openedLines.TryGetValue(roll.LabelId, out var line)
                || line.GetProperty("labelCode").GetString() != roll.Code
                || line.GetProperty("productId").GetGuid() != product.ProductId
                || line.GetProperty("quantityKg").GetString() != "1000"))
        {
            throw new InvalidOperationException("Inventory opening count did not answer every isolated MFG case roll.");
        }

        var customerOpening = await context.PostJsonAsync(
            RecorderActor,
            CompanyGdc,
            OpeningCountsPath,
            new
            {
                countId = CustomerOpeningCountId,
                locationId,
                countedOn = ProductSalesCandidateFixture.EffectiveOn,
                sheetReference = "SYN-MFG-CUSTOMER-OWNED-1",
                positionContext = new
                {
                    ownerKind = "Counterparty",
                    ownerId = customer.PartyId,
                    ownerCompanyPartyAccountId = customer.AccountId,
                    custodianKind = "Company",
                    custodianId = CompanyGdc,
                },
                lines = new[]
                {
                    new
                    {
                        lineId = CustomerOwnedRoll.LineId,
                        labelId = CustomerOwnedRoll.LabelId,
                        labelCode = CustomerOwnedRoll.Code,
                        lotId = CustomerOwnedRoll.LotId,
                        heatNumber = (string?)null,
                        coilNumber = CustomerOwnedRoll.CoilNumber,
                        productId = product.ProductId,
                        productVersion = product.ProductVersion,
                        quantityKg = "1000",
                    },
                },
            },
            "customer-owned-opening-count",
            cancellationToken);
        var customerOpenedLine = customerOpening.GetProperty("lines")[0];
        if (customerOpenedLine.GetProperty("labelId").GetGuid() != CustomerOwnedRoll.LabelId
            || customerOpenedLine.GetProperty("quantityKg").GetString() != "1000")
        {
            throw new InvalidOperationException("Inventory did not reopen the exact customer-owned MFG roll.");
        }

        var customerCandidates = await context.GetJsonAsync(
            RecorderActor,
            CompanyGdc,
            $"{ParentRollCandidatesPath}?q={CustomerOwnedRoll.Code}&limit=25",
            cancellationToken);
        var customerRoll = customerCandidates.GetProperty("items").EnumerateArray()
            .SingleOrDefault(candidate => candidate.GetProperty("labelId").GetGuid() == CustomerOwnedRoll.LabelId);
        var customerPositions = customerRoll.ValueKind == JsonValueKind.Object
            ? customerRoll.GetProperty("positions").EnumerateArray().ToArray()
            : [];
        if (customerRoll.ValueKind != JsonValueKind.Object
            || !customerRoll.GetProperty("draftSelectable").GetBoolean()
            || customerPositions.Length != 1
            || customerPositions[0].GetProperty("ownerKind").GetString() != "Counterparty"
            || customerPositions[0].GetProperty("ownerId").GetGuid() != customer.PartyId
            || customerPositions[0].GetProperty("custodianKind").GetString() != "Company"
            || customerPositions[0].GetProperty("custodianId").GetGuid() != CompanyGdc)
        {
            throw new InvalidOperationException("The customer-owned roll did not reopen with its exact Inventory position.");
        }
        var customerRollVersion = customerRoll.GetProperty("labelVersion").GetString();
        if (string.IsNullOrWhiteSpace(customerRollVersion))
        {
            throw new InvalidOperationException("The customer-owned roll did not answer a label version.");
        }

        var customerPlan = await context.PostJsonAsync(
            RecorderActor,
            CompanyGdc,
            $"{ManufacturingPath}/plans",
            new
            {
                planId = CustomerPlanId,
                workCenterId,
                inputs = new[]
                {
                    new
                    {
                        parentRollId = CustomerOwnedRoll.LabelId,
                        plannedKg = "1000",
                        expectedLabelVersion = customerRollVersion,
                        commercialContext = new
                        {
                            partyId = customer.PartyId,
                            companyPartyAccountId = customer.AccountId,
                            partyVersion,
                            accountVersion,
                        },
                    },
                },
                target = (object?)null,
            },
            "customer-owned-plan",
            cancellationToken);
        RequireString(customerPlan, "status", "Draft", "Approved");
        var approvedCustomerPlan = await context.PostJsonAsync(
            RecorderActor,
            CompanyGdc,
            $"{ManufacturingPath}/plans/{CustomerPlanId:D}/approval",
            new { expectedVersion = 1 },
            "customer-owned-plan-approval",
            cancellationToken);
        RequireString(approvedCustomerPlan, "status", "Approved");
        var customerOrder = await context.PostJsonAsync(
            RecorderActor,
            CompanyGdc,
            $"{ManufacturingPath}/orders",
            new { orderId = CustomerOrderId, planId = CustomerPlanId },
            "customer-owned-order",
            cancellationToken);
        RequireString(customerOrder, "status", "Open");
        var customerOrderRoll = customerOrder.GetProperty("rolls")[0];
        var capturedContext = customerOrderRoll.GetProperty("commercialContext");
        if (customerOrder.GetProperty("orderId").GetGuid() != CustomerOrderId
            || customerOrderRoll.GetProperty("parentRollId").GetGuid() != CustomerOwnedRoll.LabelId
            || capturedContext.GetProperty("partyId").GetGuid() != customer.PartyId
            || capturedContext.GetProperty("companyPartyAccountId").GetGuid() != customer.AccountId
            || capturedContext.GetProperty("partyVersion").GetString() != partyVersion
            || capturedContext.GetProperty("accountVersion").GetString() != accountVersion)
        {
            throw new InvalidOperationException("The order did not capture the exact customer-owned commercial context.");
        }

        // Publish output facts from the real close-case Inventory candidate. The browser must send
        // these facts explicitly; it does not infer position policy from the parent or output class.
        var closeCandidates = await context.GetJsonAsync(
            RecorderActor,
            CompanyGdc,
            $"{ParentRollCandidatesPath}?q={CloseRoll.Code}&limit=25",
            cancellationToken);
        var closeRoll = closeCandidates.GetProperty("items").EnumerateArray()
            .SingleOrDefault(candidate => candidate.GetProperty("labelId").GetGuid() == CloseRoll.LabelId);
        var closePositions = closeRoll.ValueKind == JsonValueKind.Object
            ? closeRoll.GetProperty("positions").EnumerateArray().ToArray()
            : [];
        if (closeRoll.ValueKind != JsonValueKind.Object
            || closeRoll.GetProperty("productId").GetGuid() != product.ProductId
            || closeRoll.GetProperty("physicalKg").GetString() != "1000"
            || !closeRoll.GetProperty("draftSelectable").GetBoolean()
            || closePositions.Length != 1)
        {
            throw new InvalidOperationException("The close-case Inventory roll did not expose one exact output position source.");
        }
        var outputPosition = closePositions[0];
        if (outputPosition.GetProperty("ownerKind").GetString() != "Company"
            || outputPosition.GetProperty("ownerId").GetGuid() != CompanyGdc
            || outputPosition.GetProperty("custodianKind").GetString() != "Company"
            || outputPosition.GetProperty("custodianId").GetGuid() != CompanyGdc
            || outputPosition.GetProperty("locationId").GetGuid() != locationId
            || outputPosition.GetProperty("productId").GetGuid() != product.ProductId
            || outputPosition.GetProperty("block").GetString() != "Free")
        {
            throw new InvalidOperationException("The close-case Inventory roll position differs from the accepted fixture facts.");
        }

        var candidates = await context.GetJsonAsync(
            RecorderActor,
            CompanyGdc,
            $"{ParentRollCandidatesPath}?q={ParentRollCode}&limit=25",
            cancellationToken);
        var parentRoll = candidates.GetProperty("items").EnumerateArray()
            .SingleOrDefault(candidate => candidate.GetProperty("labelId").GetGuid() == ParentRollId);
        if (parentRoll.ValueKind != JsonValueKind.Object
            || parentRoll.GetProperty("labelCode").GetString() != ParentRollCode
            || !parentRoll.GetProperty("draftSelectable").GetBoolean()
            || parentRoll.GetProperty("physicalKg").GetString() != "1000")
        {
            throw new InvalidOperationException("The real Inventory parent-roll candidate is not selectable for MFG.");
        }
        var parentRollVersion = parentRoll.GetProperty("labelVersion").GetString();
        if (string.IsNullOrWhiteSpace(parentRollVersion))
        {
            throw new InvalidOperationException("The real Inventory parent roll did not answer a label version.");
        }

        var plan = await context.PostJsonAsync(
            RecorderActor,
            CompanyGdc,
            $"{ManufacturingPath}/plans",
            new
            {
                planId = PlanId,
                workCenterId,
                inputs = new[]
                {
                    new
                    {
                        parentRollId = ParentRollId,
                        plannedKg = "1000",
                        expectedLabelVersion = parentRollVersion,
                    },
                },
                target = (object?)null,
            },
            "plan",
            cancellationToken);
        RequireString(plan, "status", "Draft", "Approved");

        var approved = await context.PostJsonAsync(
            RecorderActor,
            CompanyGdc,
            $"{ManufacturingPath}/plans/{PlanId:D}/approval",
            new { expectedVersion = 1 },
            "plan-approval",
            cancellationToken);
        RequireString(approved, "status", "Approved");

        var order = await context.PostJsonAsync(
            RecorderActor,
            CompanyGdc,
            $"{ManufacturingPath}/orders",
            new { orderId = OrderId, planId = PlanId },
            "order",
            cancellationToken);
        RequireString(order, "status", "Open");
        if (order.GetProperty("orderId").GetGuid() != OrderId)
        {
            throw new InvalidOperationException("The seeded order answered another id.");
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["recorderActor"] = RecorderActor,
            ["readerActor"] = ReaderActor,
            ["closerActor"] = CloserActor,
            ["companyId"] = CompanyGdc.ToString("D"),
            ["workCenterId"] = workCenterId.ToString("D"),
            ["orderId"] = OrderId.ToString("D"),
            ["rollId"] = ParentRollId.ToString("D"),
            ["issueRollId"] = IssueRoll.LabelId.ToString("D"),
            ["issueRollCode"] = IssueRoll.Code,
            ["replayRollId"] = ReplayRoll.LabelId.ToString("D"),
            ["replayRollCode"] = ReplayRoll.Code,
            ["conflictRollId"] = ConflictRoll.LabelId.ToString("D"),
            ["conflictRollCode"] = ConflictRoll.Code,
            ["listRollId"] = ListRoll.LabelId.ToString("D"),
            ["listRollCode"] = ListRoll.Code,
            ["closeRollId"] = CloseRoll.LabelId.ToString("D"),
            ["closeRollCode"] = CloseRoll.Code,
            ["planCreateRollId"] = PlanCreateRoll.LabelId.ToString("D"),
            ["planCreateRollCode"] = PlanCreateRoll.Code,
            ["planReplayRollId"] = PlanReplayRoll.LabelId.ToString("D"),
            ["planReplayRollCode"] = PlanReplayRoll.Code,
            ["planRefusalRollId"] = PlanRefusalRoll.LabelId.ToString("D"),
            ["planRefusalRollCode"] = PlanRefusalRoll.Code,
            ["outputUiRollId"] = OutputUiRoll.LabelId.ToString("D"),
            ["outputUiRollCode"] = OutputUiRoll.Code,
            ["outputProductId"] = closeRoll.GetProperty("productId").GetGuid().ToString("D"),
            ["outputProductVersion"] = product.ProductVersion,
            ["outputRequiredDocuments"] = closeRoll.GetProperty("requiredDocuments").GetRawText(),
            ["outputOwnerKind"] = outputPosition.GetProperty("ownerKind").GetString()!,
            ["outputOwnerId"] = outputPosition.GetProperty("ownerId").GetGuid().ToString("D"),
            ["outputCustodianKind"] = outputPosition.GetProperty("custodianKind").GetString()!,
            ["outputCustodianId"] = outputPosition.GetProperty("custodianId").GetGuid().ToString("D"),
            ["outputLocationId"] = outputPosition.GetProperty("locationId").GetGuid().ToString("D"),
            ["outputInitialBlock"] = outputPosition.GetProperty("block").GetString()!,
        };
    }

    private static ParentRollFixture Roll(int suffix) => new(
        Guid.Parse($"c3000000-0000-4000-8000-{suffix:D12}"),
        Guid.Parse($"c3200000-0000-4000-8000-{suffix:D12}"),
        Guid.Parse($"c3300000-0000-4000-8000-{suffix:D12}"),
        $"SYN-MFG-ROLL-{suffix}",
        $"SYN-MFG-COIL-{suffix}");

    private static void RequireString(JsonElement answer, string property, params string[] allowed)
    {
        var value = answer.GetProperty(property).GetString();
        if (value is null || !allowed.Contains(value, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"The seed expected {property} in [{string.Join(", ", allowed)}], the API answered '{value}'.");
        }
    }
}
