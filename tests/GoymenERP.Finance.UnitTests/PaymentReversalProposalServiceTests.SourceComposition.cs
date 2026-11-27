using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class PaymentReversalProposalServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Actual_module_source_descriptors_resolve_optional_guarded_reversal_owners_and_reach_their_decision_read(bool payment)
    {
        var company = new CompanyContext(); company.Set(Company);
        await using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase("reversal-source-composition-" + Guid.NewGuid()).Options, company);
        var services = new ServiceCollection(); services.AddFinanceServices();
        var kinds = new[] { typeof(FinanceManualBankPaymentReversalWorkInboxSourceV1), typeof(FinanceManualReceiptReversalWorkInboxSourceV1) };
        // Bounded composition slice: retain both actual source descriptors and ALL owner factories unchanged.
        // Other inbox families need unrelated host ports; removing only their source descriptors avoids fake replacements.
        var targetDescriptors = services.Where(d => d.ServiceType == typeof(IWorkInboxSourceV1) && d.ImplementationType is { } type && kinds.Contains(type)).ToArray();
        Assert.Equal(2, targetDescriptors.Length); Assert.All(targetDescriptors, d => Assert.Equal(ServiceLifetime.Scoped, d.Lifetime));
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IWorkInboxSourceV1) && !targetDescriptors.Contains(d)).ToArray())
            services.Remove(descriptor);
        var authority = new SourceCompositionAuthority(); var writer = new DecisionEntryWriter(); var collector = new AuditEventCollector();
        services.AddSingleton<ICompanyContext>(company); services.AddSingleton<ICurrentUser>(new User("checker"));
        services.AddSingleton<IErpAuthorityRevalidationService>(authority); services.AddSingleton(db);
        services.AddSingleton<ICorrelationContext>(new CorrelationContext()); services.AddSingleton<IAuditEventCollector>(collector);
        services.AddSingleton<IAuditTransactionWriterV1>(writer); services.AddSingleton<IFinanceRecordPeriodRule>(new OpenPeriod());
        services.AddSingleton(TimeProvider.System);
        Assert.Single(services, d => d.ServiceType == typeof(IPaymentReversalProposalService));
        Assert.Single(services, d => d.ServiceType == typeof(IReversalProposalService));
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var resolved = scope.ServiceProvider.GetServices<IWorkInboxSourceV1>().ToArray(); Assert.Equal(2, resolved.Length);
        Assert.Null(scope.ServiceProvider.GetService<PaymentReversalProposalService>());
        Assert.Null(scope.ServiceProvider.GetService<ReversalProposalService>());
        var id = Guid.NewGuid(); var workContext = new WorkInboxSourceContextV1(Company, "checker", "source-di", null);
        var nativeContext = new FinanceContext(Company, "checker", "native-di", null);
        if (payment)
        {
            var owner = Assert.IsType<GuardedPaymentReversalProposalService>(scope.ServiceProvider.GetRequiredService<IPaymentReversalProposalService>());
            Assert.IsAssignableFrom<IPaymentReversalDecisionPreviewReadV1>(owner);
            var source = Assert.IsType<FinanceManualBankPaymentReversalWorkInboxSourceV1>(Assert.Single(resolved, s => s.Kind == FinanceManualBankPaymentReversalWorkInboxSourceV1.KindName));
            Assert.IsAssignableFrom<IWorkApprovalDecisionSourceV1>(source);
            // Missing native source facts produce THIS error only after the optional owner was injected and called.
            // A missing optional registration would instead throw "decision owner is unavailable".
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(workContext, id, default));
            Assert.Equal("Payment reversal decision facts are unavailable.", error.Message);
            Assert.Contains(FinancePermissionNames.PaymentReverseApprove, authority.Permissions);
            Assert.Equal(CommandOutcome.Unavailable, (await owner.ApproveAsync(nativeContext, Key("di-entry"), new(id, "1"), default)).Outcome);
        }
        else
        {
            var owner = Assert.IsType<GuardedReversalProposalService>(scope.ServiceProvider.GetRequiredService<IReversalProposalService>());
            Assert.IsAssignableFrom<IReceiptReversalDecisionReadV1>(owner);
            var source = Assert.IsType<FinanceManualReceiptReversalWorkInboxSourceV1>(Assert.Single(resolved, s => s.Kind == FinanceManualReceiptReversalWorkInboxSourceV1.KindName));
            Assert.IsAssignableFrom<IWorkApprovalDecisionSourceV1>(source);
            // Native owner NotFound reaches the source as null, rather than missing-owner Unavailable.
            Assert.Null(await source.ReadDecisionAsync(workContext, id, default));
            Assert.Contains(FinancePermissionNames.ReverseApprove, authority.Permissions);
            Assert.Equal(CommandOutcome.Unavailable, (await owner.ApproveAsync(nativeContext, Key("di-entry"), new(id, "1"), default)).Outcome);
        }
        // Configured nonrelational refusal is NOT successful SQL/Audit/treasury acceptance.
        Assert.False(db.ChangeTracker.HasChanges()); Assert.Empty(await db.Commands.ToArrayAsync()); Assert.Empty(collector.Pending);
        Assert.False(writer.HasPendingDrafts); Assert.Equal(0, writer.Writes); Assert.Equal(0, writer.Discards);
    }

    private sealed class SourceCompositionAuthority : IErpAuthorityRevalidationService
    {
        public List<string> Permissions { get; } = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken token = default)
        {
            Permissions.Add(permission);
            // Controlled existing-permission authority only; no Identity grant or role is created.
            return Task.FromResult(new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }
}
