using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Infrastructure;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Actual module composition and clean-entry refusal, without a SQL connection or transaction acceptance claim.
/// This source expects the root-owned Organization factory; it never substitutes a tax service registration.</summary>
public sealed class OrganizationCompanyTaxIdApprovalCompositionTests
{
    private static readonly Guid CompanyId = Guid.Parse("ac590000-0000-4000-8000-000000000001");
    private static readonly Guid RequestId = Guid.Parse("ac590000-0000-4000-8000-000000000002");
    private const string Actor = "SENTETIK-tax-composition-checker";

    [Fact]
    public async Task Real_modules_preserve_the_exact_scoped_caller_collector_added_during_first_authority_await_without_SQL()
    {
        var connection = new NeverOpen(); using var provider = Services(connection).BuildServiceProvider(validateScopes: true);
        using var owner = provider.CreateScope(); var scope = owner.ServiceProvider;
        var core = scope.GetRequiredService<IOrganizationMasterDataService>();
        var guarded = scope.GetRequiredService<IOrganizationCompanyTaxIdService>();
        Assert.IsType<OrganizationMasterDataService>(core); Assert.IsNotType<OrganizationMasterDataService>(guarded);
        Assert.NotSame(core, guarded); Assert.Same(core, scope.GetRequiredService<IOrganizationMasterDataService>());
        Assert.Same(guarded, scope.GetRequiredService<IOrganizationCompanyTaxIdService>());
        Assert.Null(scope.GetService<OrganizationMasterDataService>()); // No independent raw concrete runtime registration.
        var db = scope.GetRequiredService<OrganizationDbContext>(); Assert.Same(db, scope.GetRequiredService<OrganizationDbContext>());
        var collector = scope.GetRequiredService<AuditEventCollector>(); Assert.Same(collector, scope.GetRequiredService<IAuditEventCollector>());
        var writer = scope.GetRequiredService<IAuditTransactionWriterV1>(); Assert.Same(writer, scope.GetRequiredService<IAuditTransactionWriterV1>());
        Assert.False(writer.HasPendingDrafts); Assert.Empty(db.ChangeTracker.Entries());
        var draft = Draft(); var enqueues = 0; var authority = scope.GetRequiredService<CompositionAuthority>();
        authority.AfterAwait = () => { enqueues++; collector.Enqueue(draft); };

        var result = await guarded.DecideCompanyTaxIdChangeAsync(RequestId, new(1, "approve", null), Metadata(), default);

        Assert.Equal(CommandResult.DependencyUnavailable, result.Result); Assert.Equal(503, result.Response.StatusCode); Assert.False(result.IsReplay);
        Assert.Null(result.Response.Created); Assert.Equal("SENTETIK-tax-composition", result.OriginalCorrelationId);
        Assert.Equal(1, authority.Reads); Assert.Equal(1, enqueues); Assert.True(writer.HasPendingDrafts);
        Assert.Same(collector, scope.GetRequiredService<IAuditEventCollector>());
        // The real collector started empty; only the captured caller draft was enqueued once. Neither native IO nor
        // writer cleanup ran: the real writer still observes that same collector after the failed post-await entry check.
        Assert.Empty(db.ChangeTracker.Entries()); Assert.Null(db.Database.CurrentTransaction); Assert.Equal(0, connection.Opens);
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
        var auditDb = scope.GetRequiredService<AuditDbContext>(); Assert.Empty(auditDb.ChangeTracker.Entries());
        Assert.Equal(ConnectionState.Closed, auditDb.Database.GetDbConnection().State);
        using var next = provider.CreateScope(); var nextScope = next.ServiceProvider;
        Assert.NotSame(core, nextScope.GetRequiredService<IOrganizationMasterDataService>());
        Assert.NotSame(guarded, nextScope.GetRequiredService<IOrganizationCompanyTaxIdService>());
        Assert.NotSame(db, nextScope.GetRequiredService<OrganizationDbContext>());
        Assert.NotSame(collector, nextScope.GetRequiredService<AuditEventCollector>());
        Assert.NotSame(writer, nextScope.GetRequiredService<IAuditTransactionWriterV1>());
        Assert.False(nextScope.GetRequiredService<IAuditTransactionWriterV1>().HasPendingDrafts); Assert.True(writer.HasPendingDrafts);
        // Caller teardown only after preservation assertions, never command-owned disposal of caller work.
        writer.DiscardPending(); Assert.False(writer.HasPendingDrafts); Assert.Equal(0, connection.Opens);
    }

    [Fact]
    public async Task Real_organization_factory_without_an_actual_writer_refuses_before_authority_or_native_SQL()
    {
        var connection = new NeverOpen(); var services = Services(connection);
        services.RemoveAll<IAuditTransactionWriterV1>(); // Dependency absence, not a replacement guard/service factory.
        using var provider = services.BuildServiceProvider(validateScopes: true); using var owner = provider.CreateScope(); var scope = owner.ServiceProvider;
        var guarded = scope.GetRequiredService<IOrganizationCompanyTaxIdService>();
        Assert.IsNotType<OrganizationMasterDataService>(guarded); Assert.Null(scope.GetService<IAuditTransactionWriterV1>());
        var collector = scope.GetRequiredService<AuditEventCollector>(); Assert.Same(collector, scope.GetRequiredService<IAuditEventCollector>());
        var authority = scope.GetRequiredService<CompositionAuthority>(); authority.AfterAwait = () => throw new InvalidOperationException("Missing writer must refuse before its first authority IO.");
        var result = await guarded.DecideCompanyTaxIdChangeAsync(RequestId, new(1, "reject", null), Metadata(), default);
        Assert.Equal(CommandResult.DependencyUnavailable, result.Result); Assert.Equal(503, result.Response.StatusCode); Assert.False(result.IsReplay);
        Assert.Null(result.Response.Created); Assert.Equal(0, authority.Reads); Assert.Equal(0, connection.Opens);
        var db = scope.GetRequiredService<OrganizationDbContext>(); Assert.Empty(db.ChangeTracker.Entries()); Assert.Null(db.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
        var auditDb = scope.GetRequiredService<AuditDbContext>(); Assert.Empty(auditDb.ChangeTracker.Entries()); Assert.Equal(ConnectionState.Closed, auditDb.Database.GetDbConnection().State);
    }

    private static ServiceCollection Services(NeverOpen connection)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DB_CONNECTION_STRING"] = "Server=unused.invalid;Database=unused;Connect Timeout=1;",
            ["Audit:RefusedAttempts:Retention:Period"] = "P2Y",
            ["Audit:RefusedAttempts:Retention:PolicyRef"] = "policy-ref-1",
            ["Audit:RefusedAttempts:Retention:AutoDisposalEnabled"] = "false",
            ["Audit:RefusedAttempts:StandaloneWrite:Timeout"] = "00:00:02",
        }).Build();
        var services = new ServiceCollection(); services.AddLogging();
        services.AddScoped<ICompanyContext>(_ => { var company = new CompanyContext(); company.Set(CompanyId); return company; });
        services.AddScoped<ICurrentUser, CompositionUser>(); services.AddScoped<CompositionAuthority>();
        services.AddScoped<IErpAuthorityRevalidationService>(p => p.GetRequiredService<CompositionAuthority>());
        services.AddScoped<IErpAuthorizationService>(p => p.GetRequiredService<CompositionAuthority>());
        services.AddAuditModule(config);
        services.AddOrganizationModule(config, (_, options) => options.AddInterceptors(connection));
        return services;
    }
    private static CommandMetadata Metadata() => new(CompanyId, Actor, "SENTETIK-tax-composition", null, SourceChannels.Web, null, null, "SENTETIK-tax-composition-key");
    private static AuditEventDraft Draft() => new(CompanyId, "SENTETIK.caller.v1", "Organization.CompanyTaxIdChangeRequest", "SENTETIK-caller", "Caller", null, 1,
        AuditActorTypes.User, Actor, OrganizationCompanyTaxIdPermissionsV1.Approve, [], "{}", "{}", "SENTETIK caller draft", null,
        AuditAiInvolvement.None, null, "test", null, null, "SENTETIK-tax-composition", null, "SENTETIK-caller-key");
    private sealed class CompositionUser : ICurrentUser
    {
        public bool IsAuthenticated => true; public string UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([CompanyId]); public bool CanAccessCompany(Guid id) => id == CompanyId;
    }
    private sealed class CompositionAuthority : IErpAuthorityRevalidationService, IErpAuthorizationService
    {
        internal int Reads; internal Action? AfterAwait;
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(OrganizationCompanyTaxIdPermissionsV1.Approve, permission); Assert.Equal(CompanyId, companyId); Reads++;
            await Task.Yield(); AfterAwait?.Invoke();
            return new(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        }
        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Composition must not enter another native authorization path.");
        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Composition must use the fresh authority port.");
        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Composition must use the fresh authority port.");
    }
    private sealed class NeverOpen : DbConnectionInterceptor
    {
        internal int Opens;
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        { Opens++; throw new InvalidOperationException("Composition must never open SQL."); }
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        { Opens++; throw new InvalidOperationException("Composition must never open SQL."); }
    }
}
