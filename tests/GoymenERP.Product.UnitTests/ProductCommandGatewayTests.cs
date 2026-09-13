using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using ProductEntity = GoymenERP.Modules.Product.Domain.Products.Product;

namespace GoymenERP.Product.UnitTests;

// Controlled consumer tests with the real shared resolver. This authority is a
// test double, never an accepted bootstrap/global eligibility provider or SQL proof.
public sealed class ProductCommandGatewayTests
{
    private static readonly Guid Group = Guid.Parse("b69228cd-c116-4b3e-b5ef-9e9c5b519c5c");
    private const string Key = "product-gateway-attempt";
    private static readonly CreateProductDraftRequest Request = new("Sac", Group);

    [Fact]
    public async Task Unconfigured_authority_rejects_before_the_terminal_reader()
    {
        var reader = new Reader(null);
        var authority = new UnconfiguredGroupSharedMasterAuthorizationService();
        var gateway = new ProductCommandGateway(authority, new GroupSharedCommandReplayResolver(authority, reader));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => gateway.PrepareCreateAsync(Key, Request, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => gateway.AuthorizeReadAsync(default));
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task Authorized_missing_prepares_input_without_claiming_a_write_or_replay()
    {
        var gateway = Gateway(null);
        var prepared = await gateway.PrepareCreateAsync(Key, Request, default);
        Assert.Null(prepared.Replay);
        Assert.Null(prepared.OriginalCorrelationId);
        Assert.Equal(GroupSharedMasterOperation.DraftCreate, prepared.Context.Operation);
        Assert.Equal(ProductCommandIntent.CreateDraft(Key, Request).RequestHash, prepared.Intent.RequestHash);
        var read = await gateway.AuthorizeReadAsync(default);
        Assert.Equal(GroupSharedMasterOperation.Read, read.Operation);
    }

    [Fact]
    public async Task Matching_replay_preserves_original_card_and_correlation()
    {
        var response = Response();
        var prepared = await Gateway(Terminal(response)).PrepareCreateAsync(Key, Request, default);
        Assert.True(prepared.Replay!.IsReplay);
        Assert.Equal(response.Card.Product.Id, prepared.Replay.Card.Product.Id);
        Assert.Equal(response.Card.Product.Version, prepared.Replay.Card.Product.Version);
        Assert.Equal("original-correlation", prepared.OriginalCorrelationId);
    }

    [Fact]
    public async Task Changed_payload_is_conflict_and_never_exposes_stored_card()
    {
        var gateway = Gateway(Terminal(Response()));
        var exception = await Assert.ThrowsAsync<ProductOperationException>(() =>
            gateway.PrepareCreateAsync(Key, Request with { Name = "Başka" }, default));
        Assert.Equal(ProductApiV1.IdempotencyConflict, exception.Code);
        Assert.Equal(409, exception.StatusCode);
        Assert.DoesNotContain("original-correlation", exception.ToString());
    }

    [Theory]
    [InlineData(200)]
    [InlineData(409)]
    [InlineData(503)]
    public async Task A_terminal_for_the_wrong_status_cannot_be_painted_as_create_success(int status)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gateway(Terminal(Response(), status)).PrepareCreateAsync(Key, Request, default));
    }

    [Fact]
    public async Task Incomplete_stored_success_json_is_rejected_before_returning_a_card()
    {
        var terminal = Terminal(Response());
        terminal = GroupSharedCommandTerminal.Create(terminal.Binding, 201, "{\"card\":{}}", "original-correlation", 1);
        await Assert.ThrowsAsync<JsonException>(() => Gateway(terminal).PrepareCreateAsync(Key, Request, default));
    }

    [Fact]
    public async Task An_edit_replay_must_describe_the_requested_product()
    {
        var response = Response();
        var target = Guid.NewGuid();
        var edit = new EditProductDraftRequest("1", new() { Name = new("Düzeltilen") }, "Düzeltme");
        var intent = ProductCommandIntent.EditDraft(Key, target, edit);
        var context = GroupSharedMasterCommandContext.FromServerDecision(Authority.Snapshot(GroupSharedMasterOperation.DraftBasicEdit),
            GroupSharedMasterOperation.DraftBasicEdit);
        var binding = GroupSharedCommandBinding.Create(context, intent.KeyHash, intent.RequestHash);
        var terminal = GroupSharedCommandTerminal.Create(binding, 200,
            JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)), "original-correlation", 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Gateway(terminal).PrepareEditAsync(Key, target, edit, default));
    }

    [Fact]
    public async Task Refresh_observes_committed_winner_without_rebuilding_the_frozen_intent()
    {
        var reader = new Reader(null);
        var authority = new Authority();
        var gateway = new ProductCommandGateway(authority, new GroupSharedCommandReplayResolver(authority, reader));
        var prepared = await gateway.PrepareCreateAsync(Key, Request, default);
        reader.Terminal = Terminal(Response());
        authority.Observe = operation => Authority.Snapshot(operation) with { AuthorityRevision = "fixture-authority-2" };
        var refreshed = await gateway.RefreshAsync(prepared, default);
        Assert.Same(prepared.Intent, refreshed.Intent);
        Assert.Equal(prepared.Intent.KeyHash, refreshed.Intent.KeyHash);
        Assert.Equal(prepared.Intent.RequestHash, refreshed.Intent.RequestHash);
        Assert.True(refreshed.Replay!.IsReplay);
        Assert.Equal("original-correlation", refreshed.OriginalCorrelationId);
        Assert.Equal("fixture-authority-2", refreshed.Context.AuthorityRevision);
        Assert.Equal(2, reader.Calls);
    }

    [Fact]
    public async Task Refresh_cannot_reuse_a_previously_prepared_replay_or_correlation()
    {
        var reader = new Reader(Terminal(Response()));
        var authority = new Authority();
        var gateway = new ProductCommandGateway(authority, new GroupSharedCommandReplayResolver(authority, reader));
        var prepared = await gateway.PrepareCreateAsync(Key, Request, default);
        reader.Terminal = null;
        var refreshed = await gateway.RefreshAsync(prepared, default);
        Assert.Null(refreshed.Replay);
        Assert.Null(refreshed.OriginalCorrelationId);
        Assert.Same(prepared.Intent, refreshed.Intent);
        Assert.Equal(2, reader.Calls);
    }

    [Fact]
    public async Task Revocation_on_refresh_denies_before_reopening_the_terminal()
    {
        var reader = new Reader(Terminal(Response()));
        var authority = new Authority();
        var gateway = new ProductCommandGateway(authority, new GroupSharedCommandReplayResolver(authority, reader));
        var prepared = await gateway.PrepareCreateAsync(Key, Request, default);
        authority.Observe = operation => GroupSharedMasterAuthorizationSnapshot.Denied(GroupSharedMasterKind.Product, operation);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => gateway.RefreshAsync(prepared, default));
        Assert.Equal(1, reader.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Refresh_cannot_move_a_missing_attempt_to_a_different_actor_or_origin(bool changeActor)
    {
        var authority = new Authority();
        var gateway = new ProductCommandGateway(authority, new GroupSharedCommandReplayResolver(authority, new Reader(null)));
        var prepared = await gateway.PrepareCreateAsync(Key, Request, default);
        authority.Observe = operation => changeActor
            ? Authority.Snapshot(operation) with { LocalActorId = Guid.NewGuid() }
            : Authority.Snapshot(operation) with { OriginCompanyId = Guid.NewGuid() };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => gateway.RefreshAsync(prepared, default));
    }

    [Fact]
    public async Task Refresh_rejects_a_context_for_another_frozen_operation_before_terminal_access()
    {
        var reader = new Reader(null);
        var authority = new Authority();
        var gateway = new ProductCommandGateway(authority, new GroupSharedCommandReplayResolver(authority, reader));
        var prepared = await gateway.PrepareCreateAsync(Key, Request, default);
        prepared = prepared with { Context = GroupSharedMasterCommandContext.FromServerDecision(
            Authority.Snapshot(GroupSharedMasterOperation.DraftBasicEdit), GroupSharedMasterOperation.DraftBasicEdit) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.RefreshAsync(prepared, default));
        Assert.Equal(1, reader.Calls);
    }

    [Fact]
    public async Task Refresh_validates_the_edit_winner_against_the_original_frozen_target()
    {
        var reader = new Reader(null);
        var authority = new Authority();
        var gateway = new ProductCommandGateway(authority, new GroupSharedCommandReplayResolver(authority, reader));
        var target = Guid.NewGuid();
        var request = new EditProductDraftRequest("1", new() { Name = new("Düzeltilen") }, "Düzeltme");
        var prepared = await gateway.PrepareEditAsync(Key, target, request, default);
        var binding = GroupSharedCommandBinding.Create(prepared.Context, prepared.Intent.KeyHash, prepared.Intent.RequestHash);
        reader.Terminal = GroupSharedCommandTerminal.Create(binding, 200,
            JsonSerializer.Serialize(Response(), new JsonSerializerOptions(JsonSerializerDefaults.Web)), "other-card", 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.RefreshAsync(prepared, default));
        Assert.Equal(2, reader.Calls);
    }

    [Fact]
    public async Task Canceled_refresh_never_opens_the_terminal_again()
    {
        var reader = new Reader(null);
        var authority = new Authority();
        var gateway = new ProductCommandGateway(authority, new GroupSharedCommandReplayResolver(authority, reader));
        var prepared = await gateway.PrepareCreateAsync(Key, Request, default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gateway.RefreshAsync(prepared, new CancellationToken(true)));
        Assert.Equal(1, reader.Calls);
    }

    private static ProductCommandGateway Gateway(GroupSharedCommandTerminal? terminal)
    {
        var authority = new Authority();
        return new(authority, new GroupSharedCommandReplayResolver(authority, new Reader(terminal)));
    }

    private static GroupSharedCommandTerminal Terminal(ProductMutationResponse response, int status = 201)
    {
        var intent = ProductCommandIntent.CreateDraft(Key, Request);
        var context = GroupSharedMasterCommandContext.FromServerDecision(Authority.Snapshot(GroupSharedMasterOperation.DraftCreate),
            GroupSharedMasterOperation.DraftCreate);
        return GroupSharedCommandTerminal.Create(GroupSharedCommandBinding.Create(context, intent.KeyHash, intent.RequestHash),
            status, JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)), "original-correlation", 1);
    }

    private static ProductMutationResponse Response()
    {
        var product = ProductEntity.CreateDraft(Guid.NewGuid(), ProductCode.FromSequence("TEST", 1), "Sac", Group,
            null, null, ProductRoles.NotEvaluated, DateTimeOffset.Parse("2026-09-12T16:00:00Z"));
        return new(new(ProductDraftView.FromProduct(product), new()), false);
    }

    private sealed class Reader(GroupSharedCommandTerminal? terminal) : IGroupSharedCommandTerminalReader
    {
        public int Calls { get; private set; }
        public GroupSharedCommandTerminal? Terminal { get; set; } = terminal;
        public Task<GroupSharedCommandTerminal?> ReadAsync(GroupSharedCommandKey key, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Terminal);
        }
    }

    private sealed class Authority : IGroupSharedMasterAuthorizationService
    {
        public Func<GroupSharedMasterOperation, GroupSharedMasterAuthorizationSnapshot> Observe { get; set; } = Snapshot;
        public static GroupSharedMasterAuthorizationSnapshot Snapshot(GroupSharedMasterOperation operation) => new(true,
            GroupSharedMasterKind.Product, operation, Guid.Parse("4e3c1a8b-0b58-441c-a2cc-a5768d0dfef0"),
            Guid.Parse("50e650f1-5546-438c-8d6c-3a867faf16f6"), "fixture-authority-1");
        public Task<GroupSharedMasterAuthorizationSnapshot> GetSnapshotAsync(GroupSharedMasterKind subject,
            GroupSharedMasterOperation operation, CancellationToken cancellationToken) => Task.FromResult(Observe(operation));
    }
}
