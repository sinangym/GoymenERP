namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

/// <summary>URT-D25: the exact Product membership, Cari supplier and optional Settings color selected on a plan.
/// These references are verified against their owners before a new plan/revision or approval is written.
/// Names and free text never prove membership, supplier eligibility or a color's identity.</summary>
public sealed record PlanPaintSelectionV1(Guid ProductId, string ProductVersion, string MembershipVersion,
    string ProductSourceVersion, Guid SupplierPartyId, Guid SupplierCompanyAccountId,
    string SupplierPartyVersion, string SupplierAccountVersion, Guid? ColorId = null, string? ColorVersion = null)
{
    public bool IsWellFormed() => ProductId != Guid.Empty && SupplierPartyId != Guid.Empty
        && SupplierCompanyAccountId != Guid.Empty && Version(ProductVersion) && Version(MembershipVersion)
        && Version(ProductSourceVersion) && Version(SupplierPartyVersion) && Version(SupplierAccountVersion)
        && (ColorId is null && ColorVersion is null || ColorId is { } id && id != Guid.Empty && Version(ColorVersion));

    private static bool Version(string? value) => value is { Length: > 0 and <= 256 }
        && value.Trim() == value && !value.Any(char.IsControl);
}
