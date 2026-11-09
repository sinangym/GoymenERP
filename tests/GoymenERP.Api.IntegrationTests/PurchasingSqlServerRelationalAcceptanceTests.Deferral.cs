using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// A2a-2 (ALS-D38, ALS-D75) on real SQL Server: a line's maturity surcharge is stored once with its price, audited and rebuilt through the
/// domain; the check refuses on a direct write what the domain refuses (two surcharges, a percent over 100, a negative amount, a
/// surcharge on a 0 price or without a price); and the migration's Down refuses (51171) while a surcharge exists. SENTETIK owners.
/// </summary>
public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_surcharge_is_stored_once_with_its_price_audited_rebuilt_and_the_check_refuses_what_the_domain_refuses()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        Guid orderId;
        await using (StoreScope a = run.Store(CompanyA))
        {
            PurchaseOrder built = PricedOrder(owners, new PurchaseOrderLinePrice(100m, KgUnit, 10m, null, 5m), 2m);
            orderId = (await a.Store.StoreOrderAsync(run.Envelope("k-deferral"), Hash("deferral"), owners.Account, Day, () => built, VersionsOf(built)))
                .Order.Order.Id;
        }

        Assert.Equal(5m, await run.ScalarAsync<decimal>("SELECT DeferralPercent FROM purchasing.PurchaseOrderLines WHERE OrderId = @id", ("@id", orderId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'purchasing.purchase-order.created.v1' AND EntityId = @id "
            + "AND AfterJson LIKE N'%\"deferralPercent\":5%'", ("@id", orderId.ToString("D"))));
        await using (StoreScope a = run.Store(CompanyA))
        {
            var rebuilt = (await a.Store.FindOrderAsync(orderId))!.Order;
            Assert.Equal((94.5m, 189m), (rebuilt.Lines[0].Price!.NetUnitPrice, rebuilt.Lines[0].Price!.LineAmount(2m, rebuilt.Money!)));
        }

        foreach (var sql in new[]
                 {
                     "UPDATE purchasing.PurchaseOrderLines SET DeferralAmount = 1 WHERE OrderId = @id",
                     "UPDATE purchasing.PurchaseOrderLines SET DeferralPercent = 100.5 WHERE OrderId = @id",
                     "UPDATE purchasing.PurchaseOrderLines SET DeferralPercent = NULL, DeferralAmount = -1 WHERE OrderId = @id",
                     "UPDATE purchasing.PurchaseOrderLines SET UnitPrice = 0, DiscountPercent = NULL WHERE OrderId = @id",
                 })
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(sql, ("@id", orderId)));
            Assert.Equal(547, refused.Number);
            Assert.Contains("CK_PurchaseOrderLines_Deferral", refused.Message, StringComparison.Ordinal);
        }

        // A surcharge without a price is refused (the price check passes on an all-null price, the surcharge check does not).
        Guid unpricedId;
        await using (StoreScope a = run.Store(CompanyA))
        {
            PurchaseOrder unpriced = Order(owners, null, null, 5m);
            unpricedId = (await a.Store.StoreOrderAsync(run.Envelope("k-deferral-unpriced"), Hash("deferral-unpriced"), owners.Account, Day,
                () => unpriced, VersionsOf(unpriced))).Order.Order.Id;
        }

        var orphan = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE purchasing.PurchaseOrderLines SET DeferralPercent = 1 WHERE OrderId = @id", ("@id", unpricedId)));
        Assert.Equal(547, orphan.Number);
        Assert.Contains("CK_PurchaseOrderLines_Deferral", orphan.Message, StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task The_surcharge_migrations_Down_passes_with_only_plain_prices_and_refuses_while_a_surcharge_exists()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        string guardSql = Assert.IsType<SqlOperation>(new AddPurchaseOrderLineDeferral().DownOperations[0]).Sql;
        await using (StoreScope a = run.Store(CompanyA))
        {
            PurchaseOrder plain = PricedOrder(owners, new PurchaseOrderLinePrice(103.5m, KgUnit), 5m);
            await a.Store.StoreOrderAsync(run.Envelope("k-deferral-plain"), Hash("deferral-plain"), owners.Account, Day, () => plain, VersionsOf(plain));
        }

        await run.ExecuteAsync(guardSql);

        await using (StoreScope a = run.Store(CompanyA))
        {
            PurchaseOrder surcharged = PricedOrder(owners, new PurchaseOrderLinePrice(100m, KgUnit, null, null, null, 3.5m), 5m);
            await a.Store.StoreOrderAsync(run.Envelope("k-deferral-down"), Hash("deferral-down"), owners.Account, Day, () => surcharged,
                VersionsOf(surcharged));
        }

        var guard = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guardSql));
        Assert.Equal(51171, guard.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrderLines WHERE DeferralAmount IS NOT NULL"));
    }
}
