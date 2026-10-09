using System.Globalization;
using GoymenERP.Api.Shell;
using GoymenERP.BrowserSuiteFixtureTool.Shared;
using GoymenERP.Modules.Accounting.Infrastructure.Fixtures;
using GoymenERP.Modules.Sales.Infrastructure.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.BrowserSuiteFixtureTool.Suites.FIN01;

/// <summary>
/// The `fin01` seed (FİNANS, card FIN-UI-1 §9): the prerequisites the Finance screens cannot create.
/// A synthetic party and its company account in GDC are opened through the real counterparty endpoint
/// (CARİ's shared seed), and Accounting's fixed synthetic Open period is installed through its owner
/// fixture port behind the existing complete runner gate. No raw SQL, no Finance row and no production grant: the
/// bank account, draft, definitive receipt and reversal are created by the browser suite through screens.
/// The same disposable gate writes one fixed Sales draft and submits it through the real Sales command so
/// Work acceptance has a real PendingApproval owner record before Chromium starts.
/// <para>
/// The seed then reads the F-04 master list through the real host as both command actors, and the
/// allocation list as the checker and read-only actor, so the run
/// fails here — not in the browser — if their grants or the Finance routes are missing. Those reads
/// are not audited; the Cari create is (one audited business command, through the API).
/// </para>
/// </summary>
public sealed class Fin01BrowserSeed : IBrowserSuiteFixture
{
    public const string CariActor = "group-bootstrap-admin";
    public const string ProposerActor = "fin01-proposer";
    public const string ApproverActor = "fin01-approver";
    public const string ReaderActor = "fin01-reader";
    public const string SalesPreparerActor = "fin01-sales-preparer";
    public const string SalesApproverActor = "fin01-sales-approver";
    public const string BothApproverActor = "fin01-both-approver";
    public const string NeitherActor = "company-a-user";

    /// <summary>The GDC company of the built-in organization seed, whose approved TRY money profile Settings answers.</summary>
    public static readonly Guid CompanyGdc = Guid.Parse("10000000-0000-0000-0000-000000000001");

    /// <summary>GSP, where the checker has only the existing official-invoice approval permission.</summary>
    public static readonly Guid CompanyGsp = Guid.Parse("10000000-0000-0000-0000-000000000002");

    private const string BankAccountMasterPath = "/api/v1/finance/bank-accounts/master";
    private const string AccountingPeriodsPath = "/api/v1/accounting/periods";
    private const string AllocationsPath = "/api/v1/finance/allocations";
    private const string PendingOfficialInvoiceBatchesPath = "/api/v1/finance/official-invoice-import-batches/pending";
    private const string SalesDraftsPath = "/api/v1/sales/drafts";
    private const string WorkApprovalsPath = "/api/v1/work/approvals";
    private const string SalesApprovalKind = "sales.order-draft.v1";
    private const string FinanceApprovalCapability = "finance.official-invoice-import.approve";
    private const string SalesApprovalCapability = "sales.orders.approve";
    private const int CustomerRole = 1;

    private static readonly Guid SalesDraftId = Guid.Parse("b5000000-0000-4000-8000-00000000f101");
    private static readonly Guid SalesCurrencyId = Guid.Parse("b5300000-0000-4000-8000-00000000f101");
    private static readonly Guid SalesMoneyProfileId = Guid.Parse("b5400000-0000-4000-8000-00000000f101");
    private static readonly Guid SalesLineId = Guid.Parse("b5500000-0000-4000-8000-00000000f101");
    private static readonly Guid SalesProductId = Guid.Parse("b5600000-0000-4000-8000-00000000f101");
    private static readonly Guid SalesUnitId = Guid.Parse("b5700000-0000-4000-8000-00000000f101");
    private static readonly Guid SalesUnitProfileId = Guid.Parse("b5800000-0000-4000-8000-00000000f101");
    private static readonly DateOnly SalesBusinessDate = new(2026, 10, 9);
    private static readonly DateOnly SalesEffectiveFrom = new(2026, 10, 1);
    private static readonly DateTimeOffset SalesCreatedAt = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    public string Suite => "fin01";

    public async Task<IReadOnlyDictionary<string, string>> SeedAsync(
        BrowserSuiteFixtureContext context,
        CancellationToken cancellationToken)
    {
        await using (var scope = context.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<C01E2eAccountingOpenPeriodFixtureWriter>()
                .EnsureAsync(cancellationToken);
        }

        var periodPage = await context.GetJsonAsync(ProposerActor, CompanyGdc, AccountingPeriodsPath, cancellationToken);
        var periods = periodPage.GetProperty("items");
        if (periods.GetArrayLength() != 1
            || periods[0].GetProperty("periodId").GetGuid() != AccountingOpenPeriodFixture.PeriodId
            || periods[0].GetProperty("companyId").GetGuid() != CompanyGdc
            || periods[0].GetProperty("startsOn").GetString() != "2026-09-01"
            || periods[0].GetProperty("endsOn").GetString() != "2026-09-30"
            || periods[0].GetProperty("status").GetString() != "Open")
        {
            throw new InvalidOperationException("The real Accounting period list did not return the synthetic fin01 Open period.");
        }

        var customer = await CariBrowserSeed.OpenPartyWithAccountAsync(
            context, CariActor, CompanyGdc, "customer", "Tahsilat müşterisi", CustomerRole, cancellationToken);

        SalesDraftFixtureOutcome salesDraft;
        await using (var scope = context.Services.CreateAsyncScope())
        {
            salesDraft = await scope.ServiceProvider.GetRequiredService<C01E2eSalesDraftFixtureWriter>()
                .EnsureAsync(new SalesDraftFixtureRequest(
                    SalesDraftId,
                    CompanyGdc,
                    context.Label("WORK-SALES-0001"),
                    context.Label(string.Empty).TrimEnd(),
                    SalesBusinessDate,
                    new SalesDraftFixtureOwners(
                        customer.PartyId,
                        customer.AccountId,
                        SalesCurrencyId,
                        SalesMoneyProfileId,
                        SalesEffectiveFrom),
                    [
                        new SalesDraftFixtureLine(
                            SalesLineId,
                            SalesProductId,
                            "WORK-SALES-COIL",
                            context.Label("Work onayı için rulo"),
                            SalesUnitId,
                            SalesUnitProfileId,
                            "kg",
                            "weight",
                            IsKilogram: true,
                            Scale: 3,
                            "1.000",
                            1.000m),
                    ],
                    context.Label("sales preparer"),
                    context.IdempotencyKey("sales-draft"),
                    SalesCreatedAt), cancellationToken);
        }

        var salesDetail = await context.GetJsonAsync(
            SalesPreparerActor, CompanyGdc, $"{SalesDraftsPath}/{SalesDraftId:D}", cancellationToken);
        var salesDetailDraft = salesDetail.GetProperty("draft");
        if (salesDetailDraft.GetProperty("partyId").GetGuid() != customer.PartyId
            || salesDetailDraft.GetProperty("companyAccountId").GetGuid() != customer.AccountId)
        {
            throw new InvalidOperationException("The real Sales detail did not preserve the fixture's real Cari owner.");
        }

        var salesState = salesDetailDraft.GetProperty("approvalState").GetString();
        if (salesState == "Preparing")
        {
            await context.PostJsonAsync(
                SalesPreparerActor,
                CompanyGdc,
                $"{SalesDraftsPath}/{SalesDraftId:D}/submission",
                new { expectedVersion = salesDraft.Version.ToString(CultureInfo.InvariantCulture) },
                "sales-submission",
                cancellationToken);
        }
        else if (salesState != "PendingApproval")
        {
            throw new InvalidOperationException("The fixture Sales draft is neither Preparing nor PendingApproval.");
        }

        var pendingSales = await context.GetJsonAsync(
            SalesApproverActor,
            CompanyGdc,
            $"{WorkApprovalsPath}?kind={SalesApprovalKind}",
            cancellationToken);
        var salesItems = pendingSales.GetProperty("items");
        if (pendingSales.GetProperty("companyId").GetGuid() != CompanyGdc
            || salesItems.GetArrayLength() != 1
            || salesItems[0].GetProperty("approvalId").GetGuid() != SalesDraftId
            || salesItems[0].GetProperty("kind").GetString() != SalesApprovalKind
            || salesItems[0].GetProperty("status").GetString() != "PendingApproval")
        {
            throw new InvalidOperationException("The real Work Sales read did not return the fixture draft as one pending approval.");
        }

        foreach (var (actor, finance, sales) in new[]
        {
            (ApproverActor, true, false),
            (SalesApproverActor, false, true),
            (BothApproverActor, true, true),
        })
        {
            var capabilities = await context.GetJsonAsync(actor, CompanyGdc, "/api/v1/shell/capabilities", cancellationToken);
            var names = capabilities.GetProperty("capabilities").EnumerateArray()
                .Select(capability => capability.GetString())
                .Where(capability => capability is not null)
                .ToHashSet(StringComparer.Ordinal);
            if (names.Contains(FinanceApprovalCapability) != finance
                || names.Contains(SalesApprovalCapability) != sales)
            {
                throw new InvalidOperationException($"The shell did not publish the exact Work approval capabilities for {actor}.");
            }
        }

        // Both actors reach the real F-04 read with their own grants; a fresh run has no account yet.
        foreach (var actor in new[] { ProposerActor, ApproverActor })
        {
            var master = await context.GetJsonAsync(actor, CompanyGdc, BankAccountMasterPath, cancellationToken);
            if (master.GetProperty("companyId").GetGuid() != CompanyGdc)
            {
                throw new InvalidOperationException($"The bank-account master answered {actor} for another company.");
            }
        }

        foreach (var actor in new[] { ApproverActor, ReaderActor })
        {
            var allocations = await context.GetJsonAsync(actor, CompanyGdc, AllocationsPath, cancellationToken);
            if (allocations.GetProperty("companyId").GetGuid() != CompanyGdc)
            {
                throw new InvalidOperationException($"The allocation list answered {actor} for another company.");
            }
        }

        var otherCompanyPending = await context.GetJsonAsync(
            ApproverActor, CompanyGsp, PendingOfficialInvoiceBatchesPath, cancellationToken);
        if (otherCompanyPending.GetProperty("companyId").GetGuid() != CompanyGsp
            || otherCompanyPending.GetProperty("items").GetArrayLength() != 0)
        {
            throw new InvalidOperationException(
                "The official-invoice approval queue did not remain empty and company-scoped in GSP.");
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["companyId"] = CompanyGdc.ToString("D"),
            ["otherCompanyId"] = CompanyGsp.ToString("D"),
            ["partyId"] = customer.PartyId.ToString("D"),
            ["companyAccountId"] = customer.AccountId.ToString("D"),
            ["proposerActor"] = ProposerActor,
            ["approverActor"] = ApproverActor,
            ["readerActor"] = ReaderActor,
            ["salesPreparerActor"] = SalesPreparerActor,
            ["financeOnlyActor"] = ApproverActor,
            ["salesOnlyActor"] = SalesApproverActor,
            ["bothActor"] = BothApproverActor,
            ["neitherActor"] = NeitherActor,
            ["salesDraftId"] = SalesDraftId.ToString("D"),
        };
    }
}
