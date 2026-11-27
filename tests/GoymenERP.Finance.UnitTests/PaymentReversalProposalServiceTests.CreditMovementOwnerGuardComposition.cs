using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class PaymentReversalProposalServiceTests
{
    [Fact]
    public async Task Actual_module_credit_movement_decision_factory_is_guarded_and_preserves_same_scope_caller_audit_and_tracker()
    {
        var services = OwnerGuardCompositionServices();
        // Movement's real factory requires a Settings port. Reuse the existing fail-closed owner adapter;
        // no profile/period is manufactured and caller work must refuse before querying any domain source.
        services.AddScoped<ISettingsPrerequisiteProvider, UnavailableSettingsPrerequisiteProvider>();
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IFinanceCreditMovementServiceV1));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime); Assert.NotNull(descriptor.ImplementationFactory);
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(Company);
        var owner = scope.ServiceProvider.GetRequiredService<IFinanceCreditMovementServiceV1>();
        Assert.IsType<GuardedFinanceCreditMovementServiceV1>(owner); Assert.IsNotType<FinanceCreditMovementServiceV1>(owner);
        Assert.Null(scope.ServiceProvider.GetService<FinanceCreditMovementServiceV1>());
        Assert.Same(owner, scope.ServiceProvider.GetRequiredService<IFinanceCreditMovementServiceV1>());
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var caller = new FinanceCreditMovementRow { Id = Guid.NewGuid(), CompanyId = Company, Version = 1,
            SourceJson = "{}", SourceIdentityHash = new string('a', 64), MoneyProfileJson = "{}", ProposedBy = "SENTETIK caller" };
        db.CreditMovements.Add(caller);
        var writer = OwnerGuardCompositionPendingAudit(scope.ServiceProvider, caller.Id);
        var result = await owner.DecideAsync(new(Company, "checker", "credit-movement-di", null), Key("credit-movement-di"), new(caller.Id, "1", true, null), default);
        Assert.Equal(CommandOutcome.Unavailable, result.Outcome); Assert.False(result.IsReplay); Assert.Null(result.Resource);
        Assert.Same(caller, Assert.Single(db.ChangeTracker.Entries()).Entity); Assert.Equal(EntityState.Added, db.Entry(caller).State);
        Assert.True(writer.HasPendingDrafts); Assert.Null(db.Database.CurrentTransaction);
        var permissions = scope.ServiceProvider.GetRequiredService<SourceCompositionAuthority>().Permissions;
        Assert.NotEmpty(permissions); Assert.All(permissions, permission => Assert.Equal(ErpPermissions.FinanceCreditsApprove, permission));
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<OwnerGuardCompositionPorts>().Reads);
        writer.DiscardPending(); Assert.False(writer.HasPendingDrafts); // Fixture cleanup only after caller preservation assertions.
    }
}
