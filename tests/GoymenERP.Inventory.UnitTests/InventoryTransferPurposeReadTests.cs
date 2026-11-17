using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryTransferPurposeReadTests
{
    private static readonly Guid Company = Guid.Parse("635cad21-a981-4484-82b0-f600049f0db3");
    private static readonly Guid Transfer = Guid.Parse("855ce98f-a846-447e-872e-9d374464cda2");
    [Theory]
    [InlineData("dispatch", InventoryPermissionManifestV1.StockTransferWrite)]
    [InlineData("receive", InventoryPermissionManifestV1.StockTransferWrite)]
    [InlineData("overage-decision", InventoryPermissionManifestV1.StockTransferOverageApprove)]
    [InlineData("loss-request", InventoryPermissionManifestV1.StockTransferLossRecord)]
    [InlineData("loss-decision", InventoryPermissionManifestV1.StockTransferLossApprove)]
    public async Task Command_context_uses_only_its_explicit_purpose_and_rechecks_after_each_owner_await(string purpose, string permission)
    {
        var company = new CompanyContext(); company.Set(Company); using var database = Database(company);
        var source = new Source(); var authority = new Authority(permission);
        var owner = new InventoryTransferReceiptWork(company, new User(), authority, source, source, database);
        var result = await owner.ReadAsync(Transfer, purpose);
        Assert.Equal((purpose, Transfer, "2"), (result.Purpose, result.Transfer.TransferId, result.Facts.Version));
        Assert.All(authority.Seen, value => Assert.Equal(permission, value)); Assert.True(authority.Seen.Count >= 3);
        authority.Deny = true;
        Assert.Equal(403, (await Assert.ThrowsAsync<InventoryTransferException>(() => owner.ReadAsync(Transfer, purpose))).Status);
        Assert.Equal(2, source.Reads);
    }
    [Theory]
    [InlineData("company")]
    [InlineData("version")]
    [InlineData("status")]
    public async Task Mixed_or_stale_owner_fact_frame_is_unavailable_instead_of_actionable_success(string fault)
    {
        var company = new CompanyContext(); company.Set(Company); using var database = Database(company);
        var source = new Source { Fault = fault };
        var owner = new InventoryTransferReceiptWork(company, new User(), new Authority(InventoryPermissionManifestV1.StockTransferWrite), source, source, database);
        Assert.Equal(503, (await Assert.ThrowsAsync<InventoryTransferException>(() => owner.ReadAsync(Transfer, "receive"))).Status);
    }
    [Fact]
    public async Task Owner_permission_loss_during_the_transfer_read_prevents_the_second_owner_read()
    {
        var company = new CompanyContext(); company.Set(Company); using var database = Database(company);
        var authority = new Authority(InventoryPermissionManifestV1.StockTransferWrite);
        var source = new Source { AfterTransfer = () => authority.Deny = true };
        var owner = new InventoryTransferReceiptWork(company, new User(), authority, source, source, database);
        Assert.Equal(403, (await Assert.ThrowsAsync<InventoryTransferException>(() => owner.ReadAsync(Transfer, "receive"))).Status);
        Assert.Equal(1, source.Reads);
    }
    [Fact]
    public async Task Planning_candidates_cannot_fall_back_to_a_successful_empty_provider_or_report_permission()
    {
        var company = new CompanyContext(); company.Set(Company);
        var authority = new Authority(InventoryPermissionManifestV1.StockTransferWrite);
        var owner = new InventoryTransferPlanCandidates(company, new User(), authority, null);
        Assert.Equal(503, (await Assert.ThrowsAsync<InventoryTransferException>(() => owner.SearchAsync(null))).Status);
        Assert.All(authority.Seen, value => Assert.Equal(InventoryPermissionManifestV1.StockTransferWrite, value));
        authority.Deny = true;
        Assert.Equal(403, (await Assert.ThrowsAsync<InventoryTransferException>(() => owner.SearchAsync(null))).Status);
    }
    private static InventoryDbContext Database(ICompanyContext company) => new(new DbContextOptionsBuilder<InventoryDbContext>()
        .UseSqlServer("Server=unused;Database=SyntheticPurposeModelOnly;Integrated Security=True;TrustServerCertificate=True").Options, company);
    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true; public string UserId => "SENTETIK";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid id) => id == Company;
    }
    private sealed class Authority(string allowed) : IErpAuthorityRevalidationService
    {
        public List<string> Seen { get; } = []; public bool Deny { get; set; }
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Seen.Add(permission);
            return Task.FromResult(!Deny && permission == allowed && companyId == Company
                ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) : PermissionScope.Denied);
        }
    }
    private sealed class Source : IInventoryTransferReaderV1, IInventoryTransferReceiptReaderV1
    {
        public string? Fault { get; init; } public Action? AfterTransfer { get; init; } public int Reads { get; private set; }
        public Task<InventoryTransferViewV1?> ReadAsync(Guid transferId, CancellationToken cancellationToken = default)
        {
            Reads++; AfterTransfer?.Invoke();
            return Task.FromResult<InventoryTransferViewV1?>(new(Transfer, Company, "2", "InTransit", Guid.NewGuid(), "Company", Company,
                Guid.NewGuid(), "Company", Company, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []));
        }
        public Task<InventoryTransferReceiptFactsViewV1?> ReadReceiptFactsAsync(Guid transferId, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult<InventoryTransferReceiptFactsViewV1?>(new(Transfer, Fault == "company" ? Guid.NewGuid() : Company,
                Fault == "version" ? "3" : "2", Fault == "status" ? "Accepted" : "InTransit", [], [], []));
        }
    }
}
