using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

public sealed partial class ProductPriceListService
{
    internal Task<PriceListResultV1> PublishOwnedAsync(Guid id, PublishPriceListVersionRequestV1 request,
        ProductPriceListCommandMetadata metadata, IAuditTransactionWriterV1? writer, CancellationToken token)
    {
        // Capture, never rebind, the company/actor across the owned boundary and every asynchronous release fence.
        var captured = new Scope(companyContext.CompanyId, currentUser.UserId ?? string.Empty, []);
        var first = true;
        return store.RunOwnedPublishAsync(writer, Fence, ct => PublishAsync(id, request, metadata, ct), token);
        async Task Fence(CancellationToken ct)
        {
            if (first)
            {
                first = false;
                // Preserve the native initial 401 / unregistered-permission 503 protocol as well as captured identity.
                var initial = await RequireAsync(ProductPriceListPermissionsV1.Publish, ct);
                if (initial.CompanyId != captured.CompanyId || initial.ActorId != captured.ActorId) throw Forbidden();
            }
            else await RequireStillAsync(ProductPriceListPermissionsV1.Publish, captured, ct);
        }
    }
}

// Mirrors the package-standard facade: one native business implementation, only Publish gains an owned boundary.
internal sealed class GuardedProductPriceListService(ProductPriceListService core, IAuditTransactionWriterV1? writer)
    : IProductPriceListServiceV1
{
    public Task<PriceListResultV1> PublishAsync(Guid id, PublishPriceListVersionRequestV1 request, ProductPriceListCommandMetadata metadata, CancellationToken token = default) => core.PublishOwnedAsync(id, request, metadata, writer, token);
    public Task<PriceListPageV1> ListAsync(int? size, string? cursor, CancellationToken token = default) => core.ListAsync(size, cursor, token);
    public Task<PriceListViewV1> GetAsync(Guid id, CancellationToken token = default) => core.GetAsync(id, token);
    public Task<PriceListProductOptionsV1> GetProductOptionsAsync(string? query, CancellationToken token = default) => core.GetProductOptionsAsync(query, token);
    public Task<PriceListResultV1> CreateAsync(CreatePriceListRequestV1 request, ProductPriceListCommandMetadata metadata, CancellationToken token = default) => core.CreateAsync(request, metadata, token);
    public Task<PriceListResultV1> ReviseDraftAsync(Guid id, RevisePriceListDraftRequestV1 request, ProductPriceListCommandMetadata metadata, CancellationToken token = default) => core.ReviseDraftAsync(id, request, metadata, token);
    public Task<PriceListResultV1> StartVersionAsync(Guid id, StartPriceListVersionRequestV1 request, ProductPriceListCommandMetadata metadata, CancellationToken token = default) => core.StartVersionAsync(id, request, metadata, token);
}
