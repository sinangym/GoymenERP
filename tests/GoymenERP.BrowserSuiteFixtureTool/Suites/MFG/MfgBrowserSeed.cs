using System.Text.Json;
using GoymenERP.Api.Shell;
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
    public const string WorkCenterCode = "WC-SLIT";

    public static readonly Guid CompanyGdc = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid PlanId = Guid.Parse("c1000000-0000-4000-8000-000000000001");
    public static readonly Guid OrderId = Guid.Parse("c2000000-0000-4000-8000-000000000001");
    public static readonly Guid ParentRollId = Guid.Parse("c3000000-0000-4000-8000-000000000001");
    public static readonly Guid OpeningCountId = Guid.Parse("c3100000-0000-4000-8000-000000000001");
    public static readonly Guid OpeningLineId = Guid.Parse("c3200000-0000-4000-8000-000000000001");
    public static readonly Guid ParentRollLotId = Guid.Parse("c3300000-0000-4000-8000-000000000001");
    public const string ParentRollCode = "SYN-MFG-ROLL-1";

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
                        lineId = OpeningLineId,
                        labelId = ParentRollId,
                        labelCode = ParentRollCode,
                        lotId = ParentRollLotId,
                        heatNumber = (string?)null,
                        coilNumber = "SYN-MFG-COIL-1",
                        productId = product.ProductId,
                        productVersion = product.ProductVersion,
                        quantityKg = "1000",
                    },
                },
            },
            "opening-count",
            cancellationToken);
        var openedLine = opening.GetProperty("count").GetProperty("lines")[0];
        if (openedLine.GetProperty("labelId").GetGuid() != ParentRollId
            || openedLine.GetProperty("labelCode").GetString() != ParentRollCode
            || openedLine.GetProperty("productId").GetGuid() != product.ProductId
            || openedLine.GetProperty("quantityKg").GetString() != "1000")
        {
            throw new InvalidOperationException("Inventory opening count did not answer the exact MFG parent roll.");
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
        };
    }

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
