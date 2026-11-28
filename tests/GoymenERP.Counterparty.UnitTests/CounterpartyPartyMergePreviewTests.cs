using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GoymenERP.Counterparty.UnitTests;

// InMemory read-boundary tests; these are not SQL transaction/merge acceptance evidence.
public sealed class CounterpartyPartyMergePreviewTests
{
    private static readonly Guid A = Guid.Parse("ca000000-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("ca000000-0000-0000-0000-000000000002");
    [Fact]
    public async Task Preview_discloses_actual_accounts_only_after_both_permissions_in_every_holding_company_and_preserves_caller_work()
    {
        await using var f = await Fixture.Create();
        var caller = new Party(Guid.NewGuid(), PartyCode.FromSequence(9), PartyKind.LegalEntity, "SENTETIK caller", null);
        f.Db.Parties.Add(caller);
        var result = await f.Read();
        Assert.Equal(CounterpartyPartyMergePreviewStatusV1.Found, result.Status);
        var preview = Assert.IsType<CounterpartyPartyMergeImpactPreviewV1>(result.Preview);
        Assert.Equal(new[] { A, B }, preview.HoldingAccounts.Select(x => x.CompanyId).Distinct().Order());
        Assert.Equal(f.Merged.Id, preview.Changes.PartyId); Assert.Equal(f.Survivor.Id, preview.Changes.MergedIntoPartyId);
        Assert.False(preview.Changes.IsActive);
        foreach (var company in new[] { A, B })
            foreach (var permission in new[] { ErpPermissions.CounterpartyPartiesMergeApprove, ErpPermissions.CounterpartyPartiesRead })
                Assert.Contains((permission, company), f.Authority.Calls);
        Assert.Equal(EntityState.Added, f.Db.Entry(caller).State);
        Assert.Equal(0, f.Audit.Writes); Assert.Empty(f.Db.PartyMerges.Local);
    }
    [Theory]
    [InlineData("permission")]
    [InlineData("scope")]
    [InlineData("membership")]
    [InlineData("actor")]
    [InlineData("company")]
    public async Task Missing_holding_company_authority_or_context_drift_never_releases_an_impact(string failure)
    {
        await using var f = await Fixture.Create();
        f.Authority.Answer = (_, company) =>
        {
            if (company != B) return Granted();
            if (failure == "actor") f.User.UserId = "other";
            if (failure == "company") f.Company.Set(B);
            if (failure == "membership") f.User.AllowedCompanyIds = new HashSet<Guid> { A };
            return failure == "permission" ? PermissionScope.Denied : Granted(failure != "scope");
        };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Read());
        Assert.Equal(0, f.Audit.Writes); Assert.Empty(f.Db.ChangeTracker.Entries());
    }
    [Theory]
    [InlineData("0", "1")]
    [InlineData("1", "0")]
    [InlineData("2", "1")]
    [InlineData("1", "2")]
    public async Task Invalid_or_changed_party_versions_cannot_release_preview(string survivor, string merged)
    {
        await using var f = await Fixture.Create();
        if (survivor == "0" || merged == "0")
            await Assert.ThrowsAsync<ArgumentException>(() => f.Read(survivor, merged));
        else
        {
            var result = await f.Read(survivor, merged);
            Assert.Equal(CounterpartyPartyMergePreviewStatusV1.Changed, result.Status); Assert.Null(result.Preview);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_before_entry_or_inside_ignoring_authority_never_releases_preview(bool duringAwait)
    {
        await using var f = await Fixture.Create(); using var cts = new CancellationTokenSource();
        if (!duringAwait) cts.Cancel();
        else f.Authority.Answer = (_, _) => { cts.Cancel(); return Granted(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Read(token: cts.Token));
        if (!duringAwait) Assert.Empty(f.Authority.Calls);
        Assert.Equal(0, f.Audit.Writes);
    }
    private static PermissionScope Granted(bool companyWide = true) => new(true, companyWide,
        new HashSet<string> { "SENTETIK-PREVIEW" }, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true; public string? UserId { get; set; } = "SENTETIK-actor";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; set; } = new HashSet<Guid> { A, B };
        public bool CanAccessCompany(Guid id) => AllowedCompanyIds.Contains(id);
    }
    private sealed class Company : ICompanyContext
    {
        public bool IsResolved => true; public Guid CompanyId { get; private set; } = A;
        public void Set(Guid id) => CompanyId = id;
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public Func<string, Guid, PermissionScope> Answer = (_, _) => Granted();
        public List<(string, Guid)> Calls { get; } = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        { Calls.Add((permission, companyId)); return Task.FromResult(Answer(permission, companyId)); }
    }
    private sealed class Audit : IAuditEventCollector
    { public int Writes; public void Enqueue(AuditEventDraft draft) { Writes++; throw new InvalidOperationException("Read cannot audit a merge."); } }
    private sealed class Fixture : IAsyncDisposable
    {
        public Company Company { get; } = new(); public User User { get; } = new();
        public Authority Authority { get; } = new(); public Audit Audit { get; } = new();
        public CounterpartyDbContext Db { get; }
        public Party Survivor { get; } = new(Guid.NewGuid(), PartyCode.FromSequence(1), PartyKind.LegalEntity, "SENTETIK survivor", null);
        public Party Merged { get; } = new(Guid.NewGuid(), PartyCode.FromSequence(2), PartyKind.LegalEntity, "SENTETIK duplicate", null);
        private Fixture() => Db = new(new DbContextOptionsBuilder<CounterpartyDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Company);
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); f.Db.Parties.AddRange(f.Survivor, f.Merged);
            f.Db.CompanyPartyAccounts.AddRange(new CompanyPartyAccount(Guid.NewGuid(), A, f.Survivor.Id, PartyRole.Customer),
                new CompanyPartyAccount(Guid.NewGuid(), A, f.Merged.Id, PartyRole.Customer));
            await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
            f.Company.Set(B); f.Db.CompanyPartyAccounts.Add(new CompanyPartyAccount(Guid.NewGuid(), B, f.Merged.Id, PartyRole.Customer));
            await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear(); f.Company.Set(A); return f;
        }
        public Task<CounterpartyPartyMergePreviewResultV1> Read(string survivor = "1", string merged = "1", CancellationToken token = default) =>
            new CounterpartyCommandService(Db, User, Company, null!, Authority, Audit, null!, null!,
                NullLogger<CounterpartyCommandService>.Instance, TimeProvider.System)
                .PreviewMergeAsync(Survivor.Id, new(Merged.Id, survivor, merged), token);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
