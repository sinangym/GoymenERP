using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D103 / SAT-D104 / SAT-D111 (EK13) over SYNTHETIC owners: while a newer revision of an approved order waits for
/// re-approval, physical shipping goes on under the LAST APPROVED revision — the Sales line port answers that revision with the
/// shipping cap (never NotApproved, never the new values) — and the order's invoicing waits (the status axis gate); a rejection
/// keeps the last approved revision for shipping (the cap restored) and opens invoicing at its prices; an approval moves every
/// consumer to the new revision. A quantity revision no longer waits for allocations or loading instructions; it may not go
/// below what is shipped (nor, fail-closed, below what is loaded).
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task A_price_revision_keeps_shipping_on_the_last_approved_revision_and_invoicing_waits_until_the_decision()
    {
        var world = new World().AllowDiscountsUpTo(10m);
        Owners(world);
        var (approved, line) = await AcceptedApprovedAsync(world);
        var port = LinePort(world);
        var current = await port.GetApprovedLineAsync(approved.Id, line);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.Approved, 0, "1.25", SalesOrderLineRevisionBasisV1.CurrentRevision, (string?)null, "1.25"),
            (current.Outcome, current.Line!.Revision, current.Line.QuantityText, current.Line.RevisionBasis, current.Line.PendingApprovalState,
                current.Line.ShippingCapQuantityText));
        Assert.Equal((SalesDraftInvoicingGatesV1.Open, SalesDraftApiV1.Rev0), await GateAsync(world, approved.Id));

        // The price changes (K3 commercial revision): Rev.1 back in preparation, Rev.0 archived.
        var revised = await world.Service().ReviseCommercialAsync(approved.Id,
            Revision("5", new SalesDraftLineCommercialV1(line, "13.00", KgUnit, "5")), Metadata("k-price"));
        Assert.Equal(("Preparing", "Rev.1"), (revised.Detail.Draft.ApprovalState, revised.Detail.Draft.Revision));

        // Shipping goes on under Rev.0 — its identity and quantity, the cap — and nothing of Rev.1 reaches the consumer.
        await AssertLastApprovedAsync(port, approved.Id, line, "Preparing", "1.25", "1.25", "6");
        var listed = Assert.Single((await port.ListApprovedLinesAsync(10, null)).Items);
        Assert.Equal((0, "1.25", SalesOrderLineRevisionBasisV1.LastApprovedRevision, "Preparing", "1.25"),
            (listed.Revision, listed.QuantityText, listed.RevisionBasis, listed.PendingApprovalState, listed.ShippingCapQuantityText));
        // Invoicing waits for the decision: no price basis.
        Assert.Equal((SalesDraftInvoicingGatesV1.AwaitingReapproval, (string?)null), await GateAsync(world, approved.Id));

        // SAT-D43: the manual price is checked by another person first (still Preparing), then submitted.
        Assert.Equal("7", await ApproveDiscountChecksAsync(world, approved.Id, "6", line));
        await AssertLastApprovedAsync(port, approved.Id, line, "Preparing", "1.25", "1.25", "7");
        await world.Service().SubmitDraftAsync(approved.Id, Command("7"), Metadata("k-price-submit"));
        await AssertLastApprovedAsync(port, approved.Id, line, "PendingApproval", "1.25", "1.25", "8");
        Assert.Equal((SalesDraftInvoicingGatesV1.AwaitingReapproval, (string?)null), await GateAsync(world, approved.Id));

        // Rejected: shipping still under Rev.0, and the order is invoiced at Rev.0's (last approved) prices.
        await world.Service().RejectDraftAsync(approved.Id, Command("8", "SENTETIK fiyat uygun değil"), Metadata("k-price-reject"));
        await AssertLastApprovedAsync(port, approved.Id, line, "Rejected", "1.25", "1.25", "9");
        Assert.Equal((SalesDraftInvoicingGatesV1.Open, SalesDraftApiV1.Rev0), await GateAsync(world, approved.Id));

        // Resubmitted and approved: every consumer moves to Rev.1 (the new price applies to the invoice).
        await world.Service().SubmitDraftAsync(approved.Id, Command("9"), Metadata("k-price-resubmit"));
        await world.Service().ApproveDraftAsync(approved.Id, Command("10"), Metadata("k-price-approve"));
        // SAT-D114 (EK14): internally approved but the customer has not accepted Rev.1 yet — invoicing waits, no price basis,
        // and (SAT-D113) shipping does not go on under the new revision either.
        Assert.Equal((SalesDraftInvoicingGatesV1.AwaitingCustomerAcceptance, (string?)null), await GateAsync(world, approved.Id));
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.NotApproved, "CustomerAcceptancePending"),
            ((await port.GetApprovedLineAsync(approved.Id, line)).Outcome, (await port.GetApprovedLineAsync(approved.Id, line)).ApprovalState));
        await world.Service().RecordCustomerAcceptanceAsync(approved.Id, Command("11"), Metadata("k-price-accept"));
        var after = await port.GetApprovedLineAsync(approved.Id, line);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.Approved, 1, SalesOrderLineRevisionBasisV1.CurrentRevision, (string?)null),
            (after.Outcome, after.Line!.Revision, after.Line.RevisionBasis, after.Line.PendingApprovalState));
        Assert.Equal((SalesDraftInvoicingGatesV1.Open, "Rev.1"), await GateAsync(world, approved.Id));
    }

    [Fact]
    public async Task A_quantity_increase_ships_only_up_to_the_last_approved_quantity_until_its_approval()
    {
        var world = new World();
        var (approved, line) = await AcceptedApprovedAsync(world);
        var (allocations, loadings) = Owners(world);
        var port = LinePort(world);

        await world.Service().ReviseQuantityAsync(approved.Id,
            new SalesDraftQuantityRevisionRequest("5", "SENTETIK müşteri 2 kg istedi", [new SalesDraftLineQuantityV1(line, "2")]), Metadata("k-up"));

        // The increase is not shipped before its approval: the cap stays Rev.0's 1.25 and the answer never says 2.
        await AssertLastApprovedAsync(port, approved.Id, line, "Preparing", "1.25", "1.25", "6");
        Assert.Empty(allocations.Asked);
        Assert.Equal([approved.Id], loadings.Asked);

        await world.Service().SubmitDraftAsync(approved.Id, Command("6"), Metadata("k-up-submit"));
        await AssertLastApprovedAsync(port, approved.Id, line, "PendingApproval", "1.25", "1.25", "7");
        await world.Service().ApproveDraftAsync(approved.Id, Command("7"), Metadata("k-up-approve"));
        await world.Service().RecordCustomerAcceptanceAsync(approved.Id, Command("8"), Metadata("k-up-accept"));
        var after = await port.GetApprovedLineForLoadingAsync(approved.Id, line, Day);
        Assert.Equal((1, "2", "2", "2", SalesOrderLineLoadingLimitSourcesV1.ApprovedOrderQuantity),
            (after.Line!.Revision, after.Line.QuantityText, after.Line.ShippingCapQuantityText, after.Line.LoadingLimit!.MaximumAggregateQuantityText,
                after.Line.LoadingLimit.SourceOwner));
    }

    [Fact]
    public async Task A_decrease_waiting_for_approval_caps_shipping_at_the_requested_quantity_and_a_rejection_restores_the_last_approved_one()
    {
        var world = new World();
        var (approved, line) = await AcceptedApprovedAsync(world);
        var (_, loadings) = Owners(world);
        // An instruction made under Rev.0 for its full quantity: it no longer blocks the revision, and the loading read still
        // answers while it stands above the decreased quantity (the bound is the largest approved quantity).
        loadings.Answer = order => new(Company, order, [new(line, KgUnit, 1, 1.25m, 0m, 0m, 0m)]);
        var port = LinePort(world);

        await world.Service().ReviseQuantityAsync(approved.Id,
            new SalesDraftQuantityRevisionRequest("5", "SENTETIK müşteri 1 kg istedi", [new SalesDraftLineQuantityV1(line, "1")]), Metadata("k-down"));

        // SAT-D111: min(last approved 1.25, requested 1) = 1 — shipping stops at the new quantity; the identity stays Rev.0's.
        await AssertLastApprovedAsync(port, approved.Id, line, "Preparing", "1.25", "1", "6");
        var listed = Assert.Single((await port.ListApprovedLinesAsync(10, null)).Items);
        Assert.Equal(("1.25", "1"), (listed.QuantityText, listed.ShippingCapQuantityText));
        var progress = await world.Service().GetDraftLoadingProgressAsync(approved.Id);
        Assert.Equal(("1", "1.25"), (progress.Lines[0].Ordered, progress.Lines[0].Instructed));

        await world.Service().SubmitDraftAsync(approved.Id, Command("6"), Metadata("k-down-submit"));
        await AssertLastApprovedAsync(port, approved.Id, line, "PendingApproval", "1.25", "1", "7");

        // Rejected: shipping goes on up to the last approved quantity again.
        await world.Service().RejectDraftAsync(approved.Id, Command("7"), Metadata("k-down-reject"));
        await AssertLastApprovedAsync(port, approved.Id, line, "Rejected", "1.25", "1.25", "8");
    }

    [Fact]
    public async Task A_quantity_revision_is_not_blocked_by_allocations_or_instructions_but_never_goes_below_the_shipped_or_loaded_quantity()
    {
        var world = new World();
        var (approved, line) = await AcceptedApprovedAsync(world);
        var (allocations, loadings) = Owners(world);
        allocations.Answer = order => new(Company, order, [new(line, 1, 1.25m)]);
        loadings.Answer = order => new(Company, order, [new(line, KgUnit, 1, 1.25m, 1.2m, 1.1m, 1m)]);

        // Below the shipped 1.1: refused on the line, a 422 not recorded (the floor moves with Logistics), nothing changes.
        var belowShipped = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseQuantityAsync(approved.Id,
            new SalesDraftQuantityRevisionRequest("5", "neden", [new SalesDraftLineQuantityV1(line, "1.05")]), Metadata("k-floor-1")));
        Assert.Equal((422, "validation_failed", "lines[0].quantity", SalesRuleCodes.RevisionBelowShipped),
            (belowShipped.Status, belowShipped.Code, belowShipped.Errors[0].Field, belowShipped.Errors[0].Code));
        // Below the loaded 1.2 (not yet shipped): refused too — fail-closed, Logistics' shipment does not re-check the cap.
        var belowLoaded = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseQuantityAsync(approved.Id,
            new SalesDraftQuantityRevisionRequest("5", "neden", [new SalesDraftLineQuantityV1(line, "1.15")]), Metadata("k-floor-2")));
        Assert.Equal((422, "lines[0].quantity", SalesRuleCodes.RevisionBelowLoaded),
            (belowLoaded.Status, belowLoaded.Errors[0].Field, belowLoaded.Errors[0].Code));
        Assert.Empty(world.Store.Refusals);
        Assert.Equal(("5", "Rev.0"), ((await world.Service().GetDraftDetailAsync(approved.Id)).Draft.Version,
            (await world.Service().GetDraftDetailAsync(approved.Id)).Draft.Revision));

        // At the loaded quantity: recorded although an active allocation and the instruction stand — Sales releases nothing
        // and does not even ask the allocation owner (an allocation above the new quantity is Inventory's concern).
        var revised = await world.Service().ReviseQuantityAsync(approved.Id,
            new SalesDraftQuantityRevisionRequest("5", "neden", [new SalesDraftLineQuantityV1(line, "1.2")]), Metadata("k-floor-3"));
        Assert.Equal(("Rev.1", "1.2"), (revised.Detail.Draft.Revision, revised.Detail.Lines[0].Quantity));
        Assert.Empty(allocations.Asked);
        Assert.Equal(3, loadings.Asked.Count);
    }

    [Fact]
    public async Task A_line_added_in_the_pending_revision_and_an_unaccepted_last_approved_revision_stay_not_approved()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        Owners(world);
        var port = LinePort(world);

        // Rev.0 approved but never accepted by the customer (required here): during the re-approval it is not a shipping basis.
        await world.Service().ReviseQuantityAsync(approved.Draft.Id,
            new SalesDraftQuantityRevisionRequest("4", "neden", [new SalesDraftLineQuantityV1(line, "2")]), Metadata("k-unaccepted"));
        var answer = await port.GetApprovedLineAsync(approved.Draft.Id, line);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.NotApproved, "CustomerAcceptancePending"), (answer.Outcome, answer.ApprovalState));
        Assert.Empty((await port.ListApprovedLinesAsync(10, null)).Items);
        Assert.Equal((SalesDraftInvoicingGatesV1.AwaitingReapproval, (string?)null), await GateAsync(world, approved.Draft.Id));

        // SAT-D114: rejected after an UNACCEPTED last approved revision — no accepted basis to invoice at.
        await world.Service().SubmitDraftAsync(approved.Draft.Id, Command("5"), Metadata("k-unaccepted-submit"));
        await world.Service().RejectDraftAsync(approved.Draft.Id, Command("6"), Metadata("k-unaccepted-reject"));
        Assert.Equal((SalesDraftInvoicingGatesV1.AwaitingCustomerAcceptance, (string?)null), await GateAsync(world, approved.Draft.Id));
    }

    [Fact]
    public async Task An_approved_revision_the_customer_has_not_accepted_keeps_invoicing_waiting()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        // SAT-D114 (EK14): customer acceptance is required here and not recorded — invoicing waits, no price basis.
        Assert.Equal((SalesDraftInvoicingGatesV1.AwaitingCustomerAcceptance, (string?)null), await GateAsync(world, approved.Draft.Id));
        await world.Service().RecordCustomerAcceptanceAsync(approved.Draft.Id, Command("4"), Metadata("k-accept"));
        Assert.Equal((SalesDraftInvoicingGatesV1.Open, SalesDraftApiV1.Rev0), await GateAsync(world, approved.Draft.Id));
    }

    [Fact]
    public async Task A_rejected_decrease_resubmitted_after_loading_up_to_the_restored_cap_is_refused_at_submission_and_approval()
    {
        var world = new World();
        var (approved, line) = await AcceptedApprovedAsync(world);
        var (_, loadings) = Owners(world);
        var port = LinePort(world);

        // Rev.1 decreases 1.25 → 1 (nothing loaded), is submitted and REJECTED: the cap is the last approved 1.25 again.
        await world.Service().ReviseQuantityAsync(approved.Id,
            new SalesDraftQuantityRevisionRequest("5", "SENTETIK azaltma", [new SalesDraftLineQuantityV1(line, "1")]), Metadata("k-p1-down"));
        await world.Service().SubmitDraftAsync(approved.Id, Command("6"), Metadata("k-p1-submit"));
        await world.Service().RejectDraftAsync(approved.Id, Command("7"), Metadata("k-p1-reject"));
        await AssertLastApprovedAsync(port, approved.Id, line, "Rejected", "1.25", "1.25", "8");

        // Logistics loads up to the restored cap; the SAME reduced Rev.1 resubmitted is refused below the loaded total (SAT-D112),
        // a 422 on the line, not recorded, nothing changes.
        loadings.Answer = order => new(Company, order, [new(line, KgUnit, 1, 1.25m, 1.25m, 0m, 0m)]);
        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().SubmitDraftAsync(approved.Id, Command("8"), Metadata("k-p1-resubmit")));
        Assert.Equal((422, "validation_failed", "lines[0].quantity", SalesRuleCodes.RevisionBelowLoaded),
            (refused.Status, refused.Code, refused.Errors[0].Field, refused.Errors[0].Code));
        Assert.Empty(world.Store.Refusals);
        Assert.Equal(("Rejected", "8"), ((await world.Service().GetDraftDetailAsync(approved.Id)).Draft.ApprovalState,
            (await world.Service().GetDraftDetailAsync(approved.Id)).Draft.Version));

        // The load reduced in Logistics: the submission passes; a load committed before the approval is caught there too.
        loadings.Answer = order => new(Company, order, [new(line, KgUnit, 1, 1.25m, 1m, 0.5m, 0m)]);
        await world.Service().SubmitDraftAsync(approved.Id, Command("8"), Metadata("k-p1-resubmit-2"));
        loadings.Answer = order => new(Company, order, [new(line, KgUnit, 1, 1.25m, 1.1m, 1.05m, 0m)]);
        var atApproval = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveDraftAsync(approved.Id, Command("9"), Metadata("k-p1-approve")));
        Assert.Equal((422, "lines[0].quantity", SalesRuleCodes.RevisionBelowShipped),
            (atApproval.Status, atApproval.Errors[0].Field, atApproval.Errors[0].Code));
        // An unreadable owner is 503 at the approval, never an assumed zero; nothing is recorded.
        loadings.Answer = order => new(Guid.NewGuid(), order, []);
        Assert.Equal((503, SalesDraftProblemCodesV1.RevisionImpactUnavailable), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveDraftAsync(approved.Id, Command("9"), Metadata("k-p1-approve-2")))));
        Assert.Empty(world.Store.Refusals);

        loadings.Answer = order => new(Company, order, [new(line, KgUnit, 1, 1.25m, 1m, 1m, 0m)]);
        var approvedRev1 = await world.Service().ApproveDraftAsync(approved.Id, Command("9"), Metadata("k-p1-approve-3"));
        Assert.Equal(("Approved", "Rev.1", "1"), (approvedRev1.Detail.Draft.ApprovalState, approvedRev1.Detail.Draft.Revision,
            approvedRev1.Detail.Lines[0].Quantity));
    }

    [Fact]
    public async Task A_rejected_revision_changed_again_caps_shipping_like_a_pending_decrease()
    {
        var world = new World();
        var (approved, line) = await AcceptedApprovedAsync(world);
        Owners(world);
        var port = LinePort(world);
        await world.Service().ReviseQuantityAsync(approved.Id,
            new SalesDraftQuantityRevisionRequest("5", "SENTETIK azaltma", [new SalesDraftLineQuantityV1(line, "1.2")]), Metadata("k-re-1"));
        await world.Service().SubmitDraftAsync(approved.Id, Command("6"), Metadata("k-re-submit"));
        await world.Service().RejectDraftAsync(approved.Id, Command("7"), Metadata("k-re-reject"));
        await AssertLastApprovedAsync(port, approved.Id, line, "Rejected", "1.25", "1.25", "8");

        // Changed again while Rejected (K3-REVIEW-B P1-1, second path): a decrease being prepared again caps at min(1.25, 1.1).
        await world.Service().ReviseQuantityAsync(approved.Id,
            new SalesDraftQuantityRevisionRequest("8", "SENTETIK yeni azaltma", [new SalesDraftLineQuantityV1(line, "1.1")]), Metadata("k-re-2"));
        await AssertLastApprovedAsync(port, approved.Id, line, "Rejected", "1.25", "1.1", "9");
    }

    [Fact]
    public async Task A_never_approved_order_has_no_shipping_answer_and_a_closed_invoicing_gate()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-new"))).Detail;
        var port = LinePort(world);

        var answer = await port.GetApprovedLineAsync(created.Draft.Id, created.Lines[0].LineId);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.NotApproved, "Preparing"), (answer.Outcome, answer.ApprovalState));
        Assert.Equal((SalesDraftInvoicingGatesV1.NotApproved, (string?)null), await GateAsync(world, created.Draft.Id));
    }

    [Fact]
    public void The_line_port_records_carry_no_price_member_and_name_their_revision_basis()
    {
        string[] barred = ["price", "amount", "discount", "currency", "rate", "payment", "money", "tax", "vat", "surcharge"];
        foreach (var type in new[] { typeof(SalesOrderLineReferenceV1), typeof(SalesApprovedLineReferenceV1), typeof(SalesOrderLineLoadingLimitV1) })
        {
            Assert.DoesNotContain(type.GetProperties(), property =>
                barred.Any(word => property.Name.Contains(word, StringComparison.OrdinalIgnoreCase)));
        }

        // The contract delta (EK13): the three new members, defaulting to the current revision for an older producer.
        foreach (var type in new[] { typeof(SalesOrderLineReferenceV1), typeof(SalesApprovedLineReferenceV1) })
        {
            var names = type.GetProperties().Select(property => property.Name).ToArray();
            Assert.Equal(["RevisionBasis", "PendingApprovalState", "ShippingCapQuantityText"], names[^3..]);
        }

        Assert.Equal(SalesOrderLineRevisionBasisV1.CurrentRevision,
            new SalesApprovedLineReferenceV1(Company, Guid.NewGuid(), "SO", Guid.NewGuid(), 1, Party, Account, Product, "7", "C", "N", KgUnit, "kg",
                "1", null, 0).RevisionBasis);
        Assert.Equal(("CurrentRevision", "LastApprovedRevision", "SalesOrderReapprovalShippingCap"),
            (SalesOrderLineRevisionBasisV1.CurrentRevision, SalesOrderLineRevisionBasisV1.LastApprovedRevision,
                SalesOrderLineLoadingLimitSourcesV1.ReapprovalShippingCap));
        Assert.Equal(("NotApproved", "Open", "AwaitingReapproval"),
            (SalesDraftInvoicingGatesV1.NotApproved, SalesDraftInvoicingGatesV1.Open, SalesDraftInvoicingGatesV1.AwaitingReapproval));
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------

    /// <summary>A one-line draft (1.25 kg) approved and accepted by the customer: version 5.</summary>
    private static async Task<(SalesOrderDraftSummaryView Draft, Guid Line)> AcceptedApprovedAsync(World world)
    {
        var approved = await ApprovedAsync(world);
        var accepted = await world.Service().RecordCustomerAcceptanceAsync(approved.Draft.Id, Command("4"), Metadata("k-accept-rev0"));
        Assert.Equal("5", accepted.Detail.Draft.Version);
        return (accepted.Detail.Draft, approved.Lines[0].LineId);
    }

    private static SalesOrderLineReferencePort LinePort(World world)
    {
        var company = new CompanyContext();
        company.Set(Company);
        return new SalesOrderLineReferencePort(world.Store, company);
    }

    private static async Task<(string Gate, string? PriceBasisRevision)> GateAsync(World world, Guid draftId)
    {
        var invoicing = (await world.Service().GetDraftStatusAsync(draftId)).Invoicing;
        Assert.Equal(SalesDraftStatusAxisStatesV1.NoSource, invoicing.State);
        return (invoicing.Gate, invoicing.PriceBasisRevision);
    }

    /// <summary>The port answers Rev.0 (the last approved revision) with its quantity and the cap, on both reads.</summary>
    private static async Task AssertLastApprovedAsync(SalesOrderLineReferencePort port, Guid draftId, Guid line, string pendingState,
        string quantity, string cap, string version)
    {
        var answer = await port.GetApprovedLineForLoadingAsync(draftId, line, Day);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.Approved, (string?)null), (answer.Outcome, answer.ApprovalState));
        var stood = answer.Line!;
        Assert.Equal((0, quantity, SalesOrderLineRevisionBasisV1.LastApprovedRevision, pendingState, cap, long.Parse(version, System.Globalization.CultureInfo.InvariantCulture)),
            (stood.Revision, stood.QuantityText, stood.RevisionBasis, stood.PendingApprovalState, stood.ShippingCapQuantityText, stood.Version));
        Assert.Equal((cap, SalesOrderLineLoadingLimitSourcesV1.ReapprovalShippingCap, stood.Version),
            (stood.LoadingLimit!.MaximumAggregateQuantityText, stood.LoadingLimit.SourceOwner, stood.LoadingLimit.SalesOrderVersion));
        var plain = await port.GetApprovedLineAsync(draftId, line);
        Assert.Equal((0, quantity, cap, (SalesOrderLineLoadingLimitV1?)null),
            (plain.Line!.Revision, plain.Line.QuantityText, plain.Line.ShippingCapQuantityText, plain.Line.LoadingLimit));
    }
}
