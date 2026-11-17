using GoymenERP.Api.FinanceRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Existing SQL scenarios that need a genuinely closed period now prepare the next OPEN
/// period and use BOTH actual owners. Only identity/permissions are synthetic; no production grant
/// or fallback close path is created. These calls belong solely to the isolated acceptance database.</summary>
internal static class AccountingPeriodCarrySqlFixture
{
    public static IAccountingPeriodCarryForwardPortV1 Port(AccountingDbContext database, ICompanyContext company,
        AuditEventCollector audit, IAuditTransactionWriterV1 auditOwner, Guid actor) => new FinancePeriodCarryForwardPortV1(database,
            company, new User(actor, company.CompanyId), new Authority(company.CompanyId), new FinancePermissions(), audit,
            auditOwner);

    public static async Task PrepareAsync(SqlServerRelationalApiFactory factory, Guid companyId, Guid periodId, Guid actor)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
        var company = services.GetRequiredService<ICompanyContext>(); company.Set(companyId);
        await services.GetRequiredService<FinanceDbContext>().Database.MigrateAsync();
        var database = services.GetRequiredService<AccountingDbContext>();
        var period = await database.Periods.AsNoTracking().SingleAsync(x => x.Id == periodId);
        var target = await database.Periods.AsNoTracking().Where(x => x.StartsOn > period.EndsOn).OrderBy(x => x.StartsOn).FirstOrDefaultAsync();
        if (target is not null) { Assert.Equal((int)AccountingPeriodStatus.Open, target.Status); return; }
        // Explicit synthetic target dates only; the production close never creates/selects dates.
        var starts = period.EndsOn.AddDays(1); var ends = starts.AddMonths(1).AddDays(-1); var preparer = Guid.NewGuid();
        var store = services.GetRequiredService<IAccountingPeriodStore>();
        var (proposed, _) = await store.ProposePeriodAsync(Guid.NewGuid(), starts, ends, preparer,
            Metadata(preparer, AccountingPermissionManifestV1.PeriodsPropose));
        await store.ApprovePeriodAsync(proposed.Period.Id, proposed.Version, actor, Metadata(actor, AccountingPermissionManifestV1.PeriodsApprove));
    }

    public static async Task<AccountingPeriodRecord> CloseAsync(SqlServerRelationalApiFactory factory, Guid companyId,
        Guid periodId, long expectedVersion, Guid actor, AccountingCommandMetadata metadata)
    {
        await PrepareAsync(factory, companyId, periodId, actor);
        await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
        var company = services.GetRequiredService<ICompanyContext>(); company.Set(companyId);
        var store = Store(services, factory.Services, actor);
        return await store.ClosePeriodAsync(periodId, expectedVersion, actor,
            metadata with { ActorId = actor.ToString("D"), PermissionCode = AccountingPermissionManifestV1.PeriodsClose });
    }
    public static AccountingStore Store(IServiceProvider services, IServiceProvider root, Guid actor)
    {
        var company = services.GetRequiredService<ICompanyContext>();
        var database = services.GetRequiredService<AccountingDbContext>();
        var audit = (AuditEventCollector)services.GetRequiredService<IAuditEventCollector>();
        return new AccountingStore(database, company, audit, TimeProvider.System,
            new AccountingAmountScaleVerifier(services.GetRequiredService<ISettingsPrerequisiteProvider>()),
            Port(database, company, audit, services.GetRequiredService<IAuditTransactionWriterV1>(), actor));
    }
    private static AccountingCommandMetadata Metadata(Guid actor, string permission) => new(actor.ToString("D"), permission,
        [], "SENTETIK period carry setup", null, Guid.NewGuid().ToString("D"), "Web");
    private sealed class FinancePermissions : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId, CancellationToken cancellationToken) =>
            Task.FromResult(PermissionDecision.Allowed);
    }
    private sealed class User(Guid actor, Guid company) : ICurrentUser
    {
        public string? UserId => actor.ToString("D"); public bool IsAuthenticated => true;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => companyId == company;
    }
    private sealed class Authority(Guid company) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(companyId == company && permission is AccountingPermissionManifestV1.PeriodsClose or AccountingPermissionManifestV1.PeriodsApprove
                ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) : PermissionScope.Denied);
    }
}
