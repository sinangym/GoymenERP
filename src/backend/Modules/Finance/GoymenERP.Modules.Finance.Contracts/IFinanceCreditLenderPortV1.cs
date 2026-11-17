namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>
/// Reads the explicitly selected lender through Counterparty ownership. A display name is never an identity or a limit key.
/// This port does not create a lender master, assign a Counterparty role, or grant authority.
/// </summary>
public interface IFinanceCreditLenderPortV1
{
    Task<FinanceCreditLenderAnswerV1> ReadAsync(Guid companyId, Guid partyId, Guid companyPartyAccountId,
        CancellationToken cancellationToken = default);
}

public enum FinanceCreditLenderStatusV1 { Available, NotFound, Inactive, Unavailable }

public sealed record FinanceCreditLenderAnswerV1(Guid CompanyId, Guid PartyId, Guid CompanyPartyAccountId,
    FinanceCreditLenderStatusV1 Status, string? DisplayName, long? PartyVersion, long? CompanyAccountVersion);
