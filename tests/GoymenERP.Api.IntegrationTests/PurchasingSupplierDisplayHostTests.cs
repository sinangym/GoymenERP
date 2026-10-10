using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class PurchasingSupplierDisplayHostTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Party = Guid.Parse("77000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Host_maps_the_exact_record_bound_request_and_owner_display()
    {
        var owner = new StubOwner
        {
            Answer = new(
                CounterpartyPurchasingRecordPartyDisplayOutcomeV1.Found,
                [new(Party, "TED-100", "Tedarikçi 100", "T100")]),
        };
        var host = new PurchasingSupplierDisplayHost(owner);

        var answer = await host.GetAuthorizedBatchAsync(
            new PurchasingSupplierDisplayBatchRequestV1(Company, "purchasing-reader", [Party]));

        Assert.Equal(PurchasingSupplierDisplayBatchOutcomeV1.Success, answer.Outcome);
        var item = Assert.Single(answer.Items);
        Assert.Equal((Party, "TED-100", "Tedarikçi 100", (string?)"T100"),
            (item.PartyId, item.Code, item.LegalName, item.TradeName));
        Assert.Equal((Company, "purchasing-reader", Party),
            (owner.Request!.CompanyId, owner.Request.ActorId, Assert.Single(owner.Request.PartyIds)));
    }

    [Theory]
    [InlineData(CounterpartyPurchasingRecordPartyDisplayOutcomeV1.Denied, PurchasingSupplierDisplayBatchOutcomeV1.Denied)]
    [InlineData(CounterpartyPurchasingRecordPartyDisplayOutcomeV1.Unavailable, PurchasingSupplierDisplayBatchOutcomeV1.Unavailable)]
    public async Task Host_preserves_closed_owner_outcomes(
        CounterpartyPurchasingRecordPartyDisplayOutcomeV1 ownerOutcome,
        PurchasingSupplierDisplayBatchOutcomeV1 expected)
    {
        var host = new PurchasingSupplierDisplayHost(new StubOwner
        {
            Answer = new(ownerOutcome, []),
        });

        var answer = await host.GetAuthorizedBatchAsync(
            new PurchasingSupplierDisplayBatchRequestV1(Company, "purchasing-reader", [Party]));

        Assert.Equal(expected, answer.Outcome);
        Assert.Empty(answer.Items);
    }

    private sealed class StubOwner : ICounterpartyPurchasingRecordPartyDisplaySourceV1
    {
        public required CounterpartyPurchasingRecordPartyDisplayAnswerV1 Answer { get; init; }
        public CounterpartyPurchasingRecordPartyDisplayRequestV1? Request { get; private set; }

        public Task<CounterpartyPurchasingRecordPartyDisplayAnswerV1> ReadForAuthorizedPurchasingRecordsAsync(
            CounterpartyPurchasingRecordPartyDisplayRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(Answer);
        }
    }
}
