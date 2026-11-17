using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Quantities;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Quantity = GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Purchasing.Domain.Quantities.PurchasingQuantityRefusals>;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// BS-1 (F-011) on real SQL Server: the invoice cost rate read port finds a recorded goods receipt line (the acceptance share) under
/// its company, names the order and line it counts on and echoes the order's recorded currency (none for an order without a money
/// snapshot), and answers Unavailable with <c>purchase_invoice_not_recorded</c> — no supplier invoice is recorded yet and BS-1 allows
/// no provisional rate. Company B's context sees company A's receipt as NotFound; asking for company A from company B's context is
/// refused; an unknown receipt is NotFound; reading writes nothing. SENTETIK owners.
/// </summary>
public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    private static readonly Guid InvoiceCostRateCurrency = Guid.Parse("a2a10000-0000-4000-8000-0000000000b1");
    private static readonly Guid InvoiceCostRateProfile = Guid.Parse("a2a10000-0000-4000-8000-0000000000b2");

    [SqlServerFact]
    public async Task Invoice_cost_rate_reads_every_recorded_receipt_unavailable_until_invoiced_and_another_company_not_found_without_writing()
    {
        await using Run run = await Run.StartAsync();
        var day = DateOnly.FromDateTime(AcceptanceTime.UtcDateTime);

        // A foreign-currency order: the receipt names its order line and the order currency, and its rate is Unavailable (BS-1).
        var (usdOrder, usdLine, usdVersion) = await InvoiceCostRateOrderAsync(run,
            PurchaseOrderMoney.Of(InvoiceCostRateCurrency, "USD", InvoiceCostRateProfile, 1, 2), day);
        Guid usdReceipt = Guid.NewGuid();
        PurchaseAcceptanceStageResultV1 usdAccepted = await StageAndCommitAsync(run, CompanyA,
            AcceptanceRequest(new AcceptanceSeed(usdOrder, usdLine, AcceptanceUnit, usdVersion), usdReceipt, "4"),
            Envelope($"invoice-rate-accept-{usdOrder:N}", $"correlation-invoice-rate-accept-{usdOrder:N}"));
        Assert.Equal(usdReceipt, usdAccepted.AcceptanceId);

        // An order without a money snapshot: the receipt is still found and Unavailable, and no currency is named.
        var (plainOrder, plainLine, plainVersion) = await InvoiceCostRateOrderAsync(run, null, day);
        Guid plainReceipt = Guid.NewGuid();
        await StageAndCommitAsync(run, CompanyA,
            AcceptanceRequest(new AcceptanceSeed(plainOrder, plainLine, AcceptanceUnit, plainVersion), plainReceipt, "2"),
            Envelope($"invoice-rate-accept-{plainOrder:N}", $"correlation-invoice-rate-accept-{plainOrder:N}"));

        var before = await InvoiceCostRateWriteCountsAsync(run);

        var usd = await ReadInvoiceCostRateAsync(run, CompanyA, CompanyA, usdReceipt);
        Assert.Equal(PurchaseInvoiceCostRateOutcomeV1.Unavailable, usd.Outcome);
        Assert.Equal((CompanyA, usdReceipt, (Guid?)usdOrder, (Guid?)usdLine, (Guid?)InvoiceCostRateCurrency, "USD"),
            (usd.CompanyId, usd.GoodsReceiptLineId, usd.PurchaseOrderId, usd.PurchaseOrderLineId, usd.OrderCurrencyId, usd.OrderCurrencyCode));
        Assert.Null(usd.Rate);
        Assert.Equal(PurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded, usd.UnavailableReason);

        var plain = await ReadInvoiceCostRateAsync(run, CompanyA, CompanyA, plainReceipt);
        Assert.Equal((PurchaseInvoiceCostRateOutcomeV1.Unavailable, (Guid?)plainOrder, (Guid?)plainLine),
            (plain.Outcome, plain.PurchaseOrderId, plain.PurchaseOrderLineId));
        Assert.Null(plain.OrderCurrencyId);
        Assert.Null(plain.OrderCurrencyCode);
        Assert.Null(plain.Rate);
        Assert.Equal(PurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded, plain.UnavailableReason);

        // Company B sees none of company A's receipts: NotFound, never 403, and nothing of A is echoed.
        var foreign = await ReadInvoiceCostRateAsync(run, CompanyB, CompanyB, usdReceipt);
        Assert.Equal((PurchaseInvoiceCostRateOutcomeV1.NotFound, CompanyB, usdReceipt), (foreign.Outcome, foreign.CompanyId, foreign.GoodsReceiptLineId));
        Assert.Null(foreign.PurchaseOrderId);
        Assert.Null(foreign.OrderCurrencyId);

        // Asking for company A from company B's context is refused before anything is read.
        await Assert.ThrowsAsync<InvalidOperationException>(() => ReadInvoiceCostRateAsync(run, CompanyB, CompanyA, usdReceipt));

        // An unknown receipt, or an order line's identity asked as a receipt, is NotFound.
        Assert.Equal(PurchaseInvoiceCostRateOutcomeV1.NotFound, (await ReadInvoiceCostRateAsync(run, CompanyA, CompanyA, Guid.NewGuid())).Outcome);
        Assert.Equal(PurchaseInvoiceCostRateOutcomeV1.NotFound, (await ReadInvoiceCostRateAsync(run, CompanyA, CompanyA, usdLine)).Outcome);

        // Reading creates nothing: no attempt, no audit event, no share and no version moved.
        Assert.Equal(before, await InvoiceCostRateWriteCountsAsync(run));
    }

    /// <summary>An approved, unbound order of company A in <paramref name="money"/>'s currency (null: none), with an explicitly optional line price.</summary>
    private static async Task<(Guid Order, Guid Line, long Version)> InvoiceCostRateOrderAsync(Run run, PurchaseOrderMoney? money, DateOnly effectiveOn,
        PurchaseOrderLinePrice? price = null)
    {
        Guid orderId = Guid.NewGuid();
        Guid lineId = Guid.NewGuid();
        await using StoreScope scope = run.Store(CompanyA);
        PurchaseOrder order = PurchaseOrder.Create(orderId, CompanyA, AcceptanceSupplier, money, AcceptanceTime);
        order.AddLine(order.Version, lineId, AcceptanceProduct, AcceptanceUnit, Quantity.Of(10m, QuantityUnit.Kilogram), sourceRequestLineId: null,
            price, AcceptanceTime);
        string createKey = $"invoice-rate-create-{orderId:N}";
        await scope.Store.StoreOrderAsync(run.Envelope(createKey), Hash(createKey), AcceptanceAccount, effectiveOn, () => order,
            new Dictionary<Guid, string> { [lineId] = "product-v1" });
        string approveKey = $"invoice-rate-approve-{orderId:N}";
        var approved = await scope.Store.MutateOrderAsync(run.Envelope(approveKey), Hash(approveKey), PurchasingCommands.OrderApproveV1, orderId,
            (candidate, now) => candidate.Approve(candidate.Version, OrderApprover, now), Approved);
        return (orderId, lineId, approved!.Order.Order.Version);
    }

    private static async Task<PurchaseInvoiceCostRateAnswerV1> ReadInvoiceCostRateAsync(Run run, Guid contextCompany, Guid askedCompany, Guid receipt)
    {
        var companyContext = new CompanyContext();
        companyContext.Set(contextCompany);
        await using var context = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema))
            .Options, companyContext);
        IPurchaseInvoiceCostRateReadPortV1 port = new PurchaseInvoiceCostRateReadPort(context, companyContext);
        return await port.GetAsync(askedCompany, receipt);
    }

    private static async Task<(int Attempts, int AuditEvents, int Shares, long Versions)> InvoiceCostRateWriteCountsAsync(Run run) => (
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts"),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAcceptanceShares"),
        await run.ScalarAsync<long>("SELECT COALESCE(SUM(Version), 0) FROM purchasing.PurchaseOrders"));
}
