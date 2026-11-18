using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventorySubcontractAllocationReleaseServiceTests
{
    [Fact]
    public async Task Release_uses_only_current_physical_allocation_authority_and_server_actor_company_roles()
    {
        var world = new World(); var answer = await world.Service.ReleaseAsync(world.Id, new("2", "SENTETIK reason"), Transport());
        Assert.Equal("20", answer.Release.ReleasedKg); Assert.Equal("80", answer.Release.PreviouslyConsumedKg);
        Assert.All(world.Authority.Requests, x => Assert.Equal(ErpPermissions.InventoryPhysicalAllocationWrite, x));
        Assert.Equal(world.User.UserId, world.Store.Metadata!.ActorId); Assert.Equal(new[] { "SENTETIK-role" }, world.Store.Metadata.RoleCodes);
        Assert.Equal(ErpPermissions.InventoryPhysicalAllocationWrite, world.Store.Metadata.PermissionCode);
    }
    [Theory]
    [InlineData("company")][InlineData("actor")][InlineData("permission")][InlineData("membership")]
    public async Task Midflight_authority_drift_refuses_before_owner_write_and_does_not_release_a_hold(string kind)
    {
        var world = new World(); world.Store.BeforeFence = () =>
        { if (kind == "company") world.Company.Set(Guid.NewGuid()); else if (kind == "actor") world.User.UserId = Guid.NewGuid().ToString("D");
            else if (kind == "membership") world.User.Access = false; else world.Authority.Granted = false; };
        var error = await Assert.ThrowsAsync<InventoryAllocationException>(() => world.Service.ReleaseAsync(world.Id, new("2", "SENTETIK"), Transport()));
        Assert.Equal(403, error.Status); Assert.Equal(0, world.Store.Writes);
    }
    [Theory]
    [InlineData("01")][InlineData("0")][InlineData("1.0")][InlineData(" 1")]
    public async Task Noncanonical_expected_version_is_400_and_never_enters_the_owner(string version)
    {
        var world = new World(); var error = await Assert.ThrowsAsync<InventoryAllocationException>(() => world.Service.ReleaseAsync(world.Id, new(version, "SENTETIK"), Transport()));
        Assert.Equal(400, error.Status); Assert.Equal(0, world.Store.Reads);
    }
    [Fact]
    public async Task Company_wide_scope_is_required_and_source_failure_does_not_become_an_empty_success()
    {
        var world = new World(); world.Authority.Wide = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<InventoryAllocationException>(() => world.Service.ListAsync(null))).Status);
        world.Authority.Wide = true; world.Store.Fail = true;
        Assert.Equal(503, (await Assert.ThrowsAsync<InventoryAllocationException>(() => world.Service.ListAsync(null))).Status);
    }
    [Fact]
    public async Task Broken_owner_source_is_declared_unavailable_instead_of_leaking_another_commands_problem_code()
    {
        var world = new World(); world.Store.SourceFailure = true;
        var error = await Assert.ThrowsAsync<InventoryAllocationException>(() => world.Service.ListAsync(null));
        Assert.Equal(503, error.Status); Assert.Equal(InventorySubcontractAllocationReleaseV1.Unavailable, error.Code);
    }
    [Fact]
    public async Task Malformed_owner_candidate_is_unavailable_instead_of_an_empty_or_null_reference_success()
    {
        var world = new World(); world.Store.MalformedCandidate = true;
        var error = await Assert.ThrowsAsync<InventoryAllocationException>(() => world.Service.ListAsync(null));
        Assert.Equal(503, error.Status); Assert.Equal(InventorySubcontractAllocationReleaseV1.Unavailable, error.Code);
    }
    private static InventoryCommandMetadata Transport() => new("SENTETIK-untrusted-actor", "SENTETIK-untrusted-permission", ["SENTETIK-untrusted-role"], "SENTETIK-correlation", null, "SENTETIK-key", "Integration");
    private sealed class World
    {
        public Guid Id { get; } = Guid.NewGuid(); public AdversarialCompanyContext Company { get; } = new(); public User User { get; } = new();
        public Authority Authority { get; } = new(); public Store Store { get; }
        public InventorySubcontractAllocationReleaseServiceV1 Service { get; }
        public World() { Company.Set(User.Company); Store = new(Company, Id); Service = new(Company, User, Authority, Store); }
    }
    private sealed class User : ICurrentUser
    {
        public Guid Company { get; } = Guid.NewGuid(); public string? UserId { get; set; } = Guid.NewGuid().ToString("D");
        public bool IsAuthenticated => true; public bool Access { get; set; } = true;
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => Access && companyId == Company;
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public bool Granted { get; set; } = true; public bool Wide { get; set; } = true; public List<string> Requests { get; } = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        { Requests.Add(permission); return Task.FromResult(new PermissionScope(Granted, Wide, new HashSet<string> { "SENTETIK-role" }, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    private sealed class Store(ICompanyContext company, Guid id) : IInventorySubcontractAllocationReleaseStoreV1
    {
        public Action? BeforeFence { get; set; } public InventoryCommandMetadata? Metadata { get; private set; }
        public int Reads { get; private set; } public int Writes { get; private set; } public bool Fail { get; set; }
        public bool SourceFailure { get; set; } public bool MalformedCandidate { get; set; }
        public async Task<InventorySubcontractAllocationReleaseCandidatesV1> ListAsync(Guid? after, Func<CancellationToken, Task> freshAuthority, CancellationToken cancellationToken = default)
        {
            Reads++; await Task.Yield(); BeforeFence?.Invoke(); await freshAuthority(cancellationToken);
            if (Fail) throw new InvalidOperationException("SENTETIK");
            if (SourceFailure) throw InventorySubcontractDeliveryContractV1.Refused(503, InventorySubcontractDeliveryContractV1.SourceMismatch);
            return MalformedCandidate ? new([new(null!, "SENTETIK", "1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())], null) : new([], null);
        }
        public async Task<InventorySubcontractAllocationReleaseResultV1> ReleaseAsync(Guid allocationId, ReleaseInventorySubcontractAllocationV1 request, InventoryCommandMetadata metadata, Func<CancellationToken, Task> freshAuthority, CancellationToken cancellationToken = default)
        {
            Reads++; Metadata = metadata; await Task.Yield(); BeforeFence?.Invoke(); await freshAuthority(cancellationToken); Writes++;
            var now = DateTimeOffset.UtcNow; var allocation = new InventoryPhysicalAllocationViewV1(id, company.CompanyId, "3", "Released", Guid.NewGuid(), Guid.NewGuid(),
                InventoryAllocationDemandKindsV1.SubcontractServiceOrderLine, Guid.NewGuid(), Guid.NewGuid(), "SENTETIK1", "100", "0", "SENTETIK original", null, now, now);
            return new(new(allocation, "4", "20", "80", request.Reason, now), false, metadata.CorrelationId);
        }
    }
    // SENTETIK adversarial port: production CompanyContext intentionally rejects this drift at Set().
    private sealed class AdversarialCompanyContext : ICompanyContext
    {
        public bool IsResolved { get; private set; }
        public Guid CompanyId { get; private set; }
        public void Set(Guid companyId) { CompanyId = companyId; IsResolved = true; }
    }

}
