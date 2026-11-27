using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class PaymentReversalProposalServiceTests
{
    [Fact]
    public async Task Actual_module_opening_decision_factory_is_guarded_and_preserves_same_scope_caller_audit_and_tracker()
    {
        using var provider = OwnerGuardCompositionServices().BuildServiceProvider(); using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(Company);
        var owner = scope.ServiceProvider.GetRequiredService<IFinanceOpeningBalanceService>();
        Assert.IsNotType<FinanceOpeningBalanceService>(owner); Assert.Null(scope.ServiceProvider.GetService<FinanceOpeningBalanceService>());
        Assert.Same(owner, scope.ServiceProvider.GetRequiredService<IFinanceOpeningBalanceService>());
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var caller = new OpeningBalanceRow { Id = Guid.NewGuid(), CompanyId = Company };
        db.OpeningBalances.Add(caller);
        var writer = OwnerGuardCompositionPendingAudit(scope.ServiceProvider, caller.Id);
        var result = await owner.ApproveAsync(new(Company, "checker", "opening-di", null), Key("opening-di"), new(caller.Id, "1"), default);
        Assert.Equal(CommandOutcome.Unavailable, result.Outcome); Assert.False(result.IsReplay); Assert.Null(result.Resource);
        Assert.Same(caller, Assert.Single(db.ChangeTracker.Entries()).Entity); Assert.True(writer.HasPendingDrafts); Assert.Null(db.Database.CurrentTransaction);
        Assert.Contains(ErpPermissions.FinanceOpeningBalancesApprove, scope.ServiceProvider.GetRequiredService<SourceCompositionAuthority>().Permissions);
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<OwnerGuardCompositionPorts>().Reads);
        writer.DiscardPending(); Assert.False(writer.HasPendingDrafts); // Fixture cleanup, after preservation assertions.
    }

    [Fact]
    public async Task Actual_module_bank_credit_limit_decision_factory_is_guarded_and_preserves_same_scope_caller_audit_and_tracker()
    {
        using var provider = OwnerGuardCompositionServices().BuildServiceProvider(); using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(Company);
        var owner = scope.ServiceProvider.GetRequiredService<IFinanceCreditLimitServiceV1>();
        Assert.IsNotType<FinanceCreditLimitServiceV1>(owner); Assert.Null(scope.ServiceProvider.GetService<FinanceCreditLimitServiceV1>());
        Assert.Same(owner, scope.ServiceProvider.GetRequiredService<IFinanceCreditLimitServiceV1>());
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var caller = new FinanceCreditLimitRow { Id = Guid.NewGuid(), CompanyId = Company, Version = 1 };
        db.CreditLimits.Add(caller);
        var writer = OwnerGuardCompositionPendingAudit(scope.ServiceProvider, caller.Id);
        var result = await owner.DecideAsync(new(Company, "checker", "credit-limit-di", null), Key("credit-limit-di"), new(caller.Id, "1", true, null), default);
        Assert.Equal(CommandOutcome.Unavailable, result.Outcome); Assert.False(result.IsReplay); Assert.Null(result.Resource);
        Assert.Same(caller, Assert.Single(db.ChangeTracker.Entries()).Entity); Assert.True(writer.HasPendingDrafts); Assert.Null(db.Database.CurrentTransaction);
        var permissions = scope.ServiceProvider.GetRequiredService<SourceCompositionAuthority>().Permissions;
        Assert.NotEmpty(permissions); Assert.All(permissions, permission => Assert.Equal(ErpPermissions.FinanceCreditsApprove, permission));
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<OwnerGuardCompositionPorts>().Reads);
        writer.DiscardPending(); Assert.False(writer.HasPendingDrafts);
    }

    private static IServiceCollection OwnerGuardCompositionServices()
    {
        // Existing actual module factories, not substituted native owners/writers. The relational provider is configured
        // but never opened: pending caller work must refuse before any SQL/owner read. No SQL-success acceptance claim.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DB_PROVIDER"] = "SqlServer", ["DB_CONNECTION_STRING"] = "Server=unused.invalid;Database=unused;Connect Timeout=1;",
            ["Audit:RefusedAttempts:Retention:Period"] = "P2Y", ["Audit:RefusedAttempts:Retention:PolicyRef"] = "SENTETIK-test-only",
            ["Audit:RefusedAttempts:Retention:AutoDisposalEnabled"] = "false", ["Audit:RefusedAttempts:StandaloneWrite:Timeout"] = "00:00:05",
        }).Build();
        var services = new ServiceCollection(); services.AddAuditModule(configuration); services.AddFinancePersistence(configuration); services.AddFinanceServices();
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, d => d.ServiceType == typeof(IFinanceOpeningBalanceService)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, d => d.ServiceType == typeof(IFinanceCreditLimitServiceV1)).Lifetime);
        var writer = Assert.Single(services, d => d.ServiceType == typeof(IAuditTransactionWriterV1));
        Assert.Equal(ServiceLifetime.Scoped, writer.Lifetime); Assert.NotNull(writer.ImplementationType);
        services.AddScoped<ICompanyContext, CompanyContext>(); services.AddScoped<ICurrentUser>(_ => new User("checker"));
        services.AddScoped<SourceCompositionAuthority>(); services.AddScoped<IErpAuthorityRevalidationService>(p => p.GetRequiredService<SourceCompositionAuthority>());
        services.AddScoped<ICorrelationContext>(_ => new CorrelationContext());
        services.AddScoped<OwnerGuardCompositionPorts>();
        services.AddScoped<IFinancePartyAccountPort>(p => p.GetRequiredService<OwnerGuardCompositionPorts>());
        services.AddScoped<IFinanceCreditLenderPortV1>(p => p.GetRequiredService<OwnerGuardCompositionPorts>());
        return services;
    }
    private static IAuditTransactionWriterV1 OwnerGuardCompositionPendingAudit(IServiceProvider services, Guid id)
    {
        var collector = services.GetRequiredService<IAuditEventCollector>();
        Assert.Same(services.GetRequiredService<AuditEventCollector>(), collector);
        var writer = services.GetRequiredService<IAuditTransactionWriterV1>(); Assert.False(writer.HasPendingDrafts);
        collector.Enqueue(new AuditEventDraft(Company, "finance.opening-balance.approved.v1", "Finance.OpeningBalance", id.ToString("D"), "Approve",
            1, 2, "User", "checker", ErpPermissions.FinanceOpeningBalancesApprove, [], "{}", "{}", "SENTETIK caller-owned audit", null,
            AuditAiInvolvement.None, null, "Web", null, null, "owner-di", null, "owner-di"));
        // Public writer observes this exact scoped collector; no Audit.Pending/internal access or fake pending flag.
        Assert.True(writer.HasPendingDrafts); return writer;
    }
    private sealed class OwnerGuardCompositionPorts : IFinancePartyAccountPort, IFinanceCreditLenderPortV1
    {
        public int Reads;
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext c, Guid party, Guid account, CancellationToken t)
        { Reads++; throw new InvalidOperationException("Caller work must refuse before Cari reads."); }
        public Task<FinanceCreditLenderAnswerV1> ReadAsync(Guid company, Guid party, Guid account, CancellationToken t = default)
        { Reads++; throw new InvalidOperationException("Caller work must refuse before lender reads."); }
    }
}
