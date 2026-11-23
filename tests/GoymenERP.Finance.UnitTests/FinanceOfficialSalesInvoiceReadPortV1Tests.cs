using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// FIN-S6 (F-005 D3): Finance's read of one official sales invoice record by official number + ETTN, and the Sales API-35
/// adapter over it, on SYNTHETIC rows. Only an approved live record carries facts; a pending one is not yet the official
/// fact (Sales: NotFound); a rejected or withdrawn one is not live (NotFound). The key is the number and the ETTN together,
/// exact; another company is never answered; a row Finance cannot read back exactly is Unavailable, never a guess. The read
/// writes nothing and carries no authority.
/// </summary>
public sealed class FinanceOfficialSalesInvoiceReadPortV1Tests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherCompany = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Customer = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid Currency = Guid.Parse("88888888-8888-4888-8888-888888888888");
    private static readonly Guid Pdf = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private const string OfficialNo = "SNT2026000000001";
    private const string Ettn = "a5000000-0000-4000-8000-0000000000bc";

    [Fact]
    public async Task Approved_import_supplies_exact_source_date_and_TL_despite_a_pending_manual_record()
    {
        await using var run = await Run.CreateAsync(Row(Guid.NewGuid(), FinanceOfficialSalesInvoiceStatusV1.PendingApproval));
        var port = run.Port(Company);
        var document = await SeedImport(run.Db, "1500.55");
        var answer = await port.LookupAsync(Company, OfficialNo, Ettn);
        Assert.Equal(FinanceOfficialSalesInvoiceReadOutcomeV1.Approved, answer.Outcome);
        Assert.Equal(document, answer.Invoice!.InvoiceId);
        Assert.Equal("approved-import", answer.Invoice.SourceKind);
        Assert.Equal("1500.55", answer.Invoice.PayableFunctionalAmount);
        Assert.Equal(new DateOnly(2026, 11, 16), answer.Invoice.InvoiceDate);
        Assert.True(answer.Invoice.HasPendingManual);
        Assert.Null(answer.Invoice.PdfDocumentId);
        Assert.NotNull(answer.Invoice.SourceBatchId);
        Assert.Equal(64, answer.Invoice.SourceChecksum!.Length);
        var sales = await run.SalesPort(Company).LookupAsync(Company, OfficialNo, Ettn);
        Assert.Equal(SalesOfficialInvoiceOutcomeV1.Found, sales.Outcome);
        Assert.Equal(answer.Invoice.InvoiceDate, sales.Invoice!.InvoiceDate);
        Assert.Equal(answer.Invoice.SourceBatchId, sales.Invoice.SourceProof!.SourceBatchId);
        Assert.Equal(answer.Invoice.SourceChecksum, sales.Invoice.SourceProof.SourceChecksum);
        Assert.Equal(document, sales.Invoice.SourceProof.InvoiceId);
        Assert.True(sales.Invoice.SourceProof.HasPendingManual);
        Assert.Equal("approved-import", sales.Invoice.SourceProof.SourceKind);

    }

    [Fact]
    public async Task A_cancelled_import_is_never_answered_as_approved()
    {
        await using (var alone = await Run.CreateAsync())
        {
            var port = alone.Port(Company);
            await SeedImport(alone.Db, "1500.55", FinanceOpenItemStatuses.Cancelled);
            var answer = await port.LookupAsync(Company, OfficialNo, Ettn);
            Assert.Equal((FinanceOfficialSalesInvoiceReadOutcomeV1.NotFound, (FinanceOfficialSalesInvoiceFactsV1?)null),
                (answer.Outcome, answer.Invoice));
            Assert.Equal(SalesOfficialInvoiceOutcomeV1.NotFound,
                (await alone.SalesPort(Company).LookupAsync(Company, OfficialNo, Ettn)).Outcome);
        }

        // Beside a pending manual record the cancelled import supplies nothing: the manual record is still pending.
        await using var run = await Run.CreateAsync(Row(Guid.NewGuid(), FinanceOfficialSalesInvoiceStatusV1.PendingApproval));
        var pending = run.Port(Company);
        await SeedImport(run.Db, "1500.55", FinanceOpenItemStatuses.Cancelled);
        Assert.Equal(FinanceOfficialSalesInvoiceReadOutcomeV1.PendingApproval,
            (await pending.LookupAsync(Company, OfficialNo, Ettn)).Outcome);
        Assert.False(run.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Missing_import_TL_is_unavailable_and_approved_manual_wins_without_rewriting_the_import()
    {
        await using (var missing = await Run.CreateAsync())
        {
            var port = missing.Port(Company);
            await SeedImport(missing.Db, "");
            Assert.Equal(FinanceOfficialSalesInvoiceReadOutcomeV1.Unavailable,
                (await port.LookupAsync(Company, OfficialNo, Ettn)).Outcome);
        }
        var manual = Row(Guid.NewGuid()); manual.InvoiceDate = new(2026, 11, 15);
        await using var run = await Run.CreateAsync(manual);
        var winner = run.Port(Company);
        await SeedImport(run.Db, "1500.55");
        var answer = await winner.LookupAsync(Company, OfficialNo, Ettn);
        Assert.Equal(manual.Id, answer.Invoice!.InvoiceId);
        Assert.Equal("manual", answer.Invoice.SourceKind);
        Assert.Equal("1250.50", answer.Invoice.PayableFunctionalAmount);
        Assert.Equal(manual.InvoiceDate, answer.Invoice.InvoiceDate);
        Assert.Single(await run.Db.OfficialInvoiceImportBatches.ToListAsync());
        Assert.False(run.Db.ChangeTracker.HasChanges());
    }

    private static async Task<Guid> SeedImport(FinanceDbContext db, string tl, int openItemStatus = FinanceOpenItemStatuses.Open)
    {
        var batchId = Guid.NewGuid(); var documentId = Guid.NewGuid();
        var row = string.Join(',', Guid.NewGuid(), documentId, Guid.NewGuid(), "1", Customer,
            Guid.NewGuid(), Currency, "1250.50", "2026-12-01", OfficialNo, Ettn, "1042.08", "208.42", "0", "2026-11-16", tl);
        var bytes = Encoding.UTF8.GetBytes(FinanceOfficialSalesInvoiceImportTemplateV3.Header + "\n" + row + "\n");
        var parsed = FinanceOfficialInvoiceImportParserV1.Parse(new(Company, "SENTETIK-checker", "SENTETIK", null), new(batchId, "1", bytes));
        Assert.True(parsed.Preview.IsValid);
        db.OfficialInvoiceImportBatches.Add(new() { Id = batchId, CompanyId = Company, BatchVersion = "1",
            BatchChecksum = parsed.Preview.SourceSha256, TemplateVersion = parsed.Preview.TemplateVersion, FileBytes = bytes,
            SourceRowsJson = JsonSerializer.Serialize(parsed.Preview.Batch!.Rows, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            RowCount = 1, UploadedBy = "SENTETIK-maker", UploadedAtUtc = ApprovedAt.AddMinutes(-1), CorrelationId = "SENTETIK" });
        db.OfficialInvoiceImportApprovals.Add(new() { Id = Guid.NewGuid(), CompanyId = Company, BatchId = batchId,
            UploadedBy = "SENTETIK-maker", ApprovedBy = "SENTETIK-checker", ApprovedAtUtc = ApprovedAt, CorrelationId = "SENTETIK" });
        foreach (var (kind, value) in new[] { (FinanceOfficialInvoiceIdentityKinds.OfficialNo, OfficialNo), (FinanceOfficialInvoiceIdentityKinds.Ettn, Ettn) })
            db.OfficialInvoiceIdentityClaims.Add(new() { Id = Guid.NewGuid(), CompanyId = Company, IdentityKind = kind,
                IdentityValue = value, Generation = 1, HolderBatchId = batchId, HolderSourceDocumentId = documentId,
                ClaimedBy = "SENTETIK-checker", ClaimedAtUtc = ApprovedAt, CorrelationId = "SENTETIK" });
        // The admission's open item of the document; API-11 cancellation marks it Cancelled (outstanding zero).
        var source = parsed.Preview.Batch!.Rows[0];
        db.OpenItems.Add(new FinanceOpenItemRow
        {
            Direction = FinanceOpenItemDirectionCodes.Receivable, Id = source.OpenItemId, CompanyId = Company,
            SourceKind = FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment, SourceId = documentId,
            SourceLineId = source.SourceInstallmentId, SourceVersion = source.SourceVersion, SourceBatchId = batchId,
            SourceBatchVersion = "1", SourceBatchChecksum = parsed.Preview.SourceSha256, PartyId = source.PartyId,
            CompanyAccountId = source.CompanyAccountId, CurrencyId = source.CurrencyId, OriginalAmountText = source.Amount,
            OutstandingAmountText = openItemStatus == FinanceOpenItemStatuses.Cancelled ? "0" : source.Amount,
            DueOn = source.DueOn ?? new DateOnly(2026, 12, 1), Status = openItemStatus, Version = 1,
            CreatedBy = "SENTETIK-checker", CreatedAtUtc = ApprovedAt, UpdatedBy = "SENTETIK-checker", UpdatedAtUtc = ApprovedAt,
        });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        return documentId;
    }

    [Fact]
    public async Task An_approved_record_answers_its_stored_exact_facts_and_the_sales_adapter_its_decimals_without_writing()
    {
        var invoice = Guid.NewGuid();
        await using var run = await Run.CreateAsync(Row(invoice));

        var answer = await run.Port(Company).LookupAsync(Company, OfficialNo, Ettn);

        Assert.Equal(FinanceOfficialSalesInvoiceReadOutcomeV1.Approved, answer.Outcome);
        var facts = answer.Invoice!;
        Assert.Equal((invoice, Company, OfficialNo, Ettn, Customer, Currency), (facts.InvoiceId, facts.CompanyId,
            facts.OfficialNo, facts.Ettn, facts.CustomerPartyId, facts.CurrencyId));
        Assert.Equal(("1250.50", "1250.50", "1042.08", "208.42", "0"), (facts.PayableAmount,
            facts.PayableFunctionalAmount, facts.TaxBaseFunctionalAmount, facts.VatFunctionalAmount,
            facts.WithholdingFunctionalAmount));
        Assert.Equal((Pdf, 1, new string('d', 64), "2", ApprovedAt), (facts.PdfDocumentId, facts.PdfDocumentVersionNo,
            facts.PdfSha256, facts.Version, facts.ApprovedAtUtc));
        Assert.False(run.Db.ChangeTracker.HasChanges());

        var sales = await run.SalesPort(Company).LookupAsync(Company, OfficialNo, Ettn);
        Assert.Equal(SalesOfficialInvoiceOutcomeV1.Found, sales.Outcome);
        // The tax base is not part of the Sales facts (FNS-D21); the scale of the stored text is kept in the decimal.
        Assert.Equal(new SalesOfficialInvoiceFacts(OfficialNo, Ettn, Customer, Currency, 1250.50m, 1250.50m, 208.42m, 0m, facts.InvoiceDate,
            new(Company, invoice, "manual", "2", ApprovedAt, Pdf, 1, new string('d', 64), null, null, false)),
            sales.Invoice);
        Assert.Equal("1250.50", sales.Invoice!.PayableAmount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.False(run.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task A_pending_record_is_not_yet_the_official_fact_and_sales_reads_it_as_not_found()
    {
        await using var run = await Run.CreateAsync(Row(Guid.NewGuid(), FinanceOfficialSalesInvoiceStatusV1.PendingApproval));

        var answer = await run.Port(Company).LookupAsync(Company, OfficialNo, Ettn);
        Assert.Equal((FinanceOfficialSalesInvoiceReadOutcomeV1.PendingApproval, (FinanceOfficialSalesInvoiceFactsV1?)null),
            (answer.Outcome, answer.Invoice));

        var sales = await run.SalesPort(Company).LookupAsync(Company, OfficialNo, Ettn);
        Assert.Equal((SalesOfficialInvoiceOutcomeV1.NotFound, (SalesOfficialInvoiceFacts?)null), (sales.Outcome, sales.Invoice));
    }

    [Theory]
    [InlineData(FinanceOfficialSalesInvoiceStatusV1.Rejected)]
    [InlineData(FinanceOfficialSalesInvoiceStatusV1.Withdrawn)]
    public async Task A_rejected_or_withdrawn_record_is_not_live_and_a_later_live_record_on_the_same_key_is_answered(
        FinanceOfficialSalesInvoiceStatusV1 decided)
    {
        var old = Guid.NewGuid();
        await using (var run = await Run.CreateAsync(Row(old, decided)))
        {
            Assert.Equal(FinanceOfficialSalesInvoiceReadOutcomeV1.NotFound,
                (await run.Port(Company).LookupAsync(Company, OfficialNo, Ettn)).Outcome);
            Assert.Equal(SalesOfficialInvoiceOutcomeV1.NotFound,
                (await run.SalesPort(Company).LookupAsync(Company, OfficialNo, Ettn)).Outcome);
        }

        // FNS-D39 frees the number and the ETTN: the new live record is the one answered, never the decided one.
        var live = Guid.NewGuid();
        await using (var run = await Run.CreateAsync(Row(old, decided), Row(live)))
        {
            var answer = await run.Port(Company).LookupAsync(Company, OfficialNo, Ettn);
            Assert.Equal((FinanceOfficialSalesInvoiceReadOutcomeV1.Approved, live), (answer.Outcome, answer.Invoice!.InvoiceId));
        }
    }

    [Fact]
    public async Task The_key_is_the_number_and_the_ettn_together_compared_exactly_with_the_ettn_in_its_stored_form()
    {
        await using var run = await Run.CreateAsync(
            Row(Guid.NewGuid()),
            Row(Guid.NewGuid(), officialNo: "SNT2026000000002", ettn: "b6000000-0000-4000-8000-0000000000cd"));
        var port = run.Port(Company);

        // An upper-case ETTN names the same record (the record stores the lowercase "D" text).
        var upper = await port.LookupAsync(Company, OfficialNo, Ettn.ToUpperInvariant());
        Assert.Equal((FinanceOfficialSalesInvoiceReadOutcomeV1.Approved, Ettn), (upper.Outcome, upper.Invoice!.Ettn));

        // One record's number with the other record's ETTN is neither record.
        Assert.Equal(FinanceOfficialSalesInvoiceReadOutcomeV1.NotFound,
            (await port.LookupAsync(Company, OfficialNo, "b6000000-0000-4000-8000-0000000000cd")).Outcome);
        // The number is exact (SAT-D89): a case variant is not the record.
        Assert.Equal(FinanceOfficialSalesInvoiceReadOutcomeV1.NotFound,
            (await port.LookupAsync(Company, OfficialNo.ToLowerInvariant(), Ettn)).Outcome);
    }

    [Theory]
    [InlineData("", Ettn)]
    [InlineData("   ", Ettn)]
    [InlineData(" SNT2026000000001", Ettn)]
    [InlineData("SNT2026000000001 ", Ettn)]
    [InlineData(OfficialNo, "")]
    [InlineData(OfficialNo, "not-a-guid")]
    [InlineData(OfficialNo, "a5000000000040008000000000bc")]
    [InlineData(OfficialNo, "{a5000000-0000-4000-8000-0000000000bc}")]
    [InlineData(OfficialNo, "00000000-0000-0000-0000-000000000000")]
    public async Task A_number_or_ettn_the_record_could_never_have_stored_names_no_record(string officialNo, string ettn)
    {
        await using var run = await Run.CreateAsync(Row(Guid.NewGuid()));

        Assert.Equal(FinanceOfficialSalesInvoiceReadOutcomeV1.NotFound,
            (await run.Port(Company).LookupAsync(Company, officialNo, ettn)).Outcome);
        Assert.Equal(SalesOfficialInvoiceOutcomeV1.NotFound,
            (await run.SalesPort(Company).LookupAsync(Company, officialNo, ettn)).Outcome);
    }

    [Fact]
    public async Task An_official_number_longer_than_the_column_names_no_record()
    {
        await using var run = await Run.CreateAsync(Row(Guid.NewGuid()));
        var tooLong = new string('S', FinanceDbContext.OfficialSalesInvoiceMaximumOfficialNoLength + 1);

        Assert.Equal(FinanceOfficialSalesInvoiceReadOutcomeV1.NotFound,
            (await run.Port(Company).LookupAsync(Company, tooLong, Ettn)).Outcome);
    }

    [Fact]
    public async Task Another_company_is_never_answered_whether_named_or_holding_the_same_key()
    {
        await using var run = await Run.CreateAsync(Row(Guid.NewGuid(), company: OtherCompany));

        // The other company's record under this company's context: not this company's record.
        Assert.Equal(FinanceOfficialSalesInvoiceReadOutcomeV1.NotFound,
            (await run.Port(Company).LookupAsync(Company, OfficialNo, Ettn)).Outcome);
        // Naming the other company while this company is resolved: never answered.
        Assert.Equal(FinanceOfficialSalesInvoiceReadOutcomeV1.Unavailable,
            (await run.Port(Company).LookupAsync(OtherCompany, OfficialNo, Ettn)).Outcome);
        Assert.Equal(SalesOfficialInvoiceOutcomeV1.Unavailable,
            (await run.SalesPort(Company).LookupAsync(OtherCompany, OfficialNo, Ettn)).Outcome);
        Assert.Equal(FinanceOfficialSalesInvoiceReadOutcomeV1.Unavailable,
            (await run.Port(Company).LookupAsync(Guid.Empty, OfficialNo, Ettn)).Outcome);
        // No resolved company at all.
        Assert.Equal(FinanceOfficialSalesInvoiceReadOutcomeV1.Unavailable,
            (await run.UnresolvedPort().LookupAsync(Company, OfficialNo, Ettn)).Outcome);
    }

    [Theory]
    [InlineData("payable-text")]
    [InlineData("payable-zero")]
    [InlineData("payable-not-normalised")]
    [InlineData("payable-functional-negative")]
    [InlineData("tax-base-empty")]
    [InlineData("vat-text")]
    [InlineData("withholding-negative")]
    [InlineData("approval-time-missing")]
    [InlineData("customer-empty")]
    [InlineData("currency-empty")]
    [InlineData("pdf-version-zero")]
    [InlineData("pdf-sha-empty")]
    public async Task An_approved_row_finance_cannot_read_back_exactly_is_unavailable_never_a_guess(string variant)
    {
        var row = Row(Guid.NewGuid());
        switch (variant)
        {
            case "payable-text": row.PayableAmountText = "bin"; break;
            case "payable-zero": row.PayableAmountText = "0"; break;
            case "payable-not-normalised": row.PayableAmountText = "01250.50"; break;
            case "payable-functional-negative": row.PayableFunctionalAmountText = "-1250.50"; break;
            case "tax-base-empty": row.TaxBaseFunctionalAmountText = string.Empty; break;
            case "vat-text": row.VatFunctionalAmountText = "KDV"; break;
            case "withholding-negative": row.WithholdingFunctionalAmountText = "-0.01"; break;
            case "approval-time-missing": row.ApprovedAtUtc = null; break;
            case "customer-empty": row.CustomerPartyId = Guid.Empty; break;
            case "currency-empty": row.CurrencyId = Guid.Empty; break;
            case "pdf-version-zero": row.PdfDocumentVersionNo = 0; break;
            case "pdf-sha-empty": row.PdfSha256 = string.Empty; break;
            default: throw new ArgumentOutOfRangeException(nameof(variant));
        }
        await using var run = await Run.CreateAsync(row);

        var answer = await run.Port(Company).LookupAsync(Company, OfficialNo, Ettn);
        Assert.Equal((FinanceOfficialSalesInvoiceReadOutcomeV1.Unavailable, (FinanceOfficialSalesInvoiceFactsV1?)null),
            (answer.Outcome, answer.Invoice));
        Assert.Equal(SalesOfficialInvoiceOutcomeV1.Unavailable,
            (await run.SalesPort(Company).LookupAsync(Company, OfficialNo, Ettn)).Outcome);
    }

    [Fact]
    public async Task Two_live_rows_on_one_key_are_unavailable_never_one_of_them()
    {
        // The filtered unique indexes forbid this in SQL Server; the read still refuses to pick one.
        await using var run = await Run.CreateAsync(Row(Guid.NewGuid()),
            Row(Guid.NewGuid(), FinanceOfficialSalesInvoiceStatusV1.PendingApproval));

        Assert.Equal(FinanceOfficialSalesInvoiceReadOutcomeV1.Unavailable,
            (await run.Port(Company).LookupAsync(Company, OfficialNo, Ettn)).Outcome);
    }

    [Fact]
    public async Task The_sales_adapter_refuses_an_amount_a_decimal_cannot_hold_exactly()
    {
        var finance = new FixedFinanceAnswer(new FinanceOfficialSalesInvoiceReadAnswerV1(
            FinanceOfficialSalesInvoiceReadOutcomeV1.Approved,
            Facts() with { VatFunctionalAmount = "0." + new string('1', 40) }));

        var answer = await new FinanceSalesOfficialInvoiceReadPortV1(finance).LookupAsync(Company, OfficialNo, Ettn);

        Assert.Equal((SalesOfficialInvoiceOutcomeV1.Unavailable, (SalesOfficialInvoiceFacts?)null),
            (answer.Outcome, answer.Invoice));
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("null-answer")]
    [InlineData("approved-without-facts")]
    [InlineData("approved-other-company")]
    [InlineData("not-found-with-facts")]
    [InlineData("pending-with-facts")]
    [InlineData("throws")]
    public async Task The_sales_adapter_reads_every_inconsistent_or_failing_finance_answer_as_unavailable(string variant)
    {
        IFinanceOfficialSalesInvoiceReadPortV1 finance = variant switch
        {
            "unavailable" => new FixedFinanceAnswer(FinanceOfficialSalesInvoiceReadAnswerV1.Unavailable),
            "null-answer" => new FixedFinanceAnswer(null),
            "approved-without-facts" => new FixedFinanceAnswer(
                new(FinanceOfficialSalesInvoiceReadOutcomeV1.Approved, null)),
            "approved-other-company" => new FixedFinanceAnswer(
                new(FinanceOfficialSalesInvoiceReadOutcomeV1.Approved, Facts() with { CompanyId = OtherCompany })),
            "not-found-with-facts" => new FixedFinanceAnswer(new(FinanceOfficialSalesInvoiceReadOutcomeV1.NotFound, Facts())),
            "pending-with-facts" => new FixedFinanceAnswer(
                new(FinanceOfficialSalesInvoiceReadOutcomeV1.PendingApproval, Facts())),
            "throws" => new ThrowingFinance(),
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };

        var answer = await new FinanceSalesOfficialInvoiceReadPortV1(finance).LookupAsync(Company, OfficialNo, Ettn);

        Assert.Equal((SalesOfficialInvoiceOutcomeV1.Unavailable, (SalesOfficialInvoiceFacts?)null),
            (answer.Outcome, answer.Invoice));
    }

    [Theory]
    [InlineData("missing-date")]
    [InlineData("missing-source-id")]
    [InlineData("missing-version")]
    [InlineData("missing-pdf")]
    [InlineData("bad-hash")]
    [InlineData("unknown-source")]
    [InlineData("import-without-batch")]
    public async Task Sales_refuses_incomplete_official_source_evidence_without_substituting_a_draft_date(string variant)
    {
        var facts = Facts();
        facts = variant switch
        {
            "missing-date" => facts with { InvoiceDate = null },
            "missing-source-id" => facts with { InvoiceId = Guid.Empty },
            "missing-version" => facts with { Version = "" },
            "missing-pdf" => facts with { PdfDocumentId = null },
            "bad-hash" => facts with { PdfSha256 = new string('x', 64) },
            "unknown-source" => facts with { SourceKind = "unknown" },
            "import-without-batch" => facts with { SourceKind = "approved-import", PdfDocumentId = null,
                PdfDocumentVersionNo = null, PdfSha256 = null, SourceChecksum = new string('a', 64) },
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        var port = new FinanceSalesOfficialInvoiceReadPortV1(new FixedFinanceAnswer(new(
            FinanceOfficialSalesInvoiceReadOutcomeV1.Approved, facts)));
        Assert.Equal(SalesOfficialInvoiceAnswerV1.Unavailable, await port.LookupAsync(Company, OfficialNo, Ettn));
    }

    [Fact]
    public async Task A_cancelled_lookup_is_never_read_as_unavailable()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new FinanceSalesOfficialInvoiceReadPortV1(new ThrowingFinance())
                .LookupAsync(Company, OfficialNo, Ettn, cancelled.Token));
        await using var run = await Run.CreateAsync(Row(Guid.NewGuid()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            run.Port(Company).LookupAsync(Company, OfficialNo, Ettn, cancelled.Token));
    }

    private static readonly DateTimeOffset ApprovedAt = new(2026, 11, 14, 9, 30, 0, TimeSpan.Zero);

    private static FinanceOfficialSalesInvoiceRow Row(Guid id,
        FinanceOfficialSalesInvoiceStatusV1 status = FinanceOfficialSalesInvoiceStatusV1.Approved, Guid? company = null,
        string officialNo = OfficialNo, string ettn = Ettn)
    {
        var row = new FinanceOfficialSalesInvoiceRow
        {
            Id = id,
            CompanyId = company ?? Company,
            OfficialNo = officialNo,
            InvoiceDate = new DateOnly(2026, 11, 14),
            Ettn = ettn,
            CustomerPartyId = Customer,
            CurrencyId = Currency,
            PayableAmountText = "1250.50",
            PayableFunctionalAmountText = "1250.50",
            TaxBaseFunctionalAmountText = "1042.08",
            VatFunctionalAmountText = "208.42",
            WithholdingFunctionalAmountText = "0",
            PdfDocumentId = Pdf,
            PdfDocumentVersionNo = 1,
            PdfSha256 = new string('d', 64),
            Status = (int)status,
            Version = status == FinanceOfficialSalesInvoiceStatusV1.PendingApproval ? 1 : 2,
            RecordedBy = "SENTETIK-maker",
            RecordedAtUtc = ApprovedAt.AddHours(-1),
            CorrelationId = "SENTETIK-correlation",
        };
        switch (status)
        {
            case FinanceOfficialSalesInvoiceStatusV1.Approved:
                row.ApprovedBy = "SENTETIK-checker";
                row.ApprovedAtUtc = ApprovedAt;
                break;
            case FinanceOfficialSalesInvoiceStatusV1.Rejected:
                row.RejectedBy = "SENTETIK-checker";
                row.RejectedAtUtc = ApprovedAt;
                row.RejectionReason = "SENTETIK ret";
                break;
            case FinanceOfficialSalesInvoiceStatusV1.Withdrawn:
                row.WithdrawnBy = "SENTETIK-maker";
                row.WithdrawnAtUtc = ApprovedAt;
                break;
        }
        return row;
    }

    private static FinanceOfficialSalesInvoiceFactsV1 Facts() => new(Guid.NewGuid(), Company, OfficialNo, Ettn, Customer,
        Currency, "1250.50", "1250.50", "1042.08", "208.42", "0", Pdf, 1, new string('d', 64), "2", ApprovedAt, new DateOnly(2026, 11, 14));

    private sealed class FixedFinanceAnswer(FinanceOfficialSalesInvoiceReadAnswerV1? answer)
        : IFinanceOfficialSalesInvoiceReadPortV1
    {
        public Task<FinanceOfficialSalesInvoiceReadAnswerV1> LookupAsync(Guid companyId, string officialNo, string ettn,
            CancellationToken cancellationToken = default) => Task.FromResult(answer!);
    }

    private sealed class ThrowingFinance : IFinanceOfficialSalesInvoiceReadPortV1
    {
        public Task<FinanceOfficialSalesInvoiceReadAnswerV1> LookupAsync(Guid companyId, string officialNo, string ettn,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("SENTETIK store fault");
        }
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string _database = $"official-sales-read-{Guid.NewGuid():N}";
        private readonly List<FinanceDbContext> _contexts = [];
        public FinanceDbContext Db { get; private set; } = null!;

        public static async Task<Run> CreateAsync(params FinanceOfficialSalesInvoiceRow[] rows)
        {
            var run = new Run();
            foreach (var company in rows.Select(row => row.CompanyId).Append(Company).Distinct())
            {
                var seed = new CompanyContext();
                seed.Set(company);
                await using var db = run.Context(seed);
                db.OfficialSalesInvoices.AddRange(rows.Where(row => row.CompanyId == company));
                await db.SaveChangesAsync();
            }

            return run;
        }

        public FinanceOfficialSalesInvoiceReadPortV1 Port(Guid company)
        {
            var context = new CompanyContext();
            context.Set(company);
            Db = Track(Context(context));
            return new FinanceOfficialSalesInvoiceReadPortV1(Db, context);
        }

        public FinanceSalesOfficialInvoiceReadPortV1 SalesPort(Guid company) => new(Port(company));

        public FinanceOfficialSalesInvoiceReadPortV1 UnresolvedPort()
        {
            var context = new CompanyContext();
            Db = Track(Context(context));
            return new FinanceOfficialSalesInvoiceReadPortV1(Db, context);
        }

        private FinanceDbContext Context(ICompanyContext company) => new(
            new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase(_database).Options, company);

        private FinanceDbContext Track(FinanceDbContext db)
        {
            _contexts.Add(db);
            return db;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var db in _contexts)
            {
                await db.DisposeAsync();
            }
        }
    }
}
