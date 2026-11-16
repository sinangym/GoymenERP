using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Quantities;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Quantity = GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Purchasing.Domain.Quantities.PurchasingQuantityRefusals>;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D295 with BS-1 on real SQL Server: the order line cost read port answers a Draft order's line
/// <c>purchase_order_not_approved</c> with the order version; once approved, a priced general line is Known with its recorded unit
/// price in the order currency at the snapshot's price scale, its discount and maturity surcharge as stored and the shared
/// calculator's exact discounted and net unit prices, the discounted one answered as the moving-average unit cost (ALS-D104,
/// SAT-D386); an unpriced line is <c>purchase_order_line_price_pending</c>; a technical
/// slice answers its general line's price (naming that line) and inherits its pending state. Company B's context sees company A's
/// line as NotFound; asking for company A from company B's context is refused; an unknown line or order is NotFound; reading writes
/// nothing. SENTETIK owners.
/// </summary>
public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    private static readonly Guid OrderLineCostCurrency = Guid.Parse("a2a10000-0000-4000-8000-0000000000c1");
    private static readonly Guid OrderLineCostProfile = Guid.Parse("a2a10000-0000-4000-8000-0000000000c2");
    private static readonly Guid OrderLineCostTechnicalProduct = Guid.Parse("74000000-0000-0000-0000-000000000029");

    [SqlServerFact]
    public async Task Order_line_cost_answers_the_recorded_price_of_an_approved_line_and_its_technical_slice_and_another_company_not_found_without_writing()
    {
        await using Run run = await Run.StartAsync();
        var day = DateOnly.FromDateTime(AcceptanceTime.UtcDateTime);
        Guid orderId = Guid.NewGuid();
        Guid pricedLine = Guid.NewGuid();
        Guid unpricedLine = Guid.NewGuid();

        // A USD order at a 4-decimal unit-price scale: one line at 12.3456 with a 10 % discount and a 2 % maturity surcharge, one unpriced.
        long draftVersion;
        await using (StoreScope scope = run.Store(CompanyA))
        {
            PurchaseOrder order = PurchaseOrder.Create(orderId, CompanyA, AcceptanceSupplier,
                PurchaseOrderMoney.Of(OrderLineCostCurrency, "USD", OrderLineCostProfile, 1, 2, 4), AcceptanceTime);
            order.AddLine(order.Version, pricedLine, AcceptanceProduct, AcceptanceUnit, Quantity.Of(10m, QuantityUnit.Kilogram),
                sourceRequestLineId: null, new PurchaseOrderLinePrice(12.3456m, AcceptanceUnit, 10m, null, 2m), AcceptanceTime);
            order.AddLine(order.Version, unpricedLine, AcceptanceProduct, AcceptanceUnit, Quantity.Of(5m, QuantityUnit.Kilogram),
                sourceRequestLineId: null, AcceptanceTime);
            string createKey = $"order-line-cost-create-{orderId:N}";
            var stored = await scope.Store.StoreOrderAsync(run.Envelope(createKey), Hash(createKey), AcceptanceAccount, day, () => order,
                new Dictionary<Guid, string> { [pricedLine] = "product-v1", [unpricedLine] = "product-v1" });
            draftVersion = stored.Order.Order.Version;
        }

        // A Draft is not an approved commitment: fail-closed, with the version it was read at.
        var draft = await ReadOrderLineCostAsync(run, CompanyA, CompanyA, orderId, pricedLine);
        Assert.Equal((PurchaseOrderLineCostOutcomeV1.Unreadable, PurchaseOrderLineCostReasonsV1.OrderNotApproved, (long?)draftVersion),
            (draft.Outcome, draft.UnreadableReason, draft.OrderVersion));
        Assert.Null(draft.Price);

        long approvedVersion;
        await using (StoreScope scope = run.Store(CompanyA))
        {
            string approveKey = $"order-line-cost-approve-{orderId:N}";
            var approved = await scope.Store.MutateOrderAsync(run.Envelope(approveKey), Hash(approveKey), PurchasingCommands.OrderApproveV1,
                orderId, (candidate, now) => candidate.Approve(candidate.Version, OrderApprover, now), Approved);
            approvedVersion = approved!.Order.Order.Version;
        }

        Guid pricedSlice = await AddOrderLineCostTechnicalSliceAsync(run, orderId, pricedLine, lineNo: 3, slice: 4m);
        Guid unpricedSlice = await AddOrderLineCostTechnicalSliceAsync(run, orderId, unpricedLine, lineNo: 4, slice: 2m);

        var before = await InvoiceCostRateWriteCountsAsync(run);

        var known = await ReadOrderLineCostAsync(run, CompanyA, CompanyA, orderId, pricedLine);
        Assert.Equal(PurchaseOrderLineCostOutcomeV1.Known, known.Outcome);
        Assert.Equal((CompanyA, orderId, pricedLine, (long?)approvedVersion),
            (known.CompanyId, known.PurchaseOrderId, known.PurchaseOrderLineId, known.OrderVersion));
        Assert.Null(known.UnreadableReason);
        var price = Assert.IsType<PurchaseOrderLineCostPriceV1>(known.Price);
        Assert.Equal((pricedLine, AcceptanceProduct, AcceptanceUnit, AcceptanceUnit, true),
            (price.PricedLineId, price.ProductId, price.LineUnitId, price.PriceUnitId, price.PriceUnitIsKilogram));
        Assert.Equal((12.3456m, OrderLineCostCurrency, 4, "USD"),
            (price.UnitPrice.Amount, price.UnitPrice.CurrencyId, price.UnitPrice.Scale, price.CurrencyCode));
        Assert.Equal(((decimal?)10m, (decimal?)null, (decimal?)2m, (decimal?)null),
            (price.DiscountPercent, price.DiscountAmount, price.DeferralPercent, price.DeferralAmount));
        Assert.Equal((11.11104m, 11.3332608m), (price.DiscountedUnitPrice, price.NetUnitPrice));
        // ALS-D104 / SAT-D386: the moving average takes the discounted price; the maturity surcharge stays out of the cost.
        Assert.Equal(11.11104m, price.MovingAverageUnitCost);

        var pending = await ReadOrderLineCostAsync(run, CompanyA, CompanyA, orderId, unpricedLine);
        Assert.Equal((PurchaseOrderLineCostOutcomeV1.Unreadable, PurchaseOrderLineCostReasonsV1.PricePending, (long?)approvedVersion),
            (pending.Outcome, pending.UnreadableReason, pending.OrderVersion));
        Assert.Null(pending.Price);

        // A technical slice carries no price of its own: it answers its general line's price and names that line (A2a-1).
        var slice = await ReadOrderLineCostAsync(run, CompanyA, CompanyA, orderId, pricedSlice);
        Assert.Equal((PurchaseOrderLineCostOutcomeV1.Known, pricedSlice), (slice.Outcome, slice.PurchaseOrderLineId));
        var slicePrice = Assert.IsType<PurchaseOrderLineCostPriceV1>(slice.Price);
        Assert.Equal((pricedLine, OrderLineCostTechnicalProduct, AcceptanceUnit), (slicePrice.PricedLineId, slicePrice.ProductId,
            slicePrice.LineUnitId));
        Assert.Equal((12.3456m, 11.3332608m), (slicePrice.UnitPrice.Amount, slicePrice.NetUnitPrice));

        var pendingSlice = await ReadOrderLineCostAsync(run, CompanyA, CompanyA, orderId, unpricedSlice);
        Assert.Equal((PurchaseOrderLineCostOutcomeV1.Unreadable, PurchaseOrderLineCostReasonsV1.PricePending),
            (pendingSlice.Outcome, pendingSlice.UnreadableReason));

        // Company B sees none of company A's lines: NotFound, never 403, and nothing of A is echoed.
        var foreign = await ReadOrderLineCostAsync(run, CompanyB, CompanyB, orderId, pricedLine);
        Assert.Equal((PurchaseOrderLineCostOutcomeV1.NotFound, CompanyB), (foreign.Outcome, foreign.CompanyId));
        Assert.Null(foreign.OrderVersion);
        Assert.Null(foreign.Price);

        // Asking for company A from company B's context is refused before anything is read.
        await Assert.ThrowsAsync<InvalidOperationException>(() => ReadOrderLineCostAsync(run, CompanyB, CompanyA, orderId, pricedLine));

        // An unknown line, an unknown order, or the order and line identities swapped is NotFound.
        Assert.Equal(PurchaseOrderLineCostOutcomeV1.NotFound,
            (await ReadOrderLineCostAsync(run, CompanyA, CompanyA, orderId, Guid.NewGuid())).Outcome);
        Assert.Equal(PurchaseOrderLineCostOutcomeV1.NotFound,
            (await ReadOrderLineCostAsync(run, CompanyA, CompanyA, Guid.NewGuid(), pricedLine)).Outcome);
        Assert.Equal(PurchaseOrderLineCostOutcomeV1.NotFound,
            (await ReadOrderLineCostAsync(run, CompanyA, CompanyA, pricedLine, orderId)).Outcome);

        // Reading creates nothing: no attempt, no audit event, no share and no version moved.
        Assert.Equal(before, await InvoiceCostRateWriteCountsAsync(run));
    }

    /// <summary>
    /// Writes one technical slice row under a general line and the general line's technical total, as a breakdown would leave them
    /// (no production command stores a breakdown yet): another product, the general line's unit, no price. Only reads follow.
    /// </summary>
    private static async Task<Guid> AddOrderLineCostTechnicalSliceAsync(Run run, Guid orderId, Guid generalLineId, int lineNo, decimal slice)
    {
        Guid technicalLineId = Guid.NewGuid();
        await run.ExecuteAsync(
            "INSERT INTO purchasing.PurchaseOrderLines (CompanyId, OrderId, LineNo, LineId, Kind, ParentLineId, SourceRequestLineId, ProductId, "
            + "ProductVersion, UnitId, UnitIsKilogram, Ordered, Technical, Accepted, Cancelled) "
            + "VALUES (@company, @order, @lineNo, @line, 2, @parent, NULL, @product, N'product-v1', @unit, 1, @slice, 0, 0, 0); "
            + "UPDATE purchasing.PurchaseOrderLines SET Technical = @slice WHERE CompanyId = @company AND OrderId = @order AND LineId = @parent;",
            ("@company", CompanyA), ("@order", orderId), ("@lineNo", lineNo), ("@line", technicalLineId), ("@parent", generalLineId),
            ("@product", OrderLineCostTechnicalProduct), ("@unit", AcceptanceUnit), ("@slice", slice));
        return technicalLineId;
    }

    private static async Task<PurchaseOrderLineCostAnswerV1> ReadOrderLineCostAsync(Run run, Guid contextCompany, Guid askedCompany, Guid orderId,
        Guid lineId)
    {
        var companyContext = new CompanyContext();
        companyContext.Set(contextCompany);
        await using var context = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema))
            .Options, companyContext);
        IPurchaseOrderLineCostReadPortV1 port = new PurchaseOrderLineCostReadPort(context, companyContext);
        return await port.GetAsync(askedCompany, orderId, lineId);
    }
}
