using GoymenERP.Api.DocumentsRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.IntegrationTests;

public sealed class InventoryDocumentOwnerLinkPortTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Label = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    [Fact]
    public async Task Exact_company_label_with_stable_actor_is_readable()
    {
        var (port, _, _) = Port((id, _) => Task.FromResult(Detail(id, Company)));

        Assert.Equal(DocumentOwnerLinkKindsV1.InventoryMaterialLabel, port.Kind);
        Assert.Equal(DocumentOwnerAccessV1.Readable, await port.CheckReadableAsync(Label));
    }

    [Theory]
    [InlineData(404, InventoryLabelDetailCodesV1.NotFound, DocumentOwnerAccessV1.NotFound)]
    [InlineData(401, InventoryLabelDetailCodesV1.Unauthenticated, DocumentOwnerAccessV1.Denied)]
    [InlineData(403, InventoryLabelDetailCodesV1.Forbidden, DocumentOwnerAccessV1.Denied)]
    [InlineData(409, InventoryLabelDetailCodesV1.ReadConflict, DocumentOwnerAccessV1.Conflict)]
    [InlineData(503, InventoryLabelDetailCodesV1.Unavailable, DocumentOwnerAccessV1.Unavailable)]
    [InlineData(409, "unknown_inventory_conflict", DocumentOwnerAccessV1.Unavailable)]
    public async Task Exact_inventory_refusals_are_mapped_fail_closed(int status, string code, DocumentOwnerAccessV1 expected)
    {
        var (port, _, _) = Port((_, _) => Task.FromException<InventoryLabelDetailV1>(new InventoryLabelDetailException(status, code)));

        Assert.Equal(expected, await port.CheckReadableAsync(Label));
    }

    [Fact]
    public async Task Malformed_identity_and_unknown_provider_fault_are_unavailable()
    {
        var (wrongId, _, _) = Port((_, _) => Task.FromResult(Detail(Guid.NewGuid(), Company)));
        Assert.Equal(DocumentOwnerAccessV1.Unavailable, await wrongId.CheckReadableAsync(Label));

        var (wrongCompany, _, _) = Port((id, _) => Task.FromResult(Detail(id, Guid.NewGuid())));
        Assert.Equal(DocumentOwnerAccessV1.Unavailable, await wrongCompany.CheckReadableAsync(Label));

        var (fault, _, _) = Port((_, _) => Task.FromException<InventoryLabelDetailV1>(new InvalidOperationException("SENTETIK")));
        Assert.Equal(DocumentOwnerAccessV1.Unavailable, await fault.CheckReadableAsync(Label));
    }

    [Fact]
    public async Task Actor_drift_after_the_await_is_denied()
    {
        MutableUser? actor = null;
        var (port, user, _) = Port((id, _) =>
        {
            actor!.UserId = "SENTETIK-other";
            return Task.FromResult(Detail(id, Company));
        });
        actor = user;

        Assert.Equal(DocumentOwnerAccessV1.Denied, await port.CheckReadableAsync(Label));
    }

    [Fact]
    public async Task Cancellation_is_not_converted_to_unavailable()
    {
        var (port, _, _) = Port((_, token) => Task.FromCanceled<InventoryLabelDetailV1>(token));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => port.CheckReadableAsync(Label, cancellation.Token));
    }

    private static (InventoryDocumentOwnerLinkPort Port, MutableUser User, CompanyContext Company) Port(
        Func<Guid, CancellationToken, Task<InventoryLabelDetailV1>> read)
    {
        var company = new CompanyContext();
        company.Set(Company);
        var user = new MutableUser { UserId = "SENTETIK-actor" };
        user.AllowedCompanyIds.Add(Company);
        return (new InventoryDocumentOwnerLinkPort(company, user, new FixedDetails(read)), user, company);
    }

    private static InventoryLabelDetailV1 Detail(Guid labelId, Guid companyId) => new(
        "1",
        new InventoryLabelViewV1(labelId, companyId, "LBL-SENTETIK", Guid.NewGuid(), Guid.NewGuid(), null, null, null,
            "Complete", [], [], [], null!),
        null!,
        null!,
        null!);

    private sealed class FixedDetails(Func<Guid, CancellationToken, Task<InventoryLabelDetailV1>> read) : IInventoryLabelDetailsV1
    {
        public Task<InventoryLabelDetailV1> GetAsync(Guid labelId, CancellationToken cancellationToken = default) =>
            read(labelId, cancellationToken);
    }

    private sealed class MutableUser : ICurrentUser
    {
        public string? UserId { get; set; }
        public bool IsAuthenticated => UserId is not null;
        public HashSet<Guid> AllowedCompanyIds { get; } = [];
        IReadOnlySet<Guid> ICurrentUser.AllowedCompanyIds => AllowedCompanyIds;
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
