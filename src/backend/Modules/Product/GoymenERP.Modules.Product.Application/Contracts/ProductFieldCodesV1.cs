using System.Collections.Frozen;

namespace GoymenERP.Modules.Product.Application.Contracts;

public static class ProductFieldCodesV1
{
    public static IReadOnlySet<string> All { get; } = new[]
    {
        "invalid_name", "invalid_role_provenance", "invalid_reference", "invalid_code_prefix",
        "template_pair_required", "explicit_attribute_change_required", "null_not_allowed",
        "required", "invalid_metadata", "invalid_reason", "invalid_version", "derived_role_readonly",
        "template_required",
    }.ToFrozenSet(StringComparer.Ordinal);
}
