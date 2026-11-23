namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// Internal requested-id-only customer evidence for Product package-standard maintenance/approval.
/// The owner requires the consuming Product function's existing authority. This is neither a Cari read
/// grant nor a Manufacturing alias, search catalogue or role assignment. Omissions disclose no reason.
/// </summary>
public interface ICounterpartyProductPackageStandardCustomerSourceV1
{
    Task<CounterpartyProductPackageStandardCustomerAnswerV1> ReadAuthorizedAsync(
        CounterpartyProductPackageStandardCustomerRequestV1 request, CancellationToken cancellationToken = default);
}

public static class CounterpartyProductPackageStandardCustomerContractV1
{
    public const int MaximumPartyCount = 100;
    public const string SourceVersion = "counterparty.product-package-standard-customers.v1";
}

public enum CounterpartyProductPackageStandardCustomerOutcomeV1 { Unavailable = 0, Denied = 1, Found = 2 }

public sealed record CounterpartyProductPackageStandardCustomerRequestV1(Guid CompanyId, string ActorId,
    IReadOnlyList<Guid> PartyIds);

/// <summary>Both revisions and eligibility facts come from the same persisted Party/account join statement.</summary>
public sealed record CounterpartyProductPackageStandardCustomerV1(Guid CompanyId, Guid PartyId, Guid CompanyAccountId,
    string PartyVersion, string AccountVersion, bool PartyIsActive, bool AccountIsActive, bool HasCustomerRole,
    string Code, string LegalName, string? TradeName);

public sealed record CounterpartyProductPackageStandardCustomerAnswerV1(CounterpartyProductPackageStandardCustomerOutcomeV1 Outcome,
    Guid CompanyId, string ActorId, IReadOnlyList<CounterpartyProductPackageStandardCustomerV1> Items, string SourceVersion);
