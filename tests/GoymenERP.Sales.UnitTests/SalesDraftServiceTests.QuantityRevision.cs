using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// DEC-SAL-002 / AT-023 over SYNTHETIC owners: the quantity revision of an approved order archives Rev.0 with the reason and
/// re-enters approval at Rev.1 under its own name; the existing approve flow approves Rev.1; the history stays readable; the
/// line port answers NotApproved while Rev.1 is pending and the NEW quantity once approved; any active allocation or loading
/// line of the order blocks (fail-closed, the cancellation's rule) and an unreadable owner is 503; a stale version, a wrong
/// state and a blocked revision are the key's terminal 409s; the same key replays with no second effect.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    /// <summary>A registered platform name standing in for `sales.orders.quantity.write` until the central catalogue knows it.</summary>
    private const string QuantityPermission = ErpPermissions.OrganizationSitesRead;

    [Fact]
    public async Task An_approved_orders_quantity_revision_archives_rev0_with_its_reason_and_rev1_is_approved_through_the_existing_flow()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0];
        var (allocations, loadings) = Owners(world);
        var company = new CompanyContext();
        company.Set(Company);
        var port = new SalesOrderLineReferencePort(world.Store, company);
        world.Authority.Measured.Clear();

        var revised = await world.Service().ReviseQuantityAsync(approved.Draft.Id,
            new SalesDraftQuantityRevisionRequest("4", "  SENTETIK müşteri 2 kg istedi  ", [new SalesDraftLineQuantityV1(line.LineId, "2")]),
            Metadata("k-quantity"));

        Assert.False(revised.IsReplay);
        Assert.Equal(("Draft", "Preparing", "5", "Rev.1", "2"), (revised.Detail.Draft.State, revised.Detail.Draft.ApprovalState,
            revised.Detail.Draft.Version, revised.Detail.Draft.Revision, revised.Detail.Lines[0].Quantity));
        Assert.Equal(new SalesDraftApprovalV1(null, null, null, null, null), revised.Detail.Approval);
        // The fences: before the replay, after the draft read, after each owner, the last one — all under the quantity name
        // (the price read is asked for the answer only).
        Assert.Equal([QuantityPermission, QuantityPermission, QuantityPermission, QuantityPermission, QuantityPermission, QuantityPermission,
                PriceReadPermission],
            world.Authority.Measured);
        Assert.Equal([approved.Draft.Id], allocations.Asked);
        Assert.Equal([approved.Draft.Id], loadings.Asked);
        var transition = world.Store.Transitions[^1];
        Assert.Equal((SalesDraftCommands.ReviseQuantityV1, QuantityPermission, "sales.order-draft.quantity-revised.v1", "ReviseQuantity", 4L, 5L, "Web"),
            (transition.Command, transition.Envelope.PermissionCode, transition.Audit.EventType, transition.Audit.Operation,
                transition.VersionBefore, transition.VersionAfter, transition.Envelope.SourceChannel));

        // While Rev.1 is pending the consumers see no approved figures.
        var pending = await port.GetApprovedLineAsync(approved.Draft.Id, line.LineId);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.NotApproved, "Preparing"), (pending.Outcome, pending.ApprovalState));
        Assert.Empty((await port.ListApprovedLinesAsync(10, null)).Items);

        // The same key and content: the stored result, replayed — the owners are not asked, nothing is revised twice.
        var replay = await world.Service().ReviseQuantityAsync(approved.Draft.Id,
            new SalesDraftQuantityRevisionRequest("4", "  SENTETIK müşteri 2 kg istedi  ", [new SalesDraftLineQuantityV1(line.LineId, "2")]),
            Metadata("k-quantity", correlation: "retry"));
        Assert.Equal((true, "synthetic-correlation", "5", "Rev.1"), (replay.IsReplay, replay.OriginalCorrelationId, replay.Detail.Draft.Version, replay.Detail.Draft.Revision));
        Assert.Equal((1, 1), (allocations.Asked.Count, loadings.Asked.Count));
        Assert.Single(world.Store.Archived);
        var other = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseQuantityAsync(approved.Draft.Id,
            new SalesDraftQuantityRevisionRequest("4", "başka", [new SalesDraftLineQuantityV1(line.LineId, "2")]), Metadata("k-quantity")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch), Status(other));

        // The existing flow approves Rev.1; close and reopen shows Rev.1 approved and Rev.0 archived with its reason.
        await world.Service().SubmitDraftAsync(approved.Draft.Id, Command("5"), Metadata("k-submit-rev1"));
        await world.Service().ApproveDraftAsync(approved.Draft.Id, Command("6"), Metadata("k-approve-rev1"));
        var detail = await world.Service().GetDraftDetailAsync(approved.Draft.Id);
        Assert.Equal(("Approved", "Rev.1", "7", "2"), (detail.Draft.ApprovalState, detail.Draft.Revision, detail.Draft.Version, detail.Lines[0].Quantity));
        var rev0 = Assert.Single((await world.Service().GetDraftRevisionsAsync(approved.Draft.Id)).Items);
        Assert.Equal(("Rev.0", "1.25", "SENTETIK müşteri 2 kg istedi", Actor), (rev0.Revision, rev0.Lines[0].Quantity, rev0.Reason, rev0.ArchivedBy));
        Assert.Equal((approved.Approval.SubmittedBy, approved.Approval.DecidedBy), (rev0.SubmittedBy, rev0.ApprovedBy));

        // Downstream reads the APPROVED revision: Rev.1 with the new quantity.
        var answer = await port.GetApprovedLineAsync(approved.Draft.Id, line.LineId);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.Approved, "2", 1), (answer.Outcome, answer.Line!.QuantityText, answer.Line.Revision));
        Assert.Equal("2", Assert.Single((await port.ListApprovedLinesAsync(10, null)).Items).QuantityText);
    }

    [Fact]
    public async Task An_active_allocation_or_a_loading_line_blocks_the_revision_with_the_key_recorded_and_nothing_changes()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        var (allocations, loadings) = Owners(world);
        allocations.Answer = order => new(Company, order, [new(line, 1, 0.5m)]);
        loadings.Answer = order => new(Company, order, [new(line, KgUnit, 1, 0.5m, 0m, 0m, 0m)]);

        var blocked = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseQuantityAsync(approved.Draft.Id,
            new SalesDraftQuantityRevisionRequest("4", "neden", [new SalesDraftLineQuantityV1(line, "2")]), Metadata("k-quantity-1")));
        Assert.Equal((409, SalesDraftProblemCodesV1.RevisionBlocked), Status(blocked));
        Assert.Equal(
            [
                new SalesDraftFieldError(SalesCancellationImpactSourcesV1.ActiveAllocations, SalesCancellationImpactStatusesV1.Present),
                new SalesDraftFieldError(SalesCancellationImpactSourcesV1.LoadingWithoutDispatchNote, SalesCancellationImpactStatusesV1.Present),
            ],
            blocked.Errors);
        var refusal = Assert.Single(world.Store.Refusals);
        Assert.Equal((QuantityPermission, (Guid?)approved.Draft.Id), (refusal.Envelope.PermissionCode, refusal.DraftId));
        var replayed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseQuantityAsync(approved.Draft.Id,
            new SalesDraftQuantityRevisionRequest("4", "neden", [new SalesDraftLineQuantityV1(line, "2")]), Metadata("k-quantity-1", correlation: "retry")));
        Assert.Equal((409, SalesDraftProblemCodesV1.RevisionBlocked, true), (replayed.Status, replayed.Code, replayed.IsReplay));
        var detail = await world.Service().GetDraftDetailAsync(approved.Draft.Id);
        Assert.Equal(("Approved", "Rev.0", "4", "1.25"), (detail.Draft.ApprovalState, detail.Draft.Revision, detail.Draft.Version, detail.Lines[0].Quantity));
        Assert.Empty(world.Store.Archived);

        // The allocation released and the instruction resolved IN THEIR OWNERS: the revision goes through under a new key.
        allocations.Answer = order => new(Company, order, []);
        loadings.Answer = order => new(Company, order, []);
        var revised = await world.Service().ReviseQuantityAsync(approved.Draft.Id,
            new SalesDraftQuantityRevisionRequest("4", "neden", [new SalesDraftLineQuantityV1(line, "2")]), Metadata("k-quantity-2"));
        Assert.Equal("Rev.1", revised.Detail.Draft.Revision);
    }

    [Theory]
    [InlineData("not-composed")]
    [InlineData("malformed")]
    public async Task An_owner_that_cannot_answer_is_503_unrecorded_and_nothing_changes(string fault)
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        var (allocations, _) = Owners(world);
        if (fault == "not-composed") world.Loadings = null;
        else allocations.Answer = order => new(Guid.NewGuid(), order, []);

        var unavailable = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseQuantityAsync(approved.Draft.Id,
            new SalesDraftQuantityRevisionRequest("4", "neden", [new SalesDraftLineQuantityV1(line, "2")]), Metadata("k-quantity")));
        Assert.Equal((503, SalesDraftProblemCodesV1.RevisionImpactUnavailable), Status(unavailable));
        Assert.Empty(world.Store.Refusals);
        Assert.Equal(("Rev.0", "4"), ((await world.Service().GetDraftDetailAsync(approved.Draft.Id)).Draft.Revision,
            (await world.Service().GetDraftDetailAsync(approved.Draft.Id)).Draft.Version));
    }

    [Fact]
    public async Task A_stale_version_or_a_pending_draft_is_the_keys_terminal_409_and_asks_no_owner()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        var (allocations, loadings) = Owners(world);

        var stale = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseQuantityAsync(approved.Draft.Id,
            new SalesDraftQuantityRevisionRequest("3", "neden", [new SalesDraftLineQuantityV1(line, "2")]), Metadata("k-stale")));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict), Status(stale));
        Assert.Equal("expectedVersion", Assert.Single(stale.Errors).Field);

        var pending = (await world.Service().CreateDraftAsync(Request(), Metadata("k-create-2"))).Detail;
        await world.Service().SubmitDraftAsync(pending.Draft.Id, Command("2"), Metadata("k-submit-2"));
        var wrongState = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseQuantityAsync(pending.Draft.Id,
            new SalesDraftQuantityRevisionRequest("3", "neden", [new SalesDraftLineQuantityV1(pending.Lines[0].LineId, "2")]), Metadata("k-pending")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), Status(wrongState));
        Assert.Equal(2, world.Store.Refusals.Count);
        Assert.Empty(allocations.Asked);
        Assert.Empty(loadings.Asked);
        // Replayed under the same key, still asking no owner.
        var again = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseQuantityAsync(approved.Draft.Id,
            new SalesDraftQuantityRevisionRequest("3", "neden", [new SalesDraftLineQuantityV1(line, "2")]), Metadata("k-stale")));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict, true), (again.Status, again.Code, again.IsReplay));
        Assert.Empty(allocations.Asked);
    }

    [Theory]
    [InlineData("  ", "reason", SalesRuleCodes.RevisionReasonRequired, 422)]
    [InlineData("0.4", "lines[0].quantity", SalesRuleCodes.SlicesExceedQuantity, 422)]
    [InlineData("1.2345", "lines[0].quantity", SalesRuleCodes.QuantityScaleExceeded, 422)]
    [InlineData("1.250", "lines", SalesRuleCodes.RevisionNoChange, 422)]
    public async Task A_rule_refusal_is_a_422_on_its_field_unrecorded(string value, string field, string code, int status)
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        Owners(world);
        var reason = field == "reason" ? value : "neden";
        var quantity = field == "reason" ? "2" : value;

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseQuantityAsync(approved.Draft.Id,
            new SalesDraftQuantityRevisionRequest("4", reason, [new SalesDraftLineQuantityV1(line, quantity)]), Metadata("k-rule")));
        Assert.Equal((status, "validation_failed", field, code), (refused.Status, refused.Code, refused.Errors[0].Field, refused.Errors[0].Code));
        Assert.Empty(world.Store.Refusals);
        Assert.Equal("4", (await world.Service().GetDraftDetailAsync(approved.Draft.Id)).Draft.Version);
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("unbound")]
    public async Task Without_the_quantity_name_nothing_is_read_asked_or_recorded(string fault)
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var (allocations, _) = Owners(world);
        if (fault == "denied") world.Authority.Denied.Add(QuantityPermission);
        else world.Bindings = world.Bindings with { QuantityPermission = null };
        world.Store.OnFindDraft = () => throw new InvalidOperationException("the draft must not be read");

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseQuantityAsync(approved.Draft.Id,
            new SalesDraftQuantityRevisionRequest("4", "neden", [new SalesDraftLineQuantityV1(approved.Lines[0].LineId, "2")]), Metadata("k-quantity")));

        Assert.Equal(fault == "unbound" ? (503, SalesDraftProblemCodesV1.PermissionUnregistered) : (403, "forbidden"), Status(refused));
        Assert.Empty(allocations.Asked);
        Assert.Empty(world.Store.Refusals);
        Assert.Equal(2, world.Store.Transitions.Count);
    }

    [Fact]
    public async Task Another_companys_or_an_unknown_draft_is_404_and_no_owner_is_asked()
    {
        var world = new World();
        var (allocations, _) = Owners(world);
        var missing = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseQuantityAsync(Guid.NewGuid(),
            new SalesDraftQuantityRevisionRequest("4", "neden", [new SalesDraftLineQuantityV1(Guid.NewGuid(), "2")]), Metadata("k-quantity")));
        Assert.Equal(404, missing.Status);
        Assert.Empty(allocations.Asked);
    }

    [Fact]
    public async Task The_price_disclosure_rule_holds_on_the_quantity_revisions_answer_and_its_archive()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        Owners(world);
        world.Authority.Denied.Add(PriceReadPermission);

        var revised = await world.Service().ReviseQuantityAsync(approved.Draft.Id,
            new SalesDraftQuantityRevisionRequest("4", "neden", [new SalesDraftLineQuantityV1(approved.Lines[0].LineId, "2")]), Metadata("k-quantity"));

        Assert.Equal(SalesPriceDisclosure.Withheld, revised.Detail.Lines[0].PriceDisclosure);
        var rev0 = Assert.Single((await world.Service().GetDraftRevisionsAsync(approved.Draft.Id)).Items);
        Assert.Equal((SalesPriceDisclosure.Withheld, (string?)null, "1.25"), (rev0.Lines[0].PriceDisclosure, rev0.Lines[0].UnitPrice, rev0.Lines[0].Quantity));
    }
}
