using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>The read side for the eventual host service; it grants no command capability.</summary>
public sealed class ProductReader(ProductReadStore store, ProductCursorCodec cursors,
    IGroupSharedMasterAuthorizationService authority)
{
    public Task<ProductPage> GetPageAsync(ProductListQuery query, CancellationToken cancellationToken) =>
        ReadAsync(async context =>
        {
            query = ProductQueryRules.Normalize(query);
            var position = query.Cursor is null ? null : cursors.Unprotect(context, query, query.Cursor);
            var page = await store.ReadPageAsync(context, query, position, cancellationToken);
            var groups = await store.ReadCodeGroupsAsync(context, cancellationToken);
            var names = groups.ToDictionary(group => group.Id, group => group.Name);
            var views = page.Items.Select(row => ProductDraftView.FromProduct(row.RestoreProduct(),
                new(names.GetValueOrDefault(row.GroupId), null, null, null))).ToArray();
            return new ProductPage(views, page.Next is null ? null : cursors.Protect(context, query, page.Next));
        }, cancellationToken);

    public Task<ProductCardView?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ReadAsync<ProductCardView?>(async context =>
        {
            var row = await store.FindAsync(context, id, cancellationToken);
            if (row is null) return null;
            var groups = await store.ReadCodeGroupsAsync(context, cancellationToken);
            var label = groups.SingleOrDefault(group => group.Id == row.GroupId)?.Name;
            return new(ProductDraftView.FromProduct(row.RestoreProduct(), new(label, null, null, null)), row.RestoreReferences());
        }, cancellationToken);

    public Task<IReadOnlyList<ProductCodeGroup>> GetCodeGroupsAsync(CancellationToken cancellationToken) =>
        ReadAsync(context => store.ReadCodeGroupsAsync(context, cancellationToken), cancellationToken);

    public Task<ProductReferenceOptions> GetReferenceOptionsAsync(ProductReferenceQuery query, CancellationToken cancellationToken) =>
        ReadAsync(async context =>
        {
            if (query.GroupId == Guid.Empty || query.ClassId == Guid.Empty)
                throw new ProductValidationException([new("query", "invalid_metadata")]);
            var groups = await store.ReadCodeGroupsAsync(context, cancellationToken);
            if (!groups.Any(group => group.Id == query.GroupId))
                throw new ProductValidationException([new("groupId", "invalid_reference")]);
            // ClassId is echoed only as the requested filter, never as a verified
            // catalogue entry. All five unavailable sources remain explicit.
            return new ProductReferenceOptions(query.GroupId, query.ClassId, [], [], [], [], [],
                ["classes", "families", "units", "trackingPolicies", "templates"]);
        }, cancellationToken);

    private async Task<T> ReadAsync<T>(Func<GroupSharedMasterReadContext, Task<T>> read, CancellationToken cancellationToken)
    {
        var initial = await AuthorizeAsync(cancellationToken);
        var result = await read(initial);
        var current = await AuthorizeAsync(cancellationToken);
        if (initial.LocalActorId != current.LocalActorId || initial.OriginCompanyId != current.OriginCompanyId
            || initial.AuthorityRevision != current.AuthorityRevision)
            throw new UnauthorizedAccessException("Product read authority changed during the request.");
        return result;
    }

    private async Task<GroupSharedMasterReadContext> AuthorizeAsync(CancellationToken cancellationToken)
    {
        var snapshot = await authority.GetSnapshotAsync(GroupSharedMasterKind.Product, GroupSharedMasterOperation.Read, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return GroupSharedMasterReadContext.FromServerDecision(snapshot);
    }
}
