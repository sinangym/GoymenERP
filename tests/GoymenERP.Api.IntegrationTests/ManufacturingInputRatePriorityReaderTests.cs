using System.Reflection;
using System.Text.Json;
using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Source tests over real owner composition with synthetic port answers. Not real SQL or runtime acceptance.</summary>
public sealed class ManufacturingInputRatePriorityReaderTests
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task An_actual_plan_order_link_uses_only_that_orders_rate_and_never_falls_back_to_a_receipt(bool available)
    {
        var f = new Fixture(linked: true); f.OrderRates.Available = available;
        var result = await f.ReadAsync(); Assert.NotNull(result);
        Assert.Equal(1, f.OrderRates.Calls); Assert.Equal(0, f.Receipts.Calls);
        Assert.Equal(available ? IssueCostCurrencyStatus.Converted : IssueCostCurrencyStatus.RateMissing, result.Currency.Status);
        Assert.Equal(available ? 50m : (decimal?)null, result.Currency.ExchangeRate);
        Assert.Contains("sales-order", result.EvidenceJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unlinked_orders_input_uses_its_actual_first_receipt_weighted_rate_instead_of_the_invoice_rate()
    {
        var f = new Fixture(linked: false);
        var result = await f.ReadAsync(); Assert.NotNull(result);
        Assert.Equal(0, f.OrderRates.Calls); Assert.Equal(1, f.Receipts.Calls);
        Assert.Null(result.Currency.ExchangeRate); Assert.Equal(new ProductionExactRatioV1("100", "3"), result.Currency.ExchangeRateRatio);
        Assert.Equal(2m, result.Currency.ForeignUnitCostPerKg);
        Assert.Contains("first-receipt", result.EvidenceJson, StringComparison.Ordinal);
        Assert.Contains("synthetic-first-entry", result.EvidenceJson, StringComparison.Ordinal);
        Assert.DoesNotContain("123.4567", result.EvidenceJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("actor")] [InlineData("link-revision")] [InlineData("rate-currency")]
    public async Task Changed_authority_plan_link_or_foreign_rate_evidence_is_rejected_without_a_fallback(string fault)
    {
        var f = new Fixture(linked: true);
        f.OrderRates.BeforeAnswer = () =>
        {
            if (fault == "actor") f.User.Actor = "another-synthetic-actor";
            if (fault == "link-revision") f.ChangeLinkOnFreshRead = true;
        };
        f.OrderRates.WrongCurrency = fault == "rate-currency";
        Assert.Null(await f.ReadAsync()); Assert.Equal(0, f.Receipts.Calls);
    }

    private sealed class Fixture
    {
        public Guid CompanyId { get; } = Guid.NewGuid(); public Guid OrderId { get; } = Guid.NewGuid();
        public Guid ParentId { get; } = Guid.NewGuid(); public Guid FactId { get; } = Guid.NewGuid();
        public Guid Currency { get; } = Guid.NewGuid(); public User User { get; }
        public OrderRates OrderRates { get; } = new(); public Receipts Receipts { get; }
        public bool ChangeLinkOnFreshRead { get; set; }
        private readonly ManufacturingInputRatePriorityReaderV1 reader;
        public Fixture(bool linked)
        {
            var now = new DateTimeOffset(2026, 11, 17, 1, 0, 0, TimeSpan.Zero);
            var planId = Guid.NewGuid(); var center = Guid.NewGuid();
            var inputs = new[] { new PlannedInput(ParentId, new Kilograms(10m)) };
            var link = linked ? new PlanSalesOrderLink(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, "synthetic-actor", now) : null;
            ProductionPlan Plan(long revision) => ProductionPlan.Restore(planId, CompanyId, center, ManufacturingProcess.Slitting,
                inputs, null, Guid.NewGuid(), now, 2, salesOrderLink: link, salesOrderLinkRevision: revision);
            var plan = Plan(linked ? 1 : 0);
            var order = ProductionOrder.Restore(OrderId, planId, CompanyId, center, ManufacturingProcess.Slitting, null, inputs,
                [new InputIssue(FactId, ParentId, new Kilograms(10m), now)], [], [], [], null, null, 2);
            var orders = ReadProxy<IManufacturingOrderStore>.Create((name, _) => name == nameof(IManufacturingOrderStore.FindOrderAsync)
                ? Task.FromResult<ProductionOrder?>(order) : throw new InvalidOperationException("Unexpected write in a read composition test."));
            var plans = ReadProxy<IManufacturingPlanStore>.Create((name, _) => name == nameof(IManufacturingPlanStore.FindPlanAsync)
                ? Task.FromResult<ProductionPlan?>(ChangeLinkOnFreshRead ? Plan(2) : plan) : throw new InvalidOperationException("Unexpected write in a read composition test."));
            var company = new CompanyContext(); company.Set(CompanyId); User = new(CompanyId);
            Receipts = new(CompanyId, ParentId, Currency, now);
            reader = new(company, User, orders, plans, OrderRates, Receipts);
        }
        public Task<ManufacturingInputRateProofV1?> ReadAsync() => reader.ReadAsync(CompanyId, OrderId, FactId, ParentId,
            new(IssueCostCurrencyStatus.Converted, Currency, 2m, 123.4567m), CancellationToken.None);
    }
    public class ReadProxy<T> : DispatchProxy where T : class
    {
        public Func<string, object?[]?, object?>? Read { get; set; }
        public static T Create(Func<string, object?[]?, object?> read)
        { var proxy = DispatchProxy.Create<T, ReadProxy<T>>(); ((ReadProxy<T>)(object)proxy).Read = read; return proxy; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Read!(targetMethod!.Name, args);
    }
    private sealed class OrderRates : IManufacturingInputOrderRateReadPortV1
    {
        public int Calls { get; private set; } public bool Available { get; set; } = true;
        public bool WrongCurrency { get; set; } public Action? BeforeAnswer { get; set; }
        public Task<ManufacturingInputOrderRateV1> ReadAsync(Guid company, Guid order, Guid line, int revision, Guid source, Guid target, CancellationToken ct)
        {
            Calls++; BeforeAnswer?.Invoke();
            return Task.FromResult(new ManufacturingInputOrderRateV1(Available ? ManufacturingInputOrderRateOutcomeV1.Available : ManufacturingInputOrderRateOutcomeV1.Unavailable,
                company, order, line, revision, WrongCurrency ? Guid.NewGuid() : source, target,
                Available ? 50m : null, null, "synthetic-approved-sales-order:7", Available ? null : ProductionInputRateCodesV1.OrderRateUnavailable));
        }
    }
    private sealed class Receipts(Guid company, Guid label, Guid sourceCurrency, DateTimeOffset now) : IInventoryFirstReceiptExchangeRateReadPortV1
    {
        public int Calls { get; private set; }
        public Task<InventoryFirstReceiptExchangeRateV1> GetAsync(Guid companyId, Guid labelId, CancellationToken ct = default)
        {
            Calls++;
            InventoryFirstReceiptRateSourceV1 Source(string numerator, string denominator, decimal rate)
            {
                var acceptance = Guid.NewGuid(); var order = Guid.NewGuid(); var day = DateOnly.FromDateTime(now.DateTime);
                // FNS-D103/D104: synthetic owner facts still satisfy the immutable first-entry envelope.
                var evidence = JsonSerializer.Serialize(new { companyId = company, acceptanceId = acceptance,
                    purchaseOrderId = order, entryDay = day, source = "synthetic-first-entry:1" });
                var snapshot = new InventoryFirstEntryRateV1(company, acceptance, order, day, InventoryFirstEntryRateStateV1.Rated,
                    sourceCurrency, SettingsCurrencyIdentitiesV1.TurkishLira, rate, null, InventoryFirstEntryRateSourcesV1.ApprovedPurchaseOrder,
                    string.Empty, evidence, null, 1);
                snapshot = snapshot with { SourceVersion = InventoryFirstEntryRateAdmissionV1.SourceVersionOf(snapshot) };
                return new(new(Guid.NewGuid(), Guid.NewGuid(), now, acceptance, sourceCurrency), new(numerator, denominator),
                    snapshot);
            }
            return Task.FromResult(InventoryFirstReceiptExchangeRateV1.RatedExact(company, label, sourceCurrency,
                SettingsCurrencyIdentitiesV1.TurkishLira, new("100", "3"), "synthetic-weighted-first-entry:1", [Source("2", "3", 40m), Source("1", "3", 20m)]));
        }
    }
    private sealed class User(Guid company) : ICurrentUser
    {
        public string Actor { get; set; } = "synthetic-actor"; public bool IsAuthenticated => true; public string UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid id) => id == company;
    }
}
