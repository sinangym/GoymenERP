using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class AccountingSourceDocumentAdmissionV1Tests
{
    [Fact]
    public async Task Typed_purchase_expense_uses_expense_mapping_and_missing_mapping_never_defaults_to_inventory()
    {
        var harness = new Harness { Mappings = { ExpectedPurpose = AccountingPostingAccountMappingPurposesV1.PurchaseInvoice, Missing = "expense" } };
        var source = Purchase();
        source = source with { Lines = [source.Lines[0] with { ItemType = "Expense", LinePurpose = "expense" },
            source.Lines[1] with { ItemType = "Expense", LinePurpose = "expense" }] };
        var waiting = await harness.AdmitAsync(source);
        Assert.Equal(AccountingSourceDocumentAdmissionReasonsV1.MappingPending, waiting.Reason);
        Assert.Empty(await harness.DraftsAsync());
        harness.Mappings.Missing = null;
        Assert.Equal(AccountingSourceDocumentAdmissionOutcomeV1.Admitted, (await harness.AdmitAsync(source)).Outcome);
        var lines = await harness.LinesAsync();
        Assert.DoesNotContain(lines, line => line.MappingKey == "inventory");
        Assert.Equal(1200m, lines.Where(line => line.MappingKey == "expense").Sum(line => line.Amount));
    }

    [Fact]
    public async Task Purchase_invoice_keeps_full_input_VAT_and_posts_withholding_payable_separately_without_guessing_accounts()
    {
        var harness = new Harness { Mappings = { ExpectedPurpose = AccountingPostingAccountMappingPurposesV1.PurchaseInvoice } };
        var request = Purchase();
        var answer = await harness.AdmitAsync(request);
        Assert.Equal(AccountingSourceDocumentAdmissionOutcomeV1.Admitted, answer.Outcome);
        var lines = await harness.LinesAsync();
        Assert.Equal(1380m, Assert.Single(lines, x => x.MappingKey == "payable").Amount);
        Assert.Equal(240m, Assert.Single(lines, x => x.MappingKey == "vat-input").Amount);
        Assert.Equal(60m, Assert.Single(lines, x => x.MappingKey == "withholding-payable").Amount);
        Assert.Equal(1000m, Assert.Single(lines, x => x.MappingKey == "inventory").Amount);
        Assert.Equal(200m, Assert.Single(lines, x => x.MappingKey == "expense").Amount);
        Assert.Equal(0m, lines.Sum(x => x.Side == AccountingSourceDocumentSidesV1.Debit ? x.Amount : -x.Amount));
        var replay = await harness.AdmitAsync(request);
        Assert.True(replay.IsReplay);
        Assert.Equal(answer.VoucherDraftId, replay.VoucherDraftId);
        Assert.Single(await harness.DraftsAsync());
        Assert.Single(harness.Audit.Events);
    }

    [Fact]
    public async Task Purchase_invoice_without_an_explicit_source_line_purpose_is_refused_and_never_guessed()
    {
        var harness = new Harness { Mappings = { ExpectedPurpose = AccountingPostingAccountMappingPurposesV1.PurchaseInvoice } };
        var request = Purchase();
        request = request with { Lines = [request.Lines[0] with { LinePurpose = null }, request.Lines[1]] };
        var answer = await harness.AdmitAsync(request);
        Assert.Equal(AccountingSourceDocumentAdmissionReasonsV1.LineInvalid, answer.Reason);
        Assert.Empty(await harness.DraftsAsync());
    }

    [Fact]
    public async Task Purchase_mapping_gap_remains_waiting_and_the_same_source_version_is_admitted_after_configuration()
    {
        var harness = new Harness { Mappings = { ExpectedPurpose = AccountingPostingAccountMappingPurposesV1.PurchaseInvoice, Missing = "withholding-payable" } };
        var waiting = await harness.AdmitAsync(Purchase());
        Assert.Equal(AccountingSourceDocumentAdmissionReasonsV1.MappingPending, waiting.Reason);
        Assert.True(waiting.Retryable);
        Assert.Empty(await harness.DraftsAsync());
        harness.Mappings.Missing = null;
        var admitted = await harness.AdmitAsync(Purchase());
        Assert.Equal(AccountingSourceDocumentAdmissionOutcomeV1.Admitted, admitted.Outcome);
        Assert.Single(await harness.DraftsAsync());
    }

    private static AccountingSourceDocumentAdmissionRequestV1 Purchase() => Valid(
        lines: [Line(1, "1000", "200", "60", "20") with { LinePurpose = "production-input" },
            Line(2, "200", "40", "0", "20") with { LinePurpose = "service" }],
        withholding: "60", payable: "1380") with
    {
        DocumentKind = AccountingSourceDocumentKindsV1.PurchaseInvoice,
        IdempotencyKey = AccountingSourceDocumentKindsV1.PurchaseInvoiceKey(Invoice, 1),
    };
}
