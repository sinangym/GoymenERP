using System.Security.Cryptography;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Product.UnitTests;

// Real gateway/service, controlled authority/terminal/runner. No SQL or grant claim.
public sealed class ProductServiceTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Origin = Guid.NewGuid();
    private static readonly Guid Group = Guid.NewGuid();
    private const string Key = "product-service-attempt";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_authority_never_enters_the_host_runner(bool edit)
    {
        await using var context = Context();
        using var cursors = Cursors();
        var authority = new UnconfiguredGroupSharedMasterAuthorizationService();
        var runner = new Runner();
        var service = Service(context, cursors, authority, new Reader(null), runner);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => edit
            ? service.EditDraftAsync(Guid.NewGuid(), new("1", new(), "Düzeltme"), Key, default)
            : service.CreateDraftAsync(new("Sac", Group), Key, default));
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task Fresh_create_hands_the_normalized_frozen_intent_to_the_host_once()
    {
        await using var context = Context();
        using var cursors = Cursors();
        var runner = new Runner();
        var service = Service(context, cursors, new Authority(), new Reader(null), runner);
        await service.CreateDraftAsync(new("  Sac  ", Group), Key, default);
        Assert.Equal(1, runner.Calls);
        Assert.Null(runner.Prepared!.Replay);
        using var request = JsonDocument.Parse(runner.Prepared.Intent.CanonicalRequestJson);
        Assert.Equal("Sac", request.RootElement.GetProperty("request").GetProperty("name").GetString());
        Assert.Equal(ProductCommandIntent.CreateDraft(Key, new("Sac", Group)).RequestHash, runner.Prepared.Intent.RequestHash);
    }

    [Fact]
    public async Task A_replay_still_reaches_the_host_with_its_original_correlation()
    {
        await using var context = Context();
        using var cursors = Cursors();
        var runner = new Runner();
        var intent = ProductCommandIntent.CreateDraft(Key, new("Sac", Group));
        var authority = GroupSharedMasterCommandContext.FromServerDecision(Snapshot(GroupSharedMasterOperation.DraftCreate),
            GroupSharedMasterOperation.DraftCreate);
        var terminal = GroupSharedCommandTerminal.Create(GroupSharedCommandBinding.Create(authority, intent.KeyHash, intent.RequestHash),
            201, JsonSerializer.Serialize(Response(), new JsonSerializerOptions(JsonSerializerDefaults.Web)), "original-correlation", 1);
        var service = Service(context, cursors, new Authority(), new Reader(terminal), runner);
        var replay = await service.CreateDraftAsync(new("Sac", Group), Key, default);
        Assert.Equal(1, runner.Calls);
        Assert.True(replay.IsReplay);
        Assert.Equal("original-correlation", runner.Prepared!.OriginalCorrelationId);
    }

    private static ProductService Service(ProductDbContext context, ProductCursorCodec cursors,
        IGroupSharedMasterAuthorizationService authority, Reader reader, Runner runner) => new(
        new(new(context), cursors, authority), new(authority, new GroupSharedCommandReplayResolver(authority, reader)), runner);
    private static ProductDbContext Context() => new(new DbContextOptionsBuilder<ProductDbContext>()
        .UseSqlServer("Server=127.0.0.1,1;Database=ProductModelOnly;Integrated Security=true;Encrypt=false").Options);
    private static ProductCursorCodec Cursors() => new(RandomNumberGenerator.GetBytes(32), TimeProvider.System, TimeSpan.FromMinutes(5));
    private static GroupSharedMasterAuthorizationSnapshot Snapshot(GroupSharedMasterOperation operation) =>
        new(true, GroupSharedMasterKind.Product, operation, Actor, Origin, "test-revision");
    private static ProductMutationResponse Response() => new(new(ProductDraftView.FromProduct(
        Modules.Product.Domain.Products.Product.CreateDraft(Guid.NewGuid(), ProductCode.FromSequence("TEST", 1), "Sac", Group,
            null, null, ProductRoles.NotEvaluated, DateTimeOffset.UtcNow)), new()), false);

    private sealed class Authority : IGroupSharedMasterAuthorizationService
    {
        public Task<GroupSharedMasterAuthorizationSnapshot> GetSnapshotAsync(GroupSharedMasterKind subject,
            GroupSharedMasterOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(Snapshot(operation));
    }
    private sealed class Reader(GroupSharedCommandTerminal? terminal) : IGroupSharedCommandTerminalReader
    {
        public Task<GroupSharedCommandTerminal?> ReadAsync(GroupSharedCommandKey key, CancellationToken cancellationToken = default) => Task.FromResult(terminal);
    }
    private sealed class Runner : IProductDraftCommandRunner
    {
        public int Calls { get; private set; }
        public ProductPreparedCommand? Prepared { get; private set; }
        public Task<ProductMutationResponse> RunAsync(ProductPreparedCommand prepared, CancellationToken cancellationToken)
        {
            Calls++;
            Prepared = prepared;
            return Task.FromResult(prepared.Replay ?? Response());
        }
    }
}
