using System.Text.Json;
using GoymenERP.Api.Shell;
using GoymenERP.Modules.Sales.Infrastructure.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.BrowserSuiteFixtureTool.Suites.S04;

/// <summary>
/// The `s04` seed (SİPARİŞ, SALES-A1 user-flow acceptance): ONE Preparing sales order draft with two lines
/// in GDC, written through the gated Sales fixture writer (W1) — the positive create path is still blocked
/// (Product activation, GDC data, Cari read), so the draft's owner snapshots are synthetic and no Product,
/// GDC or Cari data is read or written. Every approval transition (submit / approve / reject) happens only
/// through the real API + UI in the browser suite, never here.
/// <para>
/// Decision C: the writer writes no DraftAttempt and no AuditEvent, and the seed makes no audited call (the
/// read-backs are not audited), so the SUITE_FIXTURE line's <c>audit_delta=0 command_delta=0</c> is that
/// evidence on the real SQL. The draft id and every synthetic id are fixed, so a re-run is a no-op.
/// </para>
/// </summary>
public sealed class S04BrowserSeed : IBrowserSuiteFixture
{
    public const string PreparerActor = "s04-preparer";
    public const string ApproverActor = "s04-approver";
    public const string ReaderActor = "s04-reader";

    /// <summary>The GDC company of the built-in organization seed (the context company of the web profile).</summary>
    public static readonly Guid CompanyGdc = Guid.Parse("10000000-0000-0000-0000-000000000001");

    public static readonly Guid DraftId = Guid.Parse("d1000000-0000-4000-8000-000000000401");
    public const string DocumentNo = "SENTETIK browser-suite:s04 SO-0001";

    // Synthetic owner identities: nothing in Cari, Settings or Product answers them; the draft stores snapshots.
    private static readonly Guid PartyId = Guid.Parse("d2000000-0000-4000-8000-000000000401");
    private static readonly Guid CompanyAccountId = Guid.Parse("d3000000-0000-4000-8000-000000000401");
    private static readonly Guid CurrencyId = Guid.Parse("d4000000-0000-4000-8000-000000000401");
    private static readonly Guid MoneyProfileId = Guid.Parse("d5000000-0000-4000-8000-000000000401");
    private static readonly Guid KgUnitId = Guid.Parse("d6000000-0000-4000-8000-000000000401");
    private static readonly Guid KgProfileId = Guid.Parse("d6100000-0000-4000-8000-000000000401");
    private static readonly Guid PieceUnitId = Guid.Parse("d7000000-0000-4000-8000-000000000401");
    private static readonly Guid PieceProfileId = Guid.Parse("d7100000-0000-4000-8000-000000000401");
    private static readonly Guid CoilProductId = Guid.Parse("d8000000-0000-4000-8000-000000000401");
    private static readonly Guid SheetProductId = Guid.Parse("d8000000-0000-4000-8000-000000000402");
    private static readonly Guid CoilLineId = Guid.Parse("d9000000-0000-4000-8000-000000000401");
    private static readonly Guid SheetLineId = Guid.Parse("d9000000-0000-4000-8000-000000000402");

    private static readonly DateOnly BusinessDate = new(2026, 9, 18);
    private static readonly DateOnly EffectiveFrom = new(2026, 9, 1);
    private static readonly DateTimeOffset At = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    private const string DraftsPath = "/api/v1/sales/drafts";
    private const string ApproveControl = "sales.orders.approve";

    public string Suite => "s04";

    public async Task<IReadOnlyDictionary<string, string>> SeedAsync(
        BrowserSuiteFixtureContext context,
        CancellationToken cancellationToken)
    {
        await using (var scope = context.Services.CreateAsyncScope())
        {
            var outcome = await scope.ServiceProvider.GetRequiredService<C01E2eSalesDraftFixtureWriter>().EnsureAsync(
                new SalesDraftFixtureRequest(
                    DraftId,
                    CompanyGdc,
                    DocumentNo,
                    context.Label(string.Empty).TrimEnd(),
                    BusinessDate,
                    new SalesDraftFixtureOwners(PartyId, CompanyAccountId, CurrencyId, MoneyProfileId, EffectiveFrom),
                    [
                        new SalesDraftFixtureLine(CoilLineId, CoilProductId, "S04-COIL", context.Label("rulo 1,25 × 1240"),
                            KgUnitId, KgProfileId, "kg", "weight", IsKilogram: true, Scale: 3, "1.250", 1.250m),
                        new SalesDraftFixtureLine(SheetLineId, SheetProductId, "S04-SHEET", context.Label("levha 2 × 1000 × 2000"),
                            PieceUnitId, PieceProfileId, "adet", "count", IsKilogram: false, Scale: 0, "3", 3m),
                    ],
                    context.Label("seed"),
                    context.IdempotencyKey("draft"),
                    At),
                cancellationToken);
            if (outcome.DraftId != DraftId)
            {
                throw new InvalidOperationException("The Sales fixture writer answered another draft id.");
            }
        }

        // Read back through the real host, as each actor: the fixture is only done when the product actually
        // answers with it — the preparer's detail, the reader's list, and the approve control published to the
        // approver only. A grant or a mount that does not resolve fails the run here, not in the browser.
        var detail = await context.GetJsonAsync(PreparerActor, CompanyGdc, $"{DraftsPath}/{DraftId:D}", cancellationToken);
        var draft = detail.GetProperty("draft");
        if (draft.GetProperty("id").GetGuid() != DraftId
            || draft.GetProperty("approvalState").GetString() != "Preparing"
            || draft.GetProperty("lineCount").GetInt32() != 2
            || detail.GetProperty("lines").GetArrayLength() != 2)
        {
            throw new InvalidOperationException("The seeded draft is not a Preparing draft with two lines on the real detail route.");
        }

        var list = await context.GetJsonAsync(ReaderActor, CompanyGdc, $"{DraftsPath}?approvalState=Preparing", cancellationToken);
        if (!list.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("id").GetGuid() == DraftId))
        {
            throw new InvalidOperationException("The reader does not see the seeded draft in the real list.");
        }

        var approver = await context.GetJsonAsync(ApproverActor, CompanyGdc, "/api/v1/shell/capabilities", cancellationToken);
        if (!Capabilities(approver).Contains(ApproveControl, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The approve control is not published to the approver.");
        }

        var reader = await context.GetJsonAsync(ReaderActor, CompanyGdc, "/api/v1/shell/capabilities", cancellationToken);
        if (Capabilities(reader).Contains(ApproveControl, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The approve control must not be published to the reader.");
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["preparerActor"] = PreparerActor,
            ["approverActor"] = ApproverActor,
            ["readerActor"] = ReaderActor,
            ["companyId"] = CompanyGdc.ToString("D"),
            ["draftId"] = DraftId.ToString("D"),
        };
    }

    private static string[] Capabilities(JsonElement snapshot) =>
        [.. snapshot.GetProperty("capabilities").EnumerateArray().Select(capability => capability.GetString()!)];
}
