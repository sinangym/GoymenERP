using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D219 (EK38): a line with an OPEN linked purchase order or production order is neither removed nor given another product
/// (the shipped-line rule of SAT-D167). The link is read from <see cref="ISalesOrderLinkedSupplyReadPortV1"/> (S-1): Present
/// refuses (422 on removed[i].lineId), Unreadable refuses (503), no link and "no source" pass; "no source" only while no module
/// declares that it records links of the kind (<see cref="ISalesOrderLinkedSupplyRecorderV1"/>) — a recorded kind without an
/// adapter is Unreadable. The link is read again at the revision's submission and approval (409). Synthetic owners, NOT_ENV.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private sealed class FakeSupplyRecorder(string kind) : ISalesOrderLinkedSupplyRecorderV1
    {
        public string SupplyKind { get; } = kind;
    }

    private static SalesOrderLinkedSupplyItemV1 OpenPurchaseOrder(Guid? orderLineId) =>
        new(Guid.NewGuid(), Guid.NewGuid(), SalesLinkedSupplyDocumentTypesV1.PurchaseOrder, "SA-1", orderLineId, "Approved", "5", KgUnit, null, null);

    private static (FakeLinkedSupply Purchasing, FakeLinkedSupply Production) SupplyOwners(World world)
    {
        var purchasing = new FakeLinkedSupply(SalesLinkedSupplyKindsV1.Purchasing, (query, kind) => Answer(query, kind));
        var production = new FakeLinkedSupply(SalesLinkedSupplyKindsV1.Production, (query, kind) => Answer(query, kind));
        world.LinkedSupplies.AddRange([purchasing, production]);
        return (purchasing, production);
    }

    [Fact]
    public async Task A_line_with_an_open_linked_purchase_order_is_neither_removed_nor_given_another_product()
    {
        var world = new World();
        var (approved, line) = await AcceptedApprovedAsync(world);
        Owners(world);
        var (purchasing, _) = SupplyOwners(world);
        var replacement = AddedLine() with { ProductId = Guid.Parse("99999999-9999-4999-8999-999999999993") };
        purchasing.Answer = (query, kind) => Answer(query, kind, OpenPurchaseOrder(line));

        var removal = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseLinesAsync(approved.Id,
            LineRevision("5", [AddedLine()], [new SalesDraftLineRemovalV1(line)]), Metadata("k-d219-remove")));
        Assert.Equal((422, "removed[0].lineId", SalesRuleCodes.LineRevisionLineLinked),
            (removal.Status, Assert.Single(removal.Errors).Field, removal.Errors[0].Code));

        var change = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseLinesAsync(approved.Id,
            LineRevision("5", [replacement], [new SalesDraftLineRemovalV1(line, replacement.LineId)]), Metadata("k-d219-change")));
        Assert.Equal((422, "removed[0].lineId", SalesRuleCodes.LineRevisionLineLinked),
            (change.Status, Assert.Single(change.Errors).Field, change.Errors[0].Code));
        Assert.DoesNotContain(world.Store.Transitions, item => item.Command == SalesDraftCommands.ReviseLinesV1);

        // Adding a line asks no supply owner; the link is closed in its owner and the removal then goes through.
        purchasing.Asked.Clear();
        await world.Service().ReviseLinesAsync(approved.Id, LineRevision("5", [AddedLine()]), Metadata("k-d219-add"));
        Assert.Empty(purchasing.Asked);
    }

    [Fact]
    public async Task A_link_on_another_line_does_not_block_while_a_whole_order_link_blocks_every_line()
    {
        var world = new World();
        var (approved, line) = await AcceptedApprovedAsync(world);
        Owners(world);
        var (purchasing, _) = SupplyOwners(world);
        var second = AddedLine();
        await world.Service().ReviseLinesAsync(approved.Id, LineRevision("5", [second]), Metadata("k-d219-second"));

        // A link naming the whole order cannot be told from any line: it blocks the removal of the first line.
        purchasing.Answer = (query, kind) => Answer(query, kind, OpenPurchaseOrder(null));
        var blocked = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseLinesAsync(approved.Id,
            LineRevision("6", [], [new SalesDraftLineRemovalV1(line)]), Metadata("k-d219-whole")));
        Assert.Equal((422, "removed[0].lineId", SalesRuleCodes.LineRevisionLineLinked),
            (blocked.Status, Assert.Single(blocked.Errors).Field, blocked.Errors[0].Code));

        // An open link on the NEW line only: removing the first line is free.
        purchasing.Answer = (query, kind) => Answer(query, kind, OpenPurchaseOrder(second.LineId));
        var revised = await world.Service().ReviseLinesAsync(approved.Id, LineRevision("6", [], [new SalesDraftLineRemovalV1(line)]), Metadata("k-d219-other"));
        Assert.Equal(second.LineId, Assert.Single(revised.Detail.Lines).LineId);
    }

    [Theory]
    [InlineData("unreadable")]
    [InlineData("throws")]
    [InlineData("recorded-without-adapter")]
    [InlineData("ambiguous")]
    public async Task An_unreadable_supply_kind_refuses_a_removal_and_a_kind_nobody_records_does_not(string case_)
    {
        var world = new World();
        var (approved, line) = await AcceptedApprovedAsync(world);
        Owners(world);
        switch (case_)
        {
            case "unreadable":
                world.LinkedSupplies.Add(new FakeLinkedSupply(SalesLinkedSupplyKindsV1.Production,
                    (query, kind) => Answer(query, kind) with { Status = SalesCancellationImpactStatusesV1.Unreadable, ReasonCode = "off" }));
                break;
            case "throws":
                world.LinkedSupplies.Add(new FakeLinkedSupply(SalesLinkedSupplyKindsV1.Purchasing,
                    (_, _) => throw new InvalidOperationException("SENTETIK")));
                break;
            case "recorded-without-adapter":
                world.LinkedSupplyRecorders.Add(new FakeSupplyRecorder(SalesLinkedSupplyKindsV1.Purchasing));
                break;
            default:
                world.LinkedSupplies.AddRange([new FakeLinkedSupply(SalesLinkedSupplyKindsV1.Production, (query, kind) => Answer(query, kind)),
                    new FakeLinkedSupply(SalesLinkedSupplyKindsV1.Production, (query, kind) => Answer(query, kind))]);
                break;
        }

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseLinesAsync(approved.Id,
            LineRevision("5", [AddedLine()], [new SalesDraftLineRemovalV1(line)]), Metadata("k-d219-" + case_)));
        Assert.Equal((503, SalesDraftProblemCodesV1.RevisionImpactUnavailable), Status(refused));
        Assert.DoesNotContain(world.Store.Transitions, item => item.Command == SalesDraftCommands.ReviseLinesV1);

        // The same removal where nobody records a link and no adapter is registered (NoSource) is allowed.
        var free = new World();
        var (freeApproved, freeLine) = await AcceptedApprovedAsync(free);
        Owners(free);
        free.LinkedSupplyRecorders.Add(new FakeSupplyRecorder("SomeoneElse"));
        var done = await free.Service().ReviseLinesAsync(freeApproved.Id,
            LineRevision("5", [AddedLine()], [new SalesDraftLineRemovalV1(freeLine)]), Metadata("k-d219-free"));
        Assert.Equal("Rev.1", done.Detail.Draft.Revision);
    }

    [Fact]
    public async Task A_recorder_with_its_adapter_answering_none_lets_the_removal_through_and_the_cancellation_keeps_its_rule()
    {
        var world = new World();
        var (approved, line) = await AcceptedApprovedAsync(world);
        Owners(world);
        SupplyOwners(world);
        world.LinkedSupplyRecorders.AddRange([new FakeSupplyRecorder(SalesLinkedSupplyKindsV1.Purchasing), new FakeSupplyRecorder(SalesLinkedSupplyKindsV1.Production)]);
        var revised = await world.Service().ReviseLinesAsync(approved.Id,
            LineRevision("5", [AddedLine()], [new SalesDraftLineRemovalV1(line)]), Metadata("k-d219-none"));
        Assert.Equal("Rev.1", revised.Detail.Draft.Revision);

        // The cancellation preview reads the same declarations: a recorded kind without an adapter is Unreadable and blocks.
        var other = new World();
        var otherApproved = await ApprovedAsync(other);
        Owners(other);
        other.LinkedSupplyRecorders.Add(new FakeSupplyRecorder(SalesLinkedSupplyKindsV1.Production));
        var preview = await other.Service().GetCancellationImpactAsync(otherApproved.Draft.Id);
        Assert.False(preview.Cancellable);
        Assert.Equal((SalesCancellationImpactStatusesV1.Unreadable, SalesCancellationImpactStatusesV1.NoSource),
            (Status(preview, SalesCancellationImpactSourcesV1.LinkedProduction), Status(preview, SalesCancellationImpactSourcesV1.LinkedPurchasing)));
    }

    [Fact]
    public async Task A_removal_is_read_again_at_its_submission_and_its_approval_and_an_open_link_on_the_passive_line_is_409()
    {
        var world = new World();
        var (approved, line) = await AcceptedApprovedAsync(world);
        Owners(world);
        var (purchasing, _) = SupplyOwners(world);
        await world.Service().ReviseLinesAsync(approved.Id, LineRevision("5", [AddedLine()], [new SalesDraftLineRemovalV1(line)]), Metadata("k-d219-rev"));

        // A purchase order opened on the (now passive, but known) line before the submission: Present, never malformed.
        purchasing.Answer = (query, kind) => Answer(query, kind, OpenPurchaseOrder(line));
        var atSubmit = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().SubmitDraftAsync(approved.Id, Command("6"), Metadata("k-d219-submit")));
        Assert.Equal((409, SalesDraftProblemCodesV1.RevisionBlocked, "removedLines[0].lineId", SalesRuleCodes.LineRevisionLineLinked),
            (atSubmit.Status, atSubmit.Code, Assert.Single(atSubmit.Errors).Field, atSubmit.Errors[0].Code));

        // Closed in its owner: the submission passes; opened again before the approval: the approval is refused too.
        purchasing.Answer = (query, kind) => Answer(query, kind);
        await world.Service().SubmitDraftAsync(approved.Id, Command("6"), Metadata("k-d219-submit-2"));
        purchasing.Answer = (query, kind) => Answer(query, kind, OpenPurchaseOrder(line));
        var atApprove = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveDraftAsync(approved.Id, Command("7"), Metadata("k-d219-approve")));
        Assert.Equal((409, SalesDraftProblemCodesV1.RevisionBlocked, "removedLines[0].lineId", SalesRuleCodes.LineRevisionLineLinked),
            (atApprove.Status, atApprove.Code, Assert.Single(atApprove.Errors).Field, atApprove.Errors[0].Code));
        Assert.Equal("PendingApproval", Assert.Single(world.Store.Drafts).ApprovalState.ToString());

        // An unreadable owner at the decision is 503 (fail-closed); readable and clear, the approval goes through.
        purchasing.Answer = (query, kind) => Answer(query, kind) with { Status = SalesCancellationImpactStatusesV1.Unreadable };
        var unread = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveDraftAsync(approved.Id, Command("7"), Metadata("k-d219-approve-2")));
        Assert.Equal((503, SalesDraftProblemCodesV1.RevisionImpactUnavailable), Status(unread));
        purchasing.Answer = (query, kind) => Answer(query, kind);
        var done = await world.Service().ApproveDraftAsync(approved.Id, Command("7"), Metadata("k-d219-approve-3"));
        Assert.Equal("Approved", done.Detail.Draft.ApprovalState);
    }
}
