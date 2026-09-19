using System.Text.Json;

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
/// <item>The parent roll is a synthetic label identity: MFG-API has no Inventory link in this slice.</item>
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

    private const string ManufacturingPath = "/api/v1/manufacturing";

    public string Suite => "mfg";

    public async Task<IReadOnlyDictionary<string, string>> SeedAsync(
        BrowserSuiteFixtureContext context,
        CancellationToken cancellationToken)
    {
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

        var plan = await context.PostJsonAsync(
            RecorderActor,
            CompanyGdc,
            $"{ManufacturingPath}/plans",
            new
            {
                planId = PlanId,
                workCenterId,
                inputs = new[] { new { parentRollId = ParentRollId, plannedKg = "1000" } },
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
