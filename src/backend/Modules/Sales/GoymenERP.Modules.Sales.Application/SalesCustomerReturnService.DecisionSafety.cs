using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.CustomerReturns;

namespace GoymenERP.Modules.Sales.Application;

public sealed partial class SalesCustomerReturnService
{
    private bool customerReturnDecisionSafety;
    // Default-off until the actual native read/save/refusal boundary and its safety acceptance are composed.
    public SalesCustomerReturnService WithCustomerReturnDecisionSafety()
    { customerReturnDecisionSafety = true; return this; }

    private async Task<SalesCustomerReturnCommandResult> RunCustomerReturnDecisionAsync(Guid id,
        SalesDraftStateCommandRequest request, string command, string permission, SalesCommandMetadata metadata,
        Action<SalesCustomerReturn, string, long, DateTimeOffset> decide, SalesDraftStateAudit audit,
        CancellationToken token, bool withReason)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(metadata); token.ThrowIfCancellationRequested();
        var boundary = returns as ISalesCustomerReturnDecisionSafetyStoreV1;
        var cleanBeforeAuthority = boundary?.CustomerReturnDecisionEntryIsClean == true;
        // Native API-40 always requires BOTH operation and price-read permission; do not weaken this to optional disclosure.
        var scope = await RequireAsync(permission, token); token.ThrowIfCancellationRequested();
        _ = ExpectedVersion(request);
        var hash = withReason ? SalesCustomerReturnHash.ComputeDecision(command, id, request) : SalesCustomerReturnHash.Compute(command, id, request);
        var envelope = Envelope(scope, metadata, permission);
        if (boundary is null || !cleanBeforeAuthority || !boundary.CustomerReturnDecisionEntryIsClean)
            throw new SalesDraftServiceException(503, "service_unavailable");
        try
        {
            var answer = await boundary.RunCustomerReturnDecisionAsync(envelope, hash, command, cleanBeforeAuthority,
                ct => DecideCoreAsync(id, request, command, permission, metadata, decide, audit, ct, withReason),
                async ct =>
                {
                    var replay = await FindReplayAsync(scope, envelope, hash, command, ct, permission);
                    return replay is null ? null : Replayed(replay);
                }, ct => RequireStillAsync(scope, permission, ct), token);
            await RequireStillAsync(scope, permission, token); token.ThrowIfCancellationRequested();
            if (!Matches(scope.CompanyId, scope.ActorId)) throw new SalesDraftServiceException(403, "forbidden");
            return answer;
        }
        catch (SalesDraftStoreException failure) { throw Store(failure); }
    }
}
