using System.Globalization;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

/// <summary>
/// ALS-D99: builds the deactivation warning list for one company's account. Every expected owner kind gets one line.
/// The line is read from the single registered owner source of that kind; no source, more than one source, a failing
/// source or a malformed answer all read as <c>unavailable</c> (fail-closed: the actor is warned that it could not be
/// checked, the deactivation itself is not blocked). The account row is read with an explicit CompanyId filter
/// (CompanyPartyAccounts has no ambient query filter), so the reader serves the group preview per company too.
/// </summary>
internal sealed class CounterpartyDeactivationExposureReader(
    CounterpartyDbContext dbContext,
    IEnumerable<ICounterpartyDeactivationExposureSourceV1> sources,
    ILogger<CounterpartyDeactivationExposureReader> logger)
    : ICounterpartyDeactivationExposureReader
{
    private static readonly CounterpartyDeactivationExposureKindV1[] ExpectedKinds =
        Enum.GetValues<CounterpartyDeactivationExposureKindV1>();

    private readonly IReadOnlyList<ICounterpartyDeactivationExposureSourceV1> registeredSources =
        sources.ToArray();

    public async Task<CounterpartyDeactivationExposureView?> ReadAsync(
        Guid companyId,
        string actorId,
        Guid partyId,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty || partyId == Guid.Empty || string.IsNullOrWhiteSpace(actorId))
        {
            throw new ArgumentException("Pasife alma uyarısı için şirket, kullanıcı ve cari zorunludur.");
        }

        var account = await dbContext.CompanyPartyAccounts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.PartyId == partyId)
            .Select(x => new { x.Version, x.IsActive })
            .SingleOrDefaultAsync(cancellationToken);
        if (account is null)
        {
            return null;
        }

        var request = new CounterpartyDeactivationExposureRequestV1(companyId, actorId, partyId);
        var items = new List<CounterpartyDeactivationExposureItemView>(ExpectedKinds.Length);
        foreach (var kind in ExpectedKinds)
        {
            var answer = await ReadKindAsync(kind, request, cancellationToken);
            items.Add(new CounterpartyDeactivationExposureItemView(
                CounterpartyDeactivationExposureWireV1.Kind(kind),
                CounterpartyDeactivationExposureWireV1.Outcome(answer.Outcome),
                answer.Outcome == CounterpartyDeactivationExposureOutcomeV1.Open ? answer.OpenItemCount : null));
        }

        return new CounterpartyDeactivationExposureView(
            companyId,
            partyId,
            account.Version.ToString(CultureInfo.InvariantCulture),
            account.IsActive,
            items,
            items.Any(item => item.Outcome != CounterpartyDeactivationExposureWireV1.Clear));
    }

    private async Task<CounterpartyDeactivationExposureAnswerV1> ReadKindAsync(
        CounterpartyDeactivationExposureKindV1 kind,
        CounterpartyDeactivationExposureRequestV1 request,
        CancellationToken cancellationToken)
    {
        var matching = registeredSources.Where(source => source.Kind == kind).ToArray();
        if (matching.Length != 1)
        {
            // NEEDS-PORT (ALS-D99): no owner adapter registered yet for this kind, or an ambiguous registration.
            return CounterpartyDeactivationExposureAnswerV1.Unavailable;
        }

        CounterpartyDeactivationExposureAnswerV1? answer;
        try
        {
            answer = await matching[0].ReadAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Counterparty deactivation warning source failed; the line reads as unavailable. Kind={Kind} CompanyId={CompanyId} PartyId={PartyId}",
                kind,
                request.CompanyId,
                request.PartyId);
            return CounterpartyDeactivationExposureAnswerV1.Unavailable;
        }

        return IsWellFormed(answer)
            ? answer!
            : CounterpartyDeactivationExposureAnswerV1.Unavailable;
    }

    internal static bool IsWellFormed(CounterpartyDeactivationExposureAnswerV1? answer)
    {
        if (answer is null || !Enum.IsDefined(answer.Outcome))
        {
            return false;
        }

        return answer.Outcome == CounterpartyDeactivationExposureOutcomeV1.Open
            ? answer.OpenItemCount > 0
            : answer.OpenItemCount == 0;
    }
}
