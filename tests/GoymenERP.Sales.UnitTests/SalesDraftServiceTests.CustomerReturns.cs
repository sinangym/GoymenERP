using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D8 / API-10 (delegation No 21, 38) over a SYNTHETIC customer-return owner: per line the approved returned quantity
/// comes from the owner's answer only, exactly and in the order unit; the read writes nothing; the draft read is measured
/// before, after the draft and after the owner; another company's draft never reaches the owner; an absent or failing
/// owner and every malformed owner answer fail closed (503) instead of showing a number.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task Approved_customer_returns_are_shown_per_line_in_the_order_unit_and_the_read_writes_nothing()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(lines: 2), Metadata("k-1"))).Detail;
        var (first, second) = (created.Lines[0], created.Lines[1]);
        var owner = new FakeCustomerReturns(order => new(Company, order, [new(first.LineId, KgUnit, 0.5m)]));
        world.CustomerReturns = owner;
        var attempts = world.Store.Attempts;
        var envelopes = world.Store.Envelopes.Count;
        world.Authority.Measured.Clear();

        var returns = await world.Service().GetDraftCustomerReturnsAsync(created.Draft.Id);

        Assert.Equal(created.Draft.Version, returns.DraftVersion);
        Assert.Equal(
            [
                new SalesDraftLineCustomerReturnV1(first.LineId, 1, KgUnit, "1.25", "0.5"),
                new SalesDraftLineCustomerReturnV1(second.LineId, 2, KgUnit, "2.25", "0"),
            ],
            returns.Lines);
        // The owner is asked once, for this draft; the draft read is measured before, after the draft and after the owner.
        Assert.Equal([created.Draft.Id], owner.Asked);
        Assert.Equal([ReadPermission, ReadPermission, ReadPermission], world.Authority.Measured);
        // Read-only: no attempt, envelope, transition or refusal anywhere in Sales.
        Assert.Equal((attempts, envelopes), (world.Store.Attempts, world.Store.Envelopes.Count));
        Assert.Empty(world.Store.Transitions);
        Assert.Empty(world.Store.Refusals);

        // Opened again after another approval: the owner's new totals, read afresh (nothing cached in Sales); two entries of
        // the same line in its unit are added exactly, and trailing zeros of the stored decimal never reach the wire.
        owner.Answer = order => new(Company, order,
            [new(first.LineId, KgUnit, 0.500m), new(first.LineId, KgUnit, 0.25m), new(second.LineId, KgUnit, 2.250m)]);
        var reopened = await world.Service().GetDraftCustomerReturnsAsync(created.Draft.Id);
        Assert.Equal(("0.75", "2.25"), (reopened.Lines[0].ApprovedReturned, reopened.Lines[1].ApprovedReturned));
        Assert.Equal(
            ["ApprovedReturned", "LineId", "LineNo", "OrderUnitId", "Ordered"],
            typeof(SalesDraftLineCustomerReturnV1).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Customer_returns_of_an_unknown_or_other_companys_draft_are_404_and_without_the_draft_read_403_and_the_owner_is_never_asked()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-1"))).Detail;
        var owner = new FakeCustomerReturns(order => new(Company, order, []));
        world.CustomerReturns = owner;

        // Another company's draft is null under the tenant filter, exactly like an unknown one.
        Assert.Equal((404, "not_found"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetDraftCustomerReturnsAsync(Guid.NewGuid()))));
        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetDraftCustomerReturnsAsync(Guid.Empty))).Status);

        world.Authority.Denied.Add(ReadPermission);
        Assert.Equal((403, "forbidden"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetDraftCustomerReturnsAsync(created.Draft.Id))));
        Assert.Empty(owner.Asked);
    }

    [Fact]
    public async Task A_draft_read_lost_while_the_return_owner_answered_is_403_and_no_number_is_released()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-1"))).Detail;
        var owner = new FakeCustomerReturns(order => new(Company, order, [new(created.Lines[0].LineId, KgUnit, 1m)]));
        world.CustomerReturns = owner;
        // Granted for the first two measurements (before, after the draft read), lost at the one after the owner's answer.
        world.Authority.LostLater = (ReadPermission, world.Authority.Calls + 2);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetDraftCustomerReturnsAsync(created.Draft.Id));

        Assert.Equal((403, "forbidden"), Status(refused));
        Assert.Single(owner.Asked);
    }

    [Fact]
    public async Task Without_a_composed_return_owner_or_when_it_fails_the_read_is_503_never_an_assumed_zero()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-1"))).Detail;

        Assert.Equal((503, SalesDraftProblemCodesV1.CustomerReturnProviderUnavailable), Status(
            await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftCustomerReturnsAsync(created.Draft.Id))));

        world.CustomerReturns = new FakeCustomerReturns(_ => throw new InvalidOperationException("SENTETIK owner outage"));
        Assert.Equal((503, SalesDraftProblemCodesV1.CustomerReturnProviderUnavailable), Status(
            await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftCustomerReturnsAsync(created.Draft.Id))));
    }

    public static TheoryData<string> MalformedCustomerReturnAnswers => new()
    {
        "null-answer", "another-company", "another-order", "null-lines", "null-entry", "unknown-line", "another-unit", "zero",
        "negative", "above-scale",
    };

    [Theory]
    [MemberData(nameof(MalformedCustomerReturnAnswers))]
    public async Task A_malformed_return_owner_answer_fails_closed_503_instead_of_showing_a_number(string fault)
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-1"))).Detail;
        var line = created.Lines[0].LineId;
        world.CustomerReturns = new FakeCustomerReturns(order => fault switch
        {
            "null-answer" => null!,
            "another-company" => new(Guid.NewGuid(), order, []),
            "another-order" => new(Company, Guid.NewGuid(), []),
            "null-lines" => new(Company, order, null!),
            "null-entry" => new(Company, order, [null!]),
            "unknown-line" => new(Company, order, [new(Guid.NewGuid(), KgUnit, 1m)]),
            // No conversion: a total recorded in another unit cannot be shown in the order unit.
            "another-unit" => new(Company, order, [new(line, Guid.NewGuid(), 1m)]),
            // An approved return always carries a positive quantity; the owner groups existing approved rows only.
            "zero" => new(Company, order, [new(line, KgUnit, 0m)]),
            "negative" => new(Company, order, [new(line, KgUnit, -1m)]),
            // Ö2: kilograms carry at most three decimals; a fourth is refused, never rounded to fit.
            "above-scale" => new(Company, order, [new(line, KgUnit, 1.0001m)]),
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        });

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetDraftCustomerReturnsAsync(created.Draft.Id));

        Assert.Equal((503, SalesDraftProblemCodesV1.ReferenceProviderInvalid), Status(refused));
    }

    /// <summary>A SYNTHETIC customer-return owner: answers what the case gives it and records which orders it was asked for.</summary>
    private sealed class FakeCustomerReturns(Func<Guid, SalesOrderCustomerReturnReadAnswerV1> answer) : ISalesOrderCustomerReturnReadPortV1
    {
        public Func<Guid, SalesOrderCustomerReturnReadAnswerV1> Answer { get; set; } = answer;

        public List<Guid> Asked { get; } = [];

        public Task<SalesOrderCustomerReturnReadAnswerV1> ReadApprovedReturnsAsync(Guid salesOrderId,
            CancellationToken cancellationToken = default)
        {
            Asked.Add(salesOrderId);
            return Task.FromResult(Answer(salesOrderId));
        }
    }
}
