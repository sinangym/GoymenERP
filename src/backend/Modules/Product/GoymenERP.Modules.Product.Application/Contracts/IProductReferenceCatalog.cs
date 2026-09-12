namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// Product-owned reference read port. Implementations resolve the currently
/// authorized, approved catalog; supplied UUIDs are never accepted by shape alone.
/// A missing catalog must reject supplied references rather than invent defaults.
/// </summary>
public interface IProductReferenceCatalog
{
    Task<ProductCodeGroup?> GetCodeGroupAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> IsClassificationValidAsync(Guid groupId, Guid? classId, Guid? familyId,
        CancellationToken cancellationToken);
    Task<bool> IsUnitValidAsync(Guid id, bool requiresKilogram, CancellationToken cancellationToken);
    Task<bool> IsTrackingPolicyValidAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductFieldError>> ValidateTemplateAsync(ProductReferences references,
        CancellationToken cancellationToken);
}

public sealed record ProductCodeGroup(Guid Id, string Name, string Prefix);
public sealed record ProductFieldError(string Field, string Code);

public sealed class ProductValidationException(IReadOnlyList<ProductFieldError> errors)
    : Exception("Ürün alanları geçersiz.")
{
    public IReadOnlyList<ProductFieldError> Errors { get; } = errors;
}
