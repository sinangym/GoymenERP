using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D167 (EK24) through the service: the line revision under the quantity-write name — an added line passes the create's owners
/// and rules, a removed line must be unshipped (the allocation and loading owners read afresh, fail-closed), a product change is a
/// removal replaced by an added line; an approved order goes to the next revision; the key replays; a priced added line needs the
/// price-write name; the field errors name added[i] / removed[i]. Synthetic ports, NOT_ENV.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private static SalesOrderDraftLineRequest AddedLine(Guid? lineId = null, string quantity = "3", string? price = null) =>
        new(lineId ?? Guid.NewGuid(), Product, "7", KgUnit, quantity, UnitPrice: price, PriceUnitId: price is null ? null : KgUnit);

    private static SalesDraftLineRevisionRequest LineRevision(string version, IReadOnlyList<SalesOrderDraftLineRequest>? added = null,
        IReadOnlyList<SalesDraftLineRemovalV1>? removed = null, string reason = "SENTETIK müşteri değişikliği") =>
        new(version, reason, added ?? [], removed ?? []);

    [Fact]
    public async Task An_approved_orders_line_revision_adds_and_removes_lines_and_goes_to_the_next_revision_and_replays()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var (allocations, loadings) = Owners(world);
        var removedId = approved.Lines[0].LineId;
        var added = AddedLine();
        var request = LineRevision(approved.Draft.Version, [added], [new SalesDraftLineRemovalV1(removedId, added.LineId)]);

        // A product change to the same product is refused by the domain (the replacement is the same product here).
        var same = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseLinesAsync(approved.Draft.Id, request,
            Metadata("k-lines-same")));
        Assert.Equal((422, "removed[0].lineId", SalesRuleCodes.LineReplacementSameProduct),
            (same.Status, Assert.Single(same.Errors).Field, same.Errors[0].Code));

        allocations.Asked.Clear();
        loadings.Asked.Clear();
        var plain = LineRevision(approved.Draft.Version, [added], [new SalesDraftLineRemovalV1(removedId)]);
        var revised = await world.Service().ReviseLinesAsync(approved.Draft.Id, plain, Metadata("k-lines"));

        Assert.Equal(("Rev.1", "Preparing", "5"), (revised.Detail.Draft.Revision, revised.Detail.Draft.ApprovalState, revised.Detail.Draft.Version));
        var line = Assert.Single(revised.Detail.Lines);
        Assert.Equal((added.LineId, "3", "Rev.1"), (line.LineId, line.Quantity, line.AddedInRevision));
        var removed = Assert.Single(revised.Detail.RemovedLines!);
        Assert.Equal((removedId, "Rev.1"), (removed.LineId, removed.RemovedInRevision));
        Assert.Equal([approved.Draft.Id], allocations.Asked);
        Assert.Equal([approved.Draft.Id], loadings.Asked);
        var transition = world.Store.Transitions.Last();
        Assert.Equal((SalesDraftCommands.ReviseLinesV1, "sales.order-draft.lines-revised.v1", 4L, 5L),
            (transition.Command, transition.Audit.EventType, transition.VersionBefore, transition.VersionAfter));

        var replay = await world.Service().ReviseLinesAsync(approved.Draft.Id, plain, Metadata("k-lines"));
        Assert.True(replay.IsReplay);
        Assert.Equal("5", replay.Detail.Draft.Version);
    }

    [Theory]
    [InlineData("allocated")]
    [InlineData("loaded")]
    public async Task A_line_with_a_shipment_is_neither_removed_nor_given_another_product(string link)
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var (allocations, loadings) = Owners(world);
        var lineId = approved.Lines[0].LineId;
        if (link == "allocated")
        {
            allocations.Answer = order => new(Company, order, [new SalesOrderLineAllocationV1(lineId, 1, 0.5m)]);
        }
        else
        {
            loadings.Answer = order => new(Company, order, [new SalesOrderLineLoadingV1(lineId, KgUnit, 1, 0.5m, 0m, 0m, 0m)]);
        }

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseLinesAsync(approved.Draft.Id,
            LineRevision(approved.Draft.Version, [AddedLine()], [new SalesDraftLineRemovalV1(lineId)]), Metadata($"k-{link}")));

        Assert.Equal((422, "removed[0].lineId", SalesRuleCodes.LineRevisionLineShipped),
            (refused.Status, Assert.Single(refused.Errors).Field, refused.Errors[0].Code));
        Assert.DoesNotContain(world.Store.Transitions, item => item.Command == SalesDraftCommands.ReviseLinesV1);
    }

    [Fact]
    public async Task An_unreadable_shipping_owner_refuses_a_removal_and_a_priced_added_line_needs_the_price_write_name()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        world.Allocations = null;
        world.Loadings = null;
        var lineId = approved.Lines[0].LineId;

        var unread = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseLinesAsync(approved.Draft.Id,
            LineRevision(approved.Draft.Version, [AddedLine()], [new SalesDraftLineRemovalV1(lineId)]), Metadata("k-unread")));
        Assert.Equal((503, SalesDraftProblemCodesV1.RevisionImpactUnavailable), (unread.Status, unread.Code));

        world.Authority.Denied.Add(PricePermission);
        var priced = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseLinesAsync(approved.Draft.Id,
            LineRevision(approved.Draft.Version, [AddedLine(price: "12.50")]), Metadata("k-priced")));
        Assert.Equal((403, SalesDraftProblemCodesV1.PriceWriteForbidden), (priced.Status, priced.Code));

        // Only adding (no removal) asks no shipping owner; the stale version is the key's terminal 409.
        world.Authority.Denied.Remove(PricePermission);
        var stale = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseLinesAsync(approved.Draft.Id,
            LineRevision("3", [AddedLine()]), Metadata("k-stale")));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict), (stale.Status, stale.Code));
        var added = await world.Service().ReviseLinesAsync(approved.Draft.Id, LineRevision(approved.Draft.Version, [AddedLine(price: "12.50")]),
            Metadata("k-add"));
        Assert.Equal(2, added.Detail.Lines.Count);
    }

    [Fact]
    public async Task A_fason_orders_added_line_follows_the_service_and_kg_rules()
    {
        var world = new World { ItemTypes = new FakeItemTypes() };
        var created = (await world.Service().CreateDraftAsync(Subcontract("10"), Metadata("k-fason-lines"))).Detail;
        world.ItemTypes = new FakeItemTypes { Answer = "Steel" };

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseLinesAsync(created.Draft.Id,
            LineRevision(created.Draft.Version, [AddedLine()]), Metadata("k-fason-goods")));

        Assert.Equal((422, "added[0].productId", SalesRuleCodes.SubcontractLineNotService),
            (refused.Status, Assert.Single(refused.Errors).Field, refused.Errors[0].Code));

        world.ItemTypes = new FakeItemTypes();
        var revised = await world.Service().ReviseLinesAsync(created.Draft.Id, LineRevision(created.Draft.Version, [AddedLine(price: "11")]),
            Metadata("k-fason-service"));
        Assert.Equal(2, revised.Detail.Lines.Count);
        Assert.Equal(SalesOrderDraft_ServiceItemType, Assert.Single(world.Store.Drafts).Lines[1].ItemType);
    }

    [Fact]
    public async Task A_removed_line_does_not_ship_while_its_removal_waits_and_comes_back_under_the_last_approved_revision_after_a_rejection()
    {
        var world = new World();
        var (approved, line) = await AcceptedApprovedAsync(world);
        Owners(world);
        var port = LinePort(world);
        var added = AddedLine();

        await world.Service().ReviseLinesAsync(approved.Id, LineRevision("5", [added], [new SalesDraftLineRemovalV1(line)]), Metadata("k-d211"));

        // SAT-D211 (EK36): being prepared and waiting for approval, the removed line is not shipped; the added one waits too.
        Assert.Equal(SalesOrderLineReferenceOutcomeV1.NotFound, (await port.GetApprovedLineAsync(approved.Id, line)).Outcome);
        Assert.Equal(SalesOrderLineReferenceOutcomeV1.NotApproved, (await port.GetApprovedLineAsync(approved.Id, added.LineId)).Outcome);
        await world.Service().SubmitDraftAsync(approved.Id, Command("6"), Metadata("k-d211-submit"));
        Assert.Equal(SalesOrderLineReferenceOutcomeV1.NotFound, (await port.GetApprovedLineAsync(approved.Id, line)).Outcome);
        Assert.Empty((await port.ListApprovedLinesAsync(10, null)).Items);

        // Rejected: the line comes back and ships under the last approved revision (Rev.0, its quantity as the cap).
        await world.Service().RejectDraftAsync(approved.Id, Command("7"), Metadata("k-d211-reject"));
        await AssertLastApprovedAsync(port, approved.Id, line, "Rejected", "1.25", "1.25", "8");
        Assert.Equal(line, Assert.Single((await port.ListApprovedLinesAsync(10, null)).Items).LineId);

        // The rejection reverted the removal in the order itself: the line is active again (not only in the port's view).
        var reverted = await world.Service().GetDraftDetailAsync(approved.Id);
        Assert.Contains(reverted.Lines, candidate => candidate.LineId == line);
        Assert.DoesNotContain(reverted.RemovedLines ?? [], candidate => candidate.LineId == line);

        // K3 review B probe 2 (regression): the line is loaded, the rejected revision is changed and resubmitted — the line stays
        // active and ships, the submission passes (no dead end) and the approval keeps the line.
        var (_, loadings) = Owners(world);
        loadings.Answer = order => new(Company, order, [new SalesOrderLineLoadingV1(line, KgUnit, 1, 0.5m, 0.5m, 0m, 0m)]);
        await world.Service().ReviseLinesAsync(approved.Id, LineRevision("8", [AddedLine()]), Metadata("k-d211-again"));
        Assert.Equal(SalesOrderLineReferenceOutcomeV1.Approved, (await port.GetApprovedLineAsync(approved.Id, line)).Outcome);
        await world.Service().SubmitDraftAsync(approved.Id, Command("9"), Metadata("k-d211-resubmit"));
        var reapproved = await world.Service().ApproveDraftAsync(approved.Id, Command("10"), Metadata("k-d211-reapprove"));
        Assert.Contains(reapproved.Detail.Lines, candidate => candidate.LineId == line);
        Assert.DoesNotContain(reapproved.Detail.RemovedLines ?? [], candidate => candidate.LineId == line);
    }

    [Fact]
    public async Task A_pending_product_change_ships_neither_the_old_line_nor_its_replacement_and_a_rejection_brings_the_old_line_back()
    {
        // SAT-D167 product change = the old line removed + its replacement added; SAT-D211 (EK36): neither ships before approval.
        var world = new World();
        var (approved, line) = await AcceptedApprovedAsync(world);
        Owners(world);
        var port = LinePort(world);
        var replacement = AddedLine() with { ProductId = Guid.Parse("99999999-9999-4999-8999-999999999993") };

        await world.Service().ReviseLinesAsync(approved.Id,
            LineRevision("5", [replacement], [new SalesDraftLineRemovalV1(line, replacement.LineId)]), Metadata("k-pc"));
        Assert.Equal(replacement.LineId, Assert.Single((await world.Service().GetDraftDetailAsync(approved.Id)).RemovedLines!).ReplacedByLineId);

        foreach (var step in new[] { "prepared", "submitted" })
        {
            if (step == "submitted")
            {
                await world.Service().SubmitDraftAsync(approved.Id, Command("6"), Metadata("k-pc-submit"));
            }

            Assert.Equal(SalesOrderLineReferenceOutcomeV1.NotFound, (await port.GetApprovedLineForLoadingAsync(approved.Id, line, Day)).Outcome);
            Assert.Equal(SalesOrderLineReferenceOutcomeV1.NotApproved,
                (await port.GetApprovedLineForLoadingAsync(approved.Id, replacement.LineId, Day)).Outcome);
            Assert.Empty((await port.ListApprovedLinesAsync(10, null)).Items);
        }

        // Rejected: the old line comes back (active) under Rev.0; the replacement goes passive in the rejected revision, so the
        // order never holds both products, and it never ships.
        await world.Service().RejectDraftAsync(approved.Id, Command("7"), Metadata("k-pc-reject"));
        await AssertLastApprovedAsync(port, approved.Id, line, "Rejected", "1.25", "1.25", "8");
        Assert.Equal(SalesOrderLineReferenceOutcomeV1.NotFound, (await port.GetApprovedLineAsync(approved.Id, replacement.LineId)).Outcome);
        var detail = await world.Service().GetDraftDetailAsync(approved.Id);
        Assert.Equal([line], detail.Lines.Select(candidate => candidate.LineId));
        Assert.Equal((replacement.LineId, "Rev.1", (Guid?)null), (Assert.Single(detail.RemovedLines!).LineId, detail.RemovedLines![0].RemovedInRevision,
            detail.RemovedLines[0].ReplacedByLineId));
        Assert.Equal(line, Assert.Single((await port.ListApprovedLinesAsync(10, null)).Items).LineId);
    }

    [Fact]
    public async Task SAT_D220_a_line_pending_removal_is_offered_to_no_supply_link_and_comes_back_after_a_rejection()
    {
        // SAT-D220 (EK39): while the removal waits, no new purchase / production link may be made to the line — the Sales line
        // port, through which a linking owner reads a sales order line, offers it on no read (direct, loading, list).
        var world = new World();
        var (approved, line) = await AcceptedApprovedAsync(world);
        Owners(world);
        var port = LinePort(world);
        await world.Service().ReviseLinesAsync(approved.Id, LineRevision("5", [AddedLine()], [new SalesDraftLineRemovalV1(line)]), Metadata("k-d220"));
        await world.Service().SubmitDraftAsync(approved.Id, Command("6"), Metadata("k-d220-submit"));

        Assert.Equal(SalesOrderLineReferenceOutcomeV1.NotFound, (await port.GetApprovedLineAsync(approved.Id, line)).Outcome);
        Assert.Equal(SalesOrderLineReferenceOutcomeV1.NotFound, (await port.GetApprovedLineForLoadingAsync(approved.Id, line, Day)).Outcome);
        Assert.DoesNotContain((await port.ListApprovedLinesAsync(10, null)).Items, item => item.LineId == line);

        // Rejected: the line comes back and is offered again.
        await world.Service().RejectDraftAsync(approved.Id, Command("7"), Metadata("k-d220-reject"));
        Assert.Equal(SalesOrderLineReferenceOutcomeV1.Approved, (await port.GetApprovedLineAsync(approved.Id, line)).Outcome);
        Assert.Contains((await port.ListApprovedLinesAsync(10, null)).Items, item => item.LineId == line);
    }

    [Fact]
    public async Task A_removed_line_comes_back_under_the_last_accepted_revision_when_the_customer_rejects_the_removal()
    {
        // SAT-D211 (EK36) with SAT-D115: the customer rejects the internally approved Rev.1 that removed the line.
        var world = new World();
        var (approved, line) = await AcceptedApprovedAsync(world);
        Owners(world);
        var port = LinePort(world);
        await world.Service().ReviseLinesAsync(approved.Id, LineRevision("5", [AddedLine()], [new SalesDraftLineRemovalV1(line)]), Metadata("k-d211c"));
        await world.Service().SubmitDraftAsync(approved.Id, Command("6"), Metadata("k-d211c-submit"));
        await world.Service().ApproveDraftAsync(approved.Id, Command("7"), Metadata("k-d211c-approve"));
        // Approved internally, the customer has not decided: the removed line is not shipped.
        Assert.Equal(SalesOrderLineReferenceOutcomeV1.NotFound, (await port.GetApprovedLineAsync(approved.Id, line)).Outcome);

        await world.Service().RecordCustomerRejectionAsync(approved.Id, Command("8", "SENTETIK müşteri satırı geri istedi"), Metadata("k-d211c-reject"));

        var answer = await port.GetApprovedLineForLoadingAsync(approved.Id, line, Day);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.Approved, 0, "1.25", "1.25", "CustomerRejected"),
            (answer.Outcome, answer.Line!.Revision, answer.Line.QuantityText, answer.Line.ShippingCapQuantityText, answer.Line.PendingApprovalState));
        Assert.Equal(line, Assert.Single((await port.ListApprovedLinesAsync(10, null)).Items).LineId);

        // K3 review A P1-2: the removed line shipped (and was returned) under the accepted revision while the customer-rejected
        // revision stands — the delivery and return owners answer it; it is the order's line, never a malformed answer (no 503).
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new SalesOrderLineDeliveryV1(line, KgUnit, 0.5m, 0.5m)]));
        world.CustomerReturns = new FakeCustomerReturns(order => new(Company, order, [new SalesOrderLineCustomerReturnV1(line, KgUnit, 0.1m)]));
        var progress = await world.Service().GetDraftDeliveryProgressAsync(approved.Id);
        Assert.DoesNotContain(progress.Lines, candidate => candidate.LineId == line);
        var returns = await world.Service().GetDraftCustomerReturnsAsync(approved.Id);
        Assert.DoesNotContain(returns.Lines, candidate => candidate.LineId == line);
        // The realization axis reads the same answers (and measures the line under the accepted revision): no 503.
        Assert.NotNull(await world.Service().GetDraftStatusAsync(approved.Id));

        // K3 review B probe 1 / review A P1-1 (regression): the line is loaded under Rev.0, then Rev.2 opens (a line added). Rev.1
        // is archived as approved (without the line); the revert brings the line back in Rev.2, which keeps shipping it under the
        // accepted Rev.0, and Rev.2's approval holds it.
        var (_, loadings) = Owners(world);
        loadings.Answer = order => new(Company, order, [new SalesOrderLineLoadingV1(line, KgUnit, 1, 0.5m, 0.5m, 0m, 0m)]);
        var opened = await world.Service().ReviseLinesAsync(approved.Id, LineRevision("9", [AddedLine()]), Metadata("k-d211c-rev2"));
        Assert.Equal(("Rev.2", "Preparing"), (opened.Detail.Draft.Revision, opened.Detail.Draft.ApprovalState));
        Assert.Contains(opened.Detail.Lines, candidate => candidate.LineId == line);
        Assert.DoesNotContain(Assert.Single(world.Store.Drafts).ArchivedRevisions[^1].Lines, archived => archived.LineId == line);
        Assert.Equal(SalesOrderLineReferenceOutcomeV1.Approved, (await port.GetApprovedLineAsync(approved.Id, line)).Outcome);
        await world.Service().SubmitDraftAsync(approved.Id, Command("10"), Metadata("k-d211c-rev2-submit"));
        var rev2 = await world.Service().ApproveDraftAsync(approved.Id, Command("11"), Metadata("k-d211c-rev2-approve"));
        Assert.Contains(rev2.Detail.Lines, candidate => candidate.LineId == line);
        Assert.DoesNotContain(rev2.Detail.RemovedLines ?? [], candidate => candidate.LineId == line);
    }

    [Fact]
    public async Task An_added_line_is_read_for_the_revision_day_and_the_existing_lines_keep_their_business_date_snapshots()
    {
        // SAT-D212 (EK36): the order is dated three days after the server's business day (Day); a line added today is read for Day.
        var world = new World { Clock = QuoteClock() };
        var created = (await world.Service().CreateDraftAsync(Request() with { BusinessDate = Day.AddDays(3) }, Metadata("k-d212"))).Detail;
        var products = world.Products;
        products.AskedOn.Clear();
        var added = AddedLine();

        await world.Service().ReviseLinesAsync(created.Draft.Id, LineRevision(created.Draft.Version, [added]), Metadata("k-d212-lines"));

        Assert.Equal([Day], products.AskedOn);
        var stored = Assert.Single(world.Store.Drafts);
        Assert.Equal(Day, stored.Lines.Single(line => line.Id == added.LineId).Product.EffectiveOn);
        Assert.All(stored.Lines.Where(line => line.Id != added.LineId), line => Assert.Equal(Day.AddDays(3), line.Product.EffectiveOn));
    }

    [Fact]
    public async Task A_removal_is_read_again_at_its_submission_and_the_review_A_gaps_are_held()
    {
        var world = new World();
        var (approved, line) = await AcceptedApprovedAsync(world);
        var (allocations, _) = Owners(world);
        var plain = LineRevision("5", [AddedLine()], [new SalesDraftLineRemovalV1(line)]);
        await world.Service().ReviseLinesAsync(approved.Id, plain, Metadata("k-a"));

        // P3-2: the same key with another body is the key's 409, nothing changes.
        var other = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseLinesAsync(approved.Id,
            plain with { Reason = "başka" }, Metadata("k-a")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch), Status(other));

        // P2-2 (the SAT-D128 pattern): an allocation opened on the removed line after the revision was recorded blocks its submission (409; no transaction
        // spans the owners); an unreadable owner refuses it too (503).
        allocations.Answer = order => new(Company, order, [new SalesOrderLineAllocationV1(line, 1, 0.5m)]);
        var linked = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().SubmitDraftAsync(approved.Id, Command("6"),
            Metadata("k-a-submit")));
        Assert.Equal((409, SalesDraftProblemCodesV1.RevisionBlocked, "removedLines[0].lineId", SalesRuleCodes.LineRevisionLineShipped),
            (linked.Status, linked.Code, Assert.Single(linked.Errors).Field, linked.Errors[0].Code));
        world.Allocations = null;
        var unread = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().SubmitDraftAsync(approved.Id, Command("6"),
            Metadata("k-a-submit-2")));
        Assert.Equal((503, SalesDraftProblemCodesV1.RevisionImpactUnavailable), Status(unread));
        Assert.Equal("Preparing", Assert.Single(world.Store.Drafts).ApprovalState.ToString());

        // P1-1: more lines than an order holds (active + passive + added) is 422 on added, before anything changes.
        var many = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseLinesAsync(approved.Id,
            LineRevision("6", [.. Enumerable.Range(0, 499).Select(_ => AddedLine())]), Metadata("k-a-many")));
        Assert.Equal((422, "added", SalesRuleCodes.TooManyLines), (many.Status, Assert.Single(many.Errors).Field, many.Errors[0].Code));
    }

    [Fact]
    public async Task A_fason_order_with_an_unpriced_added_service_line_is_not_submitted()
    {
        // P3-2 (SAT-D201): the submit rule runs over the active lines, the added one included.
        var world = new World { ItemTypes = new FakeItemTypes() };
        var created = (await world.Service().CreateDraftAsync(Subcontract("10"), Metadata("k-a-fason"))).Detail;
        var revised = await world.Service().ReviseLinesAsync(created.Draft.Id, LineRevision(created.Draft.Version, [AddedLine()]),
            Metadata("k-a-fason-lines"));

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().SubmitDraftAsync(created.Draft.Id,
            Command(revised.Detail.Draft.Version), Metadata("k-a-fason-submit")));
        Assert.Equal((422, "draft", SalesRuleCodes.SubcontractPriceRequired), (refused.Status, Assert.Single(refused.Errors).Field, refused.Errors[0].Code));
    }

    private const string SalesOrderDraft_ServiceItemType = GoymenERP.Modules.Sales.Domain.Drafts.SalesOrderDraft.ServiceItemType;

    [Fact]
    public void The_line_revision_body_is_strict()
    {
        var added = Guid.NewGuid();
        var removed = Guid.NewGuid();
        string Body(string changes) => "{\"expectedVersion\":\"4\",\"reason\":\"r\"," + changes + "}";
        var line = $"{{\"lineId\":\"{added}\",\"productId\":\"{Product}\",\"expectedProductVersion\":\"7\",\"orderUnitId\":\"{KgUnit}\",\"quantity\":\"3\"}}";

        var request = SalesDraftJson.ReadLineRevision(Body($"\"added\":[{line}],\"removed\":[{{\"lineId\":\"{removed}\",\"replacedByLineId\":\"{added}\"}}]"));
        Assert.Equal((added, removed, (Guid?)added), (request.Added[0].LineId, request.Removed[0].LineId, request.Removed[0].ReplacedByLineId));

        foreach (var invalid in new[]
                 {
                     Body($"\"added\":[{line},{line}],\"removed\":[]"),
                     Body($"\"added\":[],\"removed\":[{{\"lineId\":\"{removed}\",\"replacedByLineId\":\"{Guid.NewGuid()}\"}}]"),
                     Body($"\"added\":[{line}],\"removed\":[{{\"lineId\":\"{added}\"}}]"),
                     Body($"\"added\":[{line}],\"removed\":[],\"extra\":1"),
                     "{\"expectedVersion\":\"0\",\"reason\":\"r\",\"added\":[],\"removed\":[]}",
                 })
        {
            Assert.ThrowsAny<System.Text.Json.JsonException>(() => SalesDraftJson.ReadLineRevision(invalid));
        }

        // The replay identity covers every member.
        Assert.NotEqual(SalesDraftRequestHash.Compute(SalesDraftCommands.ReviseLinesV1, Guid.Empty, request),
            SalesDraftRequestHash.Compute(SalesDraftCommands.ReviseLinesV1, Guid.Empty, request with { Reason = "başka" }));
    }
    [Fact]
    public async Task A_quantity_header_or_commercial_revision_is_a_422_validation_failure_while_a_customer_rejected_removal_waits()
    {
        // K3 review B P3-1: the domain's fail-closed refusal reaches the caller as 422 validation_failed on every command.
        var world = new World();
        Owners(world);
        var approved = await ApprovedAsync(world);
        var removedId = approved.Lines[0].LineId;
        var accepted = await world.Service().RecordCustomerAcceptanceAsync(approved.Draft.Id, Command(approved.Draft.Version), Metadata("k-pend-accept"));
        var revised = await world.Service().ReviseLinesAsync(approved.Draft.Id,
            LineRevision(accepted.Detail.Draft.Version, [AddedLine()], [new SalesDraftLineRemovalV1(removedId)]), Metadata("k-pend-lines"));
        var submitted = await world.Service().SubmitDraftAsync(approved.Draft.Id, Command(revised.Detail.Draft.Version), Metadata("k-pend-submit"));
        var approvedAgain = await world.Service().ApproveDraftAsync(approved.Draft.Id, Command(submitted.Detail.Draft.Version), Metadata("k-pend-approve"));
        var rejected = await world.Service().RecordCustomerRejectionAsync(approved.Draft.Id,
            Command(approvedAgain.Detail.Draft.Version, "SENTETIK musteri istemedi"), Metadata("k-pend-reject"));
        var version = rejected.Detail.Draft.Version;
        var lineId = rejected.Detail.Lines[0].LineId;
        var transitions = world.Store.Transitions.Count;

        var quantity = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseQuantityAsync(approved.Draft.Id,
            new SalesDraftQuantityRevisionRequest(version, "SENTETIK", [new SalesDraftLineQuantityV1(lineId, "2")]), Metadata("k-pend-quantity")));
        var header = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseHeaderAsync(approved.Draft.Id,
            HeaderRevision(version, account: NewAccount, currency: NewCurrency, reason: "SENTETIK"), Metadata("k-pend-header")));
        var commercial = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(approved.Draft.Id,
            Revision(version, new SalesDraftLineCommercialV1(lineId, "10", KgUnit)), Metadata("k-pend-commercial")));

        foreach (var refused in new[] { quantity, header, commercial })
        {
            Assert.Equal((422, "validation_failed", SalesRuleCodes.RevisionReinstatementPending),
                (refused.Status, refused.Code, Assert.Single(refused.Errors).Code));
        }

        Assert.Equal(transitions, world.Store.Transitions.Count);
    }
}
