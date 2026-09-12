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
        public Task<GroupSharedCommandTerminal?> ReadAsync(GroupSharedCommandKey key, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(terminal);
        }
    }

    private sealed class Authority : IGroupSharedMasterAuthorizationService
    {
        public static GroupSharedMasterAuthorizationSnapshot Snapshot(GroupSharedMasterOperation operation) => new(true,
            GroupSharedMasterKind.Product, operation, Guid.Parse("4e3c1a8b-0b58-441c-a2cc-a5768d0dfef0"),
            Guid.Parse("50e650f1-5546-438c-8d6c-3a867faf16f6"), "fixture-authority-1");
        public Task<GroupSharedMasterAuthorizationSnapshot> GetSnapshotAsync(GroupSharedMasterKind subject,
            GroupSharedMasterOperation operation, CancellationToken cancellationToken) => Task.FromResult(Snapshot(operation));
    }
}
