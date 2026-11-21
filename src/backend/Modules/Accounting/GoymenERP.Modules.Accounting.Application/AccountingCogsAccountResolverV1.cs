using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application.Contracts;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>Source for a proposal's immutable class/mapping snapshot. This is not posting authority and never infers
/// a class from steel/scrap, a receipt or manufacturing origin. Callers persist Value with their source proposal.</summary>
public sealed record AccountingCogsAccountSnapshotV1(Guid CompanyId, Guid ProductId,
    InvoiceLineCogsClassificationV1 Classification, string ProductVersion, string ClassificationVersion,
    Guid AccountId, long MappingVersion);
public sealed record AccountingCogsAccountReadV1(string Outcome, AccountingCogsAccountSnapshotV1? Value);

public interface IAccountingCogsAccountResolverV1
{
    Task<AccountingCogsAccountReadV1> ResolveAsync(Guid companyId, Guid productId, DateOnly businessDate,
        CancellationToken cancellationToken = default);
}

public sealed class AccountingCogsAccountResolverV1(IAccountingInvoiceLineProductReferenceV1 products,
    IAccountingPostingAccountMappingResolverV1 mappings, ICompanyContext company) : IAccountingCogsAccountResolverV1
{
    public async Task<AccountingCogsAccountReadV1> ResolveAsync(Guid companyId, Guid productId, DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        if (!Matches(companyId) || productId == Guid.Empty || businessDate == default) return Missing("Unavailable");
        var product = await products.GetAsync(companyId, productId, cancellationToken);
        if (!Matches(companyId) || product.CompanyId != companyId || product.ProductId != productId
            || product.Outcome != "Available" || product.Classification is not (InvoiceLineCogsClassificationV1.TradeGoods
                or InvoiceLineCogsClassificationV1.Manufactured)
            || string.IsNullOrWhiteSpace(product.ProductVersion) || string.IsNullOrWhiteSpace(product.ClassificationVersion))
            return Missing("ClassificationMissing");
        var mapped = await mappings.ResolveAsync(companyId, AccountingPostingAccountMappingPurposesV1.CostOfGoodsSold,
            businessDate, AccountingCogsMappingDefaultsV1.Key(product.Classification.Value), cancellationToken: cancellationToken);
        if (!Matches(companyId)) return Missing("Unavailable");
        if (mapped.Kind != AccountingPostingAccountMappingResolutionKindV1.Mapped || mapped.AccountId is not { } accountId || accountId == Guid.Empty
            || mapped.MappingVersion is null or < 1) return Missing("MappingMissing");
        return new("Available", new(companyId, productId, product.Classification.Value, product.ProductVersion,
            product.ClassificationVersion, accountId, mapped.MappingVersion.Value));
    }
    private bool Matches(Guid expected) => expected != Guid.Empty && company.IsResolved && company.CompanyId == expected;
    private static AccountingCogsAccountReadV1 Missing(string outcome) => new(outcome, null);
}
