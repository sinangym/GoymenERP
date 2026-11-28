using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.SalesContracts;

namespace GoymenERP.Modules.Sales.Application;

public sealed partial class SalesContractService
{
    private bool contractDiscountDecisionSafety;
    // Default-off until the configured native service is composed with all owned read/save/refusal boundaries.
    public SalesContractService WithContractDiscountDecisionSafety()
    { contractDiscountDecisionSafety = true; return this; }

    private async Task<SalesContractCommandResult> RunContractDiscountDecisionAsync(Guid contractId,
        SalesDraftStateCommandRequest request, string commandName, SalesCommandMetadata metadata,
        Action<SalesContract, string, long, DateTimeOffset> decide, SalesDraftStateAudit audit, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(metadata); token.ThrowIfCancellationRequested();
        var boundary = contracts as ISalesContractDiscountDecisionSafetyStoreV1;
        var cleanBeforeAuthority = boundary?.ContractDiscountDecisionEntryIsClean == true;
        var scope = await RequireAsync(ApprovePermission, token); token.ThrowIfCancellationRequested();
        // Preserve the native legacy version spelling and reason behavior; no new command policy is introduced here.
        if (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1)
            throw new SalesDraftServiceException(400, "validation_failed", [new("expectedVersion", "validation_failed")]);
        var hash = SalesContractHash.Compute(commandName, contractId, request);
        var envelope = Envelope(scope, ApprovePermission, metadata);
        if (boundary is null || !cleanBeforeAuthority || !boundary.ContractDiscountDecisionEntryIsClean)
            throw new SalesDraftServiceException(503, "service_unavailable");
        try
        {
            var answer = await boundary.RunContractDiscountDecisionAsync(envelope, hash, commandName, cleanBeforeAuthority,
                ct => DecideCoreAsync(contractId, request, commandName, metadata, decide, audit, ct),
                async ct =>
                {
                    var replay = await FindReplayAsync(scope, ApprovePermission, envelope, hash, commandName, ct);
                    return replay is null ? null : await ReplayedAsync(scope, replay, ct);
                }, ct => RequireStillAsync(scope, ApprovePermission, ct), token);
            await RequireStillAsync(scope, ApprovePermission, token); token.ThrowIfCancellationRequested();
            var firstDisclosure = await DiscloseAmountsAsync(scope, token); token.ThrowIfCancellationRequested();
            await RequireStillAsync(scope, ApprovePermission, token); token.ThrowIfCancellationRequested();
            var finalDisclosure = await DiscloseAmountsAsync(scope, token); token.ThrowIfCancellationRequested();
            if (!Matches(scope.CompanyId, scope.ActorId)) throw new SalesDraftServiceException(403, "forbidden");
            // A late price grant never upgrades an already withheld native result; a late loss removes every amount/rate.
            if (firstDisclosure == SalesPriceDisclosure.Withheld || finalDisclosure == SalesPriceDisclosure.Withheld)
                answer = answer with { Contract = answer.Contract with
                {
                    AmountDisclosure = SalesPriceDisclosure.Withheld, PlannedTotalAmount = null, DiscountLimitPercent = null,
                    Installments = answer.Contract.Installments.Select(x => x with { Amount = null }).ToArray(),
                    DiscountRules = answer.Contract.DiscountRules.Select(x => x with { RatePercent = null }).ToArray(),
                } };
            return answer;
        }
        catch (SalesDraftStoreException failure) { throw Store(failure); }
    }
}
