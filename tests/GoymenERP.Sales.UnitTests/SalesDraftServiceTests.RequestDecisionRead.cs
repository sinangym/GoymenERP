using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Requests;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Theory]
    [InlineData("Submitted", true, true)]
    [InlineData("Approved", false, true)]
    [InlineData("Rejected", false, false)]
    public async Task Selected_request_human_read_reuses_all_native_price_free_facts_and_native_action_states(string state, bool approve, bool reject)
    {
        var world = new World(); var native = world.Service();
        var created = await native.CreateRequestAsync(RequestOf(lines: 2), Metadata("purpose-create")); var id = created.Detail.Request.Id;
        if (state == "Approved") await native.ApproveRequestAsync(id, Decision("1"), Metadata("purpose-approve"));
        if (state == "Rejected") await native.RejectRequestAsync(id, Decision("1", "  SENTETIK ret  "), Metadata("purpose-reject"));
        var expected = await native.GetRequestDetailAsync(id);
        world.Authority.Measured.Clear(); world.Authority.Denied.Add(SalesPermissionManifestV1.RequestsRead); world.Authority.Denied.Add(PriceReadPermission);
        var reader = new PurposeRequestStore(world.Requests!); var callbacks = 0;
        var answer = await PurposeService(world, reader).ReadRequestDecisionAsync(id, _ => { callbacks++; return Task.CompletedTask; });
        Assert.Equal(SalesRequestDecisionReadStateV1.Found, answer.State); var detail = Assert.IsType<SalesRequestDecisionDetailV1>(answer.Detail);
        Assert.Equal((state, approve, reject), (detail.Detail.Request.State, detail.ApprovalSupported, detail.RejectionSupported));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(expected, SalesDraftJson.Options), System.Text.Json.JsonSerializer.Serialize(detail.Detail, SalesDraftJson.Options));
        Assert.Equal(SalesRequestDecisionDigestsV1.SourceFactsDigest(detail.Detail), detail.SourceFactsDigest);
        Assert.Equal(SalesRequestDecisionDigestsV1.PreviewDigest(detail), detail.PreviewDigest);
        Assert.Equal(4, callbacks); Assert.Equal(1, reader.Reads);
        Assert.All(world.Authority.Measured, permission => Assert.Equal(SalesPermissionManifestV1.RequestsApprove, permission));
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Selected_request_consumer_normal_completion_cannot_release_facts_after_permission_loss_or_cancellation(int boundary)
    {
        foreach (var cancel in new[] { false, true })
        {
            var world = new World(); var id = (await world.Service().CreateRequestAsync(RequestOf(), Metadata("purpose-fence"))).Detail.Request.Id;
            using var cancellation = new CancellationTokenSource(); var callbacks = 0;
            Task Fence(CancellationToken _)
            {
                if (++callbacks == boundary) { if (cancel) cancellation.Cancel(); else world.Authority.Granted = false; }
                return Task.CompletedTask;
            }
            var service = PurposeService(world, new PurposeRequestStore(world.Requests!));
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadRequestDecisionAsync(id, Fence, cancellation.Token));
            else
            {
                var result = await service.ReadRequestDecisionAsync(id, Fence);
                Assert.Equal(SalesRequestDecisionReadStateV1.Denied, result.State); Assert.Null(result.Detail);
            }
            Assert.Equal(boundary, callbacks);
        }
    }

    [Theory]
    [InlineData("unconfigured")] [InlineData("missing")] [InlineData("wrong-id")]
    [InlineData("unauthenticated")] [InlineData("permission")]
    [InlineData("membership")] [InlineData("scoped")]
    public async Task Selected_request_read_fails_closed_without_falling_back_to_general_read(string fault)
    {
        var world = new World(); var id = (await world.Service().CreateRequestAsync(RequestOf(), Metadata("purpose-negative"))).Detail.Request.Id;
        var reader = new PurposeRequestStore(world.Requests!);
        if (fault == "unauthenticated") world.User.Authenticated = false;
        if (fault == "permission") world.Authority.Granted = false;
        if (fault == "membership") world.User.Company = Guid.NewGuid();
        if (fault == "scoped") world.Authority.CompanyWide = false;
        var readId = fault is "missing" or "wrong-id" ? Guid.NewGuid() : id;
        if (fault == "wrong-id") reader.Override = await world.Requests!.FindRequestAsync(id);
        var service = PurposeService(world, fault == "unconfigured" ? world.Requests! : reader);
        var result = await service.ReadRequestDecisionAsync(readId, _ => Task.CompletedTask);
        var expected = fault == "missing" ? SalesRequestDecisionReadStateV1.NotFound
            : fault is "unconfigured" or "wrong-id" ? SalesRequestDecisionReadStateV1.Unavailable : SalesRequestDecisionReadStateV1.Denied;
        Assert.Equal(expected, result.State); Assert.Null(result.Detail);
        if (fault is "unauthenticated" or "permission" or "membership" or "scoped" or "unconfigured") Assert.Equal(0, reader.Reads);
    }

    [Theory]
    [InlineData("actor")] [InlineData("membership")]
    public async Task Selected_request_final_consumer_cannot_switch_the_captured_actor_or_company_membership(string change)
    {
        var world = new World(); var id = (await world.Service().CreateRequestAsync(RequestOf(), Metadata("purpose-identity"))).Detail.Request.Id;
        var callbacks = 0;
        var result = await PurposeService(world, new PurposeRequestStore(world.Requests!)).ReadRequestDecisionAsync(id, _ =>
        {
            if (++callbacks == 4) { if (change == "actor") world.User.Id = "other"; else world.User.Company = Guid.NewGuid(); }
            return Task.CompletedTask;
        });
        Assert.Equal(SalesRequestDecisionReadStateV1.Denied, result.State); Assert.Null(result.Detail); Assert.Equal(4, callbacks);
    }

    [Fact]
    public async Task Selected_request_initial_cancellation_never_calls_the_consumer_or_owner()
    {
        var world = new World(); var reader = new PurposeRequestStore(world.Requests!); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var callbacks = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PurposeService(world, reader).ReadRequestDecisionAsync(Guid.NewGuid(),
            _ => { callbacks++; return Task.CompletedTask; }, cancellation.Token));
        Assert.Equal(0, callbacks); Assert.Equal(0, reader.Reads); Assert.Equal(0, world.Authority.Calls);
    }

    private static SalesDraftService PurposeService(World world, ISalesRequestStore reader)
    {
        var company = new CompanyContext(); company.Set(Company);
        return new(company, world.User, world.Authority, world.ActiveRoleCodes, new FixedCorrelation(), world.Accounts, world.Settings,
            world.Products, world.Store, world.Bindings, world.Clock, requests: reader);
    }

    // Explicit synthetic selected-read protocol. Transaction/SQL acceptance is in the real relational fixture, not this adapter.
    private sealed class PurposeRequestStore(FakeRequestStore inner) : ISalesRequestStore, ISalesRequestDecisionReadStoreV1
    {
        internal int Reads; internal SalesRequest? Override;
        public async Task<SalesRequest?> ReadRequestDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken ct = default)
        { await fence(ct); Reads++; var value = Override ?? await inner.FindRequestAsync(id, ct); await fence(ct); return value; }
        public Task<SalesRequestStoreResult> StoreRequestAsync(SalesDraftWriteEnvelope e, string h, Func<string, SalesRequest> build, CancellationToken ct = default) => inner.StoreRequestAsync(e, h, build, ct);
        public Task<SalesRequestReplay?> FindRequestReplayAsync(SalesDraftWriteEnvelope e, string h, string command, CancellationToken ct = default) => inner.FindRequestReplayAsync(e, h, command, ct);
        public Task RecordRequestRefusalAsync(SalesDraftWriteEnvelope e, string h, string command, Guid? id, SalesDraftTerminalRefusal refusal, CancellationToken ct = default) => inner.RecordRequestRefusalAsync(e, h, command, id, refusal, ct);
        public Task<SalesRequestStoreResult?> DecideRequestAsync(SalesDraftWriteEnvelope e, string h, string command, Guid id, Action<SalesRequest, DateTimeOffset> mutate, SalesDraftStateAudit audit, CancellationToken ct = default) => inner.DecideRequestAsync(e, h, command, id, mutate, audit, ct);
        public Task<SalesRequest?> FindRequestAsync(Guid id, CancellationToken ct = default) => inner.FindRequestAsync(id, ct);
        public Task<SalesRequestPage> ListRequestsAsync(int size, string? cursor, SalesRequestState? state, CancellationToken ct = default) => inner.ListRequestsAsync(size, cursor, state, ct);
    }
}
