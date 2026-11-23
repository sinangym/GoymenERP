namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>K6: the issuer is selected explicitly from the published Settings bank institution catalog.
/// A bank account, free-text bank name or IBAN is never an institution identity.</summary>
public sealed record FinanceCollateralBankProofV1(Guid CompanyId, Guid BankInstitutionId, string Name,
    string EftCode, string SourceVersion);
public sealed record FinanceCollateralBankCatalogV1(bool IsAvailable, Guid CompanyId,
    IReadOnlyList<FinanceCollateralBankProofV1> Entries);
public sealed record FinanceCollateralRateReadV1(bool IsAvailable, string? RatePercent, string? SourceVersion,
    string? Scope, string? ReasonCode, string? CollateralTypeVersion = null);

/// <summary>Internal source read only. Its caller retains its own fresh operation authority.</summary>
public interface IFinanceCollateralPolicyReaderV1
{
    Task<FinanceCollateralBankCatalogV1> GetBankInstitutionsAsync(Guid companyId, DateOnly businessDate,
        CancellationToken cancellationToken = default);
    Task<FinanceCollateralRateReadV1> GetRateAsync(Guid companyId, Guid collateralTypeId, Guid? bankInstitutionId,
        string? bankSourceVersion, DateOnly? validUntil, DateOnly businessDate, CancellationToken cancellationToken = default);
}

public sealed record PartyCollateralBankCandidates(Guid CompanyId, IReadOnlyList<FinanceCollateralBankProofV1> Items);
public interface IFinanceCollateralBankCandidatesV1
{
    Task<ReadResult<PartyCollateralBankCandidates>> BankCandidatesAsync(FinanceContext context,
        CancellationToken cancellationToken);
}

/// <summary>Actual read-time rate evidence. No amount, assignment, bank account or customer data is copied here.</summary>
public sealed record FinanceCollateralRateProofV1(Guid CompanyId, Guid CollateralId, string CollateralRevision,
    Guid CollateralTypeId, string TypeVersion, Guid? BankInstitutionId, string? BankSourceVersion,
    string RatePercent, string SourceVersion, string Scope, DateOnly BusinessDate);
