using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Application;

public sealed partial class SalesDraftService
{
    private bool priceFloorApprovalSafety;

    // All aliases of the same scoped production core remain protected once composed.
    public SalesDraftService WithPriceFloorApprovalSafety()
    {
        priceFloorApprovalSafety = true;
        return this;
    }

    private async Task<SalesOrderDraftResponse> RunPriceFloorApprovalAsync(Guid draftId,
        SalesDraftPriceFloorExceptionApprovalRequest request, SalesCommandMetadata metadata, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(metadata);
        token.ThrowIfCancellationRequested();
        var boundary = store as ISalesPriceFloorApprovalStoreV1;
        var cleanBeforeAuthority = boundary?.PriceFloorApprovalEntryIsClean == true;
        var permission = bindings.PriceFloorExceptionApprovePermission;
        var scope = await RequireAsync(permission, token);
        token.ThrowIfCancellationRequested();
        ValidatePriceFloorApprovalShape(request); // Preserve native authorized shape refusal before entry ownership.
        if (boundary is null || !cleanBeforeAuthority || !boundary.PriceFloorApprovalEntryIsClean)
            throw new SalesDraftServiceException(503, "service_unavailable");
        const string command = SalesDraftCommands.ApprovePriceFloorExceptionsV1;
        var hash = SalesDraftRequestHash.Compute(command, draftId, request);
        var envelope = Envelope(scope, metadata, permission!);
        try
        {
            var result = await boundary.RunPriceFloorApprovalAsync(envelope, hash, cleanBeforeAuthority,
                ct => ApprovePriceFloorExceptionsCoreAsync(draftId, request, metadata, ct),
                async ct =>
                {
                    var replay = await FindReplayAsync(scope, permission, envelope, hash, command, ct);
                    return replay is null ? null : await ReplayedAsync(scope, replay, ct);
                },
                async ct => { scope = await RequireStillAsync(scope, permission, ct); ct.ThrowIfCancellationRequested(); }, token);
            scope = await RequireStillAsync(scope, permission, token); token.ThrowIfCancellationRequested();
            var disclosure = await ReplayDisclosureAsync(scope, token); token.ThrowIfCancellationRequested();
            scope = await RequireStillAsync(disclosure.Scope, permission, token); token.ThrowIfCancellationRequested();
            // Receipt reads and release fences may revoke PriceRead after the native core chose its response.
            // Only reduce visibility; a newly granted PriceRead never upgrades the first Withheld answer.
            var finalPrice = await DiscloseAsync(scope, token); token.ThrowIfCancellationRequested();
            if (!Matches(scope.CompanyId, scope.ActorId)) throw new SalesDraftServiceException(403, "forbidden");
            if (disclosure.Disclosure == SalesPriceDisclosure.Withheld || finalPrice == SalesPriceDisclosure.Withheld)
            {
                var hidden = result.OriginalFloorApprovalReceipt?.OriginalResult.Withheld
                    ?? result.Detail with { Lines = result.Detail.Lines.Select(line => line with
                    { UnitPrice = null, PriceUnitId = null, DiscountPercent = null, PriceDisclosure = SalesPriceDisclosure.Withheld }).ToArray() };
                result = result with { Detail = hidden with { IsOwnDecisionRestricted = hidden.IsOwnDraft && scope.IsSalesSupport } };
            }
            if (result.OriginalFloorApprovalReceipt is { } receipt)
                result = result with { OriginalFloorApprovalReceipt = receipt with
                { CurrentResponseDigest = SalesPriceFloorDecisionDigestsV1.CurrentResponseDigest(result) } };
            return result;
        }
        catch (SalesDraftStoreException exception) { throw Store(exception); }
    }

    private static long ValidatePriceFloorApprovalShape(SalesDraftPriceFloorExceptionApprovalRequest request)
    {
        var validationErrors = new List<SalesDraftFieldError>();
        if (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion)
            || expectedVersion < 1)
        {
            validationErrors.Add(new("expectedVersion", "validation_failed"));
        }
        if (request.Lines is null || request.Lines.Any(static line => line is null))
        {
            validationErrors.Add(new("lines", "validation_failed"));
        }
        if (validationErrors.Count > 0)
        {
            throw new SalesDraftServiceException(400, "validation_failed", validationErrors);
        }
        return expectedVersion;
    }
}
