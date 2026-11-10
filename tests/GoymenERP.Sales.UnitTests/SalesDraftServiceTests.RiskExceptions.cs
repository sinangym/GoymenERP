using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.RiskExceptions;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// API-07 (EK21 SAT-D153…D162; Sinan 2026-11-10) over a SYNTHETIC in-memory risk exception store and the draft fakes: the request
/// is measured under its own name only (never <c>sales.drafts.read</c>), the decisions under the approve name; the requester never
/// decides their own request (the key's recorded 409, answered again on retry); the approval is valid until the end of its
/// Europe/Istanbul business date; a pending or today-valid exception of the same shipment is a 409 that is not recorded; the same
/// key replays; another company's exception is invisible (404) and a company the caller cannot reach is 403; the order is read,
/// never changed (no revision, SAT-D161).
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private const string RiskRequestPermission = SalesPermissionManifestV1.OrdersRiskExceptionRequest;
    private const string RiskApprovePermission = SalesPermissionManifestV1.OrdersRiskExceptionApprove;
    private const string RiskApprover = "synthetic-approver";
    private static readonly Guid RiskShipment = Guid.Parse("e1e1e1e1-e1e1-4e1e-8e1e-e1e1e1e1e1e1");
    private static readonly Guid OtherShipment = Guid.Parse("e2e2e2e2-e2e2-4e2e-8e2e-e2e2e2e2e2e2");
    private static readonly Guid OtherRiskCompany = Guid.Parse("12121212-1212-4121-8121-121212121212");

    private static RequestSalesRiskExceptionRequest RiskOf(Guid? shipment = null, string? reason = "SENTETIK ödeme sözü") =>
        new(shipment ?? RiskShipment, reason);

    private static SalesRiskExceptionService RiskService(World world, FakeRiskExceptionStore store, TimeProvider? clock = null,
        Guid? company = null)
    {
        var context = new CompanyContext();
        context.Set(company ?? Company);
        return new SalesRiskExceptionService(context, world.User, world.Authority, new FixedCorrelation(), store, world.Store,
            clock ?? world.Clock);
    }

    [Fact]
    public async Task A_request_is_measured_under_its_own_name_only_and_answers_a_pending_exception_without_changing_the_order()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var store = new FakeRiskExceptionStore();
        world.Authority.Measured.Clear();
        world.Authority.Denied.Add(SalesPermissionManifestV1.DraftsRead);

        var result = await RiskService(world, store).RequestRiskExceptionAsync(approved.Draft.Id, RiskOf(), Metadata("r-1"));

        Assert.False(result.IsReplay);
        Assert.Equal((1, RiskShipment, "Requested", "1", Actor, false),
            (result.Exception.ExceptionNo, result.Exception.LoadingInstructionId, result.Exception.State, result.Exception.Version,
            result.Exception.RequestedBy, result.Exception.IsValidToday));
        Assert.All(world.Authority.Measured, permission => Assert.Equal(RiskRequestPermission, permission));
        var write = Assert.Single(store.Writes);
        Assert.Equal((SalesRiskExceptionCommands.RequestV1, RiskRequestPermission, "sales.order.risk-exception.requested.v1"),
            (write.Command, write.Envelope.PermissionCode, write.Audit.EventType));
        var order = (await world.Store.FindDraftAsync(approved.Draft.Id))!;
        Assert.Equal((4L, 0), (order.Version, order.ArchivedRevisions.Count));
    }

    [Fact]
    public async Task The_same_key_replays_another_content_under_it_is_a_mismatch_and_a_second_pending_request_is_an_unrecorded_409()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var store = new FakeRiskExceptionStore();
        var service = RiskService(world, store);
        await service.RequestRiskExceptionAsync(approved.Draft.Id, RiskOf(), Metadata("r-1"));

        var replay = await service.RequestRiskExceptionAsync(approved.Draft.Id, RiskOf(), Metadata("r-1", correlation: "retry"));
        Assert.True(replay.IsReplay);
        Assert.Equal("synthetic-correlation", replay.OriginalCorrelationId);
        Assert.Single(store.Writes);

        var mismatch = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            service.RequestRiskExceptionAsync(approved.Draft.Id, RiskOf(reason: "başka"), Metadata("r-1")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch), Status(mismatch));

        var duplicate = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            service.RequestRiskExceptionAsync(approved.Draft.Id, RiskOf(), Metadata("r-2")));
        Assert.Equal((409, SalesRiskExceptionProblemCodesV1.Duplicate), Status(duplicate));
        Assert.Empty(store.Refusals);

        // Another shipment of the same order is its own exception (number 2).
        var other = await service.RequestRiskExceptionAsync(approved.Draft.Id, RiskOf(OtherShipment), Metadata("r-3"));
        Assert.Equal(2, other.Exception.ExceptionNo);
    }

    [Fact]
    public async Task SAT_D153_the_requester_cannot_approve_their_own_request_the_refusal_is_recorded_and_replayed()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var store = new FakeRiskExceptionStore();
        var service = RiskService(world, store);
        await service.RequestRiskExceptionAsync(approved.Draft.Id, RiskOf(), Metadata("r-1"));

        var self = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            service.ApproveRiskExceptionAsync(approved.Draft.Id, 1, Command("1"), Metadata("a-1")));
        Assert.Equal((409, SalesRiskExceptionProblemCodesV1.SelfDecision), Status(self));
        Assert.Equal(SalesRiskExceptionProblemCodesV1.SelfDecision, Assert.Single(store.Refusals).Code);
        Assert.Equal(SalesShipmentRiskExceptionState.Requested, store.Items.Single().State);

        var again = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            service.ApproveRiskExceptionAsync(approved.Draft.Id, 1, Command("1"), Metadata("a-1", correlation: "retry")));
        Assert.Equal((409, SalesRiskExceptionProblemCodesV1.SelfDecision, true), (again.Status, again.Code, again.IsReplay));

        var reject = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            service.RejectRiskExceptionAsync(approved.Draft.Id, 1, Command("1", "kendi"), Metadata("j-1")));
        Assert.Equal((409, SalesRiskExceptionProblemCodesV1.SelfDecision), Status(reject));
    }

    [Fact]
    public async Task SAT_D160_an_approval_by_another_actor_is_valid_until_the_end_of_the_istanbul_business_date()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var store = new FakeRiskExceptionStore();
        await RiskService(world, store).RequestRiskExceptionAsync(approved.Draft.Id, RiskOf(), Metadata("r-1"));

        // 20:30 UTC = 23:30 Istanbul on 18 Sep.
        var clock = new StepClock(new DateTimeOffset(2026, 9, 18, 20, 30, 0, TimeSpan.Zero));
        world.User.Id = RiskApprover;
        world.Authority.Measured.Clear();
        var decided = await RiskService(world, store, clock).ApproveRiskExceptionAsync(approved.Draft.Id, 1, Command("1"), Metadata("a-1"));

        Assert.Equal(("Approved", "2", RiskApprover, (DateOnly?)Day, true),
            (decided.Exception.State, decided.Exception.Version, decided.Exception.DecidedBy, decided.Exception.ValidOn,
            decided.Exception.IsValidToday));
        Assert.All(world.Authority.Measured, permission => Assert.Equal(RiskApprovePermission, permission));
        Assert.Equal("sales.order.risk-exception.approved.v1", store.Writes[^1].Audit.EventType);

        // Still 18 Sep in UTC, but 00:30 on 19 Sep in Istanbul: the approval has lapsed and a new request of the shipment is open.
        clock.Now = new DateTimeOffset(2026, 9, 18, 21, 30, 0, TimeSpan.Zero);
        world.User.Id = Actor;
        var list = await RiskService(world, store, clock).GetRiskExceptionsAsync(approved.Draft.Id);
        Assert.False(Assert.Single(list.Items).IsValidToday);
        var next = await RiskService(world, store, clock).RequestRiskExceptionAsync(approved.Draft.Id, RiskOf(), Metadata("r-2"));
        Assert.Equal(2, next.Exception.ExceptionNo);
    }

    [Fact]
    public async Task A_today_valid_exception_refuses_a_new_request_and_a_rejection_frees_the_shipment()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var store = new FakeRiskExceptionStore();
        await RiskService(world, store).RequestRiskExceptionAsync(approved.Draft.Id, RiskOf(), Metadata("r-1"));
        world.User.Id = RiskApprover;
        await RiskService(world, store).ApproveRiskExceptionAsync(approved.Draft.Id, 1, Command("1"), Metadata("a-1"));
        world.User.Id = Actor;

        var duplicate = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            RiskService(world, store).RequestRiskExceptionAsync(approved.Draft.Id, RiskOf(), Metadata("r-2")));
        Assert.Equal((409, SalesRiskExceptionProblemCodesV1.Duplicate), Status(duplicate));

        await RiskService(world, store).RequestRiskExceptionAsync(approved.Draft.Id, RiskOf(OtherShipment), Metadata("r-3"));
        world.User.Id = RiskApprover;
        var rejected = await RiskService(world, store).RejectRiskExceptionAsync(approved.Draft.Id, 2, Command("1", "SENTETIK yüksek risk"),
            Metadata("j-1"));
        Assert.Equal(("Rejected", "SENTETIK yüksek risk", (DateOnly?)null), (rejected.Exception.State, rejected.Exception.RejectionReason,
            rejected.Exception.ValidOn));
        world.User.Id = Actor;
        var again = await RiskService(world, store).RequestRiskExceptionAsync(approved.Draft.Id, RiskOf(OtherShipment), Metadata("r-4"));
        Assert.Equal(3, again.Exception.ExceptionNo);
    }

    [Fact]
    public async Task A_stale_version_and_a_second_decision_are_the_keys_recorded_409s()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var store = new FakeRiskExceptionStore();
        await RiskService(world, store).RequestRiskExceptionAsync(approved.Draft.Id, RiskOf(), Metadata("r-1"));
        world.User.Id = RiskApprover;
        var service = RiskService(world, store);

        var stale = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            service.ApproveRiskExceptionAsync(approved.Draft.Id, 1, Command("2"), Metadata("a-0")));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict), Status(stale));

        await service.ApproveRiskExceptionAsync(approved.Draft.Id, 1, Command("1"), Metadata("a-1"));
        var replay = await service.ApproveRiskExceptionAsync(approved.Draft.Id, 1, Command("1"), Metadata("a-1", correlation: "retry"));
        Assert.True(replay.IsReplay);

        var second = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            service.RejectRiskExceptionAsync(approved.Draft.Id, 1, Command("2", "geç"), Metadata("j-1")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), Status(second));
        Assert.Equal([SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.StateConflict],
            store.Refusals.Select(refusal => refusal.Code));
    }

    [Fact]
    public async Task Permission_refusals_are_403_before_anything_is_read_and_nothing_is_written()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var store = new FakeRiskExceptionStore();
        world.Authority.Denied.Add(RiskRequestPermission);
        var findCalls = 0;
        world.Store.OnFindDraft = () => findCalls++;

        var request = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            RiskService(world, store).RequestRiskExceptionAsync(approved.Draft.Id, RiskOf(), Metadata("r-1")));
        Assert.Equal((403, "forbidden"), Status(request));

        world.Authority.Denied.Clear();
        await RiskService(world, store).RequestRiskExceptionAsync(approved.Draft.Id, RiskOf(), Metadata("r-1"));
        findCalls = 0;
        // The request name does not decide (SAT-D153): an approver needs the approve name.
        world.User.Id = RiskApprover;
        world.Authority.Denied.Add(RiskApprovePermission);
        var approve = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            RiskService(world, store).ApproveRiskExceptionAsync(approved.Draft.Id, 1, Command("1"), Metadata("a-1")));
        Assert.Equal((403, "forbidden"), Status(approve));
        var reject = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            RiskService(world, store).RejectRiskExceptionAsync(approved.Draft.Id, 1, Command("1", "r"), Metadata("j-1")));
        Assert.Equal((403, "forbidden"), Status(reject));

        // The list is a `sales.drafts.read` read: the request name alone does not show it (no amounts either way).
        world.Authority.Denied.Clear();
        world.Authority.Denied.Add(SalesPermissionManifestV1.DraftsRead);
        var list = await Assert.ThrowsAsync<SalesDraftServiceException>(() => RiskService(world, store).GetRiskExceptionsAsync(approved.Draft.Id));
        Assert.Equal((403, "forbidden"), Status(list));

        Assert.Equal(0, findCalls);
        Assert.Single(store.Writes);
        Assert.Empty(store.Refusals);

        // A permission lost before the save (the last fence) writes nothing either.
        world.Authority.Denied.Clear();
        world.Authority.LostLater = (RiskApprovePermission, world.Authority.Calls + 3);
        var late = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            RiskService(world, store).ApproveRiskExceptionAsync(approved.Draft.Id, 1, Command("1"), Metadata("a-2")));
        Assert.Equal((403, "forbidden"), Status(late));
        Assert.Single(store.Writes);
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_401_and_a_company_the_caller_cannot_reach_is_403()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var store = new FakeRiskExceptionStore();

        var foreign = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            RiskService(world, store, company: OtherRiskCompany).RequestRiskExceptionAsync(approved.Draft.Id, RiskOf(), Metadata("r-1")));
        Assert.Equal((403, "forbidden"), Status(foreign));

        world.User.Authenticated = false;
        var anonymous = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            RiskService(world, store).RequestRiskExceptionAsync(approved.Draft.Id, RiskOf(), Metadata("r-1")));
        Assert.Equal((401, "unauthenticated"), Status(anonymous));
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task Another_companys_exception_is_invisible_under_the_tenant_filter_and_answers_404()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var store = new FakeRiskExceptionStore();
        // A SYNTHETIC row of another company on the same order id and number (the filter, not the id, keeps it out).
        var foreign = SalesShipmentRiskException.Request(Guid.NewGuid(), OtherRiskCompany, approved.Draft.Id, 1, RiskShipment, "r",
            "synthetic-foreign", new DateTimeOffset(2026, 9, 18, 8, 0, 0, TimeSpan.Zero));
        store.Seed(foreign);
        world.User.Id = RiskApprover;

        var missing = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            RiskService(world, store).ApproveRiskExceptionAsync(approved.Draft.Id, 1, Command("1"), Metadata("a-1")));
        Assert.Equal((404, "not_found"), Status(missing));
        Assert.Empty((await RiskService(world, store).GetRiskExceptionsAsync(approved.Draft.Id)).Items);

        // Our own request of the same shipment is not blocked by the foreign pending row and is numbered within our company.
        world.User.Id = Actor;
        var own = await RiskService(world, store).RequestRiskExceptionAsync(approved.Draft.Id, RiskOf(), Metadata("r-1"));
        Assert.Equal(1, own.Exception.ExceptionNo);
        Assert.Equal(SalesShipmentRiskExceptionState.Requested, foreign.State);
    }

    [Fact]
    public async Task An_order_that_is_not_approved_or_is_cancelled_takes_no_exception_and_the_refusal_is_not_recorded()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-create"))).Detail.Draft;
        await world.Service().SubmitDraftAsync(created.Id, Command("2"), Metadata("k-submit"));
        var store = new FakeRiskExceptionStore();

        var pending = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            RiskService(world, store).RequestRiskExceptionAsync(created.Id, RiskOf(), Metadata("r-1")));
        Assert.Equal((409, SalesRiskExceptionProblemCodesV1.OrderNotEligible), Status(pending));

        var unknown = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            RiskService(world, store).RequestRiskExceptionAsync(Guid.NewGuid(), RiskOf(), Metadata("r-2")));
        Assert.Equal((404, "not_found"), Status(unknown));
        Assert.Empty(store.Writes);
        Assert.Empty(store.Refusals);
    }

    [Fact]
    public async Task The_body_is_validated_before_any_read_and_an_over_long_reason_is_422()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var store = new FakeRiskExceptionStore();

        var empty = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            RiskService(world, store).RequestRiskExceptionAsync(approved.Draft.Id, RiskOf(Guid.Empty), Metadata("r-1")));
        Assert.Equal((400, "validation_failed"), Status(empty));

        var tooLong = await Assert.ThrowsAsync<SalesDraftServiceException>(() => RiskService(world, store).RequestRiskExceptionAsync(
            approved.Draft.Id, RiskOf(reason: new string('x', SalesShipmentRiskException.MaximumReasonLength + 1)), Metadata("r-2")));
        Assert.Equal((422, SalesRuleCodes.RiskExceptionReasonTooLong), Status(tooLong));

        var noReason = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            RiskService(world, store).RequestRiskExceptionAsync(approved.Draft.Id, RiskOf(reason: " "), Metadata("r-3")));
        Assert.Equal((422, "validation_failed"), Status(noReason));

        var version = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            RiskService(world, store).ApproveRiskExceptionAsync(approved.Draft.Id, 1, Command("x"), Metadata("a-1")));
        Assert.Equal((400, "validation_failed"), Status(version));
        Assert.Empty(store.Writes);
    }

    [Fact]
    public void The_strict_bodies_refuse_unknown_members_and_the_approval_takes_no_reason()
    {
        var request = SalesRiskExceptionJson.ReadRequest($$"""{"loadingInstructionId":"{{RiskShipment}}","reason":"r"}""");
        Assert.Equal(RiskOf(reason: "r"), request);
        Assert.ThrowsAny<System.Text.Json.JsonException>(() =>
            SalesRiskExceptionJson.ReadRequest($$"""{"loadingInstructionId":"{{RiskShipment}}","reason":"r","amount":1}"""));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => SalesRiskExceptionJson.ReadApproval("""{"expectedVersion":"1","reason":"r"}"""));
        Assert.Equal("r", SalesRiskExceptionJson.ReadRejection("""{"expectedVersion":"1","reason":"r"}""").Reason);
    }

    private sealed class StepClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>
    /// A SYNTHETIC risk exception store under a tenant filter on <see cref="Company"/>: the attempts are keyed by command and key,
    /// a different actor or content under a key is a mismatch, and other companies' rows are invisible.
    /// </summary>
    private sealed class FakeRiskExceptionStore : ISalesRiskExceptionStore
    {
        private readonly List<SalesShipmentRiskException> _items = [];
        private readonly Dictionary<(string Command, string Key), (string Actor, string Hash, SalesShipmentRiskException? Item,
            SalesDraftTerminalRefusal? Refusal, string Correlation)> _attempts = [];

        public List<(string Command, SalesDraftWriteEnvelope Envelope, SalesDraftStateAudit Audit)> Writes { get; } = [];
        public List<SalesDraftTerminalRefusal> Refusals { get; } = [];
        public IEnumerable<SalesShipmentRiskException> Items => Visible;
        private IEnumerable<SalesShipmentRiskException> Visible => _items.Where(item => item.CompanyId == Company);

        public void Seed(SalesShipmentRiskException item) => _items.Add(item);

        public Task<SalesRiskExceptionReplay?> FindRiskExceptionReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash,
            string commandName, CancellationToken cancellationToken = default)
        {
            if (!_attempts.TryGetValue((commandName, envelope.IdempotencyKey), out var attempt))
            {
                return Task.FromResult<SalesRiskExceptionReplay?>(null);
            }

            if (attempt.Actor != envelope.ActorId || attempt.Hash != requestHash)
            {
                throw new SalesDraftStoreException(SalesDraftStoreCodes.SameKeyPayloadMismatch, "synthetic");
            }

            return Task.FromResult<SalesRiskExceptionReplay?>(new(attempt.Item, attempt.Refusal, attempt.Correlation));
        }

        public Task<SalesRiskExceptionStoreResult> StoreRiskExceptionAsync(SalesDraftWriteEnvelope envelope, string requestHash,
            Guid orderId, Func<int, DateTimeOffset, SalesShipmentRiskException> build, SalesDraftStateAudit audit,
            CancellationToken cancellationToken = default)
        {
            var next = Visible.Where(item => item.OrderId == orderId).Select(item => item.ExceptionNo).DefaultIfEmpty(0).Max() + 1;
            var item = build(next, new DateTimeOffset(2026, 9, 18, 9, 0, 0, TimeSpan.Zero));
            _items.Add(item);
            _attempts[(SalesRiskExceptionCommands.RequestV1, envelope.IdempotencyKey)] =
                (envelope.ActorId, requestHash, item, null, envelope.CorrelationId);
            Writes.Add((SalesRiskExceptionCommands.RequestV1, envelope, audit));
            return Task.FromResult(new SalesRiskExceptionStoreResult(item, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId));
        }

        public Task RecordRiskExceptionRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
            Guid orderId, SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken = default)
        {
            _attempts[(commandName, envelope.IdempotencyKey)] = (envelope.ActorId, requestHash, null, refusal, envelope.CorrelationId);
            Refusals.Add(refusal);
            return Task.CompletedTask;
        }

        public Task<SalesRiskExceptionStoreResult?> DecideRiskExceptionAsync(SalesDraftWriteEnvelope envelope, string requestHash,
            string commandName, Guid orderId, int exceptionNo, Action<SalesShipmentRiskException, DateTimeOffset> mutate,
            SalesDraftStateAudit audit, CancellationToken cancellationToken = default)
        {
            var item = Visible.SingleOrDefault(row => row.OrderId == orderId && row.ExceptionNo == exceptionNo);
            if (item is null)
            {
                return Task.FromResult<SalesRiskExceptionStoreResult?>(null);
            }

            mutate(item, item.RequestedAtUtc.AddHours(1));
            _attempts[(commandName, envelope.IdempotencyKey)] = (envelope.ActorId, requestHash, item, null, envelope.CorrelationId);
            Writes.Add((commandName, envelope, audit));
            return Task.FromResult<SalesRiskExceptionStoreResult?>(new(item, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId));
        }

        public Task<SalesShipmentRiskException?> FindRiskExceptionAsync(Guid orderId, int exceptionNo,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Visible.SingleOrDefault(row => row.OrderId == orderId && row.ExceptionNo == exceptionNo));

        public Task<IReadOnlyList<SalesShipmentRiskException>> ListRiskExceptionsAsync(Guid orderId, int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SalesShipmentRiskException>>(
                [.. Visible.Where(row => row.OrderId == orderId).OrderByDescending(row => row.ExceptionNo).Take(limit)]);

        public Task<IReadOnlyList<SalesShipmentRiskException>> ListShipmentRiskExceptionsAsync(Guid orderId, Guid loadingInstructionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SalesShipmentRiskException>>([.. Visible
                .Where(row => row.OrderId == orderId && row.LoadingInstructionId == loadingInstructionId)
                .OrderByDescending(row => row.ExceptionNo)]);
    }
}
