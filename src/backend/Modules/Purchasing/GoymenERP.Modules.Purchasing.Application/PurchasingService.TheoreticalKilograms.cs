using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;

namespace GoymenERP.Modules.Purchasing.Application;

public sealed partial class PurchasingService
{
    /// <summary>
    /// AK-3 (SAT-D437): before approval, the theoretical kg per piece of every piece general line still without a snapshot is read
    /// from the Product owner (through <see cref="IPurchaseTheoreticalKilogramsPerUnitPortV1"/>) on the order's business day.
    /// Kilogram-only orders, a stale expected version and a non-Draft order read nothing (the mutation refuses them as before).
    /// A line with no kilogram source is left out, so the domain refuses the approval fail-closed
    /// (<see cref="PurchasingRuleCodes.TheoreticalKilogramsMissing"/>); denied is 403 and an unreadable owner 503. Nothing is
    /// guessed, defaulted or rounded.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, PurchaseOrderLineTheoreticalKilograms>> ReadTheoreticalKilogramsAsync(Scope scope,
        Guid orderId, long expectedVersion, CancellationToken cancellationToken)
    {
        var permission = bindings.ApprovePermission;
        var stored = await GuardedAsync(scope, permission, () => store.FindOrderAsync(orderId, cancellationToken), cancellationToken);
        if (stored is null || stored.Order.Version != expectedVersion || stored.Order.Status != PurchaseOrderStatus.Draft)
        {
            return new Dictionary<Guid, PurchaseOrderLineTheoreticalKilograms>();
        }

        var pending = stored.Order.Lines.Where(line => line.NeedsTheoreticalKilograms && line.TheoreticalKilograms is null).ToArray();
        var result = new Dictionary<Guid, PurchaseOrderLineTheoreticalKilograms>();
        if (pending.Length == 0) return result;
        var port = theoreticalKilograms
            ?? throw new PurchasingServiceException(503, PurchasingProblemCodesV1.TheoreticalKilogramsUnavailable);
        foreach (var line in pending)
        {
            PurchaseTheoreticalKilogramsPerUnitAnswerV1? answer;
            try
            {
                answer = await GuardedAsync(scope, permission,
                    () => port.ReadAsync(line.ProductId, line.UnitId, stored.EffectiveOn, cancellationToken), cancellationToken);
            }
            catch (PurchasingServiceException) { throw; }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is not UnauthorizedAccessException)
            {
                throw new PurchasingServiceException(503, PurchasingProblemCodesV1.TheoreticalKilogramsUnavailable);
            }

            switch (answer?.Outcome)
            {
                case PurchaseTheoreticalKilogramsPerUnitOutcomeV1.Found
                    when answer.KilogramsPerUnit is { } kilograms && answer.SourceVersion is { } source:
                    try
                    {
                        result.Add(line.Id, new PurchaseOrderLineTheoreticalKilograms(kilograms, source));
                    }
                    catch (PurchasingRuleException)
                    {
                        // A figure the document cannot hold exactly is an unusable owner answer, never a rounded snapshot.
                        throw new PurchasingServiceException(503, PurchasingProblemCodesV1.TheoreticalKilogramsUnavailable);
                    }

                    break;
                case PurchaseTheoreticalKilogramsPerUnitOutcomeV1.NoSource:
                    break; // fail-closed in the domain
                case PurchaseTheoreticalKilogramsPerUnitOutcomeV1.Denied:
                    throw new PurchasingServiceException(403, "forbidden");
                default:
                    throw new PurchasingServiceException(503, PurchasingProblemCodesV1.TheoreticalKilogramsUnavailable);
            }
        }

        return result;
    }
}
