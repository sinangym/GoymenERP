using System.Text;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Finance.UnitTests;

/// <summary>FIN-S5 (BS-4, FNS-D28, FNS-D19 D5): the v2 official document columns and the identity claim policy.</summary>
public sealed class FinanceOfficialSalesInvoiceImportV2Tests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Batch = Guid.Parse("11000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherBatch = Guid.Parse("11000000-0000-0000-0000-000000000002");
    private static readonly Guid DocumentA = Guid.Parse("22000000-0000-0000-0000-000000000001");
    private static readonly Guid DocumentB = Guid.Parse("22000000-0000-0000-0000-000000000002");
    private const string EttnA = "aa000000-0000-4000-8000-000000000001";
    private const string EttnB = "bb000000-0000-4000-8000-000000000002";

    [Theory]
    [InlineData("2050.15", "2050.15")]
    [InlineData("", null)]
    public void V3_keeps_the_source_date_and_optional_TL_without_calculating_an_exchange_rate(string value, string? expected)
    {
        var bytes = Encoding.UTF8.GetBytes(FinanceOfficialSalesInvoiceImportTemplateV3.Header + "\n"
            + Row(1, DocumentA, "SNT1", EttnA) + ",2026-11-16," + value + "\n");
        var parsed = FinanceOfficialInvoiceImportParserV1.Parse(Context(), new(Batch, "v3", bytes));
        Assert.True(parsed.Preview.IsValid);
        Assert.Equal(FinanceOfficialSalesInvoiceImportTemplateV3.Version, parsed.Preview.TemplateVersion);
        var document = Assert.Single(parsed.Documents);
        Assert.Equal(new DateOnly(2026, 11, 16), document.InvoiceDate);
        Assert.Equal(expected, document.PayableFunctionalAmount);
    }

    [Theory]
    [InlineData("2026-02-30", "1200", "invoiceDate")]
    [InlineData("", "1200", "invoiceDate")]
    [InlineData("2026-11-16", "0", "payableFunctionalAmount")]
    [InlineData("2026-11-16", "1e3", "payableFunctionalAmount")]
    public void V3_refuses_invalid_source_date_or_supplied_TL(string date, string value, string field)
    {
        var bytes = Encoding.UTF8.GetBytes(FinanceOfficialSalesInvoiceImportTemplateV3.Header + "\n"
            + Row(1, DocumentA, "SNT1", EttnA) + "," + date + "," + value + "\n");
        var parsed = FinanceOfficialInvoiceImportParserV1.Parse(Context(), new(Batch, "v3", bytes));
        Assert.False(parsed.Preview.IsValid);
        Assert.Contains(parsed.Preview.Issues, issue => issue.Field == field);
    }

    [Fact]
    public void A_v2_file_keeps_the_v1_batch_and_returns_one_document_per_source_document_in_first_row_order()
    {
        var bytes = Csv(
            Row(1, DocumentB, "SNT2026000000002", EttnB),
            Row(2, DocumentA, "SNT2026000000001", EttnA),
            Row(3, DocumentB, "SNT2026000000002", EttnB, installment: 2));

        var parsed = FinanceOfficialInvoiceImportParserV1.Parse(Context(), new(Batch, "official-v2", bytes));

        Assert.True(parsed.Preview.IsValid);
        Assert.Equal(FinanceOfficialSalesInvoiceImportTemplateV2.Version, parsed.Preview.TemplateVersion);
        var batch = Assert.IsType<ApprovedFinanceOpenItemSourceBatch>(parsed.Preview.Batch);
        Assert.Equal(3, batch.Rows.Count);
        Assert.Equal(new[] { DocumentB, DocumentA }, parsed.Documents.Select(document => document.SourceDocumentId));
        var first = parsed.Documents[0];
        Assert.Equal(("SNT2026000000002", EttnB, "1000", "200", "0"),
            (first.OfficialNo, first.Ettn, first.TaxBaseFunctionalAmount, first.VatFunctionalAmount,
                first.WithholdingFunctionalAmount));
    }

    [Fact]
    public void A_v1_file_is_unchanged_and_carries_no_documents()
    {
        var v1 = string.Join(',', Guid.Parse("21000000-0000-0000-0000-000000000001"), DocumentA,
            Guid.Parse("23000000-0000-0000-0000-000000000001"), "invoice-v1",
            Guid.Parse("24000000-0000-0000-0000-000000000001"), Guid.Parse("25000000-0000-0000-0000-000000000001"),
            Guid.Parse("26000000-0000-0000-0000-000000000001"), "1200", "2026-11-30");
        var bytes = Encoding.UTF8.GetBytes(FinanceOfficialInvoiceImportTemplateV1.Header + "\n" + v1 + "\n");

        var parsed = FinanceOfficialInvoiceImportParserV1.Parse(Context(), new(Batch, "official-v1", bytes));

        Assert.True(parsed.Preview.IsValid);
        Assert.Equal(FinanceOfficialInvoiceImportTemplateV1.Version, parsed.Preview.TemplateVersion);
        Assert.Empty(parsed.Documents);
    }

    [Theory]
    [InlineData("", EttnA, "1000", "officialNo", FinanceOfficialSalesInvoiceImportCodesV2.OfficialNoRequired)]
    [InlineData(" SNT1", EttnA, "1000", "officialNo", FinanceOfficialSalesInvoiceImportCodesV2.OfficialNoInvalid)]
    [InlineData("SNT1", "", "1000", "ettn", FinanceOfficialSalesInvoiceImportCodesV2.EttnRequired)]
    [InlineData("SNT1", "not-a-guid", "1000", "ettn", FinanceOfficialSalesInvoiceImportCodesV2.EttnInvalid)]
    [InlineData("SNT1", "00000000-0000-0000-0000-000000000000", "1000", "ettn",
        FinanceOfficialSalesInvoiceImportCodesV2.EttnInvalid)]
    [InlineData("SNT1", EttnA, "", "taxBaseFunctionalAmount",
        FinanceOfficialSalesInvoiceImportCodesV2.FunctionalAmountRequired)]
    [InlineData("SNT1", EttnA, "-1", "taxBaseFunctionalAmount",
        FinanceOfficialSalesInvoiceImportCodesV2.FunctionalAmountInvalid)]
    [InlineData("SNT1", EttnA, "1e3", "taxBaseFunctionalAmount",
        FinanceOfficialSalesInvoiceImportCodesV2.FunctionalAmountInvalid)]
    public void An_invalid_document_column_is_listed_on_its_row_and_no_batch_is_produced(
        string officialNo, string ettn, string taxBase, string field, string code)
    {
        var parsed = FinanceOfficialInvoiceImportParserV1.Parse(Context(),
            new(Batch, "official-v2", Csv(Row(1, DocumentA, officialNo, ettn, taxBase: taxBase))));

        Assert.Contains(parsed.Preview.Issues, issue => issue.RowNumber == 2 && issue.Field == field && issue.Code == code);
        Assert.Null(parsed.Preview.Batch);
        Assert.Empty(parsed.Documents);
    }

    [Fact]
    public void An_overlong_official_number_is_refused()
    {
        var overlong = new string('S', FinanceDbContext.OfficialSalesInvoiceMaximumOfficialNoLength + 1);

        var parsed = FinanceOfficialInvoiceImportParserV1.Parse(Context(),
            new(Batch, "official-v2", Csv(Row(1, DocumentA, overlong, EttnA))));

        Assert.Contains(parsed.Preview.Issues,
            issue => issue.Code == FinanceOfficialSalesInvoiceImportCodesV2.OfficialNoInvalid);
        Assert.Null(parsed.Preview.Batch);
    }

    [Fact]
    public void An_uppercase_ettn_is_kept_as_canonical_lowercase_and_equal_amount_spellings_are_one_document()
    {
        var parsed = FinanceOfficialInvoiceImportParserV1.Parse(Context(), new(Batch, "official-v2", Csv(
            Row(1, DocumentA, "SNT1", EttnA.ToUpperInvariant()),
            Row(2, DocumentA, "SNT1", EttnA, installment: 2, taxBase: "1000.00"))));

        Assert.True(parsed.Preview.IsValid);
        var document = Assert.Single(parsed.Documents);
        Assert.Equal(EttnA, document.Ettn);
    }

    [Fact]
    public void Installments_of_one_document_with_different_document_columns_are_refused()
    {
        var parsed = FinanceOfficialInvoiceImportParserV1.Parse(Context(), new(Batch, "official-v2", Csv(
            Row(1, DocumentA, "SNT1", EttnA),
            Row(2, DocumentA, "SNT1", EttnA, installment: 2, taxBase: "999"))));

        Assert.Contains(parsed.Preview.Issues, issue => issue.RowNumber == 3
            && issue.Code == FinanceOfficialSalesInvoiceImportCodesV2.DocumentInconsistent);
        Assert.Null(parsed.Preview.Batch);
    }

    [Fact]
    public void Two_documents_of_one_file_may_not_share_an_official_number_or_an_ettn()
    {
        var officialNo = FinanceOfficialInvoiceImportParserV1.Parse(Context(), new(Batch, "official-v2", Csv(
            Row(1, DocumentA, "SNT1", EttnA), Row(2, DocumentB, "SNT1", EttnB))));
        var ettn = FinanceOfficialInvoiceImportParserV1.Parse(Context(), new(Batch, "official-v2", Csv(
            Row(1, DocumentA, "SNT1", EttnA), Row(2, DocumentB, "SNT2", EttnA))));

        Assert.Contains(officialNo.Preview.Issues, issue => issue.RowNumber == 3 && issue.Field == "officialNo"
            && issue.Code == FinanceOfficialSalesInvoiceImportCodesV2.OfficialNoDuplicateInFile);
        Assert.Contains(ettn.Preview.Issues, issue => issue.RowNumber == 3 && issue.Field == "ettn"
            && issue.Code == FinanceOfficialSalesInvoiceImportCodesV2.EttnDuplicateInFile);
        Assert.Null(officialNo.Preview.Batch);
        Assert.Null(ettn.Preview.Batch);
    }

    [Fact]
    public void The_v2_template_extends_v1_without_a_company_or_formula_column()
    {
        Assert.StartsWith(FinanceOfficialInvoiceImportTemplateV1.Header + ",",
            FinanceOfficialSalesInvoiceImportTemplateV2.Header, StringComparison.Ordinal);
        Assert.DoesNotContain("companyId", FinanceOfficialSalesInvoiceImportTemplateV2.Header, StringComparison.Ordinal);
        Assert.DoesNotContain("payable", FinanceOfficialSalesInvoiceImportTemplateV2.Header, StringComparison.Ordinal);
        Assert.NotEqual(FinanceOfficialInvoiceImportTemplateV1.Version, FinanceOfficialSalesInvoiceImportTemplateV2.Version);
    }

    [Fact]
    public void Unclaimed_identities_get_the_first_generation()
    {
        var (batch, documents) = Approved(Row(1, DocumentA, "SNT1", EttnA));

        var decision = FinanceOfficialInvoiceIdentityClaimPolicy.Decide(batch, documents,
            FinanceOfficialInvoiceIdentityClaimPolicy.CurrentHolders([]), new HashSet<(Guid, Guid)>());

        Assert.Empty(decision.Issues);
        Assert.Equal(
            new[]
            {
                (FinanceOfficialInvoiceIdentityKinds.OfficialNo, "SNT1", 1, DocumentA),
                (FinanceOfficialInvoiceIdentityKinds.Ettn, EttnA, 1, DocumentA),
            },
            decision.Claims.Select(claim => (claim.Kind, claim.Value, claim.Generation, claim.SourceDocumentId)));
    }

    [Fact]
    public void An_identity_held_by_another_acceptance_with_a_live_open_item_is_refused_on_the_documents_first_row()
    {
        var (batch, documents) = Approved(
            Row(1, DocumentB, "SNT2", EttnB),
            Row(2, DocumentA, "SNT1", EttnA));
        var holders = FinanceOfficialInvoiceIdentityClaimPolicy.CurrentHolders(
            [Claim(FinanceOfficialInvoiceIdentityKinds.Ettn, EttnA, 1, OtherBatch, DocumentA)]);

        var decision = FinanceOfficialInvoiceIdentityClaimPolicy.Decide(batch, documents, holders,
            new HashSet<(Guid, Guid)> { (OtherBatch, DocumentA) });

        var issue = Assert.Single(decision.Issues);
        Assert.Equal(("rows[1].ettn", FinanceOfficialSalesInvoiceImportCodesV2.EttnAlreadyOpen), (issue.Field, issue.Code));
        // One refused identity refuses the whole acceptance: nothing is claimed.
        Assert.Empty(decision.Claims);
    }

    [Fact]
    public void An_official_number_held_by_a_closed_but_not_cancelled_receivable_still_blocks()
    {
        // FNS-D19: a fully paid (closed) receivable is not cancelled, so the admission service reports it as live.
        var (batch, documents) = Approved(Row(1, DocumentA, "SNT1", EttnA));
        var holders = FinanceOfficialInvoiceIdentityClaimPolicy.CurrentHolders(
            [Claim(FinanceOfficialInvoiceIdentityKinds.OfficialNo, "SNT1", 1, OtherBatch, DocumentB)]);

        var decision = FinanceOfficialInvoiceIdentityClaimPolicy.Decide(batch, documents, holders,
            new HashSet<(Guid, Guid)> { (OtherBatch, DocumentB) });

        Assert.Equal(FinanceOfficialSalesInvoiceImportCodesV2.OfficialNoAlreadyOpen, Assert.Single(decision.Issues).Code);
    }

    [Fact]
    public void An_identity_whose_holder_is_fully_cancelled_is_claimed_with_the_next_generation_of_the_highest()
    {
        var (batch, documents) = Approved(Row(1, DocumentA, "SNT1", EttnA));
        var holders = FinanceOfficialInvoiceIdentityClaimPolicy.CurrentHolders(
        [
            Claim(FinanceOfficialInvoiceIdentityKinds.OfficialNo, "SNT1", 2, OtherBatch, DocumentB),
            Claim(FinanceOfficialInvoiceIdentityKinds.OfficialNo, "SNT1", 1, Batch, DocumentA),
        ]);

        var decision = FinanceOfficialInvoiceIdentityClaimPolicy.Decide(batch, documents, holders,
            new HashSet<(Guid, Guid)>());

        Assert.Empty(decision.Issues);
        Assert.Contains(decision.Claims, claim => claim.Kind == FinanceOfficialInvoiceIdentityKinds.OfficialNo
            && claim.Generation == 3 && claim.SourceDocumentId == DocumentA);
    }

    [Fact]
    public void An_identity_already_held_by_the_same_document_is_not_claimed_again()
    {
        var (batch, documents) = Approved(Row(1, DocumentA, "SNT1", EttnA));
        var holders = FinanceOfficialInvoiceIdentityClaimPolicy.CurrentHolders(
        [
            Claim(FinanceOfficialInvoiceIdentityKinds.OfficialNo, "SNT1", 1, Batch, DocumentA),
            Claim(FinanceOfficialInvoiceIdentityKinds.Ettn, EttnA, 1, Batch, DocumentA),
        ]);

        var decision = FinanceOfficialInvoiceIdentityClaimPolicy.Decide(batch, documents, holders,
            new HashSet<(Guid, Guid)> { (Batch, DocumentA) });

        Assert.Empty(decision.Issues);
        Assert.Empty(decision.Claims);
    }

    [Fact]
    public void Stored_bytes_that_no_longer_yield_the_approved_batch_are_unavailable()
    {
        var bytes = Csv(Row(1, DocumentA, "SNT1", EttnA));
        var parsed = FinanceOfficialInvoiceImportParserV1.Parse(Context(), new(Batch, "official-v2", bytes));
        var batch = parsed.Preview.Batch!;
        var upload = new FinanceOfficialInvoiceImportBatchRow
        {
            Id = Batch, CompanyId = Company, BatchVersion = "official-v2", BatchChecksum = parsed.Preview.SourceSha256,
            TemplateVersion = FinanceOfficialSalesInvoiceImportTemplateV2.Version, FileBytes = bytes,
        };

        Assert.NotNull(FinanceOfficialInvoiceIdentityClaimPolicy.ReadDocuments(Context(), upload, batch));
        upload.TemplateVersion = FinanceOfficialInvoiceImportTemplateV1.Version;
        Assert.Null(FinanceOfficialInvoiceIdentityClaimPolicy.ReadDocuments(Context(), upload, batch));
        upload.TemplateVersion = FinanceOfficialSalesInvoiceImportTemplateV2.Version;
        Assert.Null(FinanceOfficialInvoiceIdentityClaimPolicy.ReadDocuments(Context(), upload,
            batch with { BatchChecksum = new string('0', parsed.Preview.SourceSha256.Length) }));
        Assert.Null(FinanceOfficialInvoiceIdentityClaimPolicy.ReadDocuments(Context(), upload,
            batch with { Rows = [batch.Rows[0] with { SourceDocumentId = DocumentB }] }));
    }

    private static (ApprovedFinanceOpenItemSourceBatch Batch, IReadOnlyList<FinanceOfficialSalesInvoiceImportDocumentV2>
        Documents) Approved(params string[] rows)
    {
        var parsed = FinanceOfficialInvoiceImportParserV1.Parse(Context(), new(Batch, "official-v2", Csv(rows)));
        Assert.True(parsed.Preview.IsValid);
        return (parsed.Preview.Batch!, parsed.Documents);
    }

    private static FinanceOfficialInvoiceIdentityClaimRow Claim(
        string kind, string value, int generation, Guid holderBatch, Guid holderDocument) => new()
    {
        Id = Guid.NewGuid(), CompanyId = Company, IdentityKind = kind, IdentityValue = value, Generation = generation,
        HolderBatchId = holderBatch, HolderSourceDocumentId = holderDocument, ClaimedBy = "SENTETIK-checker",
        ClaimedAtUtc = DateTimeOffset.UnixEpoch, CorrelationId = "correlation",
    };

    private static FinanceContext Context() => new(Company, "actor", "correlation", null);

    private static byte[] Csv(params string[] rows) => Encoding.UTF8.GetBytes(
        FinanceOfficialSalesInvoiceImportTemplateV2.Header + "\n" + string.Join("\n", rows) + "\n");

    private static string Row(
        int line,
        Guid document,
        string officialNo,
        string ettn,
        int installment = 1,
        string taxBase = "1000") => string.Join(',',
        Guid.Parse($"21000000-0000-0000-0000-{line:D12}"),
        document,
        Guid.Parse($"23000000-0000-0000-{installment:D4}-{document.ToString("N")[^12..]}"),
        "invoice-v2",
        Guid.Parse("24000000-0000-0000-0000-000000000001"),
        Guid.Parse("25000000-0000-0000-0000-000000000001"),
        Guid.Parse("26000000-0000-0000-0000-000000000001"),
        "600",
        "2026-11-30",
        officialNo,
        ettn,
        taxBase,
        "200",
        "0");
}
