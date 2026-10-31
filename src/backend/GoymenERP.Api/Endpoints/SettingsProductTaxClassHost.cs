using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// MOD-03 (SAT-D73/D74/D82): the central adapter that gives the Settings line tax port the product's VAT and withholding
/// classes from the Product owner's company-scoped classification. Settings reads no Product table; Product re-fences the
/// company and the caller. The company asked about must be the server company (a copy of the context, never an input).
/// </summary>
public sealed class SettingsProductTaxClassHost(IProductTaxClassificationReaderV1 reader, ICompanyContext companyContext)
    : ISettingsProductTaxClassSourceV1
{
    public async Task<SettingsProductTaxClassAnswerV1> GetAsync(Guid companyId, Guid productId,
        CancellationToken cancellationToken = default)
    {
        if (!companyContext.IsResolved || companyContext.CompanyId != companyId)
        {
            return new SettingsProductTaxClassAnswerV1(SettingsProductTaxClassOutcomeV1.Denied, null, null);
        }

        var answer = await reader.GetAsync(productId, cancellationToken);
        return answer.Outcome switch
        {
            ProductTaxClassificationOutcomeV1.Found => new SettingsProductTaxClassAnswerV1(SettingsProductTaxClassOutcomeV1.Found,
                answer.VatClassId, answer.WithholdingClassId),
            ProductTaxClassificationOutcomeV1.NotClassified =>
                new SettingsProductTaxClassAnswerV1(SettingsProductTaxClassOutcomeV1.NotClassified, null, null),
            ProductTaxClassificationOutcomeV1.NotFound =>
                new SettingsProductTaxClassAnswerV1(SettingsProductTaxClassOutcomeV1.NotFound, null, null),
            ProductTaxClassificationOutcomeV1.Denied =>
                new SettingsProductTaxClassAnswerV1(SettingsProductTaxClassOutcomeV1.Denied, null, null),
            _ => new SettingsProductTaxClassAnswerV1(SettingsProductTaxClassOutcomeV1.Unavailable, null, null),
        };
    }
}
