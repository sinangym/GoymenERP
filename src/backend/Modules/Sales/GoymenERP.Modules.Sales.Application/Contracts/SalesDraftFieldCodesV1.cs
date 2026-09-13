using System.Collections.Frozen;
namespace GoymenERP.Modules.Sales.Application.Contracts;

public static class SalesDraftFieldCodesV1
{
    public static IReadOnlySet<string> All { get; } = new[]
    {
        "validation_failed", "company_mismatch", "passive_company_account",
        "unsupported_product", "unsupported_unit", "invalid_quantity",
        "invalid_decimal_text", "same_key_payload_mismatch",
        "settings_provider_unavailable", "settings_access_denied"
    }.ToFrozenSet(StringComparer.Ordinal);
}
