using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Future_supply_schedule_uses_latest_revision_and_subtracts_receipts_in_slice_order_without_writing()
    {
        await using Run run = await Run.StartAsync();
        var (orderId, lineId, version) = await ApprovedAcceptanceOrderAsync(run, 100m);
        var late = KgSlice(30m, ScheduleD2);
        var early = KgSlice(70m, ScheduleD1);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.MutateOrderAsync(run.Envelope("future-schedule"), Hash("future-schedule"),
                PurchasingCommands.OrderLineScheduleV1, orderId,
                (order, now) => order.SetLineDeliverySchedule(version, lineId, [late, early], run.Actor, now), LineScheduleSet);
        }

        // ALS-D81: acceptance covers stored slice 1 first, even though its date is later.
        await StageAndCommitAsync(run, CompanyA, AcceptanceRequest(new AcceptanceSeed(orderId, lineId, AcceptanceUnit, version + 1),
                Guid.NewGuid(), "40"), Envelope($"future-schedule-accept-{orderId:N}", $"future-schedule-correlation-{orderId:N}"));
        var before = await run.OrderCountsAsync(orderId);
        var read = await ReadFutureSupplyScheduleAsync(run, CompanyA, orderId, lineId);
        Assert.Equal(PurchaseFutureSupplyScheduleOutcomeV1.Found, read.Outcome);
        Assert.Equal((CompanyA, orderId, lineId, AcceptanceUnit, version + 2),
            (read.CompanyId, read.OrderId, read.LineId, read.UnitId, read.ObservedOrderVersion));
        Assert.Collection(read.Slices,
            slice => Assert.Equal((late.SliceId, 1, ScheduleD2, "30", "0"),
                (slice.SliceId, slice.SliceNo, slice.DeliveryDate, slice.QuantityText, slice.RemainingText)),
            slice => Assert.Equal((early.SliceId, 2, ScheduleD1, "70", "60"),
                (slice.SliceId, slice.SliceNo, slice.DeliveryDate, slice.QuantityText, slice.RemainingText)));
        Assert.Equal(before, await run.OrderCountsAsync(orderId));

        var foreign = await ReadFutureSupplyScheduleAsync(run, CompanyB, orderId, lineId);
        Assert.Equal(PurchaseFutureSupplyScheduleOutcomeV1.NotFound, foreign.Outcome);
        Assert.Empty(foreign.Slices);
        Assert.Equal(PurchaseFutureSupplyScheduleOutcomeV1.NotFound,
            (await ReadFutureSupplyScheduleAsync(run, CompanyA, orderId, Guid.NewGuid())).Outcome);
    }

    [SqlServerFact]
    public async Task A_future_supply_line_without_a_schedule_is_readable_and_undated_but_an_unresolved_company_is_not()
    {
        await using Run run = await Run.StartAsync();
        var (orderId, lineId, _) = await ApprovedAcceptanceOrderAsync(run, 10m);
        var read = await ReadFutureSupplyScheduleAsync(run, CompanyA, orderId, lineId);
        Assert.Equal(PurchaseFutureSupplyScheduleOutcomeV1.Found, read.Outcome);
        Assert.Empty(read.Slices);
        var unresolved = await ReadFutureSupplyScheduleAsync(run, Guid.Empty, orderId, lineId);
        Assert.Equal(PurchaseFutureSupplyScheduleOutcomeV1.NotFound, unresolved.Outcome);
        Assert.Empty(unresolved.Slices);
    }

    private static async Task<PurchaseFutureSupplyScheduleV1> ReadFutureSupplyScheduleAsync(Run run, Guid companyId, Guid orderId, Guid lineId)
    {
        var company = new CompanyContext();
        if (companyId != Guid.Empty) company.Set(companyId);
        var collector = new AuditEventCollector();
        await using var context = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options, company);
        var store = new PurchasingStore(context, company, collector, TimeProvider.System);
        return await new PurchaseFutureSupplyScheduleReadPort(store, company).GetLineScheduleAsync(orderId, lineId);
    }
}
