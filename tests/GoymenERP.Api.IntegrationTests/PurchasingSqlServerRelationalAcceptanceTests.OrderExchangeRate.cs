using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Quantities;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Quantity = GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Purchasing.Domain.Quantities.PurchasingQuantityRefusals>;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// FNS-D73 on real SQL Server: the purchase order exchange rate read port answers a Draft order
/// <c>purchase_order_not_approved</c> with its version and currency; an approved order that records no exchange rate (a legacy
/// order approved before ALC-SC33 asked for one) answers <c>purchase_order_exchange_rate_not_specified</c> with the approved version
/// and its recorded USD currency, never a substituted rate. Company B's context sees company A's order as NotFound; asking for
/// company A from company B's context is refused; an unknown order is NotFound; reading writes nothing. ALC-SC33 (FNS-D79/D80/D91):
/// an order opened with the automatic default rate, changed by the preparer before approval, answers the changed rate once approved,
/// keeps both changes in its history, rebuilds with that rate and refuses a change after approval. SENTETIK owners.
/// </summary>
public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    private static readonly Guid OrderExchangeRateCurrency = Guid.Parse("a2a10000-0000-4000-8000-0000000000d1");
    private static readonly Guid OrderExchangeRateProfile = Guid.Parse("a2a10000-0000-4000-8000-0000000000d2");

    [SqlServerFact]
    public async Task Order_exchange_rate_answers_a_draft_not_approved_an_approved_order_rate_not_specified_with_its_currency_and_another_company_not_found_without_writing()
    {
        await using Run run = await Run.StartAsync();
        var day = DateOnly.FromDateTime(AcceptanceTime.UtcDateTime);
        Guid orderId = Guid.NewGuid();
        Guid lineId = Guid.NewGuid();

        long draftVersion;
        await using (StoreScope scope = run.Store(CompanyA))
        {
            PurchaseOrder order = PurchaseOrder.Create(orderId, CompanyA, AcceptanceSupplier,
                PurchaseOrderMoney.Of(OrderExchangeRateCurrency, "USD", OrderExchangeRateProfile, 1, 2, 4), AcceptanceTime);
            order.AddLine(order.Version, lineId, AcceptanceProduct, AcceptanceUnit, Quantity.Of(10m, QuantityUnit.Kilogram),
                sourceRequestLineId: null, new PurchaseOrderLinePrice(12.3456m, AcceptanceUnit, 10m, null, 2m), AcceptanceTime);
            string createKey = $"order-exchange-rate-create-{orderId:N}";
            var stored = await scope.Store.StoreOrderAsync(run.Envelope(createKey), Hash(createKey), AcceptanceAccount, day, () => order,
                new Dictionary<Guid, string> { [lineId] = "product-v1" });
            draftVersion = stored.Order.Order.Version;
        }

        var draft = await ReadOrderExchangeRateAsync(run, CompanyA, CompanyA, orderId);
        Assert.Equal((PurchaseOrderExchangeRateOutcomeV1.Unavailable, PurchaseOrderExchangeRateReasonsV1.OrderNotApproved,
            (long?)draftVersion), (draft.Outcome, draft.UnavailableReason, draft.OrderVersion));
        Assert.Equal(((Guid?)OrderExchangeRateCurrency, "USD"), (draft.OrderCurrencyId, draft.OrderCurrencyCode));
        Assert.Null(draft.Rate);

        long approvedVersion;
        await using (StoreScope scope = run.Store(CompanyA))
        {
            string approveKey = $"order-exchange-rate-approve-{orderId:N}";
            var approved = await scope.Store.MutateOrderAsync(run.Envelope(approveKey), Hash(approveKey), PurchasingCommands.OrderApproveV1,
                orderId, (candidate, now) => candidate.Approve(candidate.Version, OrderApprover, now), Approved);
            approvedVersion = approved!.Order.Order.Version;
        }

        var before = await InvoiceCostRateWriteCountsAsync(run);

        // FNS-D73: this legacy order records no rate, so the conversion stays "hesaplanamadı"; no other rate is substituted.
        var notSpecified = await ReadOrderExchangeRateAsync(run, CompanyA, CompanyA, orderId);
        Assert.Equal((PurchaseOrderExchangeRateOutcomeV1.Unavailable, PurchaseOrderExchangeRateReasonsV1.RateNotSpecified,
            (long?)approvedVersion), (notSpecified.Outcome, notSpecified.UnavailableReason, notSpecified.OrderVersion));
        Assert.Equal((CompanyA, orderId), (notSpecified.CompanyId, notSpecified.PurchaseOrderId));
        Assert.Equal(((Guid?)OrderExchangeRateCurrency, "USD"), (notSpecified.OrderCurrencyId, notSpecified.OrderCurrencyCode));
        Assert.Null(notSpecified.Rate);

        // Company B sees none of company A's orders: NotFound, never 403, and nothing of A is echoed.
        var foreign = await ReadOrderExchangeRateAsync(run, CompanyB, CompanyB, orderId);
        Assert.Equal((PurchaseOrderExchangeRateOutcomeV1.NotFound, CompanyB), (foreign.Outcome, foreign.CompanyId));
        Assert.Null(foreign.OrderVersion);
        Assert.Null(foreign.OrderCurrencyId);
        Assert.Null(foreign.Rate);

        // Asking for company A from company B's context is refused before anything is read.
        await Assert.ThrowsAsync<InvalidOperationException>(() => ReadOrderExchangeRateAsync(run, CompanyB, CompanyA, orderId));

        // An unknown order, or a line identity in place of the order, is NotFound.
        Assert.Equal(PurchaseOrderExchangeRateOutcomeV1.NotFound,
            (await ReadOrderExchangeRateAsync(run, CompanyA, CompanyA, Guid.NewGuid())).Outcome);
        Assert.Equal(PurchaseOrderExchangeRateOutcomeV1.NotFound,
            (await ReadOrderExchangeRateAsync(run, CompanyA, CompanyA, lineId)).Outcome);

        // Reading creates nothing: no attempt, no audit event, no share and no version moved.
        Assert.Equal(before, await InvoiceCostRateWriteCountsAsync(run));
    }

    [SqlServerFact]
    public async Task Order_exchange_rate_answers_the_rate_the_preparer_changed_before_approval_keeps_the_history_and_refuses_a_change_after_approval()
    {
        await using Run run = await Run.StartAsync();
        var day = DateOnly.FromDateTime(AcceptanceTime.UtcDateTime);
        Guid lira = SettingsCurrencyIdentitiesV1.TurkishLira;
        Guid orderId = Guid.NewGuid();
        Guid lineId = Guid.NewGuid();
        var automatic = new PurchaseOrderExchangeRate(34.5m, lira, PurchaseOrderExchangeRateKind.TcmbForexSelling, day,
            PurchaseOrderExchangeRateSource.Automatic, day.AddDays(-1));
        var manual = new PurchaseOrderExchangeRate(35.25m, lira, PurchaseOrderExchangeRateKind.TcmbForexSelling, day,
            PurchaseOrderExchangeRateSource.Manual, null);
        var rateChanged = new PurchasingMutationAudit("purchasing.purchase-order.exchange-rate-changed.v1", "ChangeExchangeRate",
            "SENTETIK kur değişti");

        await using (StoreScope scope = run.Store(CompanyA))
        {
            PurchaseOrder order = PurchaseOrder.Create(orderId, CompanyA, AcceptanceSupplier,
                PurchaseOrderMoney.Of(OrderExchangeRateCurrency, "USD", OrderExchangeRateProfile, 1, 2, 4), AcceptanceTime);
            order.AssignDefaultExchangeRate(automatic, run.Actor, AcceptanceTime);
            order.AddLine(order.Version, lineId, AcceptanceProduct, AcceptanceUnit, Quantity.Of(10m, QuantityUnit.Kilogram),
                sourceRequestLineId: null, new PurchaseOrderLinePrice(12.3456m, AcceptanceUnit, 10m, null, 2m), AcceptanceTime);
            string createKey = $"order-exchange-rate-default-{orderId:N}";
            await scope.Store.StoreOrderAsync(run.Envelope(createKey), Hash(createKey), AcceptanceAccount, day, () => order,
                new Dictionary<Guid, string> { [lineId] = "product-v1" });
        }

        await using (StoreScope scope = run.Store(CompanyA))
        {
            string changeKey = $"order-exchange-rate-change-{orderId:N}";
            await scope.Store.MutateOrderAsync(run.Envelope(changeKey), Hash(changeKey), PurchasingCommands.OrderExchangeRateChangeV1,
                orderId, (candidate, now) => candidate.SetExchangeRate(candidate.Version, manual, run.Actor, "SENTETIK teyit", now),
                rateChanged);
        }

        long approvedVersion;
        await using (StoreScope scope = run.Store(CompanyA))
        {
            string approveKey = $"order-exchange-rate-rated-approve-{orderId:N}";
            var approved = await scope.Store.MutateOrderAsync(run.Envelope(approveKey), Hash(approveKey), PurchasingCommands.OrderApproveV1,
                orderId, (candidate, now) => candidate.Approve(candidate.Version, OrderApprover, now, lira), Approved);
            approvedVersion = approved!.Order.Order.Version;
        }

        // FNS-D73/D80: the approved order answers the number it recorded — the preparer's, not the automatic default.
        var rated = await ReadOrderExchangeRateAsync(run, CompanyA, CompanyA, orderId);
        Assert.Equal((PurchaseOrderExchangeRateOutcomeV1.Rate, (long?)approvedVersion), (rated.Outcome, rated.OrderVersion));
        Assert.Null(rated.UnavailableReason);
        Assert.Equal(((Guid?)OrderExchangeRateCurrency, "USD"), (rated.OrderCurrencyId, rated.OrderCurrencyCode));
        Assert.NotNull(rated.Rate);
        Assert.Equal((OrderExchangeRateCurrency, lira, 35.25m), (rated.Rate!.SourceCurrencyId, rated.Rate.TargetCurrencyId, rated.Rate.Rate));

        // FNS-D91: both the opening default and the change are kept, the change with its reason.
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.PurchaseOrderExchangeRateChanges WHERE OrderId = @order", ("@order", orderId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.PurchaseOrderExchangeRateChanges WHERE OrderId = @order AND Reason = N'SENTETIK teyit'",
            ("@order", orderId)));

        // The rebuilt order carries the changed rate; a change after approval is refused and records nothing.
        await using (StoreScope scope = run.Store(CompanyA))
        {
            var stored = await scope.Store.FindOrderAsync(orderId);
            Assert.Equal(manual, stored!.Order.ExchangeRate);
            Assert.Equal(PurchaseOrderStatus.Approved, stored.Order.Status);

            string lateKey = $"order-exchange-rate-late-{orderId:N}";
            var locked = await Assert.ThrowsAsync<PurchasingRuleException>(() => scope.Store.MutateOrderAsync(run.Envelope(lateKey),
                Hash(lateKey), PurchasingCommands.OrderExchangeRateChangeV1, orderId,
                (candidate, now) => candidate.SetExchangeRate(candidate.Version, manual, run.Actor, null, now), rateChanged));
            Assert.Equal(PurchasingRuleCodes.OrderExchangeRateLocked, locked.Code);
        }

        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.PurchaseOrderExchangeRateChanges WHERE OrderId = @order", ("@order", orderId)));
        Assert.Equal(approvedVersion, await run.ScalarAsync<long>(
            "SELECT Version FROM purchasing.PurchaseOrders WHERE Id = @order", ("@order", orderId)));
    }

    [SqlServerFact]
    public async Task The_order_exchange_rate_migrations_Down_passes_with_only_orders_without_a_rate_and_refuses_while_a_rate_exists()
    {
        await using Run run = await Run.StartAsync();
        var day = DateOnly.FromDateTime(AcceptanceTime.UtcDateTime);
        string guardSql = Assert.IsType<SqlOperation>(new AddPurchaseOrderExchangeRate().DownOperations[0]).Sql;
        await using (StoreScope scope = run.Store(CompanyA))
        {
            // A legacy USD draft recorded before ALC-SC33: no rate row.
            PurchaseOrder legacy = PurchaseOrder.Create(Guid.NewGuid(), CompanyA, AcceptanceSupplier,
                PurchaseOrderMoney.Of(OrderExchangeRateCurrency, "USD", OrderExchangeRateProfile, 1, 2, 4), AcceptanceTime);
            Guid legacyLineId = Guid.NewGuid();
            legacy.AddLine(legacy.Version, legacyLineId, AcceptanceProduct, AcceptanceUnit, Quantity.Of(10m, QuantityUnit.Kilogram),
                sourceRequestLineId: null, new PurchaseOrderLinePrice(12.3456m, AcceptanceUnit, 10m, null, 2m), AcceptanceTime);
            string legacyKey = $"order-exchange-rate-down-legacy-{legacy.Id:N}";
            await scope.Store.StoreOrderAsync(run.Envelope(legacyKey), Hash(legacyKey), AcceptanceAccount, day, () => legacy,
                new Dictionary<Guid, string> { [legacyLineId] = "product-v1" });
        }

        await run.ExecuteAsync(guardSql);

        Guid ratedId = Guid.NewGuid();
        await using (StoreScope scope = run.Store(CompanyA))
        {
            PurchaseOrder rated = PurchaseOrder.Create(ratedId, CompanyA, AcceptanceSupplier,
                PurchaseOrderMoney.Of(OrderExchangeRateCurrency, "USD", OrderExchangeRateProfile, 1, 2, 4), AcceptanceTime);
            rated.AssignDefaultExchangeRate(new PurchaseOrderExchangeRate(34.5m, SettingsCurrencyIdentitiesV1.TurkishLira,
                PurchaseOrderExchangeRateKind.TcmbForexSelling, day, PurchaseOrderExchangeRateSource.Automatic, day.AddDays(-1)),
                run.Actor, AcceptanceTime);
            Guid ratedLineId = Guid.NewGuid();
            rated.AddLine(rated.Version, ratedLineId, AcceptanceProduct, AcceptanceUnit, Quantity.Of(10m, QuantityUnit.Kilogram),
                sourceRequestLineId: null, new PurchaseOrderLinePrice(12.3456m, AcceptanceUnit, 10m, null, 2m), AcceptanceTime);
            string ratedKey = $"order-exchange-rate-down-rated-{ratedId:N}";
            await scope.Store.StoreOrderAsync(run.Envelope(ratedKey), Hash(ratedKey), AcceptanceAccount, day, () => rated,
                new Dictionary<Guid, string> { [ratedLineId] = "product-v1" });
        }

        var guard = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guardSql));
        Assert.Equal(51833, guard.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.PurchaseOrderExchangeRateChanges WHERE OrderId = @order", ("@order", ratedId)));
    }

    private static async Task<PurchaseOrderExchangeRateAnswerV1> ReadOrderExchangeRateAsync(Run run, Guid contextCompany, Guid askedCompany,
        Guid orderId)
    {
        var companyContext = new CompanyContext();
        companyContext.Set(contextCompany);
        await using var context = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema))
            .Options, companyContext);
        IPurchaseOrderExchangeRateReadPortV1 port = new PurchaseOrderExchangeRateReadPort(context, companyContext);
        return await port.GetAsync(askedCompany, orderId);
    }
}
