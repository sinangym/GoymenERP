using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>
/// The first Draft slice has a real Product code-group catalogue. Other registries
/// have no accepted adapter yet: absent optional values are allowed, supplied UUIDs
/// are rejected rather than accepted by shape. This is not U-08/U-09 completion.
/// Construct within the command scope; the atomic writer must recheck authority.
/// </summary>
public sealed class ProductCodeOnlyReferenceCatalog : IProductReferenceCatalog
{
    private readonly ProductDbContext context;

    public ProductCodeOnlyReferenceCatalog(ProductDbContext context, GroupSharedMasterCommandContext authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        this.context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<ProductCodeGroup?> GetCodeGroupAsync(Guid id, CancellationToken cancellationToken)
    {
        var group = await context.CodeGroups.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        return group?.ToContract();
    }

    public async Task<bool> IsClassificationValidAsync(Guid groupId, Guid? classId, Guid? familyId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return classId is null && familyId is null && await GetCodeGroupAsync(groupId, cancellationToken) is not null;
    }

    public Task<bool> IsUnitValidAsync(Guid id, bool requiresKilogram, CancellationToken cancellationToken) => Unavailable(cancellationToken);
    public Task<bool> IsTrackingPolicyValidAsync(Guid id, CancellationToken cancellationToken) => Unavailable(cancellationToken);

    public Task<IReadOnlyList<ProductFieldError>> ValidateTemplateAsync(ProductReferences references, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ProductFieldError>>([new("references.templateId", "invalid_reference")]);
    }

    private static Task<bool> Unavailable(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }
}
