using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D406 (1) / SAT-D367 over SYNTHETIC owners: the risk's approved-unshipped remainder is the shipment plan — per order line the
/// open quantity of the issued, not yet shipped loading instructions, read from the plan owner (Logistics) in one batched read —
/// valued at the approved basis' payable amount. It replaces SAT-D351's approved − definitely shipped. A plan owner that is not
/// composed, fails, refuses or answers anything malformed makes the remainder "Unavailable", never zero.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task SAT_D406_the_remainder_is_the_open_instruction_plan_not_the_approved_quantity_less_shipped()
    {
        var (world, approved, deliveries, _, _) = await UnbilledWorldAsync();
        var line = approved.Lines[0];
        var whole = (await world.Service().GetDraftTotalsAsync(approved.Draft.Id)).Totals.PayableAmount;
        Assert.NotNull(whole);
        var plan = new FakePlannedQuantities(asked => Planned(asked));
        world.PlannedQuantities = plan;

        // Nothing shipped and nothing on an issued instruction: nothing is planned, so nothing remains — approved − shipped would
        // have been the whole order.
        var unplanned = await world.Service().GetDraftRiskCheckAsync(approved.Draft.Id);
        Assert.Equal(("Available", ""), (unplanned.Components[RemainderIndex].State, AmountsOf(unplanned.Components[RemainderIndex])));
        Assert.Equal(approved.Draft.Id, Assert.Single(Assert.Single(plan.Batches)));
        Assert.Equal(Company, Assert.Single(plan.Companies));

        // The whole quantity on an issued instruction: the whole payable amount remains.
        plan.Answer = asked => Planned(asked, new SalesOrderPlannedQuantityLineV1(approved.Draft.Id, line.LineId, KgUnit, 1.25m));
        var planned = await world.Service().GetDraftRiskCheckAsync(approved.Draft.Id);
        Assert.Equal(("Available", whole), (planned.Components[RemainderIndex].State, AmountsOf(planned.Components[RemainderIndex])));

        // A shipment does not reduce the plan Sales reads (the owner's open quantity already left it out): the definite shipment is
        // the unbilled stage's, the plan stays the remainder.
        deliveries.Answer = order => new(Company, order, [new(line.LineId, KgUnit, 0.25m, 0m)]);
        var shipped = await world.Service().GetDraftRiskCheckAsync(approved.Draft.Id);
        Assert.Equal(("Available", whole), (shipped.Components[RemainderIndex].State, AmountsOf(shipped.Components[RemainderIndex])));

        // Part of the order planned: a smaller amount, from the same calculator.
        plan.Answer = asked => Planned(asked, new SalesOrderPlannedQuantityLineV1(approved.Draft.Id, line.LineId, KgUnit, 0.5m));
        var part = await world.Service().GetDraftRiskCheckAsync(approved.Draft.Id);
        Assert.Equal("Available", part.Components[RemainderIndex].State);
        Assert.True(decimal.Parse(AmountsOf(part.Components[RemainderIndex]), NumberStyles.Number, CultureInfo.InvariantCulture)
            < decimal.Parse(whole!, NumberStyles.Number, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task SAT_D406_a_plan_owner_that_is_absent_fails_refuses_or_answers_malformed_makes_the_remainder_unavailable_never_zero()
    {
        var (world, approved, _, _, _) = await UnbilledWorldAsync();
        var order = approved.Draft.Id;
        var line = approved.Lines[0].LineId;

        world.NoPlannedQuantities = true;
        Assert.Equal(("Unavailable", SalesDraftRiskReasonCodesV1.PlanProviderUnavailable), await RemainderStateAsync(world, order));
        world.NoPlannedQuantities = false;

        var plan = new FakePlannedQuantities(_ => throw new InvalidOperationException("SENTETIK logistics outage"));
        world.PlannedQuantities = plan;
        Assert.Equal(("Unavailable", SalesDraftRiskReasonCodesV1.PlanProviderUnavailable), await RemainderStateAsync(world, order));
        foreach (var outcome in new[] { SalesOrderPlannedQuantityOutcomeV1.Denied, SalesOrderPlannedQuantityOutcomeV1.Unavailable })
        {
            plan.Answer = asked => new(outcome, Company, [], [], false);
            Assert.Equal(("Unavailable", SalesDraftRiskReasonCodesV1.PlanProviderUnavailable), await RemainderStateAsync(world, order));
        }

        foreach (var malformed in new Func<IReadOnlyCollection<Guid>, SalesOrderPlannedQuantityReadV1>[]
                 {
                     // Another company's answer.
                     asked => Planned(asked) with { CompanyId = Guid.NewGuid() },
                     // A final answer that leaves an asked order out (a missing line would read as zero).
                     _ => new(SalesOrderPlannedQuantityOutcomeV1.Found, Company, [], [], false),
                     // An answered order nobody asked for.
                     asked => Planned([.. asked, Guid.NewGuid()]),
                     // A line of an order the answer does not answer.
                     asked => Planned(asked, new SalesOrderPlannedQuantityLineV1(Guid.NewGuid(), line, KgUnit, 1m)),
                     // The same line twice.
                     asked => Planned(asked, new SalesOrderPlannedQuantityLineV1(order, line, KgUnit, 0.5m),
                         new SalesOrderPlannedQuantityLineV1(order, line, KgUnit, 0.5m)),
                     // A zero or a negative open quantity (the contract leaves a zero line out).
                     asked => Planned(asked, new SalesOrderPlannedQuantityLineV1(order, line, KgUnit, 0m)),
                     asked => Planned(asked, new SalesOrderPlannedQuantityLineV1(order, line, KgUnit, -1m)),
                     // Another unit than the order line's: never converted.
                     asked => Planned(asked, new SalesOrderPlannedQuantityLineV1(order, line, Guid.NewGuid(), 1m)),
                     // A page that answers nothing while it says more follows: no progress, never a loop.
                     _ => new(SalesOrderPlannedQuantityOutcomeV1.Found, Company, [], [], true),
                 })
        {
            plan.Answer = malformed;
            Assert.Equal(("Unavailable", SalesDraftRiskReasonCodesV1.PlanAnswerInvalid), await RemainderStateAsync(world, order));
        }

        // A planned line the approved basis does not have: no approved price, never free.
        plan.Answer = asked => Planned(asked, new SalesOrderPlannedQuantityLineV1(order, Guid.NewGuid(), KgUnit, 1m));
        Assert.Equal(("Unavailable", SalesDraftRiskReasonCodesV1.OrderBasisUnreadable), await RemainderStateAsync(world, order));
    }

    [Fact]
    public async Task SAT_D406_the_plan_is_asked_again_for_the_orders_a_page_left_unanswered_and_never_without_an_open_approved_order()
    {
        var world = new World { NoPlannedQuantities = false };
        world.PartyRisk = new FakePartyRisk(FinanceLike([]));
        world.Deliveries = new FakeDeliveries(order => new(Company, order, []));
        var plan = new FakePlannedQuantities(asked => Planned(asked));
        world.PlannedQuantities = plan;

        // No open approved order of the customer: the plan owner is never needed.
        var unapproved = (await world.Service().CreateDraftAsync(Priced(), Metadata("k-plan-create"))).Detail.Draft;
        var none = await world.Service().GetDraftRiskCheckAsync(unapproved.Id);
        Assert.Equal(("Available", "IncludingTax"), (none.Components[RemainderIndex].State, none.Components[RemainderIndex].TaxBasis));
        Assert.Empty(plan.Batches);

        var first = await PricedApprovedAsync(world, "p1");
        var second = await PricedApprovedAsync(world, "p2");
        plan.Batches.Clear();
        plan.Answered.Clear();
        // The owner answers one order per page and says more follows: the next read asks only the orders still unanswered.
        plan.Answer = asked => asked.Count > 1 ? Planned([asked.First()]) with { HasMore = true } : Planned(asked);
        var check = await world.Service().GetDraftRiskCheckAsync(unapproved.Id);

        Assert.Equal(("Available", ""), (check.Components[RemainderIndex].State, AmountsOf(check.Components[RemainderIndex])));
        Assert.Equal(2, plan.Batches.Count);
        Assert.Equal(2, plan.Answered.Count);
        Assert.Equal(new[] { first.Draft.Id, second.Draft.Id }.Order(), plan.Batches[0].Order());
        Assert.DoesNotContain(Assert.Single(plan.Batches[1]), plan.Answered[0]);
    }

    private static SalesOrderPlannedQuantityReadV1 Planned(IReadOnlyCollection<Guid> answered, params SalesOrderPlannedQuantityLineV1[] lines) =>
        new(SalesOrderPlannedQuantityOutcomeV1.Found, Company, lines, [.. answered], false);

    /// <summary>A SYNTHETIC shipment-plan owner: answers what the case gives it and records every batch it was asked.</summary>
    private sealed class FakePlannedQuantities(Func<IReadOnlyCollection<Guid>, SalesOrderPlannedQuantityReadV1> answer)
        : ISalesOrderPlannedQuantityReadPortV1
    {
        public Func<IReadOnlyCollection<Guid>, SalesOrderPlannedQuantityReadV1> Answer { get; set; } = answer;

        public List<Guid[]> Batches { get; } = [];

        public List<Guid> Companies { get; } = [];

        /// <summary>The orders each answer answered, in call order.</summary>
        public List<IReadOnlyList<Guid>> Answered { get; } = [];

        public Task<SalesOrderPlannedQuantityReadV1> ReadOpenInstructionQuantitiesAsync(Guid companyId, IReadOnlyCollection<Guid> orderIds,
            CancellationToken cancellationToken = default)
        {
            Companies.Add(companyId);
            Batches.Add([.. orderIds]);
            var read = Answer(orderIds);
            Answered.Add(read.AnsweredOrderIds);
            return Task.FromResult(read);
        }
    }

    /// <summary>
    /// The default SYNTHETIC plan owner of <see cref="World"/>: it plans exactly the approved basis (the customer-accepted revision,
    /// else the current lines while approved, else the last approved revision) less the case's definitely shipped quantities, so
    /// the cases whose subject is not the plan keep their approved − shipped figures.
    /// </summary>
    private sealed class ApprovedLessShippedPlan(World world) : ISalesOrderPlannedQuantityReadPortV1
    {
        public Task<SalesOrderPlannedQuantityReadV1> ReadOpenInstructionQuantitiesAsync(Guid companyId, IReadOnlyCollection<Guid> orderIds,
            CancellationToken cancellationToken = default)
        {
            var asked = orderIds.Distinct().ToArray();
            var lines = new List<SalesOrderPlannedQuantityLineV1>();
            foreach (var orderId in asked)
            {
                var order = world.Store.Drafts.FirstOrDefault(draft => draft.Id == orderId && draft.CompanyId == companyId);
                if (order is null)
                {
                    continue;
                }

                var shipped = Shipped(orderId);
                foreach (var (lineId, unitId, quantity) in Basis(order))
                {
                    var open = quantity - shipped.GetValueOrDefault(lineId);
                    if (open > 0m)
                    {
                        lines.Add(new SalesOrderPlannedQuantityLineV1(orderId, lineId, unitId, open));
                    }
                }
            }

            return Task.FromResult(new SalesOrderPlannedQuantityReadV1(SalesOrderPlannedQuantityOutcomeV1.Found, companyId, lines, asked, false));
        }

        private Dictionary<Guid, decimal> Shipped(Guid orderId)
        {
            if (world.Deliveries is not FakeDeliveries deliveries)
            {
                return new Dictionary<Guid, decimal>();
            }

            try
            {
                return deliveries.Answer(orderId).Lines.GroupBy(line => line.OrderLineId)
                    .ToDictionary(group => group.Key, group => group.Sum(line => line.ShippedQuantity));
            }
            catch (InvalidOperationException)
            {
                // The case's delivery owner fails: the service refused before it asked the plan.
                return new Dictionary<Guid, decimal>();
            }
        }

        private static IEnumerable<(Guid LineId, Guid UnitId, decimal Quantity)> Basis(SalesOrderDraft order)
        {
            if (order.CustomerAcceptedFallback is { } accepted)
            {
                return Archived(accepted);
            }

            if (order.ApprovalState == InternalApprovalState.Approved)
            {
                return order.Lines.Select(line => (line.Id, line.OrderUnit.UnitId, line.Quantity.Value));
            }

            return order.ArchivedRevisions.Count == 0 ? [] : Archived(order.ArchivedRevisions[^1]);
        }

        private static IEnumerable<(Guid LineId, Guid UnitId, decimal Quantity)> Archived(ApprovedRevisionSnapshot revision) =>
            revision.Lines.Select(line => (line.LineId, line.OrderUnitId,
                decimal.Parse(line.QuantityText, NumberStyles.Number, CultureInfo.InvariantCulture)));
    }
}
