using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// AT-024 ("Kısmi teslimat sipariş kalanını doğru tutar") over a SYNTHETIC delivery owner: per line the delivered and the
/// remaining quantity come from the owner's recorded totals only, exactly and in the order unit; the read writes nothing;
/// the draft read is measured before, after the draft and after the owner; another company's draft never reaches the owner;
/// every malformed owner answer fails closed (503) instead of showing a number. No tolerance is derived (OPEN-004).
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task A_partial_delivery_leaves_the_exact_remaining_per_line_and_the_read_writes_nothing()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(lines: 2), Metadata("k-1"))).Detail;
        var (first, second) = (created.Lines[0], created.Lines[1]);
        // Two instructions served the first line in its order unit: 0.5 shipped / 0.5 delivered and 0.5 shipped / 0.25 delivered.
        var owner = new FakeDeliveries(order => new(Company, order,
            [new(first.LineId, KgUnit, 0.5m, 0.5m), new(first.LineId, KgUnit, 0.5m, 0.25m)]));
        world.Deliveries = owner;
        var attempts = world.Store.Attempts;
        var envelopes = world.Store.Envelopes.Count;
        world.Authority.Measured.Clear();

        var progress = await world.Service().GetDraftDeliveryProgressAsync(created.Draft.Id);

        Assert.Equal(created.Draft.Version, progress.DraftVersion);
        Assert.Equal(
            [
                new SalesDraftLineDeliveryProgressV1(first.LineId, 1, KgUnit, "1.25", "1", "0.75", "0.5", "0"),
                new SalesDraftLineDeliveryProgressV1(second.LineId, 2, KgUnit, "2.25", "0", "0", "2.25", "0"),
            ],
            progress.Lines);
        // The owner is asked once, for this draft; the draft read is measured before, after the draft and after the owner.
        Assert.Equal([created.Draft.Id], owner.Asked);
        Assert.Equal([ReadPermission, ReadPermission, ReadPermission], world.Authority.Measured);
        // Read-only: no attempt, envelope, transition or refusal anywhere in Sales.
        Assert.Equal((attempts, envelopes), (world.Store.Attempts, world.Store.Envelopes.Count));
        Assert.Empty(world.Store.Transitions);
        Assert.Empty(world.Store.Refusals);

        // Opened again after one more recorded delivery: the owner's new total, read afresh (nothing cached in Sales).
        owner.Answer = order => new(Company, order, [new(first.LineId, KgUnit, 1.25m, 1.25m), new(second.LineId, KgUnit, 1m, 0.001m)]);
        var reopened = await world.Service().GetDraftDeliveryProgressAsync(created.Draft.Id);
        Assert.Equal(("1.25", "0", "0"), (reopened.Lines[0].Delivered, reopened.Lines[0].Remaining, reopened.Lines[0].Excess));
        Assert.Equal(("1", "0.001", "2.249"), (reopened.Lines[1].Shipped, reopened.Lines[1].Delivered, reopened.Lines[1].Remaining));
    }

    [Fact]
    public async Task More_delivered_than_ordered_is_shown_as_an_excess_fact_with_nothing_remaining_and_no_tolerance_judgement()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-1"))).Detail;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(created.Lines[0].LineId, KgUnit, 1.5m, 1.5m)]));

        var line = Assert.Single((await world.Service().GetDraftDeliveryProgressAsync(created.Draft.Id)).Lines);

        // OPEN-004 (tolerance) is open: the facts only — no completion, warning or approval member exists on the wire.
        Assert.Equal(("1.25", "1.5", "0", "0.25"), (line.Ordered, line.Delivered, line.Remaining, line.Excess));
        Assert.Equal(
            ["Delivered", "Excess", "LineId", "LineNo", "OrderUnitId", "Ordered", "Remaining", "Shipped"],
            typeof(SalesDraftLineDeliveryProgressV1).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_unknown_or_other_companys_draft_is_404_and_without_the_draft_read_it_is_403_and_the_owner_is_never_asked()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-1"))).Detail;
        var owner = new FakeDeliveries(order => new(Company, order, []));
        world.Deliveries = owner;

        // Another company's draft is null under the tenant filter, exactly like an unknown one.
        Assert.Equal((404, "not_found"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetDraftDeliveryProgressAsync(Guid.NewGuid()))));
        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetDraftDeliveryProgressAsync(Guid.Empty))).Status);

        world.Authority.Denied.Add(ReadPermission);
        Assert.Equal((403, "forbidden"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetDraftDeliveryProgressAsync(created.Draft.Id))));
        Assert.Empty(owner.Asked);
    }

    [Fact]
    public async Task A_draft_read_lost_while_the_owner_answered_is_403_and_no_number_is_released()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-1"))).Detail;
        var owner = new FakeDeliveries(order => new(Company, order, [new(created.Lines[0].LineId, KgUnit, 1m, 1m)]));
        world.Deliveries = owner;
        // Granted for the first two measurements (before, after the draft read), lost at the one after the owner's answer.
        world.Authority.LostLater = (ReadPermission, world.Authority.Calls + 2);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftDeliveryProgressAsync(created.Draft.Id));

        Assert.Equal((403, "forbidden"), Status(refused));
        Assert.Single(owner.Asked);
    }

    [Fact]
    public async Task Without_a_composed_delivery_owner_or_when_it_fails_the_read_is_503_never_an_assumed_zero()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-1"))).Detail;

        Assert.Equal((503, SalesDraftProblemCodesV1.DeliveryProviderUnavailable), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetDraftDeliveryProgressAsync(created.Draft.Id))));

        world.Deliveries = new FakeDeliveries(_ => throw new InvalidOperationException("SENTETIK owner outage"));
        Assert.Equal((503, SalesDraftProblemCodesV1.DeliveryProviderUnavailable), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetDraftDeliveryProgressAsync(created.Draft.Id))));
    }

    public static TheoryData<string> MalformedAnswers => new()
    {
        "another-company", "another-order", "null-lines", "unknown-line", "another-unit", "delivered-above-shipped",
        "negative", "above-scale",
    };

    [Theory]
    [MemberData(nameof(MalformedAnswers))]
    public async Task A_malformed_owner_answer_fails_closed_503_instead_of_showing_a_number(string fault)
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-1"))).Detail;
        var line = created.Lines[0].LineId;
        world.Deliveries = new FakeDeliveries(order => fault switch
        {
            "another-company" => new(Guid.NewGuid(), order, []),
            "another-order" => new(Company, Guid.NewGuid(), []),
            "null-lines" => new(Company, order, null!),
            "unknown-line" => new(Company, order, [new(Guid.NewGuid(), KgUnit, 1m, 1m)]),
            // No conversion: a definite total in another unit cannot be added to the order unit's quantity.
            "another-unit" => new(Company, order, [new(line, Guid.NewGuid(), 1m, 0m)]),
            "delivered-above-shipped" => new(Company, order, [new(line, KgUnit, 1m, 1.001m)]),
            "negative" => new(Company, order, [new(line, KgUnit, -1m, 0m)]),
            // Ö2: kilograms carry at most three decimals; a fourth is refused, never rounded to fit.
            "above-scale" => new(Company, order, [new(line, KgUnit, 1.0001m, 0m)]),
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        });

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftDeliveryProgressAsync(created.Draft.Id));

        Assert.Equal((503, SalesDraftProblemCodesV1.ReferenceProviderInvalid), Status(refused));
    }

    [Fact]
    public async Task A_line_only_instructed_or_loaded_in_another_unit_adds_nothing_and_is_not_a_malformed_answer()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-1"))).Detail;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(created.Lines[0].LineId, Guid.NewGuid(), 0m, 0m)]));

        var line = Assert.Single((await world.Service().GetDraftDeliveryProgressAsync(created.Draft.Id)).Lines);

        Assert.Equal(("0", "0", "1.25", "0"), (line.Shipped, line.Delivered, line.Remaining, line.Excess));
    }

    private static (int Status, string Code) Status(SalesDraftServiceException exception) => (exception.Status, exception.Code);

    /// <summary>A SYNTHETIC delivery owner: answers what the case gives it and records which orders it was asked for.</summary>
    private sealed class FakeDeliveries(Func<Guid, SalesOrderDeliveryReadAnswerV1> answer) : ISalesOrderDeliveryReadPortV1
    {
        public Func<Guid, SalesOrderDeliveryReadAnswerV1> Answer { get; set; } = answer;

        public List<Guid> Asked { get; } = [];

        public Task<SalesOrderDeliveryReadAnswerV1> ReadOrderDeliveriesAsync(Guid orderId, CancellationToken cancellationToken = default)
        {
            Asked.Add(orderId);
            return Task.FromResult(Answer(orderId));
        }
    }
}
