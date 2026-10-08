using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Modules.Counterparty.Application;

/// <summary>
/// Owner application boundary. Only the central Sales adapter may consume the public source, and
/// only after it has authorized the Sales records that supplied the Party ids.
/// </summary>
public sealed class CounterpartySalesRecordPartyDisplaySourceV1(
    ICounterpartySalesRecordPartyDisplayOwnerReaderV1 ownerReader)
    : ICounterpartySalesRecordPartyDisplaySourceV1
{
    private const int MaximumPartyCount = 100;

    public async Task<CounterpartySalesRecordPartyDisplayAnswerV1> ReadForAuthorizedSalesRecordsAsync(
        CounterpartySalesRecordPartyDisplayRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryCanonicalize(request, out var canonical))
        {
            return Closed(CounterpartySalesRecordPartyDisplayOutcomeV1.Denied);
        }

        CounterpartySalesRecordPartyDisplayAnswerV1 answer;
        try
        {
            answer = await ownerReader.ReadAsync(canonical, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Closed(CounterpartySalesRecordPartyDisplayOutcomeV1.Denied);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Closed(CounterpartySalesRecordPartyDisplayOutcomeV1.Unavailable);
        }

        if (answer.Outcome != CounterpartySalesRecordPartyDisplayOutcomeV1.Found)
        {
            return Closed(answer.Outcome);
        }

        var requested = canonical.PartyIds.ToHashSet();
        var seen = new HashSet<Guid>();
        if (answer.Items is null
            || answer.Items.Any(item => item is null
                || item.PartyId == Guid.Empty
                || !requested.Contains(item.PartyId)
                || !seen.Add(item.PartyId)
                || string.IsNullOrWhiteSpace(item.Code)
                || string.IsNullOrWhiteSpace(item.LegalName)))
        {
            return Closed(CounterpartySalesRecordPartyDisplayOutcomeV1.Unavailable);
        }

        return answer with { Items = answer.Items.OrderBy(item => item.PartyId).ToArray() };
    }

    private static bool TryCanonicalize(
        CounterpartySalesRecordPartyDisplayRequestV1? request,
        out CounterpartySalesRecordPartyDisplayRequestV1 canonical)
    {
        canonical = null!;
        if (request is null
            || request.CompanyId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.ActorId)
            || request.PartyIds is null
            || request.PartyIds.Count is 0 or > MaximumPartyCount
            || request.PartyIds.Any(id => id == Guid.Empty))
        {
            return false;
        }

        var partyIds = request.PartyIds.Distinct().Order().ToArray();
        if (partyIds.Length != request.PartyIds.Count)
        {
            return false;
        }

        canonical = request with { PartyIds = partyIds };
        return true;
    }

    private static CounterpartySalesRecordPartyDisplayAnswerV1 Closed(
        CounterpartySalesRecordPartyDisplayOutcomeV1 outcome) => new(outcome, []);
}

/// <summary>
/// Owner-local persistence port. It is not a consumer contract and must only be registered by the
/// Counterparty module for <see cref="CounterpartySalesRecordPartyDisplaySourceV1"/>.
/// </summary>
public interface ICounterpartySalesRecordPartyDisplayOwnerReaderV1
{
    Task<CounterpartySalesRecordPartyDisplayAnswerV1> ReadAsync(
        CounterpartySalesRecordPartyDisplayRequestV1 request,
        CancellationToken cancellationToken = default);
}
