using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Api.DocumentsRuntime;

public sealed class LogisticsDispatchDocumentOwnerLinkPort(ILogisticsWeighingDifferenceScopeV1 scopes,
    ILogisticsWeighingDifferenceStoreV1 store, LogisticsWeighingDifferenceBindingsV1 bindings) : IDocumentOwnerLinkPortV1
{
    public string Kind => DocumentOwnerLinkKindsV1.LogisticsDispatchNote;

    public async Task<DocumentOwnerAccessV1> CheckReadableAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        if (ownerId == Guid.Empty) return DocumentOwnerAccessV1.NotFound;
        try
        {
            var scope = await scopes.RequireAsync(bindings.ReadPermission, cancellationToken);
            var source = await store.ReadSourceAsync(ownerId, cancellationToken);
            await scopes.RequireStillAsync(scope, bindings.ReadPermission, cancellationToken);
            return source?.DispatchNoteId == ownerId ? DocumentOwnerAccessV1.Readable : DocumentOwnerAccessV1.NotFound;
        }
        catch (OperationCanceledException) { throw; }
        catch (LogisticsWeighingDifferenceServiceException exception) when (exception.Status is 401 or 403)
        { return DocumentOwnerAccessV1.Denied; }
        catch { return DocumentOwnerAccessV1.Unavailable; }
    }
}
