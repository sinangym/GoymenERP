using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.FinanceRuntime;

/// <summary>KLN-D71/77: Finance reads a recorded legal succession or duplicate merge through the actual Cari owner.</summary>
public sealed class CounterpartyFinancePartyBalanceTransferOwnerPortV1(
    ICompanyContext company, ICurrentUser user, ICounterpartyReadService parties,
    ICounterpartyIdentityHistoryService identities, ICounterpartyPartyMergeService merges)
    : IFinancePartyBalanceTransferOwnerPortV1
{
    public async Task<FinancePartyBalanceTransferOwnerAnswerV1> ReadAsync(FinanceContext context,
        Guid predecessorPartyId, Guid predecessorAccountId, Guid successorPartyId, Guid successorAccountId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool Current() => company.IsResolved && company.CompanyId == context.CompanyId && context.CompanyId != Guid.Empty
            && user.IsAuthenticated && !string.IsNullOrWhiteSpace(context.ActorId)
            && user.UserId == context.ActorId && user.CanAccessCompany(context.CompanyId);
        static FinancePartyBalanceTransferOwnerAnswerV1 Refuse(FinancePartyBalanceTransferOwnerOutcomeV1 outcome) => new(outcome, null);
        if (!Current() || predecessorPartyId == Guid.Empty || successorPartyId == Guid.Empty
            || predecessorAccountId == Guid.Empty || successorAccountId == Guid.Empty
            || predecessorPartyId == successorPartyId || predecessorAccountId == successorAccountId)
            return Refuse(FinancePartyBalanceTransferOwnerOutcomeV1.InvalidRelation);
        try
        {
            var predecessor = await parties.GetAuthorizedPartyAsync(predecessorPartyId, cancellationToken);
            if (!Current()) return Refuse(FinancePartyBalanceTransferOwnerOutcomeV1.Unavailable);
            var successor = await parties.GetAuthorizedPartyAsync(successorPartyId, cancellationToken);
            if (!Current()) return Refuse(FinancePartyBalanceTransferOwnerOutcomeV1.Unavailable);
            if (predecessor is null || successor is null)
                return Refuse(FinancePartyBalanceTransferOwnerOutcomeV1.NotFound);
            var previousAccounts = predecessor.CompanyAccounts.Where(x => x.CompanyId == context.CompanyId && x.Id == predecessorAccountId).ToArray();
            var nextAccounts = successor.CompanyAccounts.Where(x => x.CompanyId == context.CompanyId && x.Id == successorAccountId).ToArray();
            if (predecessor.Id != predecessorPartyId || successor.Id != successorPartyId || previousAccounts.Length != 1 || nextAccounts.Length != 1)
                return Refuse(FinancePartyBalanceTransferOwnerOutcomeV1.NotFound);
            var previous = previousAccounts[0]; var next = nextAccounts[0];
            if (predecessor.IsActive && previous.IsActive || !successor.IsActive || !next.IsActive)
                return Refuse(FinancePartyBalanceTransferOwnerOutcomeV1.InvalidRelation);
            if (new[] { predecessor.Version, successor.Version, previous.Version, next.Version }.Any(value => !Version(value))
                || new[] { predecessor.Code, predecessor.LegalName, successor.Code, successor.LegalName }.Any(string.IsNullOrWhiteSpace))
                return Refuse(FinancePartyBalanceTransferOwnerOutcomeV1.Unavailable);

            var history = await identities.GetIdentityHistoryAsync(predecessorPartyId, cancellationToken);
            if (!Current()) return Refuse(FinancePartyBalanceTransferOwnerOutcomeV1.Unavailable);
            if (history is null || history.CompanyId != context.CompanyId || history.PartyId != predecessorPartyId
                || history.AccountVersion != previous.Version || history.AccountIsActive != previous.IsActive)
                return Refuse(FinancePartyBalanceTransferOwnerOutcomeV1.Unavailable);
            FinancePartyBalanceTransferLinkKindV1 kind;
            DateOnly? effective = null; string? basis = null; DateTimeOffset? recorded = null;
            if (history.Successor is { } link)
            {
                if (link.PredecessorPartyId != predecessorPartyId || link.SuccessorPartyId != successorPartyId
                    || link.OtherPartyId != successorPartyId || previous.IsActive || link.EffectiveOn == default
                    || link.RecordedAtUtc == default || string.IsNullOrWhiteSpace(link.LegalBasis))
                    return Refuse(FinancePartyBalanceTransferOwnerOutcomeV1.InvalidRelation);
                kind = FinancePartyBalanceTransferLinkKindV1.Succession;
                effective = link.EffectiveOn; basis = link.LegalBasis; recorded = link.RecordedAtUtc;
            }
            else
            {
                var merge = await merges.GetMergeAsync(predecessorPartyId, cancellationToken);
                if (!Current()) return Refuse(FinancePartyBalanceTransferOwnerOutcomeV1.Unavailable);
                if (merge is null || merge.CompanyId != context.CompanyId || merge.PartyId != predecessorPartyId
                    || merge.Version != predecessor.Version)
                    return Refuse(FinancePartyBalanceTransferOwnerOutcomeV1.Unavailable);
                if (predecessor.IsActive || merge.MergedInto?.PartyId != successorPartyId)
                    return Refuse(FinancePartyBalanceTransferOwnerOutcomeV1.InvalidRelation);
                kind = FinancePartyBalanceTransferLinkKindV1.DuplicateMerge;
            }
            return new(FinancePartyBalanceTransferOwnerOutcomeV1.Available, new(context.CompanyId,
                predecessorPartyId, predecessorAccountId, predecessor.Version, previous.Version, predecessor.Code, predecessor.LegalName,
                successorPartyId, successorAccountId, successor.Version, next.Version, successor.Code, successor.LegalName,
                kind, effective, basis, recorded));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Refuse(FinancePartyBalanceTransferOwnerOutcomeV1.Unavailable);
        }
    }

    private static bool Version(string text) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        && value > 0 && value.ToString(CultureInfo.InvariantCulture) == text;
}
