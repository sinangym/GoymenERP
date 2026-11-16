using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryFutureAllocationCommandTests
{
    private static readonly Guid Company = Guid.Parse("8a190000-0000-4000-8000-000000000001");
    private static readonly Guid Allocation = Guid.Parse("8a190000-0000-4000-8000-000000000002");

    [Fact]
    public async Task The_user_boundary_derives_actor_permission_and_roles_and_revalidates_replays()
    {
        var context = new CompanyContext(); context.Set(Company);
        var authority = new Authority();
        var writer = new Writer { Replay = true };
        var command = new InventoryFutureAllocationCommandService(context, new User(), authority, writer);
        var result = await command.CreateAsync(Request(), Transport());
        Assert.True(result.IsReplay);
        Assert.Equal(("SENTETIK-actor", ErpPermissions.InventoryFutureAllocationWrite),
            (writer.Metadata!.ActorId, writer.Metadata.PermissionCode));
        Assert.Equal(["SENTETIK-measured"], writer.Metadata.RoleCodes);
        Assert.All(authority.Permissions, permission => Assert.Equal(ErpPermissions.InventoryFutureAllocationWrite, permission));
        Assert.True(authority.Permissions.Count >= 3);
        Assert.Equal(InventoryPermissionManifestV1.FutureAllocationWrite, ErpPermissions.InventoryFutureAllocationWrite);
        Assert.Equal(InventoryPermissionManifestV1.FutureAllocationRead, ErpPermissions.InventoryFutureAllocationRead);
    }

    [Fact]
    public async Task Denial_precedes_the_writer_and_a_revoked_grant_prevents_a_write_fence()
    {
        var context = new CompanyContext(); context.Set(Company);
        var authority = new Authority { Granted = false };
        var writer = new Writer();
        var command = new InventoryFutureAllocationCommandService(context, new User(), authority, writer);
        var denied = await Assert.ThrowsAsync<InventoryAllocationException>(() => command.CreateAsync(Request(), Transport()));
        Assert.Equal(403, denied.Status);
        Assert.Null(writer.Metadata);

        authority.Granted = true;
        writer.BeforeFence = () => authority.Granted = false;
        var revoked = await Assert.ThrowsAsync<InventoryAllocationException>(() => command.ReleaseAsync(Allocation,
            new("1", null, "manual-release", "SENTETIK gerekçe"), Transport()));
        Assert.Equal(403, revoked.Status);
        Assert.False(writer.Saved);
    }

    private static CreateFutureSupplyAllocationV1 Request() => new(Allocation, Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), "1", Guid.NewGuid(), "SENTETIK gerekçe");
    private static InventoryCommandMetadata Transport() => new("spoofed", "spoofed", ["spoofed"], "SENTETIK-corr", null, "SENTETIK-key", "Web");

    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => "SENTETIK-actor";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }

    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public bool Granted { get; set; } = true;
        public List<string> Permissions { get; } = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Permissions.Add(permission);
            return Task.FromResult(new PermissionScope(Granted, true, new HashSet<string> { "SENTETIK-measured" }, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }

    private sealed class Writer : IInventoryFutureAllocationWriterV1
    {
        public bool Replay { get; init; }
        public bool Saved { get; private set; }
        public Action? BeforeFence { get; set; }
        public InventoryCommandMetadata? Metadata { get; private set; }
        public Task<InventoryFutureAllocationResultV1> CreateAsync(CreateFutureSupplyAllocationV1 request, InventoryCommandMetadata metadata,
            IInventoryFutureAllocationAuthorityFenceV1 fence, CancellationToken cancellationToken = default) => Answer(metadata, fence, cancellationToken);
        public Task<InventoryFutureAllocationResultV1> ReleaseAsync(Guid allocationId, ReleaseFutureSupplyAllocationV1 request, InventoryCommandMetadata metadata,
            IInventoryFutureAllocationAuthorityFenceV1 fence, CancellationToken cancellationToken = default) => Answer(metadata, fence, cancellationToken);
        private async Task<InventoryFutureAllocationResultV1> Answer(InventoryCommandMetadata metadata,
            IInventoryFutureAllocationAuthorityFenceV1 fence, CancellationToken cancellationToken)
        {
            Metadata = metadata;
            BeforeFence?.Invoke();
            await fence.RequireStillAsync(cancellationToken);
            Saved = !Replay;
            var id = Guid.NewGuid();
            return new(new(Allocation, Company, "1", "Active", id, id, id, id, id, "1", id,
                "1", "1", "0", "0", null, "SENTETIK gerekçe", null, null, null,
                DateTimeOffset.Parse("2026-11-16T09:00:00Z"), DateTimeOffset.Parse("2026-11-16T09:00:00Z"), null), Replay, "SENTETIK-original");
        }
    }
}
