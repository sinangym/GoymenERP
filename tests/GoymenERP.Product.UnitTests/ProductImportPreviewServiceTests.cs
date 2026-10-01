using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductImportPreviewServiceTests
{
    private static readonly Guid Actor = Guid.Parse("20000000-0000-0000-0000-000000000003");
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Group = Guid.Parse("82000000-0000-0000-0000-000000000001");
    private static readonly ProductImportColumnMappingV1 Mapping = new("Eski Kod", "Ürün Adı", "Grup Kimliği");

    [Fact]
    public async Task Stable_draft_create_authority_returns_an_actor_company_and_revision_bound_preview()
    {
        var authority = new Authority();
        var catalog = new Catalog();
        var workbook = new WorkbookReader();
        var service = new ProductImportPreviewService(authority, catalog, workbook);

        var result = await service.PreviewAsync(new byte[] { 1, 2, 3 }, Mapping, default);

        Assert.Equal(ProductImportPreviewTokenV1.Bind(WorkbookReader.Raw, Actor, Company, "revision-1"),
            result.PreviewToken);
        Assert.NotEqual(WorkbookReader.Raw.PreviewToken, result.PreviewToken);
        Assert.Equal(2, authority.Calls);
        Assert.Equal(1, catalog.Calls);
        Assert.Equal(1, workbook.Calls);
        Assert.Same(catalog.Result.RegisteredCodes, workbook.ExistingCodes);
        Assert.Same(catalog.Result.CodeGroupIds, workbook.ExistingGroups);
    }

    [Fact]
    public async Task Missing_draft_create_authority_refuses_before_catalogue_or_workbook_read()
    {
        var authority = new Authority
        {
            Snapshot = GroupSharedMasterAuthorizationSnapshot.Denied(GroupSharedMasterKind.Product,
                GroupSharedMasterOperation.DraftCreate),
        };
        var catalog = new Catalog();
        var workbook = new WorkbookReader();
        var service = new ProductImportPreviewService(authority, catalog, workbook);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.PreviewAsync(new byte[] { 1 }, Mapping, default));

        Assert.Equal(1, authority.Calls);
        Assert.Equal(0, catalog.Calls);
        Assert.Equal(0, workbook.Calls);
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("actor")]
    [InlineData("company")]
    [InlineData("revision")]
    public async Task Authority_change_after_catalogue_read_hides_the_preview(string change)
    {
        var authority = new Authority();
        var catalog = new Catalog(() => authority.Snapshot = change switch
        {
            "revoked" => authority.Snapshot with { IsGranted = false },
            "actor" => authority.Snapshot with { LocalActorId = Guid.NewGuid() },
            "company" => authority.Snapshot with { OriginCompanyId = Guid.NewGuid() },
            _ => authority.Snapshot with { AuthorityRevision = "revision-2" },
        });
        var workbook = new WorkbookReader();
        var service = new ProductImportPreviewService(authority, catalog, workbook);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.PreviewAsync(new byte[] { 1 }, Mapping, default));

        Assert.Equal(2, authority.Calls);
        Assert.Equal(1, catalog.Calls);
        Assert.Equal(1, workbook.Calls);
    }

    [Fact]
    public async Task Stable_authority_preserves_catalogue_failure_only_after_final_reauthorization()
    {
        var authority = new Authority();
        var catalog = new Catalog(error: new InvalidOperationException("catalogue unavailable"));
        var workbook = new WorkbookReader();
        var service = new ProductImportPreviewService(authority, catalog, workbook);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PreviewAsync(new byte[] { 1 }, Mapping, default));

        Assert.Equal("catalogue unavailable", error.Message);
        Assert.Equal(2, authority.Calls);
        Assert.Equal(1, catalog.Calls);
        Assert.Equal(0, workbook.Calls);
    }

    [Fact]
    public async Task Revoked_authority_hides_catalogue_failure()
    {
        var authority = new Authority();
        var catalog = new Catalog(
            () => authority.Snapshot = authority.Snapshot with { IsGranted = false },
            new InvalidOperationException("must not escape"));
        var workbook = new WorkbookReader();
        var service = new ProductImportPreviewService(authority, catalog, workbook);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.PreviewAsync(new byte[] { 1 }, Mapping, default));

        Assert.Equal(2, authority.Calls);
        Assert.Equal(1, catalog.Calls);
        Assert.Equal(0, workbook.Calls);
    }

    private sealed class Authority : IGroupSharedMasterAuthorizationService
    {
        public int Calls { get; private set; }
        public GroupSharedMasterAuthorizationSnapshot Snapshot { get; set; } = new(true,
            GroupSharedMasterKind.Product, GroupSharedMasterOperation.DraftCreate, Actor, Company, "revision-1");

        public Task<GroupSharedMasterAuthorizationSnapshot> GetSnapshotAsync(GroupSharedMasterKind subject,
            GroupSharedMasterOperation operation, CancellationToken cancellationToken = default)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Snapshot);
        }
    }

    private sealed class Catalog(Action? afterRead = null, Exception? error = null) : IProductImportCatalogReader
    {
        public int Calls { get; private set; }
        public ProductImportCatalogSnapshot Result { get; } = new(
            new HashSet<string>(["OLD-EXISTING"], StringComparer.Ordinal),
            new HashSet<Guid>([Group]));

        public Task<ProductImportCatalogSnapshot> ReadAsync(GroupSharedMasterCommandContext authority,
            CancellationToken cancellationToken)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            afterRead?.Invoke();
            return error is null ? Task.FromResult(Result) : Task.FromException<ProductImportCatalogSnapshot>(error);
        }
    }

    private sealed class WorkbookReader : IProductImportWorkbookReaderV1
    {
        public static ProductImportPreviewV1 Raw { get; } = new("FILE-HASH", "CONTENT-TOKEN", 1,
            [new(2, "OLD-001", "Galvaniz Rulo", Group)], []);

        public int Calls { get; private set; }
        public IReadOnlySet<string>? ExistingCodes { get; private set; }
        public IReadOnlySet<Guid>? ExistingGroups { get; private set; }

        public ProductImportPreviewV1 Read(ReadOnlyMemory<byte> workbook, ProductImportColumnMappingV1 mapping,
            IReadOnlySet<string> existingCodes, IReadOnlySet<Guid> existingGroupIds)
        {
            Calls++;
            ExistingCodes = existingCodes;
            ExistingGroups = existingGroupIds;
            return Raw;
        }
    }
}
