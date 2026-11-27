using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceExpensePaymentSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Carry_reversal_refuses_after_exact_voucher_read_before_the_next_read_or_staging_and_preserves_the_borrowed_owner_transaction()
    {
        await using var run = await Run.StartAsync(); var (period, _) = await CarryPeriods(run, includeTarget: true);
        Guid closure; Guid voucher;
        await using (var setup = OpenCarry(run, CarryChecker))
        {
            await setup.Store.ClosePeriodAsync(period, 2, CarryChecker, CarryMetadata(CarryChecker, AccountingPermissionManifestV1.PeriodsClose));
            var source = Assert.Single((await setup.Store.ReadLifecycleAsync(period, default))!.Closures);
            closure = source.Id; voucher = source.CarryVoucherId;
        }
        await using var scope = OpenCarry(run, CarryChecker); var services = scope.Scope.ServiceProvider;
        var company = services.GetRequiredService<ICompanyContext>(); var collector = services.GetRequiredService<IAuditEventCollector>();
        var writer = services.GetRequiredService<IAuditTransactionWriterV1>();
        await using var transaction = await scope.Db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var callbacks = 0;
        await using (var session = await FinancePeriodCarryForwardSession.EnlistAsync(scope.Db.Database.GetDbConnection(), transaction.GetDbTransaction(),
            company, new FixedCurrentUser(CarryChecker.ToString("D"), CompanyA), new SyntheticPermissions(), _ =>
            {
                callbacks++;
                // Enlist1, Reverse entry2, exact immutable voucher read3: deny before the duplicate read/stage.
                if (callbacks == 3) throw new UnauthorizedAccessException("SENTETIK current reopening permission/delegation revoked");
                return Task.CompletedTask;
            }, collector, writer, default))
        {
            var metadata = CarryMetadata(CarryChecker, AccountingPermissionManifestV1.PeriodsReopen);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.ReverseAsync(
                new FinanceContext(CompanyA, CarryChecker.ToString("D"), metadata.CorrelationId, null),
                new FinancePeriodCarryReversalV1(Guid.NewGuid(), CompanyA, closure, voucher, CarryChecker, DateTimeOffset.UtcNow, "SENTETIK reason"),
                new CommandMetadata(metadata.IdempotencyKey), new FinanceAuditEnvelope(metadata.PermissionCode, [], "Web", null, null), default));
        }
        Assert.Equal(3, callbacks); Assert.Same(transaction, scope.Db.Database.CurrentTransaction);
        Assert.False(scope.Db.ChangeTracker.HasChanges()); Assert.False(writer.HasPendingDrafts);
        await transaction.RollbackAsync();
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PeriodCarryReversals"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'finance.period-carry.reversed.v1'"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PeriodCarryVouchers"));
    }
}
