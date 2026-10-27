using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Requests;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// API-20 / API-21 over SYNTHETIC ports: the request use cases on the draft service's fence — authority before anything is
/// read (403 / 503 never recorded), the replay before any owner, the Cari / Product / Settings owners' answers, the
/// single-approver decision and the reasoned rejection, the terminal 409s of ADR-011 §7, and another company's request as
/// 404. The Sales request names are registered in the central catalogue, so the real names are bound here. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private static CreateSalesRequestRequest RequestOf(int lines = 1, string quantity = "1.25") => new(Party, Account, Day,
        [.. Enumerable.Range(0, lines).Select(index => new SalesRequestLineRequest(
            Guid.Parse($"{index + 1:D8}-0000-4000-8000-00000000000a"), Product, "7", KgUnit, quantity, Note: index == 0 ? "ilk satır" : null))],
        new DateOnly(2026, 9, 30), "Sentetik müşteri talebi");

    private static SalesDraftStateCommandRequest Decision(string version, string? reason = null) => new(version, reason);

    [Fact]
    public async Task Requests_create_is_stored_once_under_the_write_name_with_the_owners_snapshots_and_no_money_profile()
    {
        var world = new World();

        var response = await world.Service().CreateRequestAsync(RequestOf(lines: 2), Metadata("r-1"));

        Assert.False(response.IsReplay);
        var summary = response.Detail.Request;
        Assert.Equal((Company, "SR-2026-000001", "Submitted", "1", Party, Account, Day, 2),
            (summary.CompanyId, summary.DocumentNo, summary.State, summary.Version, summary.PartyId, summary.CompanyAccountId,
                summary.BusinessDate, summary.LineCount));
        var line = response.Detail.Lines[0];
        Assert.Equal((1, Product, "7", "GDC-001", "Sentetik bant", KgUnit, "kg", "1.25", "ilk satır"),
            (line.LineNo, line.ProductId, line.ProductVersion, line.ProductCode, line.ProductName, line.OrderUnitId, line.OrderUnitCode,
                line.Quantity, line.Note));
        Assert.Null(response.Detail.Decision);
        Assert.Equal(("Sentetik müşteri talebi", new DateOnly(2026, 9, 30)), (response.Detail.Note, response.Detail.RequestedDeliveryDate));
        // The Cari account once, each line's Product eligibility and order-unit profile; a request reads no money profile.
        Assert.Equal((1, 2), (world.Accounts.Calls, world.Products.Calls));
        Assert.Null(world.Settings.MoneyAsked);
        var envelope = Assert.Single(world.Requests!.Envelopes);
        Assert.Equal((Actor, SalesPermissionManifestV1.RequestsWrite, "r-1"), (envelope.ActorId, envelope.PermissionCode, envelope.IdempotencyKey));
        Assert.All(world.Authority.Measured, name => Assert.Equal(SalesPermissionManifestV1.RequestsWrite, name));
        Assert.Empty(world.Store.Envelopes);
    }

    [Theory]
    [InlineData(SalesPermissionManifestV1.RequestsWrite)]
    [InlineData("all-but-write")]
    public async Task Requests_without_the_write_name_create_is_403_before_anything_is_read_and_no_other_name_confers_it(string denied)
    {
        var world = new World();
        world.Authority.Denied.Add(SalesPermissionManifestV1.RequestsWrite);
        if (denied == "all-but-write")
        {
            // The draft, order and request read/approve names are all held: none of them confers the request write.
            Assert.DoesNotContain(SalesPermissionManifestV1.RequestsWrite, new[]
            {
                CreatePermission, ReadPermission, ApprovePermission, PricePermission, SalesPermissionManifestV1.RequestsRead,
                SalesPermissionManifestV1.RequestsApprove,
            });
        }

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateRequestAsync(RequestOf(), Metadata("r-1")));

        Assert.Equal((403, "forbidden"), (refused.Status, refused.Code));
        Assert.Equal((0, 0, 0, 0), (world.Accounts.Calls, world.Products.Calls, world.Settings.Calls, world.Requests!.Attempts));
    }

    [Fact]
    public async Task Requests_an_unbound_or_unregistered_name_is_503_and_a_host_without_the_store_is_503_after_the_fence()
    {
        var world = new World();
        world.Bindings = world.Bindings with { RequestWritePermission = "sales.requests.not-registered" };
        var unregistered = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateRequestAsync(RequestOf(), Metadata("r-1")));
        Assert.Equal((503, SalesDraftProblemCodesV1.PermissionUnregistered), (unregistered.Status, unregistered.Code));

        var noStore = new World { Requests = null };
        var missing = await Assert.ThrowsAsync<SalesDraftServiceException>(() => noStore.Service().GetRequestListAsync(new SalesRequestListQuery()));
        Assert.Equal(503, missing.Status);
        noStore.Authority.Denied.Add(SalesPermissionManifestV1.RequestsRead);
        var fenced = await Assert.ThrowsAsync<SalesDraftServiceException>(() => noStore.Service().GetRequestListAsync(new SalesRequestListQuery()));
        Assert.Equal(403, fenced.Status);
    }

    [Fact]
    public async Task Requests_the_same_key_content_and_actor_replays_before_any_owner_and_another_content_is_one_409()
    {
        var world = new World();
        var request = RequestOf();
        var first = await world.Service().CreateRequestAsync(request, Metadata("r-1"));
        world.ResetOwnerCalls();

        var replay = await world.Service().CreateRequestAsync(request, Metadata("r-1", correlation: "second"));

        Assert.True(replay.IsReplay);
        Assert.Equal((first.Detail.Request.Id, "synthetic-correlation"), (replay.Detail.Request.Id, replay.OriginalCorrelationId));
        Assert.Equal((0, 0, 0), (world.Accounts.Calls, world.Settings.Calls, world.Products.Calls));
        var mismatch = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().CreateRequestAsync(RequestOf(quantity: "1.250"), Metadata("r-1")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch), (mismatch.Status, mismatch.Code));
        world.User.Id = "another-actor";
        var otherActor = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateRequestAsync(request, Metadata("r-1")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch), (otherActor.Status, otherActor.Code));
        Assert.Equal(1, world.Requests!.Count);
    }

    [Theory]
    [InlineData("not-found", 409, SalesDraftProblemCodesV1.AccountState)]
    [InlineData("passive-account", 409, SalesDraftProblemCodesV1.AccountState)]
    [InlineData("other-company", 409, SalesDraftProblemCodesV1.AccountState)]
    [InlineData("denied", 403, "forbidden")]
    [InlineData("unavailable", 503, SalesDraftProblemCodesV1.CounterpartyProviderUnavailable)]
    public async Task Requests_the_customer_is_the_Cari_owners_answer_and_nothing_is_recorded(string fault, int status, string code)
    {
        var world = new World();
        world.Accounts.Fault = fault;

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateRequestAsync(RequestOf(), Metadata("r-1")));

        Assert.Equal((status, code), (refused.Status, refused.Code));
        Assert.Equal((0, 0), (world.Products.Calls, world.Requests!.Attempts));
    }

    [Theory]
    [InlineData("field-errors", 422, "unsupported_product")]
    [InlineData("other-unit", 422, "unsupported_unit")]
    [InlineData("unavailable", 503, SalesDraftProblemCodesV1.ProductEligibilityUnavailable)]
    [InlineData("unit-not-found", 422, "unsupported_unit")]
    public async Task Requests_the_product_and_its_order_unit_are_the_owners_answers(string fault, int status, string code)
    {
        var world = new World();
        if (fault == "unit-not-found") world.Settings.Fault = fault;
        else world.Products.Fault = fault;

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateRequestAsync(RequestOf(), Metadata("r-1")));

        Assert.Equal((status, code), (refused.Status, refused.Code));
        Assert.Equal(0, world.Requests!.Attempts);
    }

    [Fact]
    public async Task Requests_a_stale_product_version_is_the_keys_terminal_refusal_recorded_and_replayed()
    {
        var world = new World();
        world.Products.VersionConflict = true;

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateRequestAsync(RequestOf(), Metadata("r-1")));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict, false), (refused.Status, refused.Code, refused.IsReplay));
        var recorded = Assert.Single(world.Requests!.Refusals);
        Assert.Equal((SalesRequestCommands.CreateV1, (Guid?)null), (recorded.Command, recorded.RequestId));

        world.Products.VersionConflict = false;
        world.ResetOwnerCalls();
        var replayed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateRequestAsync(RequestOf(), Metadata("r-1")));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict, true, "synthetic-correlation"),
            (replayed.Status, replayed.Code, replayed.IsReplay, replayed.OriginalCorrelationId));
        Assert.Equal((0, 0), (world.Products.Calls, world.Requests.Count));
    }

    [Fact]
    public async Task Requests_detail_and_list_read_under_the_read_name_and_another_companys_request_is_404()
    {
        var world = new World();
        var created = await world.Service().CreateRequestAsync(RequestOf(), Metadata("r-1"));
        var second = await world.Service().CreateRequestAsync(RequestOf(lines: 2), Metadata("r-2"));
        await world.Service().ApproveRequestAsync(second.Detail.Request.Id, Decision("1"), Metadata("r-approve"));
        world.Authority.Measured.Clear();

        var detail = await world.Service().GetRequestDetailAsync(created.Detail.Request.Id);
        Assert.Equal(created.Detail.Request.Id, detail.Request.Id);
        var list = await world.Service().GetRequestListAsync(new SalesRequestListQuery());
        Assert.Equal([second.Detail.Request.Id, created.Detail.Request.Id], list.Items.Select(item => item.Id));
        var approved = await world.Service().GetRequestListAsync(new SalesRequestListQuery(State: "Approved"));
        Assert.Equal([second.Detail.Request.Id], approved.Items.Select(item => item.Id));
        Assert.All(world.Authority.Measured, name => Assert.Equal(SalesPermissionManifestV1.RequestsRead, name));

        foreach (var state in new[] { "approved", "Unknown", "", "1" })
        {
            var bad = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetRequestListAsync(new SalesRequestListQuery(State: state)));
            Assert.Equal((400, "validation_failed"), (bad.Status, bad.Code));
        }

        var foreign = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetRequestDetailAsync(Guid.NewGuid()));
        Assert.Equal(404, foreign.Status);
        world.Authority.Denied.Add(SalesPermissionManifestV1.RequestsRead);
        var denied = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetRequestDetailAsync(created.Detail.Request.Id));
        Assert.Equal(403, denied.Status);
    }

    [Fact]
    public async Task Requests_one_approver_decides_under_the_approve_name_and_a_resent_key_replays()
    {
        var world = new World();
        var created = await world.Service().CreateRequestAsync(RequestOf(), Metadata("r-1"));
        var id = created.Detail.Request.Id;
        world.Authority.Measured.Clear();
        world.User.Id = "synthetic-approver";

        var approved = await world.Service().ApproveRequestAsync(id, Decision("1"), Metadata("r-approve"));

        Assert.Equal(("Approved", "2", "synthetic-approver", (string?)null),
            (approved.Detail.Request.State, approved.Detail.Request.Version, approved.Detail.Decision!.DecidedBy,
                approved.Detail.Decision.RejectionReason));
        Assert.All(world.Authority.Measured, name => Assert.Equal(SalesPermissionManifestV1.RequestsApprove, name));
        var audit = Assert.Single(world.Requests!.Audits);
        Assert.Equal(("sales.request.approved.v1", "Approve"), (audit.EventType, audit.Operation));
        var replay = await world.Service().ApproveRequestAsync(id, Decision("1"), Metadata("r-approve", correlation: "again"));
        Assert.Equal((true, "synthetic-correlation", "Approved"), (replay.IsReplay, replay.OriginalCorrelationId, replay.Detail.Request.State));
        Assert.Single(world.Requests.Audits);
    }

    [Fact]
    public async Task Requests_the_write_and_read_names_do_not_confer_the_decision_and_it_is_403_before_the_request_is_read()
    {
        var world = new World();
        var created = await world.Service().CreateRequestAsync(RequestOf(), Metadata("r-1"));
        world.Authority.Denied.Add(SalesPermissionManifestV1.RequestsApprove);
        var reads = world.Requests!.Reads;

        var approve = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveRequestAsync(created.Detail.Request.Id, Decision("1"), Metadata("r-approve")));
        var reject = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RejectRequestAsync(created.Detail.Request.Id, Decision("1", "neden"), Metadata("r-reject")));

        Assert.Equal((403, 403), (approve.Status, reject.Status));
        Assert.Equal(reads, world.Requests.Reads);
        Assert.Equal(1, world.Requests.Attempts);
        // The order approval name is a separate name: holding it alone does not decide a request (it is a stand-in bound above).
        Assert.NotEqual(SalesPermissionManifestV1.OrdersApprove, SalesPermissionManifestV1.RequestsApprove);
    }

    [Fact]
    public async Task Requests_a_rejection_needs_its_reason_is_allowed_from_approved_and_a_rejected_request_is_the_keys_terminal_409()
    {
        var world = new World();
        var id = (await world.Service().CreateRequestAsync(RequestOf(), Metadata("r-1"))).Detail.Request.Id;

        var blank = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RejectRequestAsync(id, Decision("1", " "), Metadata("r-reject-blank")));
        Assert.Equal((422, SalesRuleCodes.RequestRejectionReasonRequired), (blank.Status, blank.Code));
        Assert.Empty(world.Requests!.Refusals);

        await world.Service().ApproveRequestAsync(id, Decision("1"), Metadata("r-approve"));
        var rejected = await world.Service().RejectRequestAsync(id, Decision("2", "Müşteri vazgeçti"), Metadata("r-reject"));
        Assert.Equal(("Rejected", "3", "Müşteri vazgeçti"),
            (rejected.Detail.Request.State, rejected.Detail.Request.Version, rejected.Detail.Decision!.RejectionReason));

        var again = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveRequestAsync(id, Decision("3"), Metadata("r-approve-again")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), (again.Status, again.Code));
        var replayed = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveRequestAsync(id, Decision("3"), Metadata("r-approve-again")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict, true), (replayed.Status, replayed.Code, replayed.IsReplay));
        var refusal = Assert.Single(world.Requests.Refusals);
        Assert.Equal((SalesRequestCommands.ApproveV1, (Guid?)id), (refusal.Command, refusal.RequestId));
    }

    [Fact]
    public async Task Requests_a_stale_version_is_the_keys_terminal_409_and_an_unknown_request_is_404()
    {
        var world = new World();
        var id = (await world.Service().CreateRequestAsync(RequestOf(), Metadata("r-1"))).Detail.Request.Id;

        var stale = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveRequestAsync(id, Decision("7"), Metadata("r-approve")));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict), (stale.Status, stale.Code));
        Assert.Equal("expectedVersion", Assert.Single(stale.Errors).Field);
        Assert.Single(world.Requests!.Refusals);

        var unknown = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveRequestAsync(Guid.NewGuid(), Decision("1"), Metadata("r-approve-unknown")));
        Assert.Equal(404, unknown.Status);
        var malformed = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveRequestAsync(id, Decision("v1"), Metadata("r-approve-malformed")));
        Assert.Equal((400, "validation_failed"), (malformed.Status, malformed.Code));
    }

    /// <summary>An in-memory request store with the contract's replay semantics; no database, no owner rule.</summary>
    private sealed class FakeRequestStore : ISalesRequestStore
    {
        private readonly Dictionary<(string Command, string Key), (string Actor, string Hash, Guid? RequestId, SalesDraftTerminalRefusal? Refusal, string Correlation)> attempts = [];
        private readonly List<SalesRequest> requests = [];
        private int number;
        public List<SalesDraftWriteEnvelope> Envelopes { get; } = [];
        public List<SalesDraftStateAudit> Audits { get; } = [];
        public List<(string Command, Guid? RequestId, SalesDraftTerminalRefusal Refusal)> Refusals { get; } = [];
        public int Attempts => attempts.Count;
        public int Count => requests.Count;
        public int Reads { get; private set; }

        public Task<SalesRequestStoreResult> StoreRequestAsync(SalesDraftWriteEnvelope envelope, string requestHash,
            Func<string, SalesRequest> build, CancellationToken cancellationToken = default)
        {
            if (Replay(envelope, requestHash, SalesRequestCommands.CreateV1) is { } replay)
            {
                return Task.FromResult(replay);
            }

            var request = build($"SR-2026-{++number:D6}");
            requests.Add(request);
            attempts[(SalesRequestCommands.CreateV1, envelope.IdempotencyKey)] = (envelope.ActorId, requestHash, request.Id, null, envelope.CorrelationId);
            Envelopes.Add(envelope);
            return Task.FromResult(new SalesRequestStoreResult(request, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId));
        }

        public Task<SalesRequestReplay?> FindRequestReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            if (!attempts.TryGetValue((commandName, envelope.IdempotencyKey), out var attempt))
            {
                return Task.FromResult<SalesRequestReplay?>(null);
            }

            if (attempt.Actor != envelope.ActorId || attempt.Hash != requestHash)
            {
                throw new SalesDraftStoreException(SalesDraftStoreCodes.SameKeyPayloadMismatch, "mismatch");
            }

            return Task.FromResult<SalesRequestReplay?>(new SalesRequestReplay(
                attempt.RequestId is { } id ? requests.Single(request => request.Id == id) : null, attempt.Refusal, attempt.Correlation));
        }

        public Task RecordRequestRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName, Guid? requestId,
            SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken = default)
        {
            attempts[(commandName, envelope.IdempotencyKey)] = (envelope.ActorId, requestHash, null, refusal, envelope.CorrelationId);
            Refusals.Add((commandName, requestId, refusal));
            return Task.CompletedTask;
        }

        public Task<SalesRequestStoreResult?> DecideRequestAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
            Guid requestId, Action<SalesRequest, DateTimeOffset> mutate, SalesDraftStateAudit audit, CancellationToken cancellationToken = default)
        {
            Reads++;
            if (Replay(envelope, requestHash, commandName) is { } replay)
            {
                return Task.FromResult<SalesRequestStoreResult?>(replay);
            }

            var request = requests.SingleOrDefault(candidate => candidate.Id == requestId);
            if (request is null)
            {
                return Task.FromResult<SalesRequestStoreResult?>(null);
            }

            mutate(request, DateTimeOffset.UtcNow);
            attempts[(commandName, envelope.IdempotencyKey)] = (envelope.ActorId, requestHash, request.Id, null, envelope.CorrelationId);
            Audits.Add(audit);
            return Task.FromResult<SalesRequestStoreResult?>(new SalesRequestStoreResult(request, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId));
        }

        public Task<SalesRequest?> FindRequestAsync(Guid requestId, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(requests.SingleOrDefault(request => request.Id == requestId));
        }

        public Task<SalesRequestPage> ListRequestsAsync(int pageSize, string? cursor, SalesRequestState? state,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            var items = requests.Where(request => state is null || request.State == state)
                .OrderByDescending(request => request.CreatedAtUtc).ThenByDescending(request => requests.IndexOf(request)).Take(pageSize).ToArray();
            return Task.FromResult(new SalesRequestPage(items, null));
        }

        private SalesRequestStoreResult? Replay(SalesDraftWriteEnvelope envelope, string requestHash, string commandName)
        {
            if (!attempts.TryGetValue((commandName, envelope.IdempotencyKey), out var attempt))
            {
                return null;
            }

            if (attempt.Actor != envelope.ActorId || attempt.Hash != requestHash)
            {
                throw new SalesDraftStoreException(SalesDraftStoreCodes.SameKeyPayloadMismatch, "mismatch");
            }

            if (attempt.RequestId is not { } id)
            {
                throw new SalesDraftStoreException(SalesDraftStoreCodes.ConcurrentChange, "refusal recorded");
            }

            return new SalesRequestStoreResult(requests.Single(request => request.Id == id), SalesDraftWriteOutcome.Replayed, attempt.Correlation);
        }
    }
}
