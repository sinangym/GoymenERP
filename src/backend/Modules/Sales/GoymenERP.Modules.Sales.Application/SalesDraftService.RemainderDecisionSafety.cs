using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Application;

public sealed partial class SalesDraftService
{
    private bool remainderDecisionSafety;
    // Default-off until root composes this same scoped core with the shared read/save/terminal relays.
    public SalesDraftService WithRemainderDecisionSafety()
    { remainderDecisionSafety = true; return this; }

    private async Task<SalesOrderDraftResponse> RunRemainderDecisionAsync(Guid draftId, int changeNo,
        SalesDraftRemainderDecisionRequest request, SalesCommandMetadata metadata, bool approving, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(metadata);
        token.ThrowIfCancellationRequested();
        var boundary = store as ISalesRemainderDecisionSafetyStoreV1;
        var cleanBeforeAuthority = boundary?.RemainderDecisionEntryIsClean == true;
        var permission = bindings.ApprovePermission;
        var scope = await RequireAsync(permission, token); token.ThrowIfCancellationRequested();
        _ = ExpectedVersion(request.ExpectedVersion); // Native NumberStyles.None accepts the same raw version spelling.
        if (approving && request.Reason is not null)
            throw new SalesDraftServiceException(400, "validation_failed", [new("reason", "validation_failed")]);
        var command = approving ? SalesDraftCommands.ApproveRemainderChangeV1 : SalesDraftCommands.RejectRemainderChangeV1;
        var hash = SalesDraftRequestHash.Compute(command, draftId, changeNo, request);
        var envelope = Envelope(scope, metadata, permission!);
        if (boundary is null || !cleanBeforeAuthority || !boundary.RemainderDecisionEntryIsClean)
            throw new SalesDraftServiceException(503, "service_unavailable");
        try
        {
            var result = await boundary.RunRemainderDecisionAsync(envelope, hash, command, cleanBeforeAuthority,
                ct => DecideRemainderChangeCoreAsync(draftId, changeNo, request, metadata, approving, ct),
                async ct =>
                {
                    var replay = await FindReplayAsync(scope, permission, envelope, hash, command, ct);
                    return replay is null ? null : await ReplayedAsync(scope, replay, ct);
                }, async ct => { scope = await RequireStillAsync(scope, permission, ct); ct.ThrowIfCancellationRequested(); }, token);
            scope = await RequireStillAsync(scope, permission, token); token.ThrowIfCancellationRequested();
            var disclosure = await ReplayDisclosureAsync(scope, token); token.ThrowIfCancellationRequested();
            scope = await RequireStillAsync(disclosure.Scope, permission, token); token.ThrowIfCancellationRequested();
            // Observe optional prices last. This only reduces the native response, never upgrades an original withheld answer.
            var finalPrice = await DiscloseAsync(scope, token); token.ThrowIfCancellationRequested();
            if (!Matches(scope.CompanyId, scope.ActorId)) throw new SalesDraftServiceException(403, "forbidden");
            if (disclosure.Disclosure == SalesPriceDisclosure.Withheld || finalPrice == SalesPriceDisclosure.Withheld)
                result = result with { Detail = result.Detail with { Lines = result.Detail.Lines.Select(line => line with
                    { UnitPrice = null, PriceUnitId = null, DiscountPercent = null, PriceDisclosure = SalesPriceDisclosure.Withheld }).ToArray() } };
            return result with { Detail = result.Detail with { IsOwnDecisionRestricted = result.Detail.IsOwnDraft && scope.IsSalesSupport } };
        }
        catch (SalesDraftStoreException refusal) { throw Store(refusal); }
    }
}
