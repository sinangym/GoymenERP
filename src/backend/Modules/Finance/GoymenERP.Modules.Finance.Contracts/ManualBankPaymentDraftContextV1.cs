namespace GoymenERP.Modules.Finance.Contracts;

public sealed record ManualBankPaymentDraftContextRequest(Guid TreasuryAccountId, Guid PartyId,
    Guid CompanyAccountId, DateOnly OccurredOn);

public sealed record ManualBankPaymentTreasuryEligibility(Guid Id, Guid CompanyId, Guid CurrencyId,
    bool IsActive, bool IsUsableForPayment, string DisplayName, string Version);

public sealed record ManualBankPaymentDraftContextView(Guid CompanyId, DateOnly OccurredOn,
    ManualBankPaymentTreasuryEligibility TreasuryAccount, DraftCurrencyContext Currency, DraftPartyLabel Party);

public sealed record ManualBankPaymentDraftContextResult(DraftReadOutcome Outcome,
    ManualBankPaymentDraftContextView? Resource, string? Code);

/// <summary>R47 owner preparation read. Its labels/profile grant no write authority or amount.</summary>
public interface IManualBankPaymentDraftContextReader
{
    Task<ManualBankPaymentDraftContextResult> GetAuthorizedAsync(FinanceContext context,
        ManualBankPaymentDraftContextRequest request, CancellationToken cancellationToken);
}
