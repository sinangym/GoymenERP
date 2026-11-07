using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// A2a-1 (TEK-01, plan API-20; ALS-D5, ALS-D18) on real SQL Server: a priced order is stored in ONE save with its money snapshot
/// and its line prices, audited with them, rebuilt through the domain and approved like any other; an unpriced order keeps every
/// price column null; the checks refuse on a direct write what the domain refuses (a negative price, two discounts, a percent
/// over 100, a discount over the price, a priced technical line, half a money snapshot); company B sees nothing; and the
/// migration's Down refuses (51169) while a price exists. SENTETIK owners.
/// </summary>
public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    private static readonly Guid PriceCurrency = Guid.Parse("a2a10000-0000-4000-8000-000000000001");
    private static readonly Guid PriceProfile = Guid.Parse("a2a10000-0000-4000-8000-000000000002");

    [SqlServerFact]
    public async Task A_priced_order_is_stored_once_with_its_currency_and_prices_audited_rebuilt_and_approved()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        Guid orderId;
        Guid lineId;
        await using (StoreScope a = run.Store(CompanyA))
        {
            PurchaseOrder built = PricedOrder(owners, new PurchaseOrderLinePrice(41.25m, KgUnit, 2.5m), 1250.5m);
            lineId = built.Lines[0].Id;
            var stored = await a.Store.StoreOrderAsync(run.Envelope("k-priced"), Hash("priced"), owners.Account, Day, () => built, VersionsOf(built));
            orderId = stored.Order.Order.Id;
            var replay = await a.Store.StoreOrderAsync(run.Envelope("k-priced", correlation: "SENTETIK-second"), Hash("priced"), owners.Account, Day,
                () => throw new InvalidOperationException("a replay never builds"), VersionsOf(built));
            Assert.Equal(PurchasingWriteOutcome.Replayed, replay.Outcome);
        }

        Assert.Equal(("TRY", 2, 41.25m, 2.5m), (
            await run.ScalarAsync<string>("SELECT CurrencyCode FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", orderId)),
            await run.ScalarAsync<int>("SELECT MoneyScale FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", orderId)),
            await run.ScalarAsync<decimal>("SELECT UnitPrice FROM purchasing.PurchaseOrderLines WHERE OrderId = @id", ("@id", orderId)),
            await run.ScalarAsync<decimal>("SELECT DiscountPercent FROM purchasing.PurchaseOrderLines WHERE OrderId = @id", ("@id", orderId))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'purchasing.purchase-order.created.v1' AND EntityId = @id "
            + "AND AfterJson LIKE N'%\"unitPrice\":41.25%' AND AfterJson LIKE N'%\"currencyCode\":\"TRY\"%'", ("@id", orderId.ToString("D"))));

        await using (StoreScope a = run.Store(CompanyA))
        {
            var rebuilt = (await a.Store.FindOrderAsync(orderId))!.Order;
            Assert.Equal((PriceCurrency, "TRY", 2), (rebuilt.Money!.CurrencyId, rebuilt.Money.CurrencyCode, rebuilt.Money.Scale));
            var price = rebuilt.Lines[0].Price!;
            Assert.Equal((41.25m, 40.21875m, 50293.55m), (price.UnitPrice, price.NetUnitPrice, price.LineAmount(rebuilt.Lines[0].Quantity.Value, rebuilt.Money)));
            var approved = await a.Store.MutateOrderAsync(run.Envelope("k-priced-approve", actor: Manager), Hash("priced-approve"),
                PurchasingCommands.OrderApproveV1, orderId, (order, now) => order.Approve(2, now), Approved);
            Assert.Equal((PurchaseOrderStatus.Approved, 41.25m), (approved!.Order.Order.Status, approved.Order.Order.Lines[0].Price!.UnitPrice));
        }

        // The checks refuse on a direct write what the domain refuses (547), and the price columns never move under the write guard.
        foreach (var (sql, check) in new[]
                 {
                     ("UPDATE purchasing.PurchaseOrderLines SET UnitPrice = -1 WHERE OrderId = @id", "CK_PurchaseOrderLines_Price"),
                     ("UPDATE purchasing.PurchaseOrderLines SET DiscountAmount = 1 WHERE OrderId = @id", "CK_PurchaseOrderLines_Price"),
                     ("UPDATE purchasing.PurchaseOrderLines SET DiscountPercent = 100.5 WHERE OrderId = @id", "CK_PurchaseOrderLines_Price"),
                     ("UPDATE purchasing.PurchaseOrderLines SET DiscountPercent = NULL, DiscountAmount = 41.26 WHERE OrderId = @id", "CK_PurchaseOrderLines_Price"),
                     ("UPDATE purchasing.PurchaseOrderLines SET PriceUnitId = NULL WHERE OrderId = @id", "CK_PurchaseOrderLines_Price"),
                     ("UPDATE purchasing.PurchaseOrders SET MoneyScale = NULL WHERE Id = @id", "CK_PurchaseOrders_Money"),
                     ("UPDATE purchasing.PurchaseOrders SET CurrencyCode = N'' WHERE Id = @id", "CK_PurchaseOrders_Money"),
                 })
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(sql, ("@id", orderId)));
            Assert.Equal(547, refused.Number);
            Assert.Contains(check, refused.Message, StringComparison.Ordinal);
        }

        Assert.Equal(lineId, await run.ScalarAsync<Guid>("SELECT LineId FROM purchasing.PurchaseOrderLines WHERE OrderId = @id", ("@id", orderId)));
    }

    [SqlServerFact]
    public async Task An_unpriced_order_keeps_every_price_column_null_and_a_priced_technical_line_is_refused_by_the_check()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        Guid orderId;
        await using (StoreScope a = run.Store(CompanyA))
        {
            PurchaseOrder built = Order(owners, null, null, 5m);
            orderId = (await a.Store.StoreOrderAsync(run.Envelope("k-unpriced"), Hash("unpriced"), owners.Account, Day, () => built, VersionsOf(built)))
                .Order.Order.Id;
        }

        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.PurchaseOrders WHERE Id = @id AND (CurrencyId IS NOT NULL OR CurrencyCode IS NOT NULL OR MoneyProfileId IS NOT NULL "
            + "OR MoneyProfileVersion IS NOT NULL OR MoneyScale IS NOT NULL)", ("@id", orderId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.PurchaseOrderLines WHERE OrderId = @id AND (UnitPrice IS NOT NULL OR PriceUnitId IS NOT NULL "
            + "OR DiscountPercent IS NOT NULL OR DiscountAmount IS NOT NULL)", ("@id", orderId)));

        var technical = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE purchasing.PurchaseOrderLines SET Kind = 2, ParentLineId = LineId, UnitPrice = 1, PriceUnitId = UnitId WHERE OrderId = @id",
            ("@id", orderId)));
        Assert.Equal(547, technical.Number);
        Assert.Contains("CK_PurchaseOrderLines_Price", technical.Message, StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task An_explicit_zero_price_is_stored_apart_from_no_price_and_a_positive_discount_amount_on_it_is_refused_by_the_check()
    {
        // Sinan (#66 6704170096): an explicit 0 is a price (line amount 0), apart from no price ("fiyat bekliyor").
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        Guid orderId;
        await using (StoreScope a = run.Store(CompanyA))
        {
            PurchaseOrder built = PricedOrder(owners, new PurchaseOrderLinePrice(0m, KgUnit, 10m), 4m);
            orderId = (await a.Store.StoreOrderAsync(run.Envelope("k-zero"), Hash("zero"), owners.Account, Day, () => built, VersionsOf(built)))
                .Order.Order.Id;
            var rebuilt = (await a.Store.FindOrderAsync(orderId))!.Order;
            Assert.Equal((0m, 0m), (rebuilt.Lines[0].Price!.UnitPrice, rebuilt.Lines[0].Price!.LineAmount(rebuilt.Lines[0].Quantity.Value, rebuilt.Money!)));
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.PurchaseOrderLines WHERE OrderId = @id AND UnitPrice = 0 AND PriceUnitId IS NOT NULL", ("@id", orderId)));
        var amount = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE purchasing.PurchaseOrderLines SET DiscountPercent = NULL, DiscountAmount = 0.01 WHERE OrderId = @id", ("@id", orderId)));
        Assert.Equal(547, amount.Number);
        Assert.Contains("CK_PurchaseOrderLines_Price", amount.Message, StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task Company_B_sees_no_price_of_company_As_order()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        Guid orderId;
        await using (StoreScope a = run.Store(CompanyA))
        {
            PurchaseOrder built = PricedOrder(owners, new PurchaseOrderLinePrice(10m, KgUnit), 3m);
            orderId = (await a.Store.StoreOrderAsync(run.Envelope("k-priced-a"), Hash("priced-a"), owners.Account, Day, () => built, VersionsOf(built)))
                .Order.Order.Id;
        }

        await using (StoreScope b = run.Store(CompanyB))
        {
            Assert.Null(await b.Store.FindOrderAsync(orderId));
            Assert.Empty((await b.Store.ListOrdersAsync(10, null, null)).Items);
        }
    }

    [SqlServerFact]
    public async Task The_price_migrations_Down_passes_without_prices_and_refuses_while_a_price_exists()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        string guardSql = Assert.IsType<SqlOperation>(new AddPurchaseOrderLinePrice().DownOperations[0]).Sql;
        await using (StoreScope a = run.Store(CompanyA))
        {
            PurchaseOrder unpriced = Order(owners, null, null, 5m);
            await a.Store.StoreOrderAsync(run.Envelope("k-down-unpriced"), Hash("down-unpriced"), owners.Account, Day, () => unpriced, VersionsOf(unpriced));
        }

        // Only unpriced orders: the guard passes (and changes nothing).
        await run.ExecuteAsync(guardSql);

        await using (StoreScope a = run.Store(CompanyA))
        {
            PurchaseOrder priced = PricedOrder(owners, new PurchaseOrderLinePrice(10m, KgUnit), 5m);
            await a.Store.StoreOrderAsync(run.Envelope("k-down-priced"), Hash("down-priced"), owners.Account, Day, () => priced, VersionsOf(priced));
        }

        var guard = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guardSql));
        Assert.Equal(51169, guard.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrderLines WHERE UnitPrice IS NOT NULL"));
    }

    /// <summary>An unbound draft of company A in TRY (scale 2) with one priced kilogram line.</summary>
    private static PurchaseOrder PricedOrder(Owners owners, PurchaseOrderLinePrice price, decimal quantity)
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), CompanyA, owners.Party, PurchaseOrderMoney.Of(PriceCurrency, "TRY", PriceProfile, 1, 2),
            T0);
        order.AddLine(order.Version, Guid.NewGuid(), Product, KgUnit, Kg(quantity), null, price, T0);
        return order;
    }
}
