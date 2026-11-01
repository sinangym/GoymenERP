using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D115 (EK14) over SYNTHETIC owners: the customer rejects the internally approved revision awaiting its acceptance (reason
/// required, under the customer-acceptance name). Nothing is deleted: the rejected revision stays recorded as rejected, and the
/// line port (SAT-D113), the invoicing gate (SAT-D114/D121), the realization basis and the acceptance axis continue on the
/// revision the customer last accepted. A newer revision is measured against that accepted revision too.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task SAT_D115_a_customer_rejection_keeps_shipping_and_invoicing_on_the_last_accepted_revision_and_nothing_is_deleted()
    {
        var world = new World();
        var (approved, line) = await AcceptedApprovedAsync(world);
        // SAT-D111/D112: the revision's submission reads Logistics' floor — the owners answer nothing here.
        Owners(world);
        var port = LinePort(world);

        // Rev.1: 1.25 → 2 kg, internally approved, waiting for the customer (SAT-D113: nothing ships; SAT-D114: invoicing waits).
        await world.Service().ReviseQuantityAsync(approved.Id,
            new SalesDraftQuantityRevisionRequest("5", "SENTETIK müşteri 2 kg istedi", [new SalesDraftLineQuantityV1(line, "2")]), Metadata("k-rj-up"));
        await world.Service().SubmitDraftAsync(approved.Id, Command("6"), Metadata("k-rj-submit"));
        await world.Service().ApproveDraftAsync(approved.Id, Command("7"), Metadata("k-rj-approve"));
        var waiting = await port.GetApprovedLineAsync(approved.Id, line);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.NotApproved, "CustomerAcceptancePending"), (waiting.Outcome, waiting.ApprovalState));
        Assert.Equal((SalesDraftInvoicingGatesV1.AwaitingCustomerAcceptance, (string?)null), await GateAsync(world, approved.Id));

        // The reason is required: 422, not recorded (the same key may carry a reason next).
        var blank = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RecordCustomerRejectionAsync(approved.Id, Command("8", "   "), Metadata("k-rj-reject")));
        Assert.Equal((422, "validation_failed", "reason", SalesRuleCodes.RevisionReasonRequired),
            (blank.Status, blank.Code, blank.Errors[0].Field, blank.Errors[0].Code));
        Assert.DoesNotContain(world.Store.Transitions, item => item.Command == SalesDraftCommands.RecordCustomerRejectionV1);

        var rejected = await world.Service().RecordCustomerRejectionAsync(approved.Id, Command("8", "  SENTETIK müşteri 2 kg istemedi "),
            Metadata("k-rj-reject"));
        Assert.Equal(("9", "Rev.1", "Approved", SalesDraftApiV1.Rev0, false), (rejected.Detail.Draft.Version, rejected.Detail.Draft.Revision,
            rejected.Detail.Draft.ApprovalState, rejected.Detail.CustomerAcceptance.AcceptedRevision,
            rejected.Detail.CustomerAcceptance.IsCurrentRevisionAccepted));
        var recorded = Assert.Single(rejected.Detail.CustomerAcceptance.Rejections!);
        Assert.Equal(("Rev.1", Actor, "SENTETIK müşteri 2 kg istemedi"), (recorded.Revision, recorded.RejectedBy, recorded.Reason));
        // Still an order: it goes on as the customer accepted it.
        Assert.Equal(SalesCommercialStageV1.Order, rejected.Detail.Draft.CommercialStage);
        // The rejected Rev.1 stays as it is (2 kg); nothing is reverted or deleted.
        Assert.Equal("2", Assert.Single(rejected.Detail.Lines).Quantity);
        var transition = world.Store.Transitions[^1];
        Assert.Equal((SalesDraftCommands.RecordCustomerRejectionV1, SalesPermissionManifestV1.OrdersCustomerAcceptanceRecord,
                "sales.order-draft.customer-rejection-recorded.v1"),
            (transition.Command, transition.Envelope.PermissionCode, transition.Audit.EventType));

        // SAT-D115: shipping continues on Rev.0 (1.25 kg, the cap), never on the rejected 2 kg.
        var answer = await port.GetApprovedLineForLoadingAsync(approved.Id, line, Day);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.Approved, 0, "1.25", "1.25", SalesOrderLineRevisionBasisV1.LastApprovedRevision, "CustomerRejected"),
            (answer.Outcome, answer.Line!.Revision, answer.Line.QuantityText, answer.Line.ShippingCapQuantityText, answer.Line.RevisionBasis,
                answer.Line.PendingApprovalState));
        Assert.Equal(("1.25", SalesOrderLineLoadingLimitSourcesV1.ReapprovalShippingCap),
            (answer.Line.LoadingLimit!.MaximumAggregateQuantityText, answer.Line.LoadingLimit.SourceOwner));
        var listed = Assert.Single((await port.ListApprovedLinesAsync(10, null)).Items);
        Assert.Equal((0, "1.25", "1.25"), (listed.Revision, listed.QuantityText, listed.ShippingCapQuantityText));
        // Invoicing opens at Rev.0's prices; the acceptance axis says the current revision was rejected; realization on Rev.0.
        Assert.Equal((SalesDraftInvoicingGatesV1.Open, SalesDraftApiV1.Rev0), await GateAsync(world, approved.Id));
        var status = await world.Service().GetDraftStatusAsync(approved.Id);
        Assert.Equal((SalesDraftStatusAxisStatesV1.Rejected, SalesDraftApiV1.Rev0, SalesDraftApiV1.Rev0),
            (status.CustomerAcceptance.State, status.CustomerAcceptance.AcceptedRevision, status.Realization.BasisRevision));
        Assert.Equal("1.25", Assert.Single(status.Realization.Lines).Ordered);

        // The same key and content: the recorded result again; nothing is written twice.
        var replay = await world.Service().RecordCustomerRejectionAsync(approved.Id, Command("8", "  SENTETIK müşteri 2 kg istemedi "),
            Metadata("k-rj-reject", correlation: "retry"));
        Assert.True(replay.IsReplay);
        Assert.Single(world.Store.Transitions, item => item.Command == SalesDraftCommands.RecordCustomerRejectionV1);

        // A rejected revision is neither rejected again nor accepted afterwards (409, recorded).
        var again = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RecordCustomerRejectionAsync(approved.Id, Command("9", "SENTETIK"), Metadata("k-rj-again")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), (again.Status, again.Code));
        var accept = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RecordCustomerAcceptanceAsync(approved.Id, Command("9"), Metadata("k-rj-accept")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), (accept.Status, accept.Code));
        // A stale version: the recorded 409 version_conflict.
        var stale = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RecordCustomerRejectionAsync(approved.Id, Command("8", "SENTETIK"), Metadata("k-rj-stale")));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict), (stale.Status, stale.Code));

        // A new revision (Rev.2, 1 kg) is measured against the ACCEPTED Rev.0: shipping goes on there, the decrease caps it.
        await world.Service().ReviseQuantityAsync(approved.Id,
            new SalesDraftQuantityRevisionRequest("9", "SENTETIK yeni teklif 1 kg", [new SalesDraftLineQuantityV1(line, "1")]), Metadata("k-rj-down"));
        await AssertLastApprovedAsync(port, approved.Id, line, "Preparing", "1.25", "1", "10");
        Assert.Equal((SalesDraftInvoicingGatesV1.AwaitingReapproval, (string?)null), await GateAsync(world, approved.Id));
        var reread = await world.Service().GetDraftDetailAsync(approved.Id);
        Assert.Equal(("Rev.2", "Rev.1"), (reread.Draft.Revision, Assert.Single(reread.CustomerAcceptance.Rejections!).Revision));
    }

    [Fact]
    public async Task SAT_D115_a_revision_never_accepted_before_or_already_accepted_is_not_rejected_and_the_name_is_required()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        // Rev.0 never accepted: there is nothing to continue on (SAT-D113: Rev.0's behaviour is unchanged) — 409.
        var first = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RecordCustomerRejectionAsync(approved.Draft.Id, Command("4", "SENTETIK"), Metadata("k-rj0")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), (first.Status, first.Code));

        var accepted = await world.Service().RecordCustomerAcceptanceAsync(approved.Draft.Id, Command("4"), Metadata("k-rj0-accept"));
        var acceptedNow = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RecordCustomerRejectionAsync(approved.Draft.Id, Command(accepted.Detail.Draft.Version, "SENTETIK"), Metadata("k-rj0-b")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), (acceptedNow.Status, acceptedNow.Code));

        // Without the customer-acceptance name: 403 before anything is read or recorded.
        world.Authority.Denied.Add(SalesPermissionManifestV1.OrdersCustomerAcceptanceRecord);
        var forbidden = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RecordCustomerRejectionAsync(approved.Draft.Id, Command("5", "SENTETIK"), Metadata("k-rj0-c")));
        Assert.Equal(403, forbidden.Status);
        Assert.DoesNotContain(world.Store.Transitions, item => item.Command == SalesDraftCommands.RecordCustomerRejectionV1);
    }

    [Fact]
    public void SAT_D115_the_domain_refuses_a_stored_rejection_that_does_not_fit_the_revisions()
    {
        var currency = Guid.NewGuid();
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), Company, "S-1", new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), currency, Day),
            new MoneyProfileSnapshot(Company, currency, SalesOrderDraft.MoneyOperation, Guid.NewGuid(), "1", Day, true, Day, null, 2),
            DateTimeOffset.UnixEpoch, customerAcceptanceRequired: true);
        // Rev.0 is never a rejected revision, and nothing above the current revision exists.
        Assert.Throws<InvalidOperationException>(() => draft.RestoreCustomerRejection(
            new CustomerRejectionFacts(0, "SENTETIK", DateTimeOffset.UnixEpoch, "SENTETIK")));
        Assert.Throws<InvalidOperationException>(() => draft.RestoreCustomerRejection(
            new CustomerRejectionFacts(1, "SENTETIK", DateTimeOffset.UnixEpoch, "SENTETIK")));
        Assert.Empty(draft.CustomerRejections);
        Assert.Null(draft.CustomerAcceptedFallback);
    }
}
