using GoymenERP.Modules.Counterparty.Application;
using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CounterpartySalesRecordPartyDisplaySourceTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid PartyA = Guid.Parse("71000000-0000-0000-0000-000000000001");
    private static readonly Guid PartyB = Guid.Parse("71000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task Authorized_record_ids_are_canonicalized_once_and_missing_rows_remain_omitted()
    {
        var reader = new Reader
        {
            Answer = new(
                CounterpartySalesRecordPartyDisplayOutcomeV1.Found,
                [new(PartyB, "CAR-B", "Cari B", null)]),
        };
        var source = new CounterpartySalesRecordPartyDisplaySourceV1(reader);

        var answer = await source.ReadForAuthorizedSalesRecordsAsync(
            new(Company, "sales-reader", [PartyB, PartyA]));

        Assert.Equal(CounterpartySalesRecordPartyDisplayOutcomeV1.Found, answer.Outcome);
        Assert.Equal(PartyB, Assert.Single(answer.Items).PartyId);
        Assert.Equal([PartyA, PartyB], Assert.Single(reader.Requests).PartyIds);
    }

    [Fact]
    public async Task Empty_duplicate_noncanonical_and_over_bound_requests_are_denied_before_the_owner_reader()
    {
        var reader = new Reader();
        var source = new CounterpartySalesRecordPartyDisplaySourceV1(reader);
        var requests = new CounterpartySalesRecordPartyDisplayRequestV1[]
        {
            new(Guid.Empty, "sales-reader", [PartyA]),
            new(Company, " ", [PartyA]),
            new(Company, "sales-reader", []),
            new(Company, "sales-reader", [Guid.Empty]),
            new(Company, "sales-reader", [PartyA, PartyA]),
            new(Company, "sales-reader", Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray()),
        };

        foreach (var request in requests)
        {
            var answer = await source.ReadForAuthorizedSalesRecordsAsync(request);
            Assert.Equal(CounterpartySalesRecordPartyDisplayOutcomeV1.Denied, answer.Outcome);
            Assert.Empty(answer.Items);
        }

        Assert.Empty(reader.Requests);
    }

    [Fact]
    public async Task Foreign_duplicate_or_malformed_owner_rows_fail_closed_without_disclosure()
    {
        var cases = new IReadOnlyList<CounterpartySalesRecordPartyDisplayV1>[]
        {
            [new(Guid.NewGuid(), "CAR-X", "Foreign", null)],
            [new(PartyA, "CAR-A", "Cari A", null), new(PartyA, "CAR-A", "Cari A", null)],
            [new(PartyA, " ", "Cari A", null)],
        };

        foreach (var items in cases)
        {
            var reader = new Reader
            {
                Answer = new(CounterpartySalesRecordPartyDisplayOutcomeV1.Found, items),
            };
            var answer = await new CounterpartySalesRecordPartyDisplaySourceV1(reader)
                .ReadForAuthorizedSalesRecordsAsync(new(Company, "sales-reader", [PartyA]));
            Assert.Equal(CounterpartySalesRecordPartyDisplayOutcomeV1.Unavailable, answer.Outcome);
            Assert.Empty(answer.Items);
        }
    }

    [Fact]
    public async Task Owner_denial_failure_and_cancellation_keep_distinct_closed_outcomes()
    {
        var denied = await new CounterpartySalesRecordPartyDisplaySourceV1(
                new Reader { Exception = new UnauthorizedAccessException() })
            .ReadForAuthorizedSalesRecordsAsync(new(Company, "sales-reader", [PartyA]));
        var unavailable = await new CounterpartySalesRecordPartyDisplaySourceV1(
                new Reader { Exception = new InvalidOperationException() })
            .ReadForAuthorizedSalesRecordsAsync(new(Company, "sales-reader", [PartyA]));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Equal(CounterpartySalesRecordPartyDisplayOutcomeV1.Denied, denied.Outcome);
        Assert.Equal(CounterpartySalesRecordPartyDisplayOutcomeV1.Unavailable, unavailable.Outcome);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CounterpartySalesRecordPartyDisplaySourceV1(new Reader())
                .ReadForAuthorizedSalesRecordsAsync(new(Company, "sales-reader", [PartyA]), cancellation.Token));
    }

    private sealed class Reader : ICounterpartySalesRecordPartyDisplayOwnerReaderV1
    {
        public List<CounterpartySalesRecordPartyDisplayRequestV1> Requests { get; } = [];
        public CounterpartySalesRecordPartyDisplayAnswerV1 Answer { get; init; } =
            new(CounterpartySalesRecordPartyDisplayOutcomeV1.Found, []);
        public Exception? Exception { get; init; }

        public Task<CounterpartySalesRecordPartyDisplayAnswerV1> ReadAsync(
            CounterpartySalesRecordPartyDisplayRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Exception is null
                ? Task.FromResult(Answer)
                : Task.FromException<CounterpartySalesRecordPartyDisplayAnswerV1>(Exception);
        }
    }
}
