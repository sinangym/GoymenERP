using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Lineage;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-S22 (API-28, URT-D63, KLN-D44) at the service boundary: the order lineage is read under the order read
/// permission measured after the reads, for the resolved company's order only. The edges and shares come only from the
/// lineage read port; the read passes them as lossless text, the backward trace per output label and the forward trace
/// per input label, and fails closed (503) without a port, with a failing port or with an answer it cannot trust.
/// Every identity and kg here is a synthetic test value.
/// </summary>
public sealed partial class ManufacturingCommandServiceTests
{
    private static readonly Guid LineageInputA = Guid.Parse("c1000000-0000-0000-0000-00000000a022");
    private static readonly Guid LineageInputB = Guid.Parse("c2000000-0000-0000-0000-00000000a022");
    private static readonly Guid LineageOutputOne = Guid.Parse("d1000000-0000-0000-0000-00000000a022");
    private static readonly Guid LineageOutputTwo = Guid.Parse("d2000000-0000-0000-0000-00000000a022");
    private static readonly Guid LineageLabelOne = Guid.Parse("e1000000-0000-0000-0000-00000000a022");
    private static readonly Guid LineageLabelTwo = Guid.Parse("e2000000-0000-0000-0000-00000000a022");
    private static readonly Guid LineageGenealogy = Guid.Parse("f1000000-0000-0000-0000-00000000a022");
    private static readonly Guid UpstreamOrder = Guid.Parse("f2000000-0000-0000-0000-00000000a022");
    private static readonly Guid DownstreamOrder = Guid.Parse("f3000000-0000-0000-0000-00000000a022");

    private static (Fixture Fixture, ManufacturingCommandService Service) OrderLineageCase(
        IManufacturingLabelLineageReadPortV1? port)
    {
        var fixture = new Fixture();
        var service = new ManufacturingCommandService(fixture.Store, fixture.Store, fixture.Store,
            new ManufacturingMassVerifier(fixture.Settings), fixture.Authority, fixture.CompanyContext, fixture.User,
            fixture.Correlation, new FixedTime(), labelLineage: port);
        return (fixture, service);
    }

    /// <summary>
    /// Output one took 75 kg of input A and 25 kg of input B; output two took 40 kg of input A only. Input B came from an
    /// upstream order's output, and output one's label went into a downstream order's output.
    /// </summary>
    private static ManufacturingOrderLineageV1 LineageAnswer(Guid orderId, bool truncated = false) => new(
        Company,
        orderId,
        truncated,
        [
            Calculated(orderId, LineageOutputOne, LineageLabelOne, 100m,
                new ManufacturingLabelLineageInputV1(LineageInputA, 75m, 0.75m),
                new ManufacturingLabelLineageInputV1(LineageInputB, 25m, 0.25m)),
            Calculated(orderId, LineageOutputTwo, LineageLabelTwo, 40m,
                new ManufacturingLabelLineageInputV1(LineageInputA, 40m, 1m)),
        ],
        [
            Calculated(UpstreamOrder, Guid.Parse("d3000000-0000-0000-0000-00000000a022"), LineageInputB, 30m,
                new ManufacturingLabelLineageInputV1(Guid.Parse("c3000000-0000-0000-0000-00000000a022"), 30m, 1m)),
        ],
        [
            Calculated(DownstreamOrder, Guid.Parse("d4000000-0000-0000-0000-00000000a022"),
                Guid.Parse("e4000000-0000-0000-0000-00000000a022"), 90m,
                new ManufacturingLabelLineageInputV1(LineageLabelOne, 90m, 1m)),
        ]);

    private static ManufacturingLabelLineageV1 Calculated(Guid orderId, Guid outputId, Guid outputLabelId,
        decimal total, params ManufacturingLabelLineageInputV1[] inputs) =>
        new(Company, orderId, outputId, outputLabelId, LineageGenealogy, ProductionLineageShareCalculatorV1.ContractVersion,
            nameof(ProductionLineageShareStatus.Calculated), null, total, inputs);

    [Fact]
    public async Task The_order_lineage_answers_backward_and_forward_traces_with_the_port_shares_under_the_order_read()
    {
        var port = new FakeLineagePort();
        var (fixture, service) = OrderLineageCase(port);
        var order = TracedOrder(fixture);
        port.Answer = LineageAnswer(order.Id);
        var effects = (fixture.Store.Writes.Count, fixture.Store.Commands.Count, fixture.Store.AuditEvents.Count);
        fixture.Authority.Permissions.Clear();

        var result = await service.GetOrderLineageAsync(order.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Ok, false, (string?)null), (result.Status, result.IsReplay, result.Code));
        var view = result.Resource!;
        Assert.Equal((1, order.Id, ProductionLineageShareCalculatorV1.ContractVersion, false),
            (view.ContractVersion, view.OrderId, view.CalculatorVersion, view.Truncated));
        Assert.Equal(2, view.Outputs.Count);
        var one = view.Outputs[0];
        Assert.Equal((order.Id, LineageOutputOne, LineageLabelOne, LineageGenealogy, "calculated", (string?)null, "100"),
            (one.OrderId, one.OutputId, one.OutputLabelId, one.GenealogyTransformationId, one.ShareStatus,
                one.ShareCode, one.TotalConsumedKg));
        Assert.Equal(
            [
                new OrderLineageInputShareViewV1(LineageInputA, "75", "0.75"),
                new OrderLineageInputShareViewV1(LineageInputB, "25", "0.25"),
            ],
            one.Inputs);
        Assert.Equal([new OrderLineageInputShareViewV1(LineageInputA, "40", "1")], view.Outputs[1].Inputs);

        // The forward trace is the same edges per input label: input A went into both outputs, input B into one.
        Assert.Equal([LineageInputA, LineageInputB], view.Inputs.Select(input => input.InputLabelId).ToArray());
        Assert.Equal(
            [
                new OrderLineageFedOutputViewV1(LineageOutputOne, LineageLabelOne, "calculated", "75", "0.75"),
                new OrderLineageFedOutputViewV1(LineageOutputTwo, LineageLabelTwo, "calculated", "40", "1"),
            ],
            view.Inputs[0].Outputs);
        Assert.Equal([new OrderLineageFedOutputViewV1(LineageOutputOne, LineageLabelOne, "calculated", "25", "0.25")],
            view.Inputs[1].Outputs);

        Assert.Equal((UpstreamOrder, LineageInputB), (Assert.Single(view.Upstream).OrderId,
            view.Upstream[0].OutputLabelId));
        Assert.Equal((DownstreamOrder, LineageLabelOne), (Assert.Single(view.Downstream).OrderId,
            Assert.Single(view.Downstream[0].Inputs).InputLabelId));
        Assert.Equal((Company, order.Id), Assert.Single(port.Calls));
        Assert.Equal([ErpPermissions.ManufacturingOrdersRead], fixture.Authority.Permissions);
        Assert.Equal((ErpPermissions.ManufacturingOrdersRead, Company), fixture.Authority.LastAsked);
        Assert.Equal(effects,
            (fixture.Store.Writes.Count, fixture.Store.Commands.Count, fixture.Store.AuditEvents.Count));
    }

    [Fact]
    public async Task An_unrounded_share_and_a_truncated_answer_pass_as_they_are()
    {
        var port = new FakeLineagePort();
        var (fixture, service) = OrderLineageCase(port);
        var order = TracedOrder(fixture);
        var third = 1m / 3m;
        port.Answer = new ManufacturingOrderLineageV1(Company, order.Id, true,
            [
                Calculated(order.Id, LineageOutputOne, LineageLabelOne, 3m,
                    new ManufacturingLabelLineageInputV1(LineageInputA, 1m, third),
                    new ManufacturingLabelLineageInputV1(LineageInputB, 2m, 1m - third)),
            ],
            [], []);

        var view = (await service.GetOrderLineageAsync(order.Id, CancellationToken.None)).Resource!;

        Assert.True(view.Truncated);
        Assert.Equal(["0.3333333333333333333333333333", "0.6666666666666666666666666667"],
            view.Outputs[0].Inputs.Select(input => input.Share).ToArray());
    }

    [Fact]
    public async Task A_lineage_without_a_calculated_share_carries_its_code_and_no_share_or_total()
    {
        var port = new FakeLineagePort();
        var (fixture, service) = OrderLineageCase(port);
        var order = TracedOrder(fixture);
        port.Answer = new ManufacturingOrderLineageV1(Company, order.Id, false,
            [
                new ManufacturingLabelLineageV1(Company, order.Id, LineageOutputOne, LineageLabelOne, LineageGenealogy,
                    ProductionLineageShareCalculatorV1.ContractVersion, nameof(ProductionLineageShareStatus.Unreadable),
                    ProductionLineageShareCodesV1.InputWithoutConsumption, null,
                    [new ManufacturingLabelLineageInputV1(LineageInputA, 0m, null)]),
            ],
            [], []);

        var output = Assert.Single((await service.GetOrderLineageAsync(order.Id, CancellationToken.None)).Resource!.Outputs);

        Assert.Equal(("unreadable", (string?)"lineage_input_without_consumption", (string?)null),
            (output.ShareStatus, output.ShareCode, output.TotalConsumedKg));
        Assert.Equal([new OrderLineageInputShareViewV1(LineageInputA, "0", null)], output.Inputs);
    }

    [Fact]
    public async Task An_order_without_lineage_edges_answers_empty_traces()
    {
        var port = new FakeLineagePort();
        var (fixture, service) = OrderLineageCase(port);
        var order = TracedOrder(fixture);
        port.Answer = new ManufacturingOrderLineageV1(Company, order.Id, false, [], [], []);

        var result = await service.GetOrderLineageAsync(order.Id, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        Assert.Equal((0, 0, 0, 0), (result.Resource!.Outputs.Count, result.Resource.Inputs.Count,
            result.Resource.Upstream.Count, result.Resource.Downstream.Count));
    }

    [Fact]
    public async Task No_lineage_port_or_a_throwing_port_fails_closed_as_a_retryable_503()
    {
        var (fixture, service) = OrderLineageCase(port: null);
        var order = TracedOrder(fixture);

        var none = await service.GetOrderLineageAsync(order.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unavailable, (OrderLineageViewV1?)null), (none.Status, none.Resource));

        var throwing = new FakeLineagePort { Fault = new InvalidOperationException("sentetik kaynak hatası") };
        var (otherFixture, otherService) = OrderLineageCase(throwing);
        var otherOrder = TracedOrder(otherFixture);

        var failed = await otherService.GetOrderLineageAsync(otherOrder.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unavailable, (OrderLineageViewV1?)null), (failed.Status, failed.Resource));
    }

    [Fact]
    public async Task A_cancelled_lineage_read_is_not_swallowed()
    {
        var port = new FakeLineagePort { Fault = new OperationCanceledException() };
        var (fixture, service) = OrderLineageCase(port);
        var order = TracedOrder(fixture);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetOrderLineageAsync(order.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData("other-company")]
    [InlineData("other-order")]
    [InlineData("own-output-of-another-order")]
    [InlineData("own-output-of-another-company")]
    [InlineData("upstream-of-this-order")]
    [InlineData("downstream-of-this-order")]
    [InlineData("other-calculator-version")]
    [InlineData("unknown-status")]
    [InlineData("calculated-without-share")]
    [InlineData("calculated-without-total")]
    [InlineData("pending-with-share")]
    [InlineData("pending-without-code")]
    public async Task An_answer_the_read_cannot_trust_fails_closed_as_a_retryable_503(string fault)
    {
        var port = new FakeLineagePort();
        var (fixture, service) = OrderLineageCase(port);
        var order = TracedOrder(fixture);
        var answer = LineageAnswer(order.Id);
        var own = answer.Outputs[0];
        port.Answer = fault switch
        {
            "other-company" => answer with { CompanyId = OtherCompany },
            "other-order" => answer with { OrderId = Guid.NewGuid() },
            "own-output-of-another-order" => answer with { Outputs = [own with { OrderId = UpstreamOrder }] },
            "own-output-of-another-company" => answer with { Outputs = [own with { CompanyId = OtherCompany }] },
            "upstream-of-this-order" => answer with { Upstream = [own] },
            "downstream-of-this-order" => answer with { Downstream = [own] },
            "other-calculator-version" => answer with
            {
                Outputs = [own with { CalculatorVersion = ProductionLineageShareCalculatorV1.ContractVersion + 1 }],
            },
            "unknown-status" => answer with { Outputs = [own with { ShareStatus = "calculated" }] },
            "calculated-without-share" => answer with
            {
                Outputs = [own with { Inputs = [new ManufacturingLabelLineageInputV1(LineageInputA, 75m, null)] }],
            },
            "calculated-without-total" => answer with { Outputs = [own with { TotalConsumedKg = null }] },
            "pending-with-share" => answer with
            {
                Outputs =
                [
                    own with
                    {
                        ShareStatus = nameof(ProductionLineageShareStatus.Pending),
                        ShareCode = ProductionLineageShareCodesV1.NoInput, TotalConsumedKg = null,
                    },
                ],
            },
            _ => answer with
            {
                Outputs =
                [
                    own with
                    {
                        ShareStatus = nameof(ProductionLineageShareStatus.Pending), TotalConsumedKg = null,
                        Inputs = [new ManufacturingLabelLineageInputV1(LineageInputA, 75m, null)],
                    },
                ],
            },
        };

        var result = await service.GetOrderLineageAsync(order.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unavailable, (OrderLineageViewV1?)null), (result.Status, result.Resource));
    }

    [Fact]
    public void The_order_lineage_view_refuses_a_null_answer()
    {
        Assert.Null(ManufacturingCommandService.OrderLineageView(Company, Guid.NewGuid(), null));
    }

    [Fact]
    public async Task The_order_lineage_answers_not_found_for_an_empty_unknown_or_other_company_order()
    {
        var port = new FakeLineagePort();
        var (fixture, service) = OrderLineageCase(port);
        var foreign = TracedOrder(fixture);
        // The order stays the first company's; the caller's resolved company becomes another one it may access.
        fixture.CompanyContext.SwitchForTest(OtherCompany);
        fixture.User.Companies.Add(OtherCompany);

        Assert.Equal(ManufacturingApiStatus.NotFound,
            (await service.GetOrderLineageAsync(Guid.Empty, CancellationToken.None)).Status);
        Assert.Equal(ManufacturingApiStatus.NotFound,
            (await service.GetOrderLineageAsync(Guid.NewGuid(), CancellationToken.None)).Status);
        var other = await service.GetOrderLineageAsync(foreign.Id, CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.NotFound, (OrderLineageViewV1?)null), (other.Status, other.Resource));
        Assert.Empty(port.Calls);
    }

    [Fact]
    public async Task A_lineage_read_meeting_concurrent_commits_is_a_retryable_503()
    {
        var port = new FakeLineagePort();
        var (fixture, service) = OrderLineageCase(port);
        var order = TracedOrder(fixture);
        port.Answer = LineageAnswer(order.Id);
        fixture.Store.ReadFault = ManufacturingStoreCodes.ConcurrentChange;

        var result = await service.GetOrderLineageAsync(order.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unavailable, (string?)null, (OrderLineageViewV1?)null),
            (result.Status, result.Code, result.Resource));
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("actor")]
    [InlineData("company")]
    public async Task A_lineage_read_rechecks_the_captured_context_and_the_order_read_after_the_read(string fault)
    {
        var port = new FakeLineagePort();
        var (fixture, service) = OrderLineageCase(port);
        var order = TracedOrder(fixture);
        port.Answer = LineageAnswer(order.Id);
        fixture.Authority.Permissions.Clear();
        fixture.Store.AfterFindOrder = _ => ApplyFreshFenceFault(fixture, fault);

        var result = await service.GetOrderLineageAsync(order.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Denied, ManufacturingApiCodes.AuthorityDenied),
            (result.Status, result.Code));
        Assert.Null(result.Resource);
        Assert.Equal([ErpPermissions.ManufacturingOrdersRead], fixture.Authority.Permissions);
    }

    [Fact]
    public async Task A_lineage_read_without_a_resolved_company_context_is_denied_before_any_read()
    {
        var port = new FakeLineagePort();
        var (fixture, service) = OrderLineageCase(port);
        var order = TracedOrder(fixture);
        fixture.User.Companies.Clear();
        fixture.Authority.Permissions.Clear();

        var result = await service.GetOrderLineageAsync(order.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Denied, (OrderLineageViewV1?)null), (result.Status, result.Resource));
        Assert.Empty(port.Calls);
    }

    /// <summary>The lineage read port as a test double: one order answer, or a fault thrown before answering.</summary>
    private sealed class FakeLineagePort : IManufacturingLabelLineageReadPortV1
    {
        public ManufacturingOrderLineageV1? Answer { get; set; }

        public Exception? Fault { get; init; }

        public List<(Guid CompanyId, Guid OrderId)> Calls { get; } = [];

        public Task<ManufacturingLabelLineageV1?> ReadBackwardAsync(Guid companyId, Guid outputLabelId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The order lineage reads by order only.");

        public Task<IReadOnlyList<ManufacturingLabelLineageV1>> ReadForwardAsync(Guid companyId, Guid inputLabelId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The order lineage reads by order only.");

        public Task<ManufacturingOrderLineageV1> ReadOrderAsync(Guid companyId, Guid orderId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((companyId, orderId));
            if (Fault is { } fault)
            {
                throw fault;
            }

            return Task.FromResult(Answer!);
        }
    }
}
