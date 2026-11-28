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
            return await boundary.RunPriceFloorApprovalAsync(envelope, hash, cleanBeforeAuthority,
                ct => ApprovePriceFloorExceptionsCoreAsync(draftId, request, metadata, ct),
                async ct =>
                {
                    var replay = await FindReplayAsync(scope, permission, envelope, hash, command, ct);
                    return replay is null ? null : await ReplayedAsync(scope, replay, ct);
                },
                async ct => { scope = await RequireStillAsync(scope, permission, ct); ct.ThrowIfCancellationRequested(); }, token);
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
