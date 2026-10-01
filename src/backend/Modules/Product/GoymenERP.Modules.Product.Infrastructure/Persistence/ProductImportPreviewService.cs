using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

public sealed record ProductImportCatalogSnapshot(
    IReadOnlySet<string> RegisteredCodes,
    IReadOnlySet<Guid> CodeGroupIds);

/// <summary>Product-owned catalogue read used only by the import preview boundary.</summary>
public interface IProductImportCatalogReader
{
    Task<ProductImportCatalogSnapshot> ReadAsync(GroupSharedMasterCommandContext authority,
        CancellationToken cancellationToken);
}

public sealed class ProductImportCatalogReader(ProductDbContext context) : IProductImportCatalogReader
{
    public async Task<ProductImportCatalogSnapshot> ReadAsync(GroupSharedMasterCommandContext authority,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (authority.Subject != GroupSharedMasterKind.Product
            || authority.Operation != GroupSharedMasterOperation.DraftCreate)
            throw new UnauthorizedAccessException("Product import catalogue requires DraftCreate authority.");

        var codes = await context.CodeRegistrations.AsNoTracking().Select(row => row.Code)
            .ToHashSetAsync(cancellationToken);
        var groups = await context.CodeGroups.AsNoTracking().Select(row => row.Id)
            .ToHashSetAsync(cancellationToken);
        return new(codes, groups);
    }
}

/// <summary>
/// Q16 preview coordinator. It authorizes before any Product catalogue read, reauthorizes after every awaited read
/// outcome, and binds the immutable preview to actor, origin company and authority revision. It never writes.
/// </summary>
public sealed class ProductImportPreviewService(
    IGroupSharedMasterAuthorizationService authority,
    IProductImportCatalogReader catalog,
    IProductImportWorkbookReaderV1 workbookReader) : IProductImportPreviewServiceV1
{
    public async Task<ProductImportPreviewV1> PreviewAsync(ReadOnlyMemory<byte> workbook,
        ProductImportColumnMappingV1 mapping, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        var initial = await AuthorizeAsync(cancellationToken);
        ProductImportPreviewV1 preview;
        try
        {
            var snapshot = await catalog.ReadAsync(initial, cancellationToken);
            preview = workbookReader.Read(workbook, mapping, snapshot.RegisteredCodes, snapshot.CodeGroupIds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await EnsureUnchangedAuthorityAsync(initial, cancellationToken);
            throw;
        }

        await EnsureUnchangedAuthorityAsync(initial, cancellationToken);
        return preview with
        {
            PreviewToken = ProductImportPreviewTokenV1.Bind(preview, initial.LocalActorId,
                initial.OriginCompanyId, initial.AuthorityRevision),
        };
    }

    private async Task<GroupSharedMasterCommandContext> AuthorizeAsync(CancellationToken cancellationToken)
    {
        var snapshot = await authority.GetSnapshotAsync(GroupSharedMasterKind.Product,
            GroupSharedMasterOperation.DraftCreate, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return GroupSharedMasterCommandContext.FromServerDecision(snapshot, GroupSharedMasterOperation.DraftCreate);
    }

    private async Task EnsureUnchangedAuthorityAsync(GroupSharedMasterCommandContext initial,
        CancellationToken cancellationToken)
    {
        var current = await AuthorizeAsync(cancellationToken);
        if (initial.LocalActorId != current.LocalActorId
            || initial.OriginCompanyId != current.OriginCompanyId
            || !string.Equals(initial.AuthorityRevision, current.AuthorityRevision, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Product import authority changed during preview.");
    }
}
