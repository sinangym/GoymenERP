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

        // The rejected revision changed again (a removal being prepared again): not shipped (fail-closed).
        await world.Service().ReviseLinesAsync(approved.Id, LineRevision("8", [AddedLine()]), Metadata("k-d211-again"));
        Assert.Equal(SalesOrderLineReferenceOutcomeV1.NotFound, (await port.GetApprovedLineAsync(approved.Id, line)).Outcome);
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
                     Body("\"added\":[],\"removed\":[]"),
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
}
