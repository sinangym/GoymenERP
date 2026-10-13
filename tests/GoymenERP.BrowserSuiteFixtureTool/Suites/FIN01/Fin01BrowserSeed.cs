using System.Globalization;
using System.Text;
using GoymenERP.Api.Shell;
using GoymenERP.BrowserSuiteFixtureTool.Shared;
using GoymenERP.Modules.Accounting.Infrastructure.Fixtures;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.BrowserSuiteFixtureTool.Suites.FIN01;

/// <summary>
/// The `fin01` seed (FİNANS, card FIN-UI-1 §9): the prerequisites the Finance screens cannot create.
/// A synthetic party and its company account in GDC are opened through the real counterparty endpoint
/// (CARİ's shared seed), and Accounting's fixed synthetic Open period is installed through its owner
/// fixture port behind the existing complete runner gate. No raw SQL or production grant: the
/// bank account, draft, definitive receipt and reversal are created by the browser suite through screens.
/// The same disposable gate writes one fixed Sales draft and submits it through the real Sales command so
/// Work acceptance has a real PendingApproval owner record before Chromium starts.
/// It also uploads 51 separate pending Finance carriers through Finance's existing real endpoint, without
/// approving or applying them, to exercise Work's real continuation and owner-detail reads.
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
    public const string OpenItemMetadataActor = "fin01-open-items-metadata";
    public const string OpenItemVisibleActor = "fin01-open-items-visible";
    public const string OpenItemAmountOnlyActor = "fin01-open-items-amount-only";
    public const string NeitherActor = "company-a-user";

    /// <summary>The GDC company of the built-in organization seed, whose approved TRY money profile Settings answers.</summary>
    public static readonly Guid CompanyGdc = Guid.Parse("10000000-0000-0000-0000-000000000001");

    /// <summary>GSP, where the checker has only the existing official-invoice approval permission.</summary>
    public static readonly Guid CompanyGsp = Guid.Parse("10000000-0000-0000-0000-000000000002");

    private const string BankAccountMasterPath = "/api/v1/finance/bank-accounts/master";
    private const string AccountingPeriodsPath = "/api/v1/accounting/periods";
    private const string AllocationsPath = "/api/v1/finance/allocations";
    private const string PendingOfficialInvoiceBatchesPath = "/api/v1/finance/official-invoice-import-batches/pending";
    private const string OfficialInvoiceBatchesPath = "/api/v1/finance/official-invoice-import-batches";
    private const string OfficialInvoiceImportsPath = "/api/v1/finance/official-invoice-imports";
    private const string OpenItemsPath = "/api/v1/finance/open-items";
    private const string CurrencyCandidatesPath = "/api/v1/finance/bank-accounts/currency-candidates";
    private const string SalesDraftsPath = "/api/v1/sales/drafts";
    private const string WorkApprovalsPath = "/api/v1/work/approvals";
    private const string SalesApprovalKind = "sales.order-draft.v1";
    private const string FinanceApprovalKind = "finance.official-invoice-import.v1";
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
    private static readonly Guid OpenItemBatchId = Guid.Parse("b6400000-0000-4000-8000-00000000f101");

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

        var financeBatchId = await SeedPendingFinanceAsync(
            context, customer.PartyId, customer.AccountId, cancellationToken);

        var openItemCustomer = await CariBrowserSeed.OpenPartyWithAccountAsync(
            context, CariActor, CompanyGdc, "fi04-open-item-customer", "Açık kalem müşterisi", CustomerRole,
            cancellationToken);
        var openItems = await SeedOpenItemsAsync(
            context, openItemCustomer.PartyId, openItemCustomer.AccountId, cancellationToken);

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
            ["financeBatchId"] = financeBatchId.ToString("D"),
            ["openItemBatchId"] = OpenItemBatchId.ToString("D"),
            ["openItemPartyId"] = openItemCustomer.PartyId.ToString("D"),
            ["openItemCompanyAccountId"] = openItemCustomer.AccountId.ToString("D"),
            ["openItemMetadataActor"] = OpenItemMetadataActor,
            ["openItemVisibleActor"] = OpenItemVisibleActor,
            ["openItemAmountOnlyActor"] = OpenItemAmountOnlyActor,
            ["openItemNeitherActor"] = NeitherActor,
            ["openItemNewestId"] = openItems.NewestId.ToString("D"),
            ["openItemContinuationId"] = openItems.ContinuationId.ToString("D"),
            ["openItemEqualTimestampLowerId"] = openItems.EqualTimestampLowerId.ToString("D"),
            ["openItemCount"] = openItems.Count.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static async Task<OpenItemFixture> SeedOpenItemsAsync(
        BrowserSuiteFixtureContext context,
        Guid partyId,
        Guid companyAccountId,
        CancellationToken cancellationToken)
    {
        var candidates = await context.GetJsonAsync(
            ProposerActor, CompanyGdc, CurrencyCandidatesPath, cancellationToken);
        var tryCandidates = candidates.GetProperty("items").EnumerateArray()
            .Where(candidate => candidate.GetProperty("code").GetString() == "TRY")
            .ToArray();
        if (candidates.GetProperty("companyId").GetGuid() != CompanyGdc || tryCandidates.Length != 1)
        {
            throw new InvalidOperationException("The real Finance currency candidates did not return one GDC TRY currency for FI04.");
        }
        var currencyId = tryCandidates[0].GetProperty("currencyId").GetGuid();

        const int itemCount = 51;
        const string batchVersion = "SENTETIK-browser-suite:fin01/FI04-OPEN-ITEMS-0001";
        var csv = new StringBuilder(FinanceOfficialInvoiceImportTemplateV1.Header).Append('\n');
        var ids = new Guid[itemCount];
        for (var index = 1; index <= itemCount; index++)
        {
            var suffix = index.ToString("x12", CultureInfo.InvariantCulture);
            var openItemId = Guid.Parse($"b6500000-0000-4000-8000-{suffix}");
            ids[index - 1] = openItemId;
            csv.AppendJoin(',',
                openItemId.ToString("D"),
                Guid.Parse($"b6600000-0000-4000-8000-{suffix}").ToString("D"),
                Guid.Parse($"b6700000-0000-4000-8000-{suffix}").ToString("D"),
                $"SENTETIK-fi04-invoice-v{index:D4}",
                partyId.ToString("D"),
                companyAccountId.ToString("D"),
                currencyId.ToString("D"),
                (1_000m + index / 100m).ToString("0.00", CultureInfo.InvariantCulture),
                new DateOnly(2026, 11, 30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            csv.Append('\n');
        }
        var utf8Csv = Convert.ToBase64String(Encoding.UTF8.GetBytes(csv.ToString()));

        var uploaded = await context.PostJsonAsync(
            ProposerActor,
            CompanyGdc,
            OfficialInvoiceBatchesPath,
            new { batchId = OpenItemBatchId, batchVersion, utf8Csv },
            "fi04-open-items-upload",
            cancellationToken);
        var uploadedResource = uploaded.GetProperty("resource");
        if (uploaded.GetProperty("outcome").GetString() != "Succeeded"
            || uploadedResource.GetProperty("batchId").GetGuid() != OpenItemBatchId
            || uploadedResource.GetProperty("status").GetString() != "Pending"
            || uploadedResource.GetProperty("rowCount").GetInt32() != itemCount)
        {
            throw new InvalidOperationException("The real Finance upload did not create the FI04 source carrier.");
        }

        var approved = await context.PostJsonAsync(
            ApproverActor,
            CompanyGdc,
            $"{OfficialInvoiceBatchesPath}/{OpenItemBatchId:D}/approval",
            new { batchId = OpenItemBatchId, expectedVersion = uploadedResource.GetProperty("version").GetString() },
            "fi04-open-items-approve",
            cancellationToken);
        var approvedResource = approved.GetProperty("resource");
        if (approved.GetProperty("outcome").GetString() != "Succeeded"
            || approvedResource.GetProperty("batchId").GetGuid() != OpenItemBatchId
            || approvedResource.GetProperty("status").GetString() != "Approved")
        {
            throw new InvalidOperationException("The real Finance checker did not approve the FI04 source carrier.");
        }

        var applied = await context.PostJsonAsync(
            ApproverActor,
            CompanyGdc,
            $"{OfficialInvoiceImportsPath}/apply",
            new { batchId = OpenItemBatchId, batchVersion, utf8Csv },
            "fi04-open-items-apply",
            cancellationToken);
        if (!applied.GetProperty("preview").GetProperty("isValid").GetBoolean()
            || applied.GetProperty("approvedSource").GetProperty("outcome").GetString() != "Found"
            || applied.GetProperty("admission").GetProperty("outcome").GetString() != "Succeeded"
            || applied.GetProperty("freshRead").GetProperty("resource").GetProperty("openItems").GetArrayLength() != itemCount)
        {
            throw new InvalidOperationException("The approved FI04 source was not admitted and reopened through Finance.");
        }

        var metadata = await context.GetJsonAsync(OpenItemMetadataActor, CompanyGdc, OpenItemsPath, cancellationToken);
        var metadataItems = metadata.GetProperty("items");
        var cursor = metadata.GetProperty("nextCursor").GetString();
        if (metadata.GetProperty("companyId").GetGuid() != CompanyGdc
            || metadataItems.GetArrayLength() != 50
            || string.IsNullOrWhiteSpace(cursor)
            || metadataItems[0].GetProperty("id").GetGuid() != ids[^1])
        {
            throw new InvalidOperationException("The FI04 metadata page did not preserve the expected page and ordering.");
        }
        foreach (var item in metadataItems.EnumerateArray())
        {
            if (item.GetProperty("status").ValueKind != System.Text.Json.JsonValueKind.Null
                || item.GetProperty("initialAmount").GetProperty("disclosure").GetString() != "Redacted"
                || item.GetProperty("initialAmount").GetProperty("money").ValueKind != System.Text.Json.JsonValueKind.Null
                || item.GetProperty("outstandingAmount").GetProperty("disclosure").GetString() != "Redacted"
                || item.GetProperty("outstandingAmount").GetProperty("money").ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                throw new InvalidOperationException("The FI04 metadata-only fixture disclosed a status or amount.");
            }
        }

        var continuation = await context.GetJsonAsync(
            OpenItemMetadataActor,
            CompanyGdc,
            $"{OpenItemsPath}?cursor={Uri.EscapeDataString(cursor)}",
            cancellationToken);
        var continuationItems = continuation.GetProperty("items");
        if (continuationItems.GetArrayLength() != 1
            || continuationItems[0].GetProperty("id").GetGuid() != ids[0]
            || continuation.GetProperty("nextCursor").ValueKind != System.Text.Json.JsonValueKind.Null)
        {
            throw new InvalidOperationException("The FI04 continuation did not return the first-page-outside item exactly once.");
        }

        var visible = await context.GetJsonAsync(OpenItemVisibleActor, CompanyGdc, OpenItemsPath, cancellationToken);
        var visibleFirst = visible.GetProperty("items")[0];
        if (visibleFirst.GetProperty("id").GetGuid() != ids[^1]
            || visibleFirst.GetProperty("status").GetString() != "Open"
            || visibleFirst.GetProperty("initialAmount").GetProperty("disclosure").GetString() != "Visible"
            || visibleFirst.GetProperty("initialAmount").GetProperty("money").ValueKind != System.Text.Json.JsonValueKind.Object
            || visibleFirst.GetProperty("outstandingAmount").GetProperty("disclosure").GetString() != "Visible"
            || visibleFirst.GetProperty("outstandingAmount").GetProperty("money").ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            throw new InvalidOperationException("The FI04 amount-authorized fixture did not disclose the stored status and amounts.");
        }

        var createdAt = metadataItems[0].GetProperty("createdAtUtc").GetDateTimeOffset();
        if (metadataItems[1].GetProperty("createdAtUtc").GetDateTimeOffset() != createdAt
            || metadataItems[1].GetProperty("id").GetGuid() != ids[^2])
        {
            throw new InvalidOperationException("The FI04 equal-timestamp ordering is not deterministic by descending id.");
        }

        return new(ids[^1], ids[0], ids[^2], itemCount);
    }

    private static async Task<Guid> SeedPendingFinanceAsync(
        BrowserSuiteFixtureContext context,
        Guid partyId,
        Guid companyAccountId,
        CancellationToken cancellationToken)
    {
        // Use the real Settings-backed candidate, not the independent Sales fixture's currency snapshot.
        var candidates = await context.GetJsonAsync(
            ProposerActor, CompanyGdc, CurrencyCandidatesPath, cancellationToken);
        var tryCandidates = candidates.GetProperty("items").EnumerateArray()
            .Where(candidate => candidate.GetProperty("code").GetString() == "TRY")
            .ToArray();
        if (candidates.GetProperty("companyId").GetGuid() != CompanyGdc || tryCandidates.Length != 1)
        {
            throw new InvalidOperationException("The real Finance currency candidates did not return one GDC TRY currency.");
        }
        var currencyId = tryCandidates[0].GetProperty("currencyId").GetGuid();

        // Finance's existing page size is 50. The 51st pending owner record gives a real next cursor;
        // no approval, application or open-item/monetary movement is produced by this seed.
        const int pendingCount = 51;
        const int expectedFirstPageCount = 50;
        var batchIds = new HashSet<Guid>();
        for (var index = 1; index <= pendingCount; index++)
        {
            var suffix = index.ToString("x12", CultureInfo.InvariantCulture);
            var batchId = Guid.Parse($"b6000000-0000-4000-8000-{suffix}");
            var openItemId = Guid.Parse($"b6100000-0000-4000-8000-{suffix}");
            var documentId = Guid.Parse($"b6200000-0000-4000-8000-{suffix}");
            var installmentId = Guid.Parse($"b6300000-0000-4000-8000-{suffix}");
            var csv = FinanceOfficialInvoiceImportTemplateV1.Header + "\n"
                + string.Join(",", openItemId.ToString("D"), documentId.ToString("D"),
                    installmentId.ToString("D"), "SENTETIK-work-invoice-v1", partyId.ToString("D"),
                    companyAccountId.ToString("D"), currencyId.ToString("D"), "10.25", "2026-10-31");
            var result = await context.PostJsonAsync(
                ProposerActor, CompanyGdc, OfficialInvoiceBatchesPath,
                new
                {
                    batchId,
                    // Finance treats batchVersion as a machine token: no whitespace or display-label text.
                    // Keep the synthetic provenance while satisfying the owner's accepted token grammar.
                    batchVersion = $"SENTETIK-browser-suite:{context.Suite}/WORK-FINANCE-{index:D4}",
                    utf8Csv = Convert.ToBase64String(Encoding.UTF8.GetBytes(csv)),
                },
                $"work-finance-upload-{index}", cancellationToken);
            var resource = result.GetProperty("resource");
            if (result.GetProperty("outcome").GetString() != "Succeeded"
                || resource.GetProperty("batchId").GetGuid() != batchId
                || resource.GetProperty("companyId").GetGuid() != CompanyGdc
                || resource.GetProperty("status").GetString() != "Pending"
                || resource.GetProperty("approvedBy").ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                throw new InvalidOperationException("The real Finance upload did not leave the Work fixture batch pending.");
            }
            batchIds.Add(batchId);
        }

        Guid? newestBatchId = null;
        foreach (var actor in new[] { ApproverActor, BothApproverActor })
        {
            // Omitted kind intentionally proves the preserved Finance-only default contract.
            var first = await context.GetJsonAsync(actor, CompanyGdc, WorkApprovalsPath, cancellationToken);
            var firstItems = first.GetProperty("items");
            var cursor = first.GetProperty("nextCursor").GetString();
            if (first.GetProperty("companyId").GetGuid() != CompanyGdc
                || firstItems.GetArrayLength() != expectedFirstPageCount || string.IsNullOrEmpty(cursor))
            {
                throw new InvalidOperationException("The real Work Finance page did not expose 50 pending rows and a cursor.");
            }
            var second = await context.GetJsonAsync(actor, CompanyGdc,
                $"{WorkApprovalsPath}?cursor={Uri.EscapeDataString(cursor)}", cancellationToken);
            var secondItems = second.GetProperty("items");
            if (second.GetProperty("companyId").GetGuid() != CompanyGdc
                || secondItems.GetArrayLength() != pendingCount - expectedFirstPageCount
                || second.GetProperty("nextCursor").ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                throw new InvalidOperationException("The real Work Finance continuation did not end with the remaining row.");
            }
            var seen = new HashSet<Guid>();
            foreach (var item in firstItems.EnumerateArray().Concat(secondItems.EnumerateArray()))
            {
                var id = item.GetProperty("approvalId").GetGuid();
                if (!batchIds.Contains(id) || !seen.Add(id)
                    || item.GetProperty("companyId").GetGuid() != CompanyGdc
                    || item.GetProperty("kind").GetString() != FinanceApprovalKind
                    || item.GetProperty("status").GetString() != "Pending")
                {
                    throw new InvalidOperationException("The real Work Finance pages contained a duplicate, foreign or non-pending item.");
                }
            }
            var newest = firstItems[0].GetProperty("approvalId").GetGuid();
            if (newestBatchId is { } expected && newest != expected)
            {
                throw new InvalidOperationException("The Finance-only and combined Work reads did not agree on the newest batch.");
            }
            newestBatchId = newest;
            var detail = await context.GetJsonAsync(actor, CompanyGdc,
                $"{WorkApprovalsPath}/{FinanceApprovalKind}/{newest:D}", cancellationToken);
            var detailItem = detail.GetProperty("item");
            if (detailItem.GetProperty("approvalId").GetGuid() != newest
                || detailItem.GetProperty("companyId").GetGuid() != CompanyGdc
                || detailItem.GetProperty("kind").GetString() != FinanceApprovalKind
                || detailItem.GetProperty("status").GetString() != "Pending")
            {
                throw new InvalidOperationException("The real Work Finance detail did not preserve the pending owner identity.");
            }
        }
        return newestBatchId ?? throw new InvalidOperationException("The Work Finance fixture has no pending batch.");
    }

    private sealed record OpenItemFixture(
        Guid NewestId,
        Guid ContinuationId,
        Guid EqualTimestampLowerId,
        int Count);
}
