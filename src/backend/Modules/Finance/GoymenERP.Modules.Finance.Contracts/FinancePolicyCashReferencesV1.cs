namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>Price-free No 133 policy-authoring references, not the cash account list or a balance read.</summary>
public interface IFinancePolicyCashReferencesV1
{
    Task<FinancePolicyCashReferencesV1> GetAsync(Guid companyId, CancellationToken cancellationToken = default);
}
public sealed record FinancePolicyCashReferenceV1(Guid CashAccountId, Guid CurrencyId, string Name, long Version);
public sealed record FinancePolicyCashReferencesV1(Guid CompanyId, IReadOnlyList<FinancePolicyCashReferenceV1> CashAccounts);
