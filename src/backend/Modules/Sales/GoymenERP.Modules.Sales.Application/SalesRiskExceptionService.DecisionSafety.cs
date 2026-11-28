using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.RiskExceptions;

namespace GoymenERP.Modules.Sales.Application;

public sealed partial class SalesRiskExceptionService
{
    private bool riskExceptionDecisionSafety;
    // Root enables this on the existing scoped service only after shared reads and reciprocal markers are fenced.
    public SalesRiskExceptionService WithRiskExceptionDecisionSafety()
    { riskExceptionDecisionSafety = true; return this; }

    private async Task<SalesRiskExceptionCommandResult> RunRiskExceptionDecisionAsync(Guid id, int no,
        SalesDraftStateCommandRequest? request, string command, string permission, SalesCommandMetadata metadata,
        Action<SalesShipmentRiskException, string, long, DateTimeOffset> decide, SalesDraftStateAudit audit, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(metadata);
        token.ThrowIfCancellationRequested();
        var boundary = exceptions as ISalesRiskExceptionDecisionSafetyStoreV1;
        var cleanBefore = boundary?.RiskExceptionDecisionEntryIsClean == true;
        var scope = await RequireAsync(permission, token); token.ThrowIfCancellationRequested();
        // Exact native version shape/order; do not canonicalize or replace the native request hash.
        if (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1)
            throw new SalesDraftServiceException(400, "validation_failed", [new("expectedVersion", "validation_failed")]);
        if (boundary is null || !ReferenceEquals(exceptions, orders) || !cleanBefore || !boundary.RiskExceptionDecisionEntryIsClean)
            throw new SalesDraftServiceException(503, "service_unavailable");
        var hash = SalesRiskExceptionHash.Compute(command, id, no, request);
        var envelope = Envelope(scope, permission, metadata);
        try
        {
            return await boundary.RunRiskExceptionDecisionAsync(envelope, hash, command, cleanBefore,
                ct => DecideCoreAsync(id, no, request, command, permission, metadata, decide, audit, ct),
                async ct =>
                {
                    var replay = await FindReplayAsync(scope, permission, envelope, hash, command, ct);
                    return replay is null ? null : Replayed(replay);
                }, async ct => { await RequireStillAsync(scope, permission, ct); ct.ThrowIfCancellationRequested(); }, token);
        }
        catch (SalesDraftStoreException refusal) { throw Store(refusal); }
    }
}
