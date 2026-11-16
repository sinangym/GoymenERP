using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-S13 (EK1 SAT-D343; SAT-D319 (6); SAT-D320 (3) / SAT-D322 (1); EK15 SAT-D389; EK20 SAT-D405) over a SYNTHETIC Logistics
/// weighing-difference seam: the invoice waits — 409 <c>invoice_not_ready</c>, nothing stored — while a share's note has no record,
/// waits for approval, has a difference whose scale kilograms cannot be carried to the lines yet, or cannot be read; the dispatch
/// read shows the owner's note totals verbatim with the same wait, and fails closed (503) on an absent, failing or foreign owner.
/// Every identity is SENTETIK.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private static SalesDispatchWeighingDifferenceReadV1 WeighingRecorded(Guid noteId, decimal theoretical = 1.25m, decimal scale = 1.25m,
        decimal excess = 0m, SalesWeighingDifferenceRecordStateView state = SalesWeighingDifferenceRecordStateView.Recorded,
        SalesWeighingDifferenceDecisionView decision = SalesWeighingDifferenceDecisionView.NoExcess, Guid? company = null) =>
        new(SalesDispatchWeighingDifferenceStatusV1.Recorded, company ?? Company, noteId,
            new SalesDispatchWeighingDifferenceV1(Guid.Parse("71717171-7171-4717-8717-717171717171"), theoretical, scale, scale - theoretical,
                excess, SalesWeighingDifferenceBandView.None, decision, state,
                SaleMayProceed: state != SalesWeighingDifferenceRecordStateView.PendingApproval, Version: 1));

    [Fact]
    public async Task SAT_D389_a_note_without_a_weighing_difference_record_waits_and_nothing_is_stored()
    {
        var invoicing = await InvoiceWorld.CreateAsync();
        invoicing.Weighings.Answer = note => new(SalesDispatchWeighingDifferenceStatusV1.NotRecorded, Company, note, null);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-weighing-none")));

        Assert.Equal((409, SalesInvoiceProblemCodesV1.InvoiceNotReady), Status(refused));
        Assert.Equal([new SalesDraftFieldError("shares[0]", SalesInvoiceReasonCodesV1.WeighingDifferenceNotRecorded)], refused.Errors);
        Assert.Single(invoicing.Weighings.Asked);
        Assert.Empty(invoicing.Invoices.Stored);
    }

    [Fact]
    public async Task SAT_D319_a_weighing_difference_pending_approval_waits()
    {
        var invoicing = await InvoiceWorld.CreateAsync();
        invoicing.Weighings.Answer = note => WeighingRecorded(note, scale: 1.5m, excess: 0.25m,
            state: SalesWeighingDifferenceRecordStateView.PendingApproval, decision: SalesWeighingDifferenceDecisionView.ApprovalRequired);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-weighing-pending")));

        Assert.Equal([new SalesDraftFieldError("shares[0]", SalesInvoiceReasonCodesV1.WeighingDifferencePendingApproval)], refused.Errors);
        Assert.Empty(invoicing.Invoices.Stored);
    }

    [Fact]
    public async Task SAT_D322_an_approved_difference_waits_for_the_per_line_scale_kilograms_instead_of_guessing_a_split()
    {
        var invoicing = await InvoiceWorld.CreateAsync();
        invoicing.Weighings.Answer = note => WeighingRecorded(note, scale: 1.3m, excess: 0.05m,
            state: SalesWeighingDifferenceRecordStateView.Approved, decision: SalesWeighingDifferenceDecisionView.ApprovalRequired);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-weighing-transfer")));

        Assert.Equal([new SalesDraftFieldError("shares[0]", SalesInvoiceReasonCodesV1.WeighingKgTransferPending)], refused.Errors);
        Assert.Empty(invoicing.Invoices.Stored);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("throws")]
    [InlineData("other-company")]
    [InlineData("other-note")]
    [InlineData("recorded-without-facts")]
    [InlineData("unavailable")]
    public async Task An_absent_failing_or_inconsistent_weighing_owner_is_a_wait_never_an_unread_invoice(string fault)
    {
        var invoicing = await InvoiceWorld.CreateAsync();
        switch (fault)
        {
            case "absent": invoicing.NoWeighings = true; break;
            case "throws": invoicing.Weighings.Fail = true; break;
            case "other-company": invoicing.Weighings.Answer = note => WeighingRecorded(note, company: Guid.NewGuid()); break;
            case "other-note": invoicing.Weighings.Answer = _ => WeighingRecorded(Guid.NewGuid()); break;
            case "recorded-without-facts":
                invoicing.Weighings.Answer = note => new(SalesDispatchWeighingDifferenceStatusV1.Recorded, Company, note, null);
                break;
            case "unavailable":
                invoicing.Weighings.Answer = note => new(SalesDispatchWeighingDifferenceStatusV1.Unavailable, Company, note, null);
                break;
        }

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-weighing-" + fault)));

        Assert.Equal((409, SalesInvoiceProblemCodesV1.InvoiceNotReady), Status(refused));
        Assert.Equal([new SalesDraftFieldError("shares[0]", SalesInvoiceReasonCodesV1.WeighingDifferenceUnavailable)], refused.Errors);
        Assert.Empty(invoicing.Invoices.Stored);
    }

    [Fact]
    public async Task A_recorded_note_without_a_difference_is_invoiced_at_its_kilograms_and_the_owner_is_asked_once_per_note()
    {
        var invoicing = await InvoiceWorld.CreateAsync();

        var created = await invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-weighing-pass"));

        Assert.False(created.IsReplay);
        Assert.Single(invoicing.Invoices.Stored);
        Assert.Single(invoicing.Weighings.Asked);
        Assert.Equal(Company, invoicing.Weighings.Asked[0].Company);
    }

    [Fact]
    public async Task SAT_D343_the_dispatch_read_shows_the_owners_note_totals_and_the_same_invoice_wait()
    {
        var dispatch = new DispatchWorld();
        var noteId = Guid.NewGuid();
        dispatch.Weighings.Answer = note => WeighingRecorded(note, theoretical: 100m, scale: 104.5m, excess: 3.25m,
            state: SalesWeighingDifferenceRecordStateView.PendingApproval, decision: SalesWeighingDifferenceDecisionView.ApprovalRequired);

        var view = await dispatch.Service().GetWeighingDifferenceAsync(noteId);

        Assert.Equal((noteId, Company, SalesDispatchWeighingDifferenceStateView.Recorded), (view.DispatchNoteId, view.CompanyId, view.State));
        Assert.Equal(("100", "104.5", "4.5", "3.25"), (view.TheoreticalKg, view.ScaleKg, view.DifferenceKg, view.TotalExcessKg));
        Assert.Equal((SalesWeighingDifferenceDecisionView.ApprovalRequired, SalesWeighingDifferenceRecordStateView.PendingApproval, false),
            (view.Decision!.Value, view.RecordState!.Value, view.SaleMayProceed));
        Assert.Equal(SalesDispatchWeighingInvoiceWaitView.PendingApproval, view.InvoiceWait);
        Assert.Equal([DispatchRead, DispatchRead], dispatch.Authority.Measured);
    }

    [Fact]
    public async Task A_negative_difference_is_shown_signed_and_a_note_without_a_record_says_it_cannot_be_invoiced()
    {
        var dispatch = new DispatchWorld();
        dispatch.Weighings.Answer = note => WeighingRecorded(note, theoretical: 50m, scale: 49.75m);
        var below = await dispatch.Service().GetWeighingDifferenceAsync(Guid.NewGuid());
        Assert.Equal(("-0.25", "0", SalesDispatchWeighingInvoiceWaitView.KgTransferPending),
            (below.DifferenceKg, below.TotalExcessKg, below.InvoiceWait!.Value));

        dispatch.Weighings.Answer = note => new(SalesDispatchWeighingDifferenceStatusV1.NotRecorded, Company, note, null);
        var none = await dispatch.Service().GetWeighingDifferenceAsync(Guid.NewGuid());
        Assert.Equal((SalesDispatchWeighingDifferenceStateView.NotRecorded, (Guid?)null, (string?)null, false,
            SalesDispatchWeighingInvoiceWaitView.NotRecorded), (none.State, none.RecordId, none.ScaleKg, none.SaleMayProceed, none.InvoiceWait!.Value));
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("throws")]
    [InlineData("other-company")]
    [InlineData("unavailable")]
    [InlineData("beyond-scale")]
    public async Task The_dispatch_weighing_read_fails_closed_on_an_absent_failing_foreign_or_malformed_owner(string fault)
    {
        var dispatch = new DispatchWorld();
        switch (fault)
        {
            case "absent": dispatch.NoWeighings = true; break;
            case "throws": dispatch.Weighings.Fail = true; break;
            case "other-company": dispatch.Weighings.Answer = note => WeighingRecorded(note, company: Guid.NewGuid()); break;
            case "unavailable":
                dispatch.Weighings.Answer = note => new(SalesDispatchWeighingDifferenceStatusV1.Unavailable, Company, note, null);
                break;
            case "beyond-scale": dispatch.Weighings.Answer = note => WeighingRecorded(note, scale: 1.2501m); break;
        }

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => dispatch.Service().GetWeighingDifferenceAsync(Guid.NewGuid()));

        Assert.Equal((503, SalesDispatchProblemCodesV1.WeighingDifferenceProviderUnavailable), Status(refused));
    }

    [Fact]
    public async Task Without_the_dispatch_read_the_weighing_read_is_403_and_the_owner_is_not_asked()
    {
        var dispatch = new DispatchWorld();
        dispatch.Authority.Denied.Add(DispatchRead);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => dispatch.Service().GetWeighingDifferenceAsync(Guid.NewGuid()));

        Assert.Equal((403, "forbidden"), Status(refused));
        Assert.Empty(dispatch.Weighings.Asked);
    }

    private sealed class FakeWeighingDifferences : ISalesDispatchWeighingDifferenceReadPortV1
    {
        public Func<Guid, SalesDispatchWeighingDifferenceReadV1> Answer { get; set; } = note => WeighingRecorded(note);
        public bool Fail { get; set; }
        public List<(Guid Company, Guid Note)> Asked { get; } = [];

        public Task<SalesDispatchWeighingDifferenceReadV1> ReadAsync(Guid companyId, Guid dispatchNoteId,
            CancellationToken cancellationToken = default)
        {
            Asked.Add((companyId, dispatchNoteId));
            return Fail
                ? throw new InvalidOperationException("SENTETIK weighing owner failure")
                : Task.FromResult(Answer(dispatchNoteId));
        }
    }
}
