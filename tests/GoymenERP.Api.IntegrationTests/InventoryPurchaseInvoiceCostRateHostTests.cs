using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// BS-1 / SAT-D295 (STK-S25): the host adapts the Purchasing-owned invoice cost rate port to Inventory's source. A recorded
/// invoice rate, a not-yet-recorded invoice and an unknown receipt map one to one; every owner failure other than the caller's
/// own cancellation, and every answer that does not echo the asked company and receipt, reads as Unavailable/unreadable, never a
/// rate. SENTETIK owners.
/// </summary>
public sealed class InventoryPurchaseInvoiceCostRateHostTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherCompany = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid Receipt = Guid.Parse("78000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherReceipt = Guid.Parse("78000000-0000-0000-0000-000000000002");
    private static readonly Guid Order = Guid.Parse("78000000-0000-0000-0000-000000000011");
    private static readonly Guid OrderLine = Guid.Parse("78000000-0000-0000-0000-000000000012");
    private static readonly Guid Usd = Guid.Parse("78000000-0000-0000-0000-000000000021");
    private static readonly Guid Lira = SettingsCurrencyIdentitiesV1.TurkishLira;

    [Fact]
    public async Task A_recorded_invoice_rate_is_carried_with_its_order_line_and_converts_the_foreign_cost()
    {
        var rate = ExchangeRate.Of(Usd, Lira, 34.5m);
        var owner = new StubOwner
        {
            Answer = PurchaseInvoiceCostRateAnswerV1.Rated(Company, Receipt, Order, OrderLine, Usd, "USD", rate),
        };

        var answer = await new InventoryPurchaseInvoiceCostRateHost(owner).ReadAsync(Company, Receipt);

        Assert.Equal((InventoryPurchaseInvoiceCostRateStatusV1.Rate, Company, Receipt, (Guid?)Order, (Guid?)OrderLine, (Guid?)Usd),
            (answer.Status, answer.CompanyId, answer.GoodsReceiptLineId, answer.PurchaseOrderId, answer.PurchaseOrderLineId, answer.OrderCurrencyId));
        Assert.Equal(rate, answer.Rate);
        Assert.Null(answer.UnavailableReason);
        Assert.Equal(((Guid?)Company, (Guid?)Receipt), (owner.AskedCompany, owner.AskedReceipt));

        var resolved = InventoryAverageCostFxRateResolverV1.Resolve(Company, Receipt, Usd, 2m, answer);
        Assert.Equal((InventoryAverageCostFxRateStatusV1.Converted, (decimal?)69m), (resolved.Status, resolved.BaseUnitCostPerStockKg));
    }

    [Fact]
    public async Task A_receipt_whose_invoice_is_not_recorded_is_unavailable_with_the_owners_reason()
    {
        var owner = new StubOwner
        {
            Answer = PurchaseInvoiceCostRateAnswerV1.Unavailable(Company, Receipt, Order, OrderLine, Usd, "USD",
                PurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded),
        };

        var answer = await new InventoryPurchaseInvoiceCostRateHost(owner).ReadAsync(Company, Receipt);

        Assert.Equal((InventoryPurchaseInvoiceCostRateStatusV1.Unavailable, (Guid?)Order, (Guid?)OrderLine, (Guid?)Usd,
                InventoryPurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded),
            (answer.Status, answer.PurchaseOrderId, answer.PurchaseOrderLineId, answer.OrderCurrencyId, answer.UnavailableReason));
        Assert.Null(answer.Rate);

        var resolved = InventoryAverageCostFxRateResolverV1.Resolve(Company, Receipt, Usd, 2m, answer);
        Assert.Equal((InventoryAverageCostFxRateStatusV1.Unavailable, InventoryAverageCostFxRateUnavailableCodesV1.InvoiceNotRecorded),
            (resolved.Status, resolved.UnavailableCode));
    }

    [Fact]
    public async Task An_order_without_a_money_snapshot_is_unavailable_and_names_no_currency()
    {
        var owner = new StubOwner
        {
            Answer = PurchaseInvoiceCostRateAnswerV1.Unavailable(Company, Receipt, Order, OrderLine, null, null,
                PurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded),
        };

        var answer = await new InventoryPurchaseInvoiceCostRateHost(owner).ReadAsync(Company, Receipt);

        Assert.Equal((InventoryPurchaseInvoiceCostRateStatusV1.Unavailable, InventoryPurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded),
            (answer.Status, answer.UnavailableReason));
        Assert.Null(answer.OrderCurrencyId);
    }

    [Fact]
    public async Task An_unknown_receipt_stays_not_found_and_is_not_turned_into_unavailable()
    {
        var owner = new StubOwner { Answer = PurchaseInvoiceCostRateAnswerV1.NotFound(Company, Receipt) };

        var answer = await new InventoryPurchaseInvoiceCostRateHost(owner).ReadAsync(Company, Receipt);

        Assert.Equal((InventoryPurchaseInvoiceCostRateStatusV1.NotFound, Company, Receipt), (answer.Status, answer.CompanyId, answer.GoodsReceiptLineId));
        Assert.Null(answer.PurchaseOrderId);
        Assert.Null(answer.UnavailableReason);
    }

    public static TheoryData<string> UntrustedAnswers => new()
    {
        "owner-refuses-company-context",
        "owner-store-failure",
        "owner-times-out",
        "another-company-echoed",
        "another-receipt-echoed",
    };

    [Theory]
    [MemberData(nameof(UntrustedAnswers))]
    public async Task An_owner_failure_or_an_answer_for_another_identity_reads_unreadable_never_a_rate(string failure)
    {
        var rate = ExchangeRate.Of(Usd, Lira, 34.5m);
        var owner = failure switch
        {
            "owner-refuses-company-context" => new StubOwner { Failure = new InvalidOperationException("SENTETIK refused context") },
            "owner-store-failure" => new StubOwner { Failure = new TimeoutException("SENTETIK store failure") },
            "owner-times-out" => new StubOwner { Failure = new OperationCanceledException("SENTETIK owner timeout") },
            "another-company-echoed" => new StubOwner
            {
                Answer = PurchaseInvoiceCostRateAnswerV1.Rated(OtherCompany, Receipt, Order, OrderLine, Usd, "USD", rate),
            },
            "another-receipt-echoed" => new StubOwner
            {
                Answer = PurchaseInvoiceCostRateAnswerV1.Rated(Company, OtherReceipt, Order, OrderLine, Usd, "USD", rate),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };

        var answer = await new InventoryPurchaseInvoiceCostRateHost(owner).ReadAsync(Company, Receipt);

        Assert.Equal((InventoryPurchaseInvoiceCostRateStatusV1.Unavailable, Company, Receipt, InventoryPurchaseInvoiceCostRateReasonsV1.Unreadable),
            (answer.Status, answer.CompanyId, answer.GoodsReceiptLineId, answer.UnavailableReason));
        Assert.Null(answer.Rate);
        Assert.Null(answer.PurchaseOrderId);
        Assert.Null(answer.OrderCurrencyId);

        var resolved = InventoryAverageCostFxRateResolverV1.Resolve(Company, Receipt, Usd, 2m, answer);
        Assert.Equal((InventoryAverageCostFxRateStatusV1.Unavailable, InventoryAverageCostFxRateUnavailableCodesV1.RateUnreadable),
            (resolved.Status, resolved.UnavailableCode));
    }

    [Fact]
    public async Task The_callers_own_cancellation_is_not_swallowed()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var owner = new StubOwner { Answer = PurchaseInvoiceCostRateAnswerV1.NotFound(Company, Receipt) };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new InventoryPurchaseInvoiceCostRateHost(owner).ReadAsync(Company, Receipt, cancellation.Token));
    }

    [Fact]
    public async Task An_empty_company_is_refused_before_the_owner_is_asked()
    {
        var owner = new StubOwner { Answer = PurchaseInvoiceCostRateAnswerV1.NotFound(Company, Receipt) };

        await Assert.ThrowsAsync<ArgumentException>(() => new InventoryPurchaseInvoiceCostRateHost(owner).ReadAsync(Guid.Empty, Receipt));
        Assert.Equal(0, owner.Calls);
    }

    private sealed class StubOwner : IPurchaseInvoiceCostRateReadPortV1
    {
        public PurchaseInvoiceCostRateAnswerV1? Answer { get; init; }

        public Exception? Failure { get; init; }

        public Guid? AskedCompany { get; private set; }

        public Guid? AskedReceipt { get; private set; }

        public int Calls { get; private set; }

        public Task<PurchaseInvoiceCostRateAnswerV1> GetAsync(Guid companyId, Guid goodsReceiptLineId, CancellationToken cancellationToken = default)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            AskedCompany = companyId;
            AskedReceipt = goodsReceiptLineId;
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(Answer ?? throw new InvalidOperationException("SENTETIK stub without an answer."));
        }
    }
}
