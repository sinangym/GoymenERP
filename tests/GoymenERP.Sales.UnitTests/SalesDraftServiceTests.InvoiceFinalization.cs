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
    public async Task A_withholding_invoice_below_the_threshold_is_a_recorded_409_and_the_same_key_replays_it()
    {
        var (invoicing, id) = await MatchedWithholdingInvoiceAsync();

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().FinalizeInvoiceAsync(id, Command("3"), Metadata("k-inv-final")));

        Assert.Equal((409, SalesInvoiceProblemCodesV1.WithholdingBelowThreshold), Status(refused));
        Assert.Contains("k-inv-final", invoicing.Invoices.StateAttempts.Keys);
        Assert.Equal("Approved", (await invoicing.Service().GetInvoiceAsync(id)).State);
        var replay = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().FinalizeInvoiceAsync(id, Command("3"), Metadata("k-inv-final", "another-correlation")));
        Assert.Equal((409, SalesInvoiceProblemCodesV1.WithholdingBelowThreshold), Status(replay));
        Assert.Equal(1, invoicing.Invoices.FinalizeCalls);
    }

    [Fact]
    public async Task A_withholding_invoice_exactly_at_the_configured_threshold_is_finalized_and_the_threshold_is_configurable()
    {
        var (invoicing, id) = await MatchedWithholdingInvoiceAsync();
        // The TL net amount of this invoice is 540.29 (see the withholding golden above); equal passes, strictly below refuses.
        invoicing.Threshold = new SalesInvoiceWithholdingThresholdPolicy(540.29m);

        var done = await invoicing.Service().FinalizeInvoiceAsync(id, Command("3"), Metadata("k-inv-final"));

        Assert.Equal("Finalized", done.Invoice.State);
    }

    [Fact]
    public void The_default_withholding_threshold_is_the_decided_12000_and_never_negative()
    {
        Assert.Equal(12000.00m, new SalesInvoiceWithholdingThresholdPolicy().ThresholdAmount);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SalesInvoiceWithholdingThresholdPolicy(-0.01m));
    }

    [Fact]
    public async Task A_withholding_free_invoice_below_the_threshold_is_finalized()
    {
        var (invoicing, id) = await MatchedInvoiceAsync(finance: null);
        invoicing.Threshold = new SalesInvoiceWithholdingThresholdPolicy(1000000m);

        var done = await invoicing.Service().FinalizeInvoiceAsync(id, Command("3"), Metadata("k-inv-final"));

        Assert.Equal("Finalized", done.Invoice.State);
    }

    [Fact]
    public async Task A_match_with_a_difference_is_a_recorded_409_and_nothing_goes_to_finance()
    {
        var finance = new FakeFinanceAdmission();
        var (invoicing, id, _) = await ApprovedInvoiceAsync();
        invoicing.Finance = finance;
        invoicing.Official.Has(648.54m);
        await invoicing.Service().MatchInvoiceAsync(id, MatchRequest(), Metadata("k-inv-match"));
        invoicing.World.User.Id = "synthetic-invoice-finalizer";
        invoicing.World.Authority.Measured.Clear();

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().FinalizeInvoiceAsync(id, Command("3"), Metadata("k-inv-final")));

        Assert.Equal((409, SalesInvoiceProblemCodesV1.MatchDifferenceOpen), Status(refused));
        Assert.Contains("k-inv-final", invoicing.Invoices.StateAttempts.Keys);
        Assert.Empty(finance.Requests);
        Assert.Equal("Approved", (await invoicing.Service().GetInvoiceAsync(id)).State);
    }
}
