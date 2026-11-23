namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

public sealed record SubcontractTaxRateOptionV1(string Code, string Name, string Percent, string? TaxCode);
public sealed record SubcontractTaxOptionsV1(Guid CompanyId, SubcontractTaxSideV1 Side, DateOnly On,
    string DefaultVersion, SubcontractTaxDefaultValueViewV1? Default, IReadOnlyList<SubcontractTaxRateOptionV1> VatRates,
    IReadOnlyList<SubcontractTaxRateOptionV1> WithholdingRatios, IReadOnlyList<TaxCodeListEntryViewV1> ExemptionCodes,
    IReadOnlyList<TaxCodeListEntryViewV1> WithholdingCodes, string SourceVersion);

/// <summary>Actual company reference data only. The consuming command measures its own authority before and after reads.</summary>
public interface ISettingsSubcontractTaxCatalogV1
{
    Task<SubcontractTaxOptionsV1> GetAsync(Guid companyId, SubcontractTaxSideV1 side, DateOnly on, CancellationToken token = default);
}
