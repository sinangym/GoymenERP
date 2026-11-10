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
