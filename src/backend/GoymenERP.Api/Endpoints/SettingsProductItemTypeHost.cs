using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// SAT-D134/D139: the central adapter that gives the Settings line tax port the product's item type from the Product owner's
/// company-scoped field. Settings reads no Product table; Product re-fences the company and the caller. The company asked about
/// must be the server company (a copy of the context, never an input). The two modules' item type wire values are the same
/// (pinned by a host test).
/// </summary>
public sealed class SettingsProductItemTypeHost(IProductItemTypeReaderV1 reader, ICompanyContext companyContext)
    : ISettingsProductItemTypeSourceV1
{
    public async Task<SettingsProductItemTypeAnswerV1> GetAsync(Guid companyId, Guid productId,
        CancellationToken cancellationToken = default)
    {
        if (!companyContext.IsResolved || companyContext.CompanyId != companyId)
        {
            return new SettingsProductItemTypeAnswerV1(SettingsProductItemTypeOutcomeV1.Denied, null);
        }

        var answer = await reader.GetAsync(productId, cancellationToken);
        return answer.Outcome switch
        {
            ProductItemTypeOutcomeV1.Found when TaxItemTypesV1.IsKnown(answer.ItemType) =>
                new SettingsProductItemTypeAnswerV1(SettingsProductItemTypeOutcomeV1.Found, answer.ItemType),
            ProductItemTypeOutcomeV1.NotSet => new SettingsProductItemTypeAnswerV1(SettingsProductItemTypeOutcomeV1.NotSet, null),
            ProductItemTypeOutcomeV1.NotFound => new SettingsProductItemTypeAnswerV1(SettingsProductItemTypeOutcomeV1.NotFound, null),
            ProductItemTypeOutcomeV1.Denied => new SettingsProductItemTypeAnswerV1(SettingsProductItemTypeOutcomeV1.Denied, null),
            _ => new SettingsProductItemTypeAnswerV1(SettingsProductItemTypeOutcomeV1.Unavailable, null),
        };
    }
}
