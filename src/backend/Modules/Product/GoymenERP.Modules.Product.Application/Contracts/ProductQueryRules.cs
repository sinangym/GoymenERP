namespace GoymenERP.Modules.Product.Application.Contracts;

public static class ProductQueryRules
{
    public static ProductListQuery Normalize(ProductListQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var roles = query.Roles?.Split(',') ?? [];
        if (query.PageSize is < 1 or > 100 || query.Q?.Length > 200 || query.Q?.Any(char.IsControl) == true
            || (query.Lifecycle is not null && query.Lifecycle is not ("Draft" or "Active" or "Inactive"))
            || roles.Length > 4 || roles.Any(role => role is not ("purchasable" or "sellable" or "productionInput" or "productionOutput"))
            || roles.Distinct(StringComparer.Ordinal).Count() != roles.Length
            || query.RoleMatch is not ("any" or "all") || query.Sort is not ("code" or "name" or "updatedAtUtc")
            || query.Direction is not ("asc" or "desc")
            || (query.Completeness is not null && query.Completeness is not ("NotEvaluated" or "Incomplete" or "Complete"))
            || query.GroupId == Guid.Empty || query.ClassId == Guid.Empty || query.FamilyId == Guid.Empty)
            throw new ProductValidationException([new("query", "invalid_metadata")]);
        return query with
        {
            Q = string.IsNullOrWhiteSpace(query.Q) ? null : query.Q.Trim(),
            Roles = roles.Length == 0 ? null : string.Join(',', roles.Order(StringComparer.Ordinal)),
        };
    }
}
