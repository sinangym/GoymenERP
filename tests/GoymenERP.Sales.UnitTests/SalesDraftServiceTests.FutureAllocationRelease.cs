using System.Data;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// X1 / KLN-D28 (9) over SYNTHETIC owners: with the host's <see cref="ISalesOrderFutureAllocationReleaseParticipantV1"/> composed,
/// the cancellation preview SHOWS each line's active future supply allocations without blocking on them, and the approved
/// cancellation releases them inside the store's own transaction under the command's envelope. The owner's terminal refusal
/// (version / state / selection conflict) is the key's recorded answer and is replayed without asking again; any other refusal
/// is not recorded and the same key retries. A physical allocation still blocks, and the header revision still refuses an
/// order holding a future allocation (it releases nothing).
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private const string OwnerVersionConflict = "synthetic_owner_version_conflict";
    private const string OwnerBusy = "synthetic_owner_busy";

    [Fact]
    public async Task With_the_release_participant_a_future_allocation_is_shown_and_released_in_the_cancellation_transaction()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        Owners(world);
        world.FutureAllocations = new FakeFutureAllocations(order => new(Company, order,
            [new SalesOrderLineAllocationV2(line, SalesOrderAllocationKindsV2.FutureSupply, 2, 7.125m, FuturePurchaseUnit)]));
        var release = new FakeFutureAllocationRelease();
        world.FutureAllocationRelease = release;

        var preview = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);

        Assert.True(preview.Cancellable);
        Assert.Equal(SalesCancellationImpactStatusesV1.None,
            preview.Sources.Single(source => source.Source == SalesCancellationImpactSourcesV1.ActiveAllocations).Status);
        // KLN-D28 (9): shown per line; the physical kilograms stay physical only.
        Assert.Equal((0, "0", (int?)2), (preview.Lines[0].ActiveAllocations, preview.Lines[0].AllocatedKg,
            preview.Lines[0].FutureSupplyAllocations));
        Assert.Empty(release.Requests);

        var cancelled = await world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "neden", preview.ImpactToken), Metadata("k-cancel-release"));

        Assert.Equal("Cancelled", cancelled.Detail.Draft.State);
        Assert.Single(world.Store.Cancellations);
        Assert.Equal(1, world.Store.CancellationTransactions);
        var asked = Assert.Single(release.Requests);
        Assert.Same(FakeTransaction.Instance, asked.Transaction);
        Assert.Equal((approved.Draft.Id, SalesOrderFutureAllocationReleaseTextsV1.CancelledOrder), (asked.Request.OrderId, asked.Request.ReasonText));
        Assert.Equal([line], asked.Request.LineIds);
        Assert.Equal((CancelPermission, "k-cancel-release", "synthetic-correlation"),
            (asked.Request.Envelope.PermissionCode, asked.Request.Envelope.IdempotencyKey, asked.Request.Envelope.CorrelationId));
        Assert.Empty(world.Store.Refusals);

        // The same key: the stored result again — the owner is not asked to release twice.
        var replay = await world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "neden", preview.ImpactToken), Metadata("k-cancel-release", correlation: "retry"));
        Assert.True(replay.IsReplay);
        Assert.Single(release.Requests);
        Assert.Equal(1, world.Store.CancellationTransactions);
    }

    [Fact]
    public async Task The_owners_terminal_release_refusal_is_recorded_with_its_code_and_replayed_without_asking_again()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        Owners(world);
        world.FutureAllocations = new FakeFutureAllocations(order => new(Company, order,
            [new SalesOrderLineAllocationV2(line, SalesOrderAllocationKindsV2.FutureSupply, 1, 1m, FuturePurchaseUnit)]));
        var release = new FakeFutureAllocationRelease
        {
            Refusal = new SalesFutureAllocationReleaseException(409, OwnerVersionConflict, terminal: true),
        };
        world.FutureAllocationRelease = release;
        var preview = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "neden", preview.ImpactToken), Metadata("k-cancel-terminal")));

        Assert.Equal((409, OwnerVersionConflict), Status(refused));
        Assert.Equal([new SalesDraftFieldError("futureSupplyAllocations", OwnerVersionConflict)], refused.Errors);
        var refusal = Assert.Single(world.Store.Refusals);
        Assert.Equal((CancelPermission, (Guid?)approved.Draft.Id), (refusal.Envelope.PermissionCode, refusal.DraftId));
        Assert.Empty(world.Store.Cancellations);

        release.Refusal = null;
        var replayed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "neden", preview.ImpactToken), Metadata("k-cancel-terminal", correlation: "retry")));
        Assert.Equal((409, OwnerVersionConflict, true), (replayed.Status, replayed.Code, replayed.IsReplay));
        Assert.Single(release.Requests);
        Assert.Empty(world.Store.Cancellations);
    }

    [Fact]
    public async Task A_non_terminal_release_refusal_is_not_recorded_and_the_same_key_retries_and_cancels()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        Owners(world);
        world.FutureAllocations = new FakeFutureAllocations(order => new(Company, order,
            [new SalesOrderLineAllocationV2(line, SalesOrderAllocationKindsV2.FutureSupply, 1, 1m, FuturePurchaseUnit)]));
        var release = new FakeFutureAllocationRelease
        {
            Refusal = new SalesFutureAllocationReleaseException(409, OwnerBusy, terminal: false),
        };
        world.FutureAllocationRelease = release;
        var preview = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);

        var busy = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "neden", preview.ImpactToken), Metadata("k-cancel-busy")));
        Assert.Equal((409, OwnerBusy, false), (busy.Status, busy.Code, busy.IsReplay));
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(world.Store.Cancellations);

        // An unreadable owner (any status other than 409/503 is answered as 503): still not recorded.
        release.Refusal = new SalesFutureAllocationReleaseException(500, "service_unavailable", terminal: false);
        var unavailable = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "neden", preview.ImpactToken), Metadata("k-cancel-busy")));
        Assert.Equal((503, "service_unavailable"), Status(unavailable));
        Assert.Empty(world.Store.Refusals);

        release.Refusal = null;
        var cancelled = await world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "neden", preview.ImpactToken), Metadata("k-cancel-busy"));
        Assert.Equal(("Cancelled", false), (cancelled.Detail.Draft.State, cancelled.IsReplay));
        Assert.Equal(3, release.Requests.Count);
        Assert.Single(world.Store.Cancellations);
    }

    [Fact]
    public async Task With_the_release_participant_a_physical_allocation_still_blocks_and_nothing_is_released()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        var (allocations, _) = Owners(world);
        allocations.Answer = order => new(Company, order, [new(line, 1, 0.5m)]);
        var release = new FakeFutureAllocationRelease();
        world.FutureAllocationRelease = release;

        var preview = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);
        Assert.False(preview.Cancellable);
        Assert.Equal((int?)0, preview.Lines[0].FutureSupplyAllocations);

        var blocked = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "neden", preview.ImpactToken), Metadata("k-cancel-physical")));
        Assert.Equal((409, SalesDraftProblemCodesV1.CancellationBlocked), Status(blocked));
        Assert.Empty(release.Requests);
        Assert.Equal(0, world.Store.CancellationTransactions);
    }

    [Fact]
    public async Task With_the_release_participant_an_unreadable_future_owner_still_refuses_the_cancellation_503_unrecorded()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        Owners(world);
        world.NoFutureAllocations = true;
        var release = new FakeFutureAllocationRelease();
        world.FutureAllocationRelease = release;

        var preview = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);
        Assert.False(preview.Cancellable);
        Assert.Null(preview.Lines[0].FutureSupplyAllocations);

        var unavailable = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "neden", preview.ImpactToken), Metadata("k-cancel-unreadable")));
        Assert.Equal((503, SalesDraftProblemCodesV1.CancellationImpactUnavailable), Status(unavailable));
        Assert.Empty(release.Requests);
        Assert.Empty(world.Store.Refusals);
    }

    [Fact]
    public async Task With_the_release_participant_the_header_revision_still_refuses_an_order_holding_a_future_allocation()
    {
        var world = new World();
        Owners(world);
        var (approved, line) = await AcceptedApprovedAsync(world);
        world.FutureAllocations = new FakeFutureAllocations(order => new(Company, order,
            [new SalesOrderLineAllocationV2(line, SalesOrderAllocationKindsV2.FutureSupply, 1, 1m, FuturePurchaseUnit)]));
        var release = new FakeFutureAllocationRelease();
        world.FutureAllocationRelease = release;

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseHeaderAsync(approved.Id,
            HeaderRevision("5", account: NewAccount), Metadata("k-future-release-revision")));

        Assert.Equal((409, SalesDraftProblemCodesV1.RevisionBlocked), Status(refused));
        Assert.Empty(release.Requests);
    }

    /// <summary>A SYNTHETIC host participant: records each release asked and answers the case's refusal, if any.</summary>
    private sealed class FakeFutureAllocationRelease : ISalesOrderFutureAllocationReleaseParticipantV1
    {
        public SalesFutureAllocationReleaseException? Refusal { get; set; }

        public List<(IDbTransaction Transaction, SalesOrderFutureAllocationReleaseRequestV1 Request)> Requests { get; } = [];

        public Task ReleaseForCancelledOrderAsync(IDbTransaction transaction, SalesOrderFutureAllocationReleaseRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add((transaction, request));
            return Refusal is null ? Task.CompletedTask : Task.FromException(Refusal);
        }
    }

    /// <summary>The in-memory store's stand-in for its SQL transaction; it is never committed or used for a statement.</summary>
    private sealed class FakeTransaction : IDbTransaction
    {
        public static readonly FakeTransaction Instance = new();

        private FakeTransaction()
        {
        }

        public IDbConnection? Connection => null;

        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        public void Commit() => throw new NotSupportedException();

        public void Rollback() => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
