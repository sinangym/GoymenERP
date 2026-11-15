using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-S3 / F-018 (plan S0 (b), SAT-D311, FNS-D53): the two Sales-owned read contracts the risk check needs before it can count a
/// finalized invoice exactly once — <see cref="ISalesPartyRiskReadPortV2"/> (the processed set in the same Finance answer as the
/// open receivables; Finance fills it in FIN-S20) and the optional <see cref="SalesInvoiceableShipmentQueryV1.OrderIds"/> filter
/// (Logistics applies it in STK-S38). Their shapes and validation rules are pinned here; every value is SENTETIK.
/// </summary>
public sealed class SalesRiskReadContractTests
{
    private static readonly Guid Company = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Party = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Account = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid Currency = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid InvoiceA = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid InvoiceB = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid Unasked = Guid.Parse("00000000-0000-0000-0000-0000000000a9");
    private static readonly Guid OrderA = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid OrderB = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    private const string FinanceSource = "SENTETIK-finance-customer-ledger";

    [Fact]
    public void The_invoiceable_shipment_query_appends_an_optional_order_filter_last_so_v1_callers_build_the_same_query()
    {
        var constructor = Assert.Single(typeof(SalesInvoiceableShipmentQueryV1).GetConstructors(),
            candidate => candidate.GetParameters().All(parameter => parameter.ParameterType != typeof(SalesInvoiceableShipmentQueryV1)));
        var parameters = constructor.GetParameters();

        Assert.Equal(["CompanyAccountId", "Limit", "OrderIds"], parameters.Select(parameter => parameter.Name!));
        Assert.Equal(typeof(IReadOnlyList<Guid>), parameters[^1].ParameterType);
        Assert.True(parameters[^1].HasDefaultValue);
        Assert.Null(parameters[^1].DefaultValue);
        Assert.All(parameters[..^1], parameter => Assert.False(parameter.HasDefaultValue));

        var v1 = new SalesInvoiceableShipmentQueryV1(Account, SalesDraftService.MaximumRiskShares);
        Assert.Null(v1.OrderIds);
        Assert.Equal(v1, v1 with { OrderIds = null });
    }

    [Fact]
    public void The_order_filter_bound_is_the_risk_reads_open_order_bound_and_takes_null_or_one_to_that_many_distinct_ids()
    {
        Assert.Equal(SalesDraftService.MaximumRiskOrders, SalesInvoiceableShipmentQueryV1.MaximumOrderIds);

        Assert.True(SalesInvoiceableShipmentQueryV1.IsValidOrderIds(null));
        Assert.True(SalesInvoiceableShipmentQueryV1.IsValidOrderIds([OrderA, OrderB]));
        Assert.True(SalesInvoiceableShipmentQueryV1.IsValidOrderIds(Ids(SalesInvoiceableShipmentQueryV1.MaximumOrderIds)));

        Assert.False(SalesInvoiceableShipmentQueryV1.IsValidOrderIds([]));
        Assert.False(SalesInvoiceableShipmentQueryV1.IsValidOrderIds([OrderA, OrderA]));
        Assert.False(SalesInvoiceableShipmentQueryV1.IsValidOrderIds([OrderA, Guid.Empty]));
        Assert.False(SalesInvoiceableShipmentQueryV1.IsValidOrderIds(Ids(SalesInvoiceableShipmentQueryV1.MaximumOrderIds + 1)));
    }

    [Fact]
    public void The_party_risk_v2_answer_carries_the_v1_figures_and_the_processed_set_in_one_record()
    {
        var parameters = Assert.Single(typeof(SalesPartyRiskReadAnswerV2).GetConstructors(),
            candidate => candidate.GetParameters().All(parameter => parameter.ParameterType != typeof(SalesPartyRiskReadAnswerV2)))
            .GetParameters();
        Assert.Equal(["CompanyId", "PartyId", "CompanyAccountId", "OpenReceivables", "CreditLimit", "Collateral", "ProcessedSalesInvoices"],
            parameters.Select(parameter => parameter.Name!));
        Assert.Equal(typeof(SalesPartyRiskProcessedInvoicesV2), parameters[^1].ParameterType);
        Assert.Equal(["PartyId", "CompanyAccountId", "SalesInvoiceIds"],
            typeof(SalesPartyRiskReadQueryV2).GetConstructors()
                .Single(candidate => candidate.GetParameters().All(parameter => parameter.ParameterType != typeof(SalesPartyRiskReadQueryV2)))
                .GetParameters().Select(parameter => parameter.Name!));
        Assert.Equal(0, (int)SalesPartyRiskProcessedSetStateV2.Unavailable);
        Assert.Equal(SalesDispatchApiV1.MaximumCandidateShares, SalesPartyRiskReadQueryV2.MaximumSalesInvoiceIds);

        var answer = Answer(Available(InvoiceA));
        Assert.Equal(
            new SalesPartyRiskReadAnswerV1(Company, Party, Account, answer.OpenReceivables, answer.CreditLimit, answer.Collateral),
            answer.ToV1());
    }

    [Fact]
    public void A_v2_query_names_a_party_an_account_and_at_most_one_share_bound_of_distinct_invoice_ids()
    {
        Assert.True(SalesPartyRiskReadQueryV2.IsValid(new(Party, Account, [])));
        Assert.True(SalesPartyRiskReadQueryV2.IsValid(new(Party, Account, [InvoiceA, InvoiceB])));
        Assert.True(SalesPartyRiskReadQueryV2.IsValid(new(Party, Account, Ids(SalesPartyRiskReadQueryV2.MaximumSalesInvoiceIds))));

        Assert.False(SalesPartyRiskReadQueryV2.IsValid(null));
        Assert.False(SalesPartyRiskReadQueryV2.IsValid(new(Guid.Empty, Account, [InvoiceA])));
        Assert.False(SalesPartyRiskReadQueryV2.IsValid(new(Party, Guid.Empty, [InvoiceA])));
        Assert.False(SalesPartyRiskReadQueryV2.IsValid(new(Party, Account, [InvoiceA, InvoiceA])));
        Assert.False(SalesPartyRiskReadQueryV2.IsValid(new(Party, Account, [Guid.Empty])));
        Assert.False(SalesPartyRiskReadQueryV2.IsValid(new(Party, Account, Ids(SalesPartyRiskReadQueryV2.MaximumSalesInvoiceIds + 1))));
    }

    [Fact]
    public void An_available_processed_set_holds_only_asked_ids_once_with_available_receivables_and_an_empty_one_means_none_processed()
    {
        var query = new SalesPartyRiskReadQueryV2(Party, Account, [InvoiceA, InvoiceB]);

        Assert.True(SalesPartyRiskReadAnswerV2.IsProcessedSetWellFormed(Answer(Available(InvoiceA)), query));
        Assert.True(SalesPartyRiskReadAnswerV2.IsProcessedSetWellFormed(Answer(Available()), query));

        Assert.False(SalesPartyRiskReadAnswerV2.IsProcessedSetWellFormed(Answer(Available(Unasked)), query));
        Assert.False(SalesPartyRiskReadAnswerV2.IsProcessedSetWellFormed(Answer(Available(InvoiceA, InvoiceA)), query));
        Assert.False(SalesPartyRiskReadAnswerV2.IsProcessedSetWellFormed(
            Answer(Available(InvoiceA) with { ReasonCode = "SENTETIK-reason" }), query));
        Assert.False(SalesPartyRiskReadAnswerV2.IsProcessedSetWellFormed(Answer(Available(InvoiceA) with { Source = " " }), query));
    }

    [Fact]
    public void A_processed_set_without_available_receivables_from_the_same_answer_is_never_well_formed()
    {
        var query = new SalesPartyRiskReadQueryV2(Party, Account, [InvoiceA]);
        var unavailableReceivables = new SalesPartyRiskFigureV1(SalesPartyRiskFigureStateV1.Unavailable, FinanceSource, [], null,
            "SENTETIK-owner-failure");

        Assert.False(SalesPartyRiskReadAnswerV2.IsProcessedSetWellFormed(
            Answer(Available(InvoiceA)) with { OpenReceivables = unavailableReceivables }, query));
        Assert.True(SalesPartyRiskReadAnswerV2.IsProcessedSetWellFormed(
            Answer(Unavailable("SENTETIK-owner-failure")) with { OpenReceivables = unavailableReceivables }, query));
    }

    [Fact]
    public void An_unavailable_processed_set_carries_a_reason_and_no_ids_and_another_customer_or_an_invalid_query_is_refused()
    {
        var query = new SalesPartyRiskReadQueryV2(Party, Account, [InvoiceA]);

        Assert.True(SalesPartyRiskReadAnswerV2.IsProcessedSetWellFormed(Answer(Unavailable("SENTETIK-owner-failure")), query));
        Assert.False(SalesPartyRiskReadAnswerV2.IsProcessedSetWellFormed(Answer(Unavailable(" ")), query));
        Assert.False(SalesPartyRiskReadAnswerV2.IsProcessedSetWellFormed(
            Answer(Unavailable("SENTETIK-owner-failure") with { ProcessedSalesInvoiceIds = [InvoiceA] }), query));
        Assert.False(SalesPartyRiskReadAnswerV2.IsProcessedSetWellFormed(
            Answer(Available(InvoiceA) with { State = (SalesPartyRiskProcessedSetStateV2)99 }), query));

        Assert.False(SalesPartyRiskReadAnswerV2.IsProcessedSetWellFormed(Answer(Available(InvoiceA)) with { PartyId = Unasked }, query));
        Assert.False(SalesPartyRiskReadAnswerV2.IsProcessedSetWellFormed(
            Answer(Available(InvoiceA)) with { CompanyAccountId = Unasked }, query));
        Assert.False(SalesPartyRiskReadAnswerV2.IsProcessedSetWellFormed(Answer(Available(InvoiceA)), query with { SalesInvoiceIds = [Guid.Empty] }));
        Assert.False(SalesPartyRiskReadAnswerV2.IsProcessedSetWellFormed(null, query));
        Assert.False(SalesPartyRiskReadAnswerV2.IsProcessedSetWellFormed(Answer(Available(InvoiceA)), null));
    }

    private static SalesPartyRiskProcessedInvoicesV2 Available(params Guid[] processed) =>
        new(SalesPartyRiskProcessedSetStateV2.Available, FinanceSource, processed, null);

    private static SalesPartyRiskProcessedInvoicesV2 Unavailable(string reason) =>
        new(SalesPartyRiskProcessedSetStateV2.Unavailable, FinanceSource, [], reason);

    private static SalesPartyRiskReadAnswerV2 Answer(SalesPartyRiskProcessedInvoicesV2 processed) =>
        new(Company, Party, Account,
            new SalesPartyRiskFigureV1(SalesPartyRiskFigureStateV1.Available, FinanceSource, [new(Currency, 1000m)], true, null),
            new SalesPartyRiskFigureV1(SalesPartyRiskFigureStateV1.NoSource, FinanceSource, [], null, "finance_credit_limit_not_set"),
            new SalesPartyRiskFigureV1(SalesPartyRiskFigureStateV1.NoSource, FinanceSource, [], null, "SENTETIK-no-collateral"),
            processed);

    private static Guid[] Ids(int count) =>
        [.. Enumerable.Range(1, count).Select(index => new Guid(index, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]))];
}
