namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>B-16 / MOD-08: the existing Cari owner account including its recorded Tolling role.
/// ALS-D24/D28 keep payment terms on each order; this port never supplies a party-level default schedule.</summary>
public interface IPurchasingPartyAccountPortV2
{
    Task<PurchasingPartyAccountAnswerV2> GetAuthorizedAsync(Guid partyId, Guid companyAccountId,
        CancellationToken cancellationToken = default);
}

public sealed record PurchasingPartyAccountAnswerV2(PurchasingPartyAccountOutcomeV1 Outcome, PurchasingPartyAccountV2? Account);

public sealed record PurchasingPartyAccountV2(Guid PartyId, Guid CompanyAccountId, Guid CompanyId,
    bool PartyIsActive, bool AccountIsActive, bool AccountHasSupplierRole, bool AccountHasTollingRole,
    string PartyVersion, string AccountVersion)
{
    /// <summary>Decided order-specific terms, not a missing or zero-day party default (ALS-D24/D28).</summary>
    public PurchasingPaymentTermsScopeV2 PaymentTermsScope => PurchasingPaymentTermsScopeV2.OrderSpecific;
}

public enum PurchasingPaymentTermsScopeV2 { OrderSpecific = 1 }
