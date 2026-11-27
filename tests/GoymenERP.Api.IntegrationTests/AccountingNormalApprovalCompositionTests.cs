using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class AccountingNormalApprovalCompositionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(AccountingCommandNames.ApprovePeriod)]
    [InlineData("accounting.period.reopening.approve.v1")]
    public async Task Real_module_approval_alias_preserves_caller_audit_added_during_its_fence_without_opening_SQL(string? periodCommand)
    {
        var company = Guid.Parse("ac490000-0000-4000-8000-000000000001");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DB_CONNECTION_STRING"] = "Server=unused.invalid;Database=unused;Connect Timeout=1;",
            // Same synthetic AddAuditModule fixture settings as the native Finance composition test.
            ["Audit:RefusedAttempts:Retention:Period"] = "P2Y",
            ["Audit:RefusedAttempts:Retention:PolicyRef"] = "policy-ref-1",
            ["Audit:RefusedAttempts:Retention:AutoDisposalEnabled"] = "false",
            ["Audit:RefusedAttempts:StandaloneWrite:Timeout"] = "00:00:02",
        }).Build();
        var services = new ServiceCollection();
        services.AddScoped<ICompanyContext>(_ =>
        {
            var context = new CompanyContext(); context.Set(company); return context;
        });
        services.AddScoped<ISettingsPrerequisiteProvider>(_ =>
            new AccountingSqlServerRelationalAcceptanceTests.SyntheticMoneyProfiles(company));
        services.AddScoped<ICurrentUser, AnonymousUser>();
        services.AddScoped<IErpAuthorityRevalidationService, DenyAuthority>();
        services.AddScoped<ICorrelationContext, CorrelationContext>();
        services.AddAuditModule(config);
        services.AddAccountingModule(config);
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var owner = provider.CreateScope();
        var scope = owner.ServiceProvider;
        var core = scope.GetRequiredService<AccountingStore>();
        var guarded = scope.GetRequiredService<IAccountingNormalJournalDraftStore>();
        Assert.NotSame(core, guarded);
        Assert.Same(guarded, scope.GetRequiredService<IAccountingNormalJournalDraftStore>());
        Assert.Same(core, scope.GetRequiredService<IAccountingCommandLog>());
        Assert.Same(core, scope.GetRequiredService<IAccountingPostingStore>());
        var periods = scope.GetRequiredService<IAccountingPeriodStore>();
        Assert.Same(core, periods);
        var api = scope.GetRequiredService<AccountingApiService>();
        Assert.Same(api, scope.GetRequiredService<IAccountingPeriodDecisionReadV1>());
        Assert.Same(scope.GetRequiredService<AccountingJournalReversalServiceV1>(), scope.GetRequiredService<IAccountingJournalReversalDecisionReadV1>());
        var collector = scope.GetRequiredService<AuditEventCollector>();
        Assert.Same(collector, scope.GetRequiredService<IAuditEventCollector>());
        var writer = scope.GetRequiredService<IAuditTransactionWriterV1>();
        Assert.False(writer.HasPendingDrafts);
        var draft = new AuditEventDraft(company, "SENTETIK.caller.v1", "Accounting.NormalJournalDraft", "SENTETIK-caller",
            "Caller", null, 1, AuditActorTypes.User, "SENTETIK-actor", NormalJournalPermissionNamesV1.DraftApprove,
            [], "{}", "{}", "SENTETIK caller draft", null, AuditAiInvolvement.None, null, "test", null, null,
            "SENTETIK-normal-composition", null, "SENTETIK-caller-key");
        var attempts = 0; var fences = 0;
        Task<AccountingApiResult<NormalJournalDraftViewV1>> Attempt(bool replayOnly)
        {
            attempts++;
            throw new InvalidOperationException("A refused clean-entry boundary must not invoke the native attempt.");
        }
        Task Fence(CancellationToken token)
        {
            fences++; collector.Enqueue(draft); return Task.CompletedTask;
        }
        var result = periodCommand is null
            ? await guarded.RunNormalApprovalAsync(Attempt, Fence)
            : await periods.RunPeriodApprovalAsync(periodCommand, Attempt, Fence);
        Assert.Equal(1, fences); Assert.Equal(0, attempts);
        Assert.Equal(AccountingApiStatus.Unavailable, result.Status); Assert.Null(result.Resource); Assert.False(result.IsReplay);
        // Only this exact caller draft was enqueued. The real writer still sees it after the refusal;
        // an owner DiscardPending/WritePending cleanup would have emptied this same collector.
        Assert.True(writer.HasPendingDrafts);
        var db = scope.GetRequiredService<AccountingDbContext>();
        Assert.False(db.ChangeTracker.HasChanges()); Assert.Null(db.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
        Assert.Equal(ConnectionState.Closed, scope.GetRequiredService<AuditDbContext>().Database.GetDbConnection().State);
        using var other = provider.CreateScope();
        Assert.NotSame(collector, other.ServiceProvider.GetRequiredService<AuditEventCollector>());
        Assert.NotSame(writer, other.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
        Assert.False(other.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>().HasPendingDrafts);
        Assert.True(writer.HasPendingDrafts);
        // Explicit caller teardown, after preservation assertions; never command-owned cleanup.
        writer.DiscardPending(); Assert.False(writer.HasPendingDrafts);
    }

    private sealed class AnonymousUser : ICurrentUser
    {
        public bool IsAuthenticated => false;
        public string? UserId => null;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>();
        public bool CanAccessCompany(Guid companyId) => false;
    }

    private sealed class DenyAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) => Task.FromResult(PermissionScope.Denied);
    }
}
