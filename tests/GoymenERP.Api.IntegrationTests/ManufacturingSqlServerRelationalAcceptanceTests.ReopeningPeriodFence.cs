using System.Data;
using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task The_real_reopening_period_fence_retains_its_lock_after_reader_disposal_until_the_shared_host_commits()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var periodId = await OpenReopeningPeriodAsync(run);
        var (_, orderId) = await run.ApprovedOrderAsync(CompanyA);
        await using (var close = run.Store(CompanyA))
            await close.Orders.CloseOrderAsync(orderId, 1, run.Actor, At, Metadata(run));

        await using (var host = GenerationHostScope.Create(run))
        {
            var company = new CompanyContext(); company.Set(CompanyA);
            var user = new ManufacturingHostCurrentUser(run.Actor, CompanyA);
            var authority = new GenerationHostAuthority(); // Only authority/actor are synthetic; both owners below are real SQL.
            await using var advisoryScope = run.Factory.Services.CreateAsyncScope();
            advisoryScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            var advisory = new AccountingPeriodStatusPort(company, user, authority,
                advisoryScope.ServiceProvider.GetRequiredService<IAccountingPeriodStore>());
            var gate = new ManufacturingReopeningAccountingPeriodStatusPortV1(host.Context, advisory, company, user, authority);
            var metadata = host.Metadata with { PermissionCode = ErpPermissions.ManufacturingOrdersReopen };
            var finalFenceCalls = 0;
            async Task<ManufacturingApiResult<object>?> Fence(DateTimeOffset closedAt, CancellationToken token)
            {
                Assert.NotNull(host.Context.Database.CurrentTransaction);
                var answer = await gate.GetStatusAsync(ManufacturingBusinessDay.DateOf(closedAt), token);
                Assert.Equal((AccountingPeriodStatusOutcomeV1.Open, periodId), (answer.Outcome, answer.PeriodId));
                finalFenceCalls++;
                // GetStatusAsync has already disposed its borrowed Accounting context. A close on a DIFFERENT real
                // connection must still be unable to write the checked period while Manufacturing has not committed.
                await using var closingScope = run.Factory.Services.CreateAsyncScope();
                closingScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
                var closingDb = closingScope.ServiceProvider.GetRequiredService<AccountingDbContext>();
                await closingDb.Database.OpenConnectionAsync(token);
                await closingDb.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 0", token);
                var closer = closingScope.ServiceProvider.GetRequiredService<AccountingStore>();
                var blocked = await Record.ExceptionAsync(() => closer.ClosePeriodAsync(periodId, 2, run.Approver,
                    ReopeningPeriodMetadata(run, run.Approver), token));
                Assert.NotNull(blocked);
                Assert.Equal(1222, Assert.IsType<SqlException>(blocked.GetBaseException()).Number);
                return null;
            }
            var reopened = await host.Host.ReopenAsync(new(CompanyA, orderId, 2, "Synthetic locked-period reopening"),
                token => host.Store.ReopenOrderAsync(orderId, 2, run.Actor, At.AddMinutes(1), "Synthetic locked-period reopening",
                    metadata, Fence, token), metadata, CancellationToken.None);
            Assert.Equal(1, finalFenceCalls); Assert.Equal(OrderStatus.Open, reopened.Status);
        }
        // Only after the host commits may the real Accounting owner close the same period.
        await using var after = run.Factory.Services.CreateAsyncScope();
        after.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        var periods = after.ServiceProvider.GetRequiredService<AccountingStore>();
        var closed = await periods.ClosePeriodAsync(periodId, 2, run.Approver, ReopeningPeriodMetadata(run, run.Approver));
        Assert.Equal("Closed", closed.Period.Status.ToString());
        await using var fresh = run.Store(CompanyA);
        Assert.Equal(OrderStatus.Open, (await fresh.Orders.FindOrderAsync(orderId))!.Status);
        Assert.Single((await fresh.Orders.FindOrderAsync(orderId))!.Reopenings);
    }

    [SqlServerFact]
    public async Task A_period_closed_by_the_real_owner_before_the_final_fence_rolls_back_reopening_history_and_inventory_work()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var periodId = await OpenReopeningPeriodAsync(run);
        var (_, orderId) = await run.ApprovedOrderAsync(CompanyA);
        await using (var close = run.Store(CompanyA))
            await close.Orders.CloseOrderAsync(orderId, 1, run.Actor, At, Metadata(run));
        await using (var closingScope = run.Factory.Services.CreateAsyncScope())
        {
            closingScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            await closingScope.ServiceProvider.GetRequiredService<AccountingStore>().ClosePeriodAsync(periodId, 2,
                run.Approver, ReopeningPeriodMetadata(run, run.Approver));
        }
        var auditsBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        await using (var host = GenerationHostScope.Create(run))
        {
            var company = new CompanyContext(); company.Set(CompanyA);
            var user = new ManufacturingHostCurrentUser(run.Actor, CompanyA); var authority = new GenerationHostAuthority();
            await using var advisoryScope = run.Factory.Services.CreateAsyncScope();
            advisoryScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            var gate = new ManufacturingReopeningAccountingPeriodStatusPortV1(host.Context,
                new AccountingPeriodStatusPort(company, user, authority, advisoryScope.ServiceProvider.GetRequiredService<IAccountingPeriodStore>()),
                company, user, authority);
            var metadata = host.Metadata with { PermissionCode = ErpPermissions.ManufacturingOrdersReopen };
            async Task<ManufacturingApiResult<object>?> Fence(DateTimeOffset closedAt, CancellationToken token)
            {
                var answer = await gate.GetStatusAsync(ManufacturingBusinessDay.DateOf(closedAt), token);
                Assert.Equal(AccountingPeriodStatusOutcomeV1.NotOpen, answer.Outcome);
                return new(ManufacturingApiStatus.Unprocessable, null, false, "manufacturing_order_reopen_period_not_open");
            }
            await Assert.ThrowsAsync<ManufacturingOrderReopenFenceExceptionV1>(() => host.Host.ReopenAsync(new(CompanyA, orderId, 2, "Synthetic closed period"),
                token => host.Store.ReopenOrderAsync(orderId, 2, run.Actor, At.AddMinutes(1), "Synthetic closed period", metadata, Fence, token),
                metadata, CancellationToken.None));
        }
        await using var fresh = run.Store(CompanyA); var order = (await fresh.Orders.FindOrderAsync(orderId))!;
        Assert.Equal((OrderStatus.Closed, 2L), (order.Status, order.Version)); Assert.Empty(order.Reopenings);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.OrderReopenings"));
        Assert.Equal(auditsBefore, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
    }

    [SqlServerFact]
    public async Task The_borrowed_period_reader_refuses_weak_transactions_and_changed_company_and_leaves_the_callers_handles_usable()
    {
        await using var run = await Run.StartAsync(); await OpenReopeningPeriodAsync(run);
        await using var manufacturing = run.Context(CompanyA);
        var company = new MutableReopeningCompany(CompanyA);
        await using (var weak = await manufacturing.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => AccountingPeriodStatusReadSession.EnlistAsync(
                manufacturing.Database.GetDbConnection(), weak.GetDbTransaction(), company));
            Assert.Same(weak, manufacturing.Database.CurrentTransaction);
            await weak.RollbackAsync();
        }
        await using var transaction = await manufacturing.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await using (var session = await AccountingPeriodStatusReadSession.EnlistAsync(manufacturing.Database.GetDbConnection(),
            transaction.GetDbTransaction(), company))
        {
            Assert.Equal(AccountingPeriodStatusOutcomeV1.Open, (await session.ReadAsync(TerminToday)).Outcome);
            company.Set(CompanyB);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.ReadAsync(TerminToday));
            company.Set(CompanyA);
            Assert.Equal(AccountingPeriodStatusOutcomeV1.Open, (await session.ReadAsync(TerminToday)).Outcome);
        }
        await using var command = manufacturing.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction(); command.CommandText = "SELECT 1";
        Assert.Equal(1, (int)(await command.ExecuteScalarAsync())!);
        Assert.Same(transaction, manufacturing.Database.CurrentTransaction); await transaction.RollbackAsync();
    }

    private static async Task<Guid> OpenReopeningPeriodAsync(Run run)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        await scope.ServiceProvider.GetRequiredService<AccountingDbContext>().Database.MigrateAsync();
        var store = scope.ServiceProvider.GetRequiredService<AccountingStore>(); var id = Guid.NewGuid();
        var (created, _) = await store.ProposePeriodAsync(id, new(2026, 9, 1), new(2026, 9, 30), run.Actor, ReopeningPeriodMetadata(run, run.Actor));
        await store.ApprovePeriodAsync(id, created.Version, run.Approver, ReopeningPeriodMetadata(run, run.Approver));
        return id;
    }
    private static AccountingCommandMetadata ReopeningPeriodMetadata(Run run, Guid actor) =>
        new(actor.ToString("D"), "synthetic.accounting.period-write", [], run.Correlation, null, Guid.NewGuid().ToString("D"), "Web");
    private sealed class MutableReopeningCompany(Guid id) : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = id;
        public void Set(Guid companyId) => CompanyId = companyId;
    }
}
