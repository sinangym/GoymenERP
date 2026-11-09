using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// A2a-1c (ALS-D72) on real SQL Server: the order's money snapshot carries the owner's unit-price scale, a four-decimal unit price is
/// stored in decimal(24,6) and rebuilt through the domain with it, the check keeps the scale beside a currency and within 0..28, a
/// legacy priced order (no scale) still rebuilds, and the migration's Down refuses (51170) while a scale exists. SENTETIK owners.
/// </summary>
public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_four_decimal_unit_price_is_stored_with_the_unit_price_scale_and_rebuilt_and_the_check_keeps_the_scale_in_bounds()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        Guid orderId;
        await using (StoreScope a = run.Store(CompanyA))
        {
            PurchaseOrder built = PricedOrderAtUnitScale(owners, new PurchaseOrderLinePrice(41.2575m, KgUnit), 1250.5m, 4);
            orderId = (await a.Store.StoreOrderAsync(run.Envelope("k-scale-4"), Hash("scale-4"), owners.Account, Day, () => built, VersionsOf(built)))
                .Order.Order.Id;
        }

        Assert.Equal((4, 2, 41.2575m), (
            await run.ScalarAsync<int>("SELECT UnitPriceScale FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", orderId)),
            await run.ScalarAsync<int>("SELECT MoneyScale FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", orderId)),
            await run.ScalarAsync<decimal>("SELECT UnitPrice FROM purchasing.PurchaseOrderLines WHERE OrderId = @id", ("@id", orderId))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'purchasing.purchase-order.created.v1' AND EntityId = @id "
            + "AND AfterJson LIKE N'%\"unitPriceScale\":4%' AND AfterJson LIKE N'%\"unitPrice\":41.2575%'", ("@id", orderId.ToString("D"))));

        await using (StoreScope a = run.Store(CompanyA))
        {
            var rebuilt = (await a.Store.FindOrderAsync(orderId))!.Order;
            Assert.Equal(((int?)4, 4, 41.2575m), (rebuilt.Money!.UnitPriceScale, rebuilt.Money.PriceScale, rebuilt.Lines[0].Price!.UnitPrice));
            // 1250.5 x 41.2575 = 51592.50375 -> 51592.50, once (SAT-D11).
            Assert.Equal(51592.50m, rebuilt.Lines[0].Price!.LineAmount(rebuilt.Lines[0].Quantity.Value, rebuilt.Money));
        }

        foreach (var sql in new[]
                 {
                     "UPDATE purchasing.PurchaseOrders SET UnitPriceScale = -1 WHERE Id = @id",
                     "UPDATE purchasing.PurchaseOrders SET UnitPriceScale = 29 WHERE Id = @id",
                 })
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(sql, ("@id", orderId)));
            Assert.Equal(547, refused.Number);
            Assert.Contains("CK_PurchaseOrders_UnitPriceScale", refused.Message, StringComparison.Ordinal);
        }

        // A scale without a money snapshot is refused (an unpriced order has none).
        Guid unpricedId;
        await using (StoreScope a = run.Store(CompanyA))
        {
            PurchaseOrder unpriced = Order(owners, null, null, 5m);
            unpricedId = (await a.Store.StoreOrderAsync(run.Envelope("k-scale-unpriced"), Hash("scale-unpriced"), owners.Account, Day, () => unpriced,
                VersionsOf(unpriced))).Order.Order.Id;
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrders WHERE Id = @id AND UnitPriceScale IS NOT NULL",
            ("@id", unpricedId)));
        var orphan = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE purchasing.PurchaseOrders SET UnitPriceScale = 4 WHERE Id = @id", ("@id", unpricedId)));
        Assert.Equal(547, orphan.Number);
        Assert.Contains("CK_PurchaseOrders_UnitPriceScale", orphan.Message, StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task The_unit_price_scale_migrations_Down_passes_with_only_legacy_priced_orders_and_refuses_while_a_scale_exists()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        string guardSql = Assert.IsType<SqlOperation>(new AddPurchaseOrderUnitPriceScale().DownOperations[0]).Sql;
        Guid legacyId;
        await using (StoreScope a = run.Store(CompanyA))
        {
            // A legacy (A2a-1) priced order: a currency, a two-decimal price, no unit-price scale.
            PurchaseOrder legacy = PricedOrderAtUnitScale(owners, new PurchaseOrderLinePrice(10.25m, KgUnit), 5m, null);
            legacyId = (await a.Store.StoreOrderAsync(run.Envelope("k-scale-legacy"), Hash("scale-legacy"), owners.Account, Day, () => legacy,
                VersionsOf(legacy))).Order.Order.Id;
            Assert.Null((await a.Store.FindOrderAsync(legacyId))!.Order.Money!.UnitPriceScale);
        }

        await run.ExecuteAsync(guardSql);

        await using (StoreScope a = run.Store(CompanyA))
        {
            PurchaseOrder scaled = PricedOrderAtUnitScale(owners, new PurchaseOrderLinePrice(10.1234m, KgUnit), 5m, 4);
            await a.Store.StoreOrderAsync(run.Envelope("k-scale-down"), Hash("scale-down"), owners.Account, Day, () => scaled, VersionsOf(scaled));
        }

        var guard = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guardSql));
        Assert.Equal(51170, guard.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrders WHERE UnitPriceScale = 4"));
    }

    /// <summary>An unbound draft of company A in TRY (amount scale 2) with the given unit-price scale and one priced kilogram line.</summary>
    private static PurchaseOrder PricedOrderAtUnitScale(Owners owners, PurchaseOrderLinePrice price, decimal quantity, int? unitPriceScale)
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), CompanyA, owners.Party,
            PurchaseOrderMoney.Of(PriceCurrency, "TRY", PriceProfile, 1, 2, unitPriceScale), T0);
        order.AddLine(order.Version, Guid.NewGuid(), Product, KgUnit, Kg(quantity), null, price, T0);
        return order;
    }
}
