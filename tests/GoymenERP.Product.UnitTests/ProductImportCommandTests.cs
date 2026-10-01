using System.Text.Json;
using System.Security.Cryptography;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductImportCommandTests
{
    private static readonly Guid Actor = Guid.Parse("20000000-0000-0000-0000-000000000003");
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Group = Guid.Parse("82000000-0000-0000-0000-000000000001");
    private const string HashA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string HashB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    [Fact]
    public void Intent_normalizes_mapping_and_hashes_the_exact_apply_identity()
    {
        var first = ProductImportCommandIntent.Create("import-key",
            Binding(HashA, HashB, new(" Eski Kod ", " Ürün Adı ", " Grup Kimliği ")));
        var replay = ProductImportCommandIntent.Create("import-key", Binding(HashA, HashB, Mapping()));
        var changed = ProductImportCommandIntent.Create("import-key", Binding(HashA, HashA, Mapping()));

        Assert.Equal(first.KeyHash, replay.KeyHash);
        Assert.Equal(first.RequestHash, replay.RequestHash);
        Assert.NotEqual(first.RequestHash, changed.RequestHash);
        Assert.Equal(Mapping(), first.Binding.Mapping);
        Assert.Equal(nameof(ProductImportCommandIntent), first.ToString());
    }

    [Theory]
    [InlineData("bad-hash", HashB, "fileSha256")]
    [InlineData(HashA, "lowercaseaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "previewToken")]
    public void Intent_refuses_noncanonical_hashes(string fileHash, string token, string field)
    {
        var error = Assert.Throws<ProductValidationException>(() =>
            ProductImportCommandIntent.Create("import-key", Binding(fileHash, token, Mapping())));
        Assert.Equal(field, Assert.Single(error.Errors).Field);
        Assert.Equal(ProductImportCodesV1.InvalidMetadata, error.Errors[0].Code);
    }

    [Fact]
    public async Task Service_freezes_workbook_bytes_before_awaited_replay_resolution()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var authority = new Authority();
        var resolver = new GroupSharedCommandReplayResolver(authority, new TerminalReader());
        var runner = new Runner(() => bytes[0] = 9);
        var preview = new Preview();
        var service = new ProductImportService(preview, new ProductImportCommandGateway(resolver), runner);

        var fileHash = Convert.ToHexString(SHA256.HashData(bytes));
        var result = await service.ApplyAsync(bytes, Binding(fileHash, HashB, Mapping()), "import-key", default);

        Assert.Equal(1, runner.ObservedFirstByte);
        Assert.False(result.IsReplay);
        Assert.Equal(2, authority.Calls);
    }

    [Fact]
    public async Task Service_refuses_bytes_that_do_not_match_the_authorized_binding_even_for_replay()
    {
        var authority = new Authority();
        var runner = new Runner(() => { });
        var service = new ProductImportService(new Preview(),
            new ProductImportCommandGateway(new GroupSharedCommandReplayResolver(authority, new TerminalReader())), runner);

        var error = await Assert.ThrowsAsync<ProductValidationException>(() => service.ApplyAsync(
            new byte[] { 1, 2, 3 }, Binding(HashA, HashB, Mapping()), "import-key", default));

        Assert.Equal(ProductImportCodesV1.PreviewMismatch, Assert.Single(error.Errors).Code);
        Assert.Equal(0, runner.Calls);
        Assert.Equal(2, authority.Calls);
    }

    [Fact]
    public async Task Gateway_returns_only_a_valid_original_terminal_as_replay()
    {
        var intent = ProductImportCommandIntent.Create("import-key", Binding(HashA, HashB, Mapping()));
        var context = Context();
        var original = Result(isReplay: false, "original-correlation");
        var terminal = GroupSharedCommandTerminal.Create(
            GroupSharedCommandBinding.Create(context, intent.KeyHash, intent.RequestHash), 201,
            JsonSerializer.Serialize(original, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            "original-correlation", 1);
        var resolver = new GroupSharedCommandReplayResolver(new Authority(), new TerminalReader(terminal));

        var prepared = await new ProductImportCommandGateway(resolver)
            .PrepareAsync("import-key", Binding(HashA, HashB, Mapping()), default);

        Assert.NotNull(prepared.Replay);
        Assert.True(prepared.Replay!.IsReplay);
        Assert.Equal("original-correlation", prepared.Replay.OriginalCorrelationId);
        Assert.Single(prepared.Replay.Products);
    }

    private static ProductImportApplyBindingV1 Binding(string fileHash, string token,
        ProductImportColumnMappingV1 mapping) => new(fileHash, token, mapping);

    private static ProductImportColumnMappingV1 Mapping() => new("Eski Kod", "Ürün Adı", "Grup Kimliği");

    private static ProductImportResultV1 Result(bool isReplay, string correlation) => new(
        [new(Guid.Parse("30000000-0000-0000-0000-000000000001"), "PSQL-1", "OLD-1", "Galvaniz Rulo", Group)],
        isReplay, correlation);

    private static GroupSharedMasterCommandContext Context() => GroupSharedMasterCommandContext.FromServerDecision(
        new(true, GroupSharedMasterKind.Product, GroupSharedMasterOperation.DraftCreate,
            Actor, Company, "revision-1"), GroupSharedMasterOperation.DraftCreate);

    private sealed class Authority : IGroupSharedMasterAuthorizationService
    {
        public int Calls { get; private set; }

        public Task<GroupSharedMasterAuthorizationSnapshot> GetSnapshotAsync(GroupSharedMasterKind subject,
            GroupSharedMasterOperation operation, CancellationToken cancellationToken = default)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new GroupSharedMasterAuthorizationSnapshot(true, subject, operation,
                Actor, Company, "revision-1"));
        }
    }

    private sealed class TerminalReader(GroupSharedCommandTerminal? terminal = null) : IGroupSharedCommandTerminalReader
    {
        public Task<GroupSharedCommandTerminal?> ReadAsync(GroupSharedCommandKey key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(terminal);
        }
    }

    private sealed class Runner(Action beforeRead) : IProductImportCommandRunner
    {
        public int Calls { get; private set; }
        public byte ObservedFirstByte { get; private set; }

        public Task<ProductImportResultV1> RunAsync(ProductImportPreparedCommand prepared,
            ReadOnlyMemory<byte> workbook, CancellationToken cancellationToken)
        {
            Calls++;
            beforeRead();
            ObservedFirstByte = workbook.Span[0];
            return Task.FromResult(Result(isReplay: false, "new-correlation"));
        }
    }

    private sealed class Preview : IProductImportPreviewServiceV1
    {
        public Task<ProductImportPreviewV1> PreviewAsync(ReadOnlyMemory<byte> workbook,
            ProductImportColumnMappingV1 mapping, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
