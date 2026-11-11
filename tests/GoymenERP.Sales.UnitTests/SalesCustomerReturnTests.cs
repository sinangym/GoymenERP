using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.CustomerReturns;
using GoymenERP.Modules.Sales.Domain.Invoices;
using static GoymenERP.Sales.UnitTests.SalesCustomerReturnFixtures;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// API-40 aggregate (SAT-D288, SAT-D290, SAT-D292 (6), SAT-D293): the original invoice must be finalised, the optional shipment link
/// must be the invoice's and hold every line, lines are copied from the original, the preparer and submitter never approve, the
/// finalisation recomputes against the finalised returns of now, and a stored return is restored only when consistent.
/// </summary>
public sealed class SalesCustomerReturnTests
{
    private static readonly Guid ReturnId = Guid.Parse("acacacac-acac-4cac-8cac-acacacacacac");
    private static readonly IReadOnlyDictionary<Guid, IReadOnlyList<SalesCustomerReturnPrior>> NoPriors =
        new Dictionary<Guid, IReadOnlyList<SalesCustomerReturnPrior>>();

    private static SalesInvoice TwoLineInvoice(SalesInvoiceState state = SalesInvoiceState.Finalized) =>
        Invoice(Try, null, state, new StoredLine(3m, 100.00m, 20.00m, 0m, NetUnitPrice: 33.3333m),
            new StoredLine(2m, 10.05m, 2.01m, 0m, NetUnitPrice: 5.025m, VatPercent: 20m));

    private static SalesCustomerReturn Create(SalesInvoice invoice, Guid? shipment, params SalesCustomerReturnLineInput[] lines) =>
        SalesCustomerReturn.Create(ReturnId, Company, invoice, shipment, " IADE-SENTETIK-1 ", Day, lines, NoPriors, Preparer, Now);

    [Fact]
    public void A_linked_return_copies_the_original_line_and_its_totals_are_the_sums_of_the_lines()
    {
        var invoice = TwoLineInvoice();

        var created = Create(invoice, ShipmentOf(1), new SalesCustomerReturnLineInput(LineId(1), 1m));

        Assert.Equal((SalesCustomerReturnState.Draft, 1L, "IADE-SENTETIK-1"), (created.State, created.Version, created.CustomerDocumentNo));
        Assert.Equal((invoice.Id, invoice.DocumentNo, Try, 2), (created.Header.OriginalInvoiceId, created.Header.OriginalDocumentNo,
            created.Header.CurrencyId, created.Header.AmountScale));
        var line = Assert.Single(created.Lines);
        Assert.Equal((LineId(1), 1, ShipmentOf(1), Product, Kg, 33.3333m, 20m), (line.OriginalInvoiceLineId, line.OriginalLineNo,
            line.ShipmentId, line.ProductId, line.UnitId, line.NetUnitPrice, line.VatPercent));
        Assert.Equal(["33.33", "6.67", "0.00", "40.00"], Text(created.Totals));
        Assert.Null(created.FunctionalTotals);
    }

    [Fact]
    public void A_return_needs_a_finalized_invoice_its_shipment_and_lines_and_refuses_an_unlinked_return_of_shipped_lines()
    {
        var invoice = TwoLineInvoice();

        Assert.Equal(SalesRuleCodes.CustomerReturnInvoiceNotFinalized, Refused(() =>
            Create(TwoLineInvoice(SalesInvoiceState.Approved), ShipmentOf(1), new SalesCustomerReturnLineInput(LineId(1), 1m))));
        Assert.Equal(SalesRuleCodes.CustomerReturnShipmentNotOnInvoice, Refused(() =>
            Create(invoice, ShipmentOf(9), new SalesCustomerReturnLineInput(LineId(1), 1m))));
        Assert.Equal(SalesRuleCodes.CustomerReturnShipmentNotOnInvoice, Refused(() =>
            Create(invoice, ShipmentOf(1), new SalesCustomerReturnLineInput(LineId(2), 1m))));
        Assert.Equal(SalesRuleCodes.CustomerReturnLineNotOnInvoice, Refused(() =>
            Create(invoice, null, new SalesCustomerReturnLineInput(LineId(7), 1m))));
        Assert.Equal(SalesRuleCodes.CustomerReturnLinesInvalid, Refused(() =>
            Create(invoice, ShipmentOf(1), new SalesCustomerReturnLineInput(LineId(1), 1m), new SalesCustomerReturnLineInput(LineId(1), 1m))));
        Assert.Equal(SalesRuleCodes.CustomerReturnLinesInvalid, Refused(() => Create(invoice, ShipmentOf(1))));
        Assert.Equal(SalesRuleCodes.CustomerReturnDocumentInvalid, Refused(() => SalesCustomerReturn.Create(ReturnId, Company, invoice,
            ShipmentOf(1), "  ", Day, [new(LineId(1), 1m)], NoPriors, Preparer, Now)));
        // SAT-D293 (4): the invoice lines are whole shipped shares, so a return without a shipment link exceeds the zero balance.
        Assert.Equal(SalesRuleCodes.CustomerReturnUnshippedBalanceExceeded, Refused(() =>
            Create(invoice, null, new SalesCustomerReturnLineInput(LineId(1), 1m))));
    }

    [Fact]
    public void The_preparer_and_the_submitter_never_approve_and_every_command_checks_the_version_and_the_state()
    {
        var created = Create(TwoLineInvoice(), ShipmentOf(1), new SalesCustomerReturnLineInput(LineId(1), 1m));

        Assert.Throws<SalesVersionConflictException>(() => created.Submit(2, "SENTETIK-submitter", Now));
        created.Submit(1, "SENTETIK-submitter", Now);
        Assert.Equal(SalesRuleCodes.StateConflict, Refused(() => created.Submit(2, "SENTETIK-submitter", Now)));
        Assert.Equal(SalesRuleCodes.CustomerReturnSelfApproval, Refused(() => created.Approve(2, Preparer, Now)));
        Assert.Equal(SalesRuleCodes.CustomerReturnSelfApproval, Refused(() => created.Approve(2, "SENTETIK-submitter", Now)));
        Assert.Equal((SalesCustomerReturnState.PendingApproval, 2L), (created.State, created.Version));

        created.Approve(2, Approver, Now);

        Assert.Equal((SalesCustomerReturnState.Approved, 3L, Approver), (created.State, created.Version, created.Facts.ApprovedBy));
    }

    [Fact]
    public void Rejecting_needs_a_pending_return_and_another_actor_and_cancelling_is_allowed_only_before_approval()
    {
        var pending = Create(TwoLineInvoice(), ShipmentOf(1), new SalesCustomerReturnLineInput(LineId(1), 1m));
        Assert.Equal(SalesRuleCodes.StateConflict, Refused(() => pending.Reject(1, Approver, null, Now)));
        pending.Submit(1, "SENTETIK-submitter", Now);

        Assert.Throws<SalesVersionConflictException>(() => pending.Reject(1, Approver, null, Now));
        Assert.Equal(SalesRuleCodes.CustomerReturnSelfApproval, Refused(() => pending.Reject(2, Preparer, null, Now)));
        Assert.Equal(SalesRuleCodes.CustomerReturnSelfApproval, Refused(() => pending.Reject(2, "SENTETIK-submitter", null, Now)));
        Assert.Equal(SalesRuleCodes.ApprovalReasonTooLong, Refused(() =>
            pending.Reject(2, Approver, new string('x', SalesCustomerReturn.MaximumReasonLength + 1), Now)));
        Assert.Equal((SalesCustomerReturnState.PendingApproval, 2L), (pending.State, pending.Version));

        pending.Reject(2, Approver, "  Belge eksik  ", Now);
        Assert.Equal((SalesCustomerReturnState.Rejected, 3L, Approver, "Belge eksik"),
            (pending.State, pending.Version, pending.Facts.RejectedBy, pending.Facts.RejectionReason));
        Assert.Equal(SalesRuleCodes.StateConflict, Refused(() => pending.Cancel(3, Approver, null, Now)));
        Assert.Equal(SalesRuleCodes.StateConflict, Refused(() => pending.Approve(3, Approver, Now)));

        // The preparer may cancel a draft (no maker-checker: nothing is decided), with a blank reason stored as none.
        var draft = Create(TwoLineInvoice(), ShipmentOf(1), new SalesCustomerReturnLineInput(LineId(1), 1m));
        draft.Cancel(1, Preparer, "   ", Now);
        Assert.Equal((SalesCustomerReturnState.Cancelled, 2L, Preparer, (string?)null),
            (draft.State, draft.Version, draft.Facts.CancelledBy, draft.Facts.CancellationReason));
        Assert.Equal(SalesRuleCodes.StateConflict, Refused(() => draft.Submit(2, Preparer, Now)));

        var submitted = Create(TwoLineInvoice(), ShipmentOf(1), new SalesCustomerReturnLineInput(LineId(1), 1m));
        submitted.Submit(1, Preparer, Now);
        submitted.Cancel(2, Preparer, "Müşteri vazgeçti", Now);
        Assert.Equal((SalesCustomerReturnState.Cancelled, 3L), (submitted.State, submitted.Version));

        var approved = Approved(Create(TwoLineInvoice(), ShipmentOf(1), new SalesCustomerReturnLineInput(LineId(1), 1m)));
        Assert.Equal(SalesRuleCodes.StateConflict, Refused(() => approved.Cancel(3, Preparer, null, Now)));
        Assert.Equal(SalesRuleCodes.StateConflict, Refused(() => approved.Reject(3, Approver, null, Now)));
    }

    [Fact]
    public void A_rejected_or_cancelled_return_is_restored_only_with_its_own_decision_facts_and_version()
    {
        var rejected = Create(TwoLineInvoice(), ShipmentOf(1), new SalesCustomerReturnLineInput(LineId(1), 1m));
        rejected.Submit(1, Preparer, Now);
        rejected.Reject(2, Approver, null, Now);
        var cancelled = Create(TwoLineInvoice(), ShipmentOf(1), new SalesCustomerReturnLineInput(LineId(1), 1m));
        cancelled.Cancel(1, Preparer, null, Now);

        SalesCustomerReturn Restore(SalesCustomerReturn source, SalesCustomerReturnFacts facts, long version) =>
            SalesCustomerReturn.Restore(source.Id, source.CompanyId, source.Header, source.ShipmentId, source.CustomerDocumentNo,
                source.CustomerDocumentDate, source.State, facts, source.CreatedBy, source.CreatedAtUtc, version, source.UpdatedAtUtc,
                source.Lines);

        Assert.Equal(SalesCustomerReturnState.Rejected, Restore(rejected, rejected.Facts, rejected.Version).State);
        Assert.Equal(SalesCustomerReturnState.Cancelled, Restore(cancelled, cancelled.Facts, cancelled.Version).State);
        Assert.Throws<InvalidOperationException>(() => Restore(rejected, rejected.Facts, 2));
        Assert.Throws<InvalidOperationException>(() => Restore(cancelled, cancelled.Facts, 1));
        Assert.Throws<InvalidOperationException>(() => Restore(rejected, rejected.Facts with { RejectedBy = Preparer }, 3));
        Assert.Throws<InvalidOperationException>(() => Restore(rejected, rejected.Facts with { RejectionReason = "" }, 3));
        Assert.Throws<InvalidOperationException>(() => Restore(rejected, rejected.Facts with { CancelledBy = Approver, CancelledAtUtc = Now }, 3));
        Assert.Throws<InvalidOperationException>(() => Restore(cancelled, cancelled.Facts with { SubmittedBy = Preparer }, 2));
        Assert.Throws<InvalidOperationException>(() => Restore(cancelled, cancelled.Facts with { ApprovedBy = Approver, ApprovedAtUtc = Now }, 2));
    }

    [Fact]
    public void Finalizing_recomputes_against_the_finalized_returns_of_now_and_refuses_changed_amounts_or_an_exceeded_quantity()
    {
        var invoice = TwoLineInvoice();
        var approved = Approved(Create(invoice, ShipmentOf(1), new SalesCustomerReturnLineInput(LineId(1), 1m)));
        var other = SalesCustomerReturnCalculator.Calculate(SalesCustomerReturnOriginalLine.Of(invoice.Lines[0]), [], 1m);
        var oneFinalized = new Dictionary<Guid, IReadOnlyList<SalesCustomerReturnPrior>> { [LineId(1)] = [Prior(other)] };
        var allFinalized = new Dictionary<Guid, IReadOnlyList<SalesCustomerReturnPrior>>
        {
            [LineId(1)] = [Prior(SalesCustomerReturnCalculator.Calculate(SalesCustomerReturnOriginalLine.Of(invoice.Lines[0]), [], 3m))],
        };

        // Another 1-of-3 return finalised meanwhile: the line total stays 40.00 but this one is no longer computed from the same
        // priors, so its stored line is not the calculator's any more only when the amounts change; here they are equal.
        approved.Finalize(3, "SENTETIK-finalizer", Now, invoice, oneFinalized);
        Assert.Equal((SalesCustomerReturnState.Finalized, 4L), (approved.State, approved.Version));

        var exceeded = Approved(Create(invoice, ShipmentOf(1), new SalesCustomerReturnLineInput(LineId(1), 1m)));
        Assert.Equal(SalesRuleCodes.CustomerReturnQuantityExceeded, Refused(() =>
            exceeded.Finalize(3, "SENTETIK-finalizer", Now, invoice, allFinalized)));

        // A 1-of-2 return of line 2 approved with no priors becomes the line's last return once the other half is finalised.
        var changed = Approved(Create(invoice, ShipmentOf(2), new SalesCustomerReturnLineInput(LineId(2), 1m)));
        var half = SalesCustomerReturnCalculator.Calculate(SalesCustomerReturnOriginalLine.Of(invoice.Lines[1]), [], 1m);
        var halfFinalized = new Dictionary<Guid, IReadOnlyList<SalesCustomerReturnPrior>> { [LineId(2)] = [Prior(half)] };
        Assert.Equal(SalesRuleCodes.CustomerReturnAmountsChanged, Refused(() =>
            changed.Finalize(3, "SENTETIK-finalizer", Now, invoice, halfFinalized)));
        Assert.Equal((SalesCustomerReturnState.Approved, 3L), (changed.State, changed.Version));
    }

    [Fact]
    public void A_stored_return_is_restored_only_when_its_state_facts_and_lines_agree()
    {
        var created = Approved(Create(TwoLineInvoice(), ShipmentOf(1), new SalesCustomerReturnLineInput(LineId(1), 1m)));

        var restored = SalesCustomerReturn.Restore(created.Id, created.CompanyId, created.Header, created.ShipmentId,
            created.CustomerDocumentNo, created.CustomerDocumentDate, created.State, created.Facts, created.CreatedBy, created.CreatedAtUtc,
            created.Version, created.UpdatedAtUtc, created.Lines);
        Assert.Equal(created.Totals, restored.Totals);

        Assert.Throws<InvalidOperationException>(() => SalesCustomerReturn.Restore(created.Id, created.CompanyId, created.Header,
            created.ShipmentId, created.CustomerDocumentNo, created.CustomerDocumentDate, SalesCustomerReturnState.Finalized, created.Facts,
            created.CreatedBy, created.CreatedAtUtc, created.Version, created.UpdatedAtUtc, created.Lines));
        Assert.Throws<InvalidOperationException>(() => SalesCustomerReturn.Restore(created.Id, created.CompanyId, created.Header,
            created.ShipmentId, created.CustomerDocumentNo, created.CustomerDocumentDate, created.State,
            created.Facts with { ApprovedBy = Preparer }, created.CreatedBy, created.CreatedAtUtc, created.Version, created.UpdatedAtUtc,
            created.Lines));
        Assert.Throws<InvalidOperationException>(() => SalesCustomerReturn.Restore(created.Id, created.CompanyId, created.Header,
            ShipmentOf(2), created.CustomerDocumentNo, created.CustomerDocumentDate, created.State, created.Facts, created.CreatedBy,
            created.CreatedAtUtc, created.Version, created.UpdatedAtUtc, created.Lines));
    }

    private static SalesCustomerReturn Approved(SalesCustomerReturn created)
    {
        created.Submit(1, Preparer, Now);
        created.Approve(2, Approver, Now);
        return created;
    }

    private static string Refused(Action action) => Assert.Throws<SalesRuleException>(action).Code;
}
