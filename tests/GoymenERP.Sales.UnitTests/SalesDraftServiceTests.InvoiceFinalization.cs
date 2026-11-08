using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

// S5 API-36: the finalisation command and the Finance hand-off (SAT-D208). Every value is SENTETIK.
public sealed partial class SalesDraftServiceTests
{
    private const string InvoiceFinalize = SalesPermissionManifestV1.InvoicesFinalize;

    private sealed class FakeFinanceAdmission : ISalesFinanceAdmissionPortV1
    {
        public SalesFinanceAdmissionAnswerV1 Answer { get; set; } = new(SalesFinanceAdmissionOutcomeV1.Admitted, "ok");
        public List<SalesFinanceAdmissionRequestV1> Requests { get; } = [];

        public Task<SalesFinanceAdmissionAnswerV1> AdmitAsync(SalesFinanceAdmissionRequestV1 request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Answer);
        }
    }

    private static async Task<(InvoiceWorld Invoicing, Guid Id)> MatchedInvoiceAsync(ISalesFinanceAdmissionPortV1? finance)
    {
        var (invoicing, id, _) = await ApprovedInvoiceAsync();
        invoicing.Finance = finance;
        invoicing.Official.Has(648.49m);
        await invoicing.Service().MatchInvoiceAsync(id, MatchRequest(), Metadata("k-inv-match"));
        invoicing.World.User.Id = "synthetic-invoice-finalizer";
        invoicing.World.Authority.Measured.Clear();
        return (invoicing, id);
    }

    [Fact]
    public async Task A_finalization_without_a_match_is_refused_as_data_and_nothing_is_recorded()
    {
        var (invoicing, id, _) = await ApprovedInvoiceAsync();
        invoicing.World.User.Id = "synthetic-invoice-finalizer";

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().FinalizeInvoiceAsync(id, Command("3"), Metadata("k-inv-final")));

        Assert.Equal((409, SalesInvoiceProblemCodesV1.MatchRequired), Status(refused));
        Assert.DoesNotContain("k-inv-final", invoicing.Invoices.StateAttempts.Keys);
        Assert.Equal("Approved", (await invoicing.Service().GetInvoiceAsync(id)).State);
    }

    [Fact]
    public async Task A_matched_invoice_is_finalized_waiting_for_finance_when_no_port_answers_and_replays()
    {
        var (invoicing, id) = await MatchedInvoiceAsync(finance: null);

        var done = await invoicing.Service().FinalizeInvoiceAsync(id, Command("3"), Metadata("k-inv-final"));

        Assert.Equal(("Finalized", "4", false), (done.Invoice.State, done.Invoice.Version, done.IsReplay));
        Assert.Equal(InvoiceFinalize, invoicing.World.Authority.Measured[0]);
        Assert.Equal(SalesInvoiceFinanceStatusesV1.Waiting, done.Invoice.Finalization!.FinanceStatus);
        Assert.Equal(0, invoicing.Invoices.FinanceAttemptCalls);

        var replay = await invoicing.Service().FinalizeInvoiceAsync(id, Command("3"), Metadata("k-inv-final", "another-correlation"));
        Assert.True(replay.IsReplay);
        Assert.Equal(1, invoicing.Invoices.FinalizeCalls);
    }

    [Fact]
    public async Task An_admitted_answer_marks_the_ledger_admitted_and_an_unavailable_one_keeps_it_waiting()
    {
        var finance = new FakeFinanceAdmission();
        var (invoicing, id) = await MatchedInvoiceAsync(finance);

        var admitted = await invoicing.Service().FinalizeInvoiceAsync(id, Command("3"), Metadata("k-inv-final"));
        Assert.Equal(SalesInvoiceFinanceStatusesV1.Admitted, admitted.Invoice.Finalization!.FinanceStatus);
        var request = Assert.Single(finance.Requests);
        Assert.Equal(admitted.Invoice.Finalization.AdmissionId, request.AdmissionId);
        Assert.Equal($"sales-invoice:{id:D}:4", request.IdempotencyKey);

        var (other, otherId) = await MatchedInvoiceAsync(new FakeFinanceAdmission { Answer = SalesFinanceAdmissionAnswerV1.Unavailable });
        var waiting = await other.Service().FinalizeInvoiceAsync(otherId, Command("3"), Metadata("k-inv-final-2"));
        Assert.Equal(("Finalized", SalesInvoiceFinanceStatusesV1.Waiting, 1), (waiting.Invoice.State,
            waiting.Invoice.Finalization!.FinanceStatus, waiting.Invoice.Finalization.AttemptCount));
    }

    [Fact]
    public async Task A_throwing_finance_port_never_admits_and_never_undoes_the_commit()
    {
        var (invoicing, id) = await MatchedInvoiceAsync(new ThrowingFinanceAdmission());

        var done = await invoicing.Service().FinalizeInvoiceAsync(id, Command("3"), Metadata("k-inv-final"));

        Assert.Equal(("Finalized", SalesInvoiceFinanceStatusesV1.Waiting), (done.Invoice.State, done.Invoice.Finalization!.FinanceStatus));
    }

    private sealed class ThrowingFinanceAdmission : ISalesFinanceAdmissionPortV1
    {
        public Task<SalesFinanceAdmissionAnswerV1> AdmitAsync(SalesFinanceAdmissionRequestV1 request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("synthetic owner failure");
    }

    [Fact]
    public async Task A_finalization_needs_its_own_permission_and_a_stale_version_is_a_recorded_conflict()
    {
        var (invoicing, id) = await MatchedInvoiceAsync(finance: null);
        invoicing.World.Authority.Denied.Add(InvoiceFinalize);
        var forbidden = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().FinalizeInvoiceAsync(id, Command("3"), Metadata("k-inv-forbid")));
        Assert.Equal(403, forbidden.Status);
        Assert.Equal(0, invoicing.Invoices.FinalizeCalls);
        invoicing.World.Authority.Denied.Clear();

        var stale = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().FinalizeInvoiceAsync(id, Command("2"), Metadata("k-inv-stale")));
        Assert.Equal(409, stale.Status);
    }

    [Fact]
    public async Task A_finance_resend_offers_a_waiting_invoice_again_and_refuses_an_admitted_one()
    {
        var finance = new FakeFinanceAdmission { Answer = SalesFinanceAdmissionAnswerV1.Unavailable };
        var (invoicing, id) = await MatchedInvoiceAsync(finance);
        await invoicing.Service().FinalizeInvoiceAsync(id, Command("3"), Metadata("k-inv-final"));
        finance.Answer = new SalesFinanceAdmissionAnswerV1(SalesFinanceAdmissionOutcomeV1.Admitted, "ok");

        var resent = await invoicing.Service().ResendFinanceAdmissionAsync(id, Command("4"), Metadata("k-inv-resend"));
        Assert.Equal(SalesInvoiceFinanceStatusesV1.Admitted, resent.Invoice.Finalization!.FinanceStatus);
        Assert.Equal($"sales-invoice:{id:D}:4:2", finance.Requests[^1].IdempotencyKey);

        var again = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().ResendFinanceAdmissionAsync(id, Command("4"), Metadata("k-inv-resend-2")));
        Assert.Equal(409, again.Status);
    }

    private sealed class RecordingDiagnostics : ISalesInvoiceDiagnosticsV1
    {
        public List<(Guid InvoiceId, string Stage)> Failures { get; } = [];

        public void FinanceHandOffFailed(Guid companyId, Guid invoiceId, string stage, Exception exception) => Failures.Add((invoiceId, stage));
    }

    [Fact]
    public async Task A_throwing_finance_port_is_reported_to_diagnostics_and_the_commit_still_stands()
    {
        var (invoicing, id) = await MatchedInvoiceAsync(new ThrowingFinanceAdmission());
        var diagnostics = new RecordingDiagnostics();
        invoicing.Diagnostics = diagnostics;

        var done = await invoicing.Service().FinalizeInvoiceAsync(id, Command("3"), Metadata("k-inv-final"));

        Assert.Equal("Finalized", done.Invoice.State);
        Assert.Equal([(id, "finance_offer")], diagnostics.Failures);
    }

    private static async Task<(InvoiceWorld Invoicing, Guid Id)> MatchedWithholdingInvoiceAsync()
    {
        var (invoicing, id, _) = await ApprovedInvoiceAsync(withholding: true, withholdingCode: "625");
        invoicing.Official.Has(594.22m);
        await invoicing.Service().MatchInvoiceAsync(id, MatchRequest(), Metadata("k-inv-match"));
        invoicing.World.User.Id = "synthetic-invoice-finalizer";
        invoicing.World.Authority.Measured.Clear();
        return (invoicing, id);
    }

    [Fact]
    public async Task A_withholding_invoice_is_finalized_and_never_held_back_by_an_amount_threshold()
    {
        // S5 (Sinan): withholding lines are not held; SAT-D93/EK13: the threshold basis is the VAT-inclusive total of the whole order,
        // read from the Ayarlar port, never a second source here. Finalisation does not block on it.
        var (invoicing, id) = await MatchedWithholdingInvoiceAsync();

        var done = await invoicing.Service().FinalizeInvoiceAsync(id, Command("3"), Metadata("k-inv-final"));

        Assert.Equal("Finalized", done.Invoice.State);
        Assert.Equal(1, invoicing.Invoices.FinalizeCalls);
    }

    [Theory]
    [InlineData("648.54")]
    [InlineData("648.44")]
    public async Task A_recorded_match_difference_blocks_the_finalization_as_a_recorded_409_that_replays(string official)
    {
        // NEEDS-SINAN #3: which amount Finance opens on a difference is an undecided money policy, so it fails closed (any currency).
        var finance = new FakeFinanceAdmission();
        var (invoicing, id, _) = await ApprovedInvoiceAsync();
        invoicing.Finance = finance;
        invoicing.Official.Has(decimal.Parse(official, System.Globalization.CultureInfo.InvariantCulture));
        await invoicing.Service().MatchInvoiceAsync(id, MatchRequest(), Metadata("k-inv-match"));
        invoicing.World.User.Id = "synthetic-invoice-finalizer";

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().FinalizeInvoiceAsync(id, Command("3"), Metadata("k-inv-final")));

        Assert.Equal((409, SalesInvoiceProblemCodesV1.MatchDifferenceOpen), Status(refused));
        Assert.Equal("sales_invoice_match_difference_open", refused.Code);
        Assert.Equal(1, invoicing.Invoices.StateAttempts.Count(entry => entry.Key == "k-inv-final"));
        Assert.NotNull(invoicing.Invoices.StateAttempts["k-inv-final"].Refusal);
        Assert.Empty(finance.Requests);
        Assert.Equal("Approved", (await invoicing.Service().GetInvoiceAsync(id)).State);

        // The same key answers the same recorded refusal and never reaches the store's write again.
        var calls = invoicing.Invoices.FinalizeCalls;
        var replay = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().FinalizeInvoiceAsync(id, Command("3"), Metadata("k-inv-final", "another-correlation")));
        Assert.Equal((409, SalesInvoiceProblemCodesV1.MatchDifferenceOpen), Status(replay));
        Assert.Equal(calls, invoicing.Invoices.FinalizeCalls);
        Assert.DoesNotContain("iptal", refused.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FenceRecordingFinanceAdmission(Func<IReadOnlyList<string>> measured) : ISalesFinanceAdmissionPortV1
    {
        public List<string> MeasuredAtOffer { get; } = [];

        public Task<SalesFinanceAdmissionAnswerV1> AdmitAsync(SalesFinanceAdmissionRequestV1 request, CancellationToken cancellationToken)
        {
            MeasuredAtOffer.AddRange(measured());
            return Task.FromResult(new SalesFinanceAdmissionAnswerV1(SalesFinanceAdmissionOutcomeV1.Admitted, "ok"));
        }
    }

    [Fact]
    public async Task The_finance_resend_measures_the_finalization_permission_right_before_it_offers()
    {
        var (invoicing, id) = await MatchedInvoiceAsync(new FakeFinanceAdmission { Answer = SalesFinanceAdmissionAnswerV1.Unavailable });
        await invoicing.Service().FinalizeInvoiceAsync(id, Command("3"), Metadata("k-inv-final"));
        var fence = new FenceRecordingFinanceAdmission(() => [.. invoicing.World.Authority.Measured]);
        invoicing.Finance = fence;

        await invoicing.Service().ResendFinanceAdmissionAsync(id, Command("4"), Metadata("k-inv-resend"));

        Assert.NotEmpty(fence.MeasuredAtOffer);
        Assert.Equal(InvoiceFinalize, fence.MeasuredAtOffer[^1]);
    }

    [Fact]
    public async Task The_finalized_outbox_payload_keeps_the_versioned_v1_shape()
    {
        var (invoicing, id) = await MatchedInvoiceAsync(finance: null);
        await invoicing.Service().FinalizeInvoiceAsync(id, Command("3"), Metadata("k-inv-final"));
        var stored = invoicing.Invoices.Stored[id];

        using var payload = System.Text.Json.JsonDocument.Parse(
            GoymenERP.Modules.Sales.Infrastructure.Persistence.SalesStore.FinalizedPayload(stored, stored.Finalization!));

        var root = payload.RootElement;
        Assert.Equal(
            ["invoiceId", "documentNo", "version", "admissionId", "partyId", "companyAccountId", "currencyId", "rate", "payable",
                "functionalPayable", "match", "instalments"],
            root.EnumerateObject().Select(member => member.Name).ToArray());
        Assert.Equal(["officialNo", "ettn", "officialFunctionalAmount", "difference"],
            root.GetProperty("match").EnumerateObject().Select(member => member.Name).ToArray());
        Assert.Equal(["openItemId", "installmentId", "sequenceNo", "dueDate", "amount", "functionalAmount"],
            root.GetProperty("instalments")[0].EnumerateObject().Select(member => member.Name).ToArray());
        Assert.Equal(stored.Id, root.GetProperty("invoiceId").GetGuid());
        Assert.Equal(stored.Finalization!.AdmissionId, root.GetProperty("admissionId").GetGuid());
        Assert.Equal(stored.DocumentNo, root.GetProperty("documentNo").GetString());
        Assert.Equal(stored.Version, root.GetProperty("version").GetInt64());
        Assert.Equal(System.Text.Json.JsonValueKind.Number, root.GetProperty("payable").ValueKind);
        Assert.Equal(stored.Totals.Payable.Amount, root.GetProperty("payable").GetDecimal());
        var rate = root.GetProperty("rate");
        Assert.Equal(stored.Header.Rate is null ? System.Text.Json.JsonValueKind.Null : System.Text.Json.JsonValueKind.Number, rate.ValueKind);
        var functionalPayable = root.GetProperty("functionalPayable");
        Assert.Equal(stored.FunctionalTotals is null ? System.Text.Json.JsonValueKind.Null : System.Text.Json.JsonValueKind.Number,
            functionalPayable.ValueKind);
        var match = root.GetProperty("match");
        Assert.Equal(System.Text.Json.JsonValueKind.String, match.GetProperty("officialNo").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.String, match.GetProperty("ettn").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Number, match.GetProperty("officialFunctionalAmount").ValueKind);
        Assert.Equal(0m, match.GetProperty("difference").GetDecimal());
        var instalments = root.GetProperty("instalments");
        Assert.Equal(stored.Finalization.Plan.Count, instalments.GetArrayLength());
        foreach (var (item, expected) in instalments.EnumerateArray().Zip(stored.Finalization.Plan))
        {
            Assert.Equal(expected.OpenItemId, item.GetProperty("openItemId").GetGuid());
            Assert.Equal(expected.InstallmentId, item.GetProperty("installmentId").GetGuid());
            Assert.Equal(expected.SequenceNo, item.GetProperty("sequenceNo").GetInt32());
            Assert.Equal(expected.DueDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                item.GetProperty("dueDate").GetString());
            Assert.Equal(expected.Amount, item.GetProperty("amount").GetDecimal());
            Assert.Equal(expected.FunctionalAmount is null ? System.Text.Json.JsonValueKind.Null : System.Text.Json.JsonValueKind.Number,
                item.GetProperty("functionalAmount").ValueKind);
        }
    }
}
