using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// ALC-SC33 (FNS-D79/D80/D83/D91): a foreign-currency order carries a rate as a number with its kind and rate day; the preparer may
/// change it before approval with an optional reason; approval needs it and locks it. The default number is the Settings resolver's
/// answer and is not tested here.
/// </summary>
public sealed class PurchaseOrderExchangeRateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 11, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly DateOnly RateDay = new(2026, 11, 11);
    private static readonly DateOnly BulletinDay = new(2026, 11, 10);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Supplier = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid KgUnit = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Functional = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid Dollar = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid Profile = Guid.Parse("88888888-8888-4888-8888-888888888888");
    private const string Preparer = "SENTETIK-hazirlayan";
    private const string Approver = "SENTETIK-onaylayan";

    private static readonly PurchaseOrderMoney DollarMoney = PurchaseOrderMoney.Of(Dollar, "USD", Profile, 1, 2);
    private static readonly PurchaseOrderMoney FunctionalMoney = PurchaseOrderMoney.Of(Functional, "TRY", Profile, 1, 2);

    private static string CodeOf(Action action) => Assert.Throws<PurchasingRuleException>(action).Code;

    private static PurchaseOrderExchangeRate Automatic(decimal rate) =>
        new(rate, Functional, PurchaseOrderExchangeRateKind.TcmbForexSelling, RateDay, PurchaseOrderExchangeRateSource.Automatic, BulletinDay);

    private static PurchaseOrderExchangeRate Manual(decimal rate) =>
        new(rate, Functional, PurchaseOrderExchangeRateKind.TcmbForexSelling, RateDay, PurchaseOrderExchangeRateSource.Manual, null);

    private static PurchaseOrder DraftWithLine(PurchaseOrderMoney? money)
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, money, T0, Preparer);
        order.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Quantity.Of(10m, QuantityUnit.Kilogram), T0);
        return order;
    }

    [Fact]
    public void The_rate_is_kept_as_written_without_trailing_zeros_and_never_rounded()
    {
        var rate = Manual(34.1250000m);

        Assert.Equal("34.125", rate.Rate.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal((Functional, PurchaseOrderExchangeRateKind.TcmbForexSelling, RateDay), (rate.TargetCurrencyId, rate.Kind, rate.RateDay));
        Assert.Null(rate.BulletinDate);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.00000000001")]
    [InlineData("1234567890123456789")]
    public void A_non_positive_too_precise_or_too_wide_rate_is_refused(string value)
    {
        var rate = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(PurchasingRuleCodes.OrderExchangeRateInvalid, CodeOf(() => Manual(rate)));
    }

    [Fact]
    public void An_automatic_rate_names_its_bulletin_day_and_a_manual_rate_names_none()
    {
        Assert.Throws<ArgumentException>(() => new PurchaseOrderExchangeRate(
            34m, Functional, PurchaseOrderExchangeRateKind.TcmbForexSelling, RateDay, PurchaseOrderExchangeRateSource.Automatic, null));
        Assert.Throws<ArgumentException>(() => new PurchaseOrderExchangeRate(
            34m, Functional, PurchaseOrderExchangeRateKind.TcmbForexSelling, RateDay, PurchaseOrderExchangeRateSource.Manual, BulletinDay));
    }

    [Fact]
    public void The_default_rate_is_part_of_the_opening_and_does_not_move_the_version()
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, DollarMoney, T0, Preparer);

        order.AssignDefaultExchangeRate(Automatic(34.5m), Preparer, T0);

        Assert.Equal(1L, order.Version);
        Assert.Equal(Automatic(34.5m), order.ExchangeRate);
        Assert.Null(order.LastExchangeRateChange!.Previous);
        Assert.Throws<InvalidOperationException>(() => order.AssignDefaultExchangeRate(Automatic(35m), Preparer, T0));
    }

    [Fact]
    public void A_foreign_currency_order_without_a_rate_is_not_approved_and_nothing_moves()
    {
        var order = DraftWithLine(DollarMoney);

        Assert.Equal(PurchasingRuleCodes.OrderExchangeRateRequired, CodeOf(() => order.Approve(2, Approver, T0, Functional)));

        Assert.Equal((PurchaseOrderStatus.Draft, 2L), (order.Status, order.Version));
        Assert.Null(order.Approval);
    }

    [Fact]
    public void The_preparer_changes_the_rate_before_approval_with_a_reason_and_approval_locks_it()
    {
        var order = DraftWithLine(DollarMoney);
        order.SetExchangeRate(2, Manual(34.5m), Preparer, null, T0);
        order.SetExchangeRate(3, Manual(35.25m), Preparer, "  tedarikçi teyidi  ", T0.AddMinutes(5));

        var change = order.LastExchangeRateChange!;
        Assert.Equal((Manual(34.5m), Manual(35.25m), "tedarikçi teyidi"), (change.Previous, change.Current, change.Reason));
        Assert.Equal(4L, order.Version);

        order.Approve(4, Approver, T0.AddHours(1), Functional);

        Assert.Equal(PurchaseOrderStatus.Approved, order.Status);
        Assert.Equal(PurchasingRuleCodes.OrderExchangeRateLocked,
            CodeOf(() => order.SetExchangeRate(5, Manual(36m), Preparer, null, T0.AddHours(2))));
        Assert.Equal(Manual(35.25m), order.ExchangeRate);
        Assert.Equal(5L, order.Version);
    }

    [Fact]
    public void A_reason_longer_than_the_limit_is_refused_and_not_cut()
    {
        var order = DraftWithLine(DollarMoney);
        var reason = new string('x', PurchaseOrder.MaximumReasonLength + 1);

        Assert.Equal(PurchasingRuleCodes.OrderExchangeRateReasonTooLong,
            CodeOf(() => order.SetExchangeRate(2, Manual(35m), Preparer, reason, T0)));
        Assert.Null(order.ExchangeRate);
        Assert.Equal(2L, order.Version);
    }

    [Fact]
    public void An_order_in_the_functional_currency_or_without_a_currency_takes_no_rate_and_approves_without_one()
    {
        var functional = DraftWithLine(FunctionalMoney);
        var unpriced = DraftWithLine(null);

        Assert.Equal(PurchasingRuleCodes.OrderExchangeRateNotApplicable,
            CodeOf(() => functional.SetExchangeRate(2, Manual(35m), Preparer, null, T0)));
        Assert.Equal(PurchasingRuleCodes.OrderExchangeRateNotApplicable,
            CodeOf(() => unpriced.SetExchangeRate(2, Manual(35m), Preparer, null, T0)));

        functional.Approve(2, Approver, T0, Functional);
        unpriced.Approve(2, Approver, T0, Functional);
        Assert.Equal((PurchaseOrderStatus.Approved, PurchaseOrderStatus.Approved), (functional.Status, unpriced.Status));
    }
}
