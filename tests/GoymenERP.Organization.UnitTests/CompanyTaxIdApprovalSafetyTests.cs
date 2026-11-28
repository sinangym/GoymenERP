using System.Data;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Companies;
using GoymenERP.Modules.Organization.Infrastructure;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GoymenERP.Organization.UnitTests;

// These are entry/composition tests without a SQL connection. Owned SQL execution is tested separately.
public sealed partial class CompanyTaxIdApprovalSafetyTests
{
    private static readonly Guid CompanyId = Guid.Parse("0f000000-0000-4000-8000-000000005901");
    private static readonly Guid RequestId = Guid.Parse("0f000000-0000-4000-8000-000000005902");
    private static CommandMetadata Metadata() => new(CompanyId, "SENTETIK-checker", "SENTETIK-correlation", null, SourceChannels.Web, null, null, "SENTETIK-key");

    [Fact]
    public async Task Missing_actual_writer_refuses_before_any_authority_or_native_store_read()
    {
        await using var run = new Entry();
        var result = await run.Core.WithCompanyTaxIdApprovalSafety(null).DecideCompanyTaxIdChangeAsync(RequestId, new(1, "approve", null), Metadata(), default);
        Assert.Equal(CommandResult.DependencyUnavailable, result.Result);
        Assert.Equal(0, run.Authority.Reads); Assert.Equal(0, run.Audit.Discards); Assert.Equal(0, run.Audit.Writes);
        Assert.Empty(run.Db.ChangeTracker.Entries()); Assert.Null(run.Db.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData("dirty")]
    [InlineData("audit")]
    [InlineData("await-dirty")]
    [InlineData("await-audit")]
    [InlineData("ambient")]
    public async Task Entry_preserves_caller_work_before_and_after_the_first_authority_await(string boundary)
    {
        await using var run = new Entry();
        var row = new Company(CompanyId, "SENTETIK", "Sentetik şirket", "9000000017", false, false);
        void AddWork() { if (boundary.EndsWith("dirty", StringComparison.Ordinal)) run.Db.Companies.Add(row); else run.Audit.Enqueue(Draft()); }
        if (boundary is "dirty" or "audit") AddWork();
        if (boundary.StartsWith("await-", StringComparison.Ordinal)) run.Authority.AfterRead = AddWork;
        using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
        var originalAmbient = Transaction.Current;
        var result = await run.Service.DecideCompanyTaxIdChangeAsync(RequestId, new(1, "reject", null), Metadata(), default);
        Assert.Equal(CommandResult.DependencyUnavailable, result.Result);
        Assert.Same(originalAmbient, Transaction.Current); Assert.Equal(1, run.Authority.Reads);
        Assert.Equal(boundary.EndsWith("dirty", StringComparison.Ordinal), run.Db.ChangeTracker.HasChanges());
        if (boundary.EndsWith("dirty", StringComparison.Ordinal)) Assert.Same(row, Assert.Single(run.Db.ChangeTracker.Entries()).Entity);
        Assert.Equal(boundary.EndsWith("audit", StringComparison.Ordinal), run.Audit.HasPendingDrafts);
        Assert.Equal(0, run.Audit.Discards); Assert.Equal(0, run.Audit.Writes); Assert.Null(run.Db.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("initial-permission")]
    [InlineData("actor")]
    [InlineData("company")]
    [InlineData("membership")]
    public async Task Authority_loss_at_entry_never_clears_existing_caller_audit(string loss)
    {
        await using var run = new Entry(); run.Audit.Enqueue(Draft());
        if (loss == "initial-permission") run.Authority.Granted = false;
        run.Authority.AfterRead = () => { if (loss == "permission") run.Authority.Granted = false; if (loss == "actor") run.User.Actor = "SENTETIK-other"; if (loss == "company") run.Company.Set(Guid.NewGuid()); if (loss == "membership") run.User.Member = false; };
        // A scope returned before a permission callback changes is revalidated at the next owned boundary;
        // entry with caller audit still refuses before beginning an owned transaction.
        var result = await run.Service.DecideCompanyTaxIdChangeAsync(RequestId, new(1, "approve", null), Metadata(), default);
        Assert.Equal(loss == "permission" ? CommandResult.DependencyUnavailable : CommandResult.NotFound, result.Result);
        Assert.True(run.Audit.HasPendingDrafts); Assert.Equal(0, run.Audit.Discards); Assert.Equal(0, run.Audit.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_before_or_during_entry_is_honored_even_if_authority_ignores_the_token(bool during)
    {
        await using var run = new Entry(); using var cancel = new CancellationTokenSource();
        if (during) run.Authority.AfterRead = cancel.Cancel; else cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.Service.DecideCompanyTaxIdChangeAsync(RequestId, new(1, "approve", null), Metadata(), cancel.Token));
        Assert.Equal(during ? 1 : 0, run.Authority.Reads); Assert.Equal(0, run.Audit.Discards); Assert.Empty(run.Db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("invalid", 1L, null)]
    [InlineData("approve", 0L, null)]
    [InlineData("reject", 1L, "invalid\nreason")]
    public async Task Native_request_shape_is_still_validated_before_the_missing_writer_refusal(string decision, long version, string? note)
    {
        await using var run = new Entry();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => run.Core.WithCompanyTaxIdApprovalSafety(null).DecideCompanyTaxIdChangeAsync(RequestId, new(version, decision, note), Metadata(), default));
        Assert.Equal(0, run.Authority.Reads); Assert.Equal(0, run.Audit.Discards);
    }

    [Fact]
    public async Task Runtime_tax_interface_is_guarded_and_uses_the_same_scoped_master_database_and_collector_alias()
    {
        var services = new ServiceCollection(); var company = new MutableCompany(); company.Set(CompanyId);
        var authority = new Authority(); var audit = new TestAudit();
        services.AddLogging(); services.AddSingleton<ICompanyContext>(company); services.AddSingleton<ICurrentUser>(new User());
        services.AddSingleton<IErpAuthorizationService>(authority); services.AddSingleton<IErpAuthorityRevalidationService>(authority);
        services.AddSingleton<IAuditEventCollector>(audit); services.AddSingleton<IAuditTransactionWriterV1>(audit);
        services.AddOrganizationModule(new FlatConfiguration());
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        var core = Assert.IsType<OrganizationMasterDataService>(scope.ServiceProvider.GetRequiredService<IOrganizationMasterDataService>());
        var runtime = Assert.IsType<GuardedCompanyTaxIdService>(scope.ServiceProvider.GetRequiredService<IOrganizationCompanyTaxIdService>());
        Assert.Same(core, scope.ServiceProvider.GetRequiredService<IOrganizationMasterDataService>());
        Assert.Same(audit, scope.ServiceProvider.GetRequiredService<IAuditEventCollector>());
        Assert.Same(audit, scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
        var row = new Company(CompanyId, "SENTETIK", "Sentetik şirket", "9000000017", false, false);
        db.Companies.Add(row); audit.Enqueue(Draft());
        var result = await runtime.DecideCompanyTaxIdChangeAsync(RequestId, new(1, "approve", null), Metadata(), default);
        Assert.Equal(CommandResult.DependencyUnavailable, result.Result);
        Assert.Same(row, Assert.Single(db.ChangeTracker.Entries()).Entity); Assert.True(audit.HasPendingDrafts); Assert.Equal(0, audit.Discards);
    }

    [Fact]
    public async Task Guard_forwards_native_request_and_both_read_contracts_without_using_its_decision_writer()
    {
        var company = new MutableCompany(); company.Set(CompanyId); var bootstrap = new OrganizationBootstrapWriteScope();
        await using var db = new OrganizationDbContext(new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase("SENTETIK-tax-forward-" + Guid.NewGuid().ToString("N")).Options, company, bootstrap);
        using (bootstrap.BeginBootstrapWrite())
        {
            db.Companies.Add(new(CompanyId, "SENTETIK", "Sentetik şirket", "9000000017", false, false)); await db.SaveChangesAsync();
        }
        db.ChangeTracker.Clear(); var authority = new Authority(); var audit = new TestAudit();
        var core = new OrganizationMasterDataService(db, new User(), company, authority, authority, new Eligibility(), audit, NullLogger<OrganizationMasterDataService>.Instance);
        var wrapped = core.WithCompanyTaxIdApprovalSafety(audit);
        var result = await wrapped.RequestCompanyTaxIdChangeAsync(new(1, "9000000033", "SENTETIK request"), Metadata(), default);
        Assert.Equal(CommandResult.Created, result.Result); Assert.False(result.IsReplay);
        Assert.Equal(await core.GetCompanyTaxIdAsync(default), await wrapped.GetCompanyTaxIdAsync(default));
        var correction = await wrapped.GetCompanyTaxIdCorrectionsAsync(default); Assert.NotNull(correction);
        Assert.Equal(0, audit.Writes); Assert.Equal(0, audit.Discards); Assert.Single(audit.Drafts);
        // InMemory here only proves forwarding/native wire. It is never owned transaction acceptance.
    }

    private static AuditEventDraft Draft() => new(CompanyId, "organization.company.tax-id.change-approved.v1", "Organization.Company", CompanyId.ToString("D"), "Update", 1, 2,
        "User", "SENTETIK-checker", OrganizationCompanyTaxIdPermissionsV1.Approve, [], "{}", "{}", "SENTETIK fixture", null, AuditAiInvolvement.None, null, "Web", null, null, "SENTETIK", null, "SENTETIK-key");
    private sealed class Entry : IAsyncDisposable
    {
        internal readonly MutableCompany Company = new(); internal readonly User User = new(); internal readonly Authority Authority = new(); internal readonly TestAudit Audit = new();
        internal OrganizationDbContext Db { get; } internal OrganizationMasterDataService Core { get; } internal IOrganizationCompanyTaxIdService Service { get; }
        internal Entry()
        {
            Company.Set(CompanyId); Db = new(new DbContextOptionsBuilder<OrganizationDbContext>().UseSqlServer("Server=127.0.0.1;Database=SENTETIK_never_opened;Integrated Security=true;TrustServerCertificate=true").Options, Company, new());
            Core = new(Db, User, Company, Authority, Authority, new Eligibility(), Audit, NullLogger<OrganizationMasterDataService>.Instance);
            Service = Core.WithCompanyTaxIdApprovalSafety(Audit);
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class MutableCompany : ICompanyContext { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } public void Set(Guid id) => CompanyId = id; }
    private sealed class User : ICurrentUser { public string Actor = "SENTETIK-checker"; public bool Member = true; public bool IsAuthenticated => true; public string UserId => Actor; public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([CompanyId]); public bool CanAccessCompany(Guid id) => Member && AllowedCompanyIds.Contains(id); }
    private sealed class Authority : IErpAuthorizationService, IErpAuthorityRevalidationService
    {
        internal bool Granted = true; internal int Reads; internal Action? AfterRead;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        { Reads++; var result = new PermissionScope(Granted, Granted, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()); AfterRead?.Invoke(); return Task.FromResult(result); }
        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) => RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) => Task.FromResult(Granted);
        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Eligibility : IOrganizationLocationOperatorEligibilityPortV1 { public Task<OrganizationLocationOperatorEligibilityV1> CheckAsync(Guid companyId, string actorId, Guid partyId, CancellationToken cancellationToken = default) => throw new NotSupportedException(); }
    private sealed class TestAudit : IAuditEventCollector, IAuditTransactionWriterV1
    { internal readonly List<AuditEventDraft> Drafts = []; internal int Discards; internal int Writes; public bool HasPendingDrafts => Drafts.Count > 0; public void Enqueue(AuditEventDraft draft) => Drafts.Add(draft); public void DiscardPending() { Discards++; Drafts.Clear(); } public Task WritePendingAsync(IDbTransaction transaction, CancellationToken cancellationToken = default) { Writes++; throw new InvalidOperationException("Entry tests must never reach an audit write."); } }
    // Same no-provider IConfiguration shape used by existing composition tests; no package or SQL connection.
    private sealed class FlatConfiguration : IConfiguration
    {
        public string? this[string key] { get => key == "DB_CONNECTION_STRING" ? "Server=127.0.0.1;Database=SENTETIK_never_opened;Integrated Security=true;TrustServerCertificate=true" : null; set => throw new NotSupportedException(); }
        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => Microsoft.Extensions.FileProviders.NullChangeToken.Singleton;
        public IConfigurationSection GetSection(string key) => new Section(key);
        private sealed class Section(string key) : IConfigurationSection
        { public string Key => key; public string Path => key; public string? Value { get => null; set => throw new NotSupportedException(); } public string? this[string child] { get => null; set => throw new NotSupportedException(); } public IEnumerable<IConfigurationSection> GetChildren() => []; public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => Microsoft.Extensions.FileProviders.NullChangeToken.Singleton; public IConfigurationSection GetSection(string child) => new Section(child); }
    }
}
