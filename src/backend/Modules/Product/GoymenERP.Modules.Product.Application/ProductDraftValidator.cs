using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;

namespace GoymenERP.Modules.Product.Application;

public sealed record ValidatedProductDraft(string Name, ProductCodeGroup Group,
    Guid? ClassId, Guid? FamilyId, ProductRoles Roles, ProductReferences References);

public sealed class ProductDraftValidator(IProductReferenceCatalog catalog)
{
    public async Task<ValidatedProductDraft> ValidateCreateAsync(CreateProductDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<ProductFieldError>();
        string name = request.Name;
        try { name = Domain.Products.Product.NormalizeName(name); }
        catch (ArgumentException) { errors.Add(new("name", "invalid_name")); }
        RequireId(request.GroupId, "groupId", errors);
        RequireOptionalId(request.ClassId, "classId", errors);
        RequireOptionalId(request.FamilyId, "familyId", errors);
        ProductRoles roles = ProductRoles.NotEvaluated;
        try { roles = request.Roles?.ApplyTo(roles) ?? roles; }
        catch (ArgumentException) { errors.Add(new("roles", "invalid_role_provenance")); }
        var references = request.References ?? new ProductReferences();
        ValidateReferenceShape(references, errors);
        ThrowIfInvalid(errors);

        var group = await catalog.GetCodeGroupAsync(request.GroupId, cancellationToken);
        if (group is null || group.Id != request.GroupId)
        {
            throw new ProductValidationException([new("groupId", "invalid_reference")]);
        }

        // Prefix validation is structural; the caller does not allocate a sequence.
        try { _ = ProductCode.FromSequence(group.Prefix, 1); }
        catch (ArgumentException) { throw new ProductValidationException([new("groupId", "invalid_code_prefix")]); }

        if (!await catalog.IsClassificationValidAsync(group.Id, request.ClassId, request.FamilyId, cancellationToken))
        {
            errors.Add(new("classification", "invalid_reference"));
        }

        await ValidateReferencesAsync(references, errors, cancellationToken);
        ThrowIfInvalid(errors);
        return new(name, group, request.ClassId, request.FamilyId, roles, references);
    }

    public async Task<ValidatedProductDraft> ValidateEditAsync(Domain.Products.Product product,
        ProductReferences existingReferences, EditProductDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Changes is null)
        {
            throw new ProductValidationException([new("changes", "required")]);
        }
        _ = NormalizeReason(request.Reason);

        long expectedVersion;
        try { expectedVersion = ProductVersion.Parse(request.ExpectedVersion); }
        catch (ArgumentException)
        {
            throw new ProductValidationException([new("expectedVersion", "invalid_version")]);
        }
        if (product.Version != expectedVersion)
        {
            throw new ProductVersionConflictException(expectedVersion, product.Version);
        }

        var changes = request.Changes;
        if ((changes.Name.IsSpecified && changes.Name.Value is null)
            || (changes.Roles.IsSpecified && changes.Roles.Value is null)
            || (changes.References.IsSpecified && changes.References.Value is null))
        {
            throw new ProductValidationException([new("changes", "null_not_allowed")]);
        }

        ProductRoles roles;
        try { roles = changes.Roles.Value?.ApplyTo(product.Roles) ?? product.Roles; }
        catch (ArgumentException)
        {
            throw new ProductValidationException([new("roles", "invalid_role_provenance")]);
        }
        // Do not round-trip server-derived roles through write DTOs.
        ValidateDerivedPreservation(product.Roles, roles);
        var references = changes.References.IsSpecified
            ? changes.References.Value!.ApplyTo(existingReferences) : existingReferences;

        var candidate = new CreateProductDraftRequest(
            changes.Name.IsSpecified ? changes.Name.Value! : product.Name, product.GroupId,
            changes.ClassId.IsSpecified ? changes.ClassId.Value : product.ClassId,
            changes.FamilyId.IsSpecified ? changes.FamilyId.Value : product.FamilyId,
            References: references);
        var validated = await ValidateCreateAsync(candidate, cancellationToken);
        return validated with { Roles = roles };
    }

    public static string NormalizeReason(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 1000 || value.Any(char.IsControl))
            throw new ProductValidationException([new("reason", "invalid_reason")]);
        return value.Trim();
    }

    private static void ValidateDerivedPreservation(ProductRoles before, ProductRoles after)
    {
        var pairs = new[] { (before.Purchasable, after.Purchasable), (before.Sellable, after.Sellable),
            (before.ProductionInput, after.ProductionInput), (before.ProductionOutput, after.ProductionOutput) };
        if (pairs.Any(pair => pair.Item1.Provenance == ProductRoleProvenance.Derived && pair.Item1 != pair.Item2))
        {
            throw new ProductValidationException([new("roles", "derived_role_readonly")]);
        }
    }

    private static void ValidateReferenceShape(ProductReferences references, List<ProductFieldError> errors)
    {
        RequireOptionalId(references.TemplateId, "references.templateId", errors);
        RequireOptionalId(references.TrackingPolicyId, "references.trackingPolicyId", errors);
        if (references.TemplateId.HasValue != (references.TemplateVersion is not null))
        {
            errors.Add(new("references.templateId", "template_pair_required"));
        }

        if (references.TemplateVersion is not null)
        {
            try { _ = ProductVersion.Parse(references.TemplateVersion); }
            catch (ArgumentException) { errors.Add(new("references.templateVersion", "invalid_version")); }
        }

        if (references.Attributes?.Count > 0 && !references.TemplateId.HasValue)
        {
            errors.Add(new("references.attributes", "template_required"));
        }

        foreach (var (field, id) in Units(references.Units)) { RequireOptionalId(id, field, errors); }
    }

    private async Task ValidateReferencesAsync(ProductReferences references, List<ProductFieldError> errors,
        CancellationToken cancellationToken)
    {
        foreach (var (field, id) in Units(references.Units))
        {
            if (id.HasValue && !await catalog.IsUnitValidAsync(id.Value, field == "references.units.stock", cancellationToken))
            {
                errors.Add(new(field, "invalid_reference"));
            }
        }

        if (references.TemplateId.HasValue)
        {
            errors.AddRange(await catalog.ValidateTemplateAsync(references, cancellationToken));
        }

        if (references.TrackingPolicyId.HasValue
            && !await catalog.IsTrackingPolicyValidAsync(references.TrackingPolicyId.Value, cancellationToken))
        {
            errors.Add(new("references.trackingPolicyId", "invalid_reference"));
        }
    }

    private static IEnumerable<(string Field, Guid? Id)> Units(ProductUnitReferences? units)
    {
        if (units is null) { yield break; }
        yield return ("references.units.stock", units.Stock);
        yield return ("references.units.purchase", units.Purchase);
        yield return ("references.units.sales", units.Sales);
        yield return ("references.units.price", units.Price);
        yield return ("references.units.production", units.Production);
        yield return ("references.units.package", units.Package);
    }

    private static void RequireId(Guid value, string field, List<ProductFieldError> errors)
    {
        if (value == Guid.Empty) { errors.Add(new(field, "invalid_reference")); }
    }

    private static void RequireOptionalId(Guid? value, string field, List<ProductFieldError> errors)
    {
        if (value.HasValue) { RequireId(value.Value, field, errors); }
    }

    private static void ThrowIfInvalid(List<ProductFieldError> errors)
    {
        if (errors.Count > 0) { throw new ProductValidationException(errors); }
    }
}
