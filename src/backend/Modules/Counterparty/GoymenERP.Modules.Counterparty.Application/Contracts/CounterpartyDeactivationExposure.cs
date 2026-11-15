namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// ALS-D99 (Sinan, 2026-11-15): an open balance or open documents never BLOCK deactivating a Cari account, but before the
/// command the system lists them and warns; the reason stays mandatory and the audit records that the warning was shown.
/// The data comes from the OWNER modules' read ports, never from their tables. Each owner answers through one source
/// registered by the API host; Counterparty declares the port and stays fail-closed: a kind with no source (or more than
/// one, or a source that fails) reads as <see cref="CounterpartyDeactivationExposureOutcomeV1.Unavailable"/>, which is a
/// warning the actor must acknowledge, never a silent "nothing open".
/// </summary>
public enum CounterpartyDeactivationExposureKindV1
{
    /// <summary>The Finance owner's open cari balance for the company's account (FIN-S11 party ledger read).</summary>
    FinanceOpenBalance = 1,

    /// <summary>The Sales owner's open documents (orders, dispatches, invoices not yet closed) for the party.</summary>
    SalesOpenDocuments = 2,

    /// <summary>The Purchasing owner's open documents (purchase orders with open lines) for the party.</summary>
    PurchasingOpenDocuments = 3,
}

/// <summary>One owner source's answer. Counts only: no amount is disclosed through the warning.</summary>
public enum CounterpartyDeactivationExposureOutcomeV1
{
    /// <summary>The owner could not answer (no source registered, source failure, owner store unavailable).</summary>
    Unavailable = 0,

    /// <summary>The actor may not read this owner's records in the company; the warning says it could not be checked.</summary>
    Denied = 1,

    /// <summary>The owner answered: nothing open for the party in the company.</summary>
    Clear = 2,

    /// <summary>The owner answered: something is open (count in the answer).</summary>
    Open = 3,
}

/// <summary>The company is explicit: a source never takes it from an ambient context, so a group read can ask per company.</summary>
public sealed record CounterpartyDeactivationExposureRequestV1(
    Guid CompanyId,
    string ActorId,
    Guid PartyId);

/// <summary><c>OpenItemCount</c> is positive only for <see cref="CounterpartyDeactivationExposureOutcomeV1.Open"/>, zero otherwise.</summary>
public sealed record CounterpartyDeactivationExposureAnswerV1(
    CounterpartyDeactivationExposureOutcomeV1 Outcome,
    int OpenItemCount)
{
    public static CounterpartyDeactivationExposureAnswerV1 Unavailable { get; } =
        new(CounterpartyDeactivationExposureOutcomeV1.Unavailable, 0);

    public static CounterpartyDeactivationExposureAnswerV1 Denied { get; } =
        new(CounterpartyDeactivationExposureOutcomeV1.Denied, 0);

    public static CounterpartyDeactivationExposureAnswerV1 Clear { get; } =
        new(CounterpartyDeactivationExposureOutcomeV1.Clear, 0);
}

/// <summary>
/// The owner-module port the ALS-D99 warning reads. Implemented by an API host adapter over the owner's own read
/// contract (Finance party ledger, Sales open documents, Purchasing open orders); Counterparty never reads those tables.
/// </summary>
public interface ICounterpartyDeactivationExposureSourceV1
{
    CounterpartyDeactivationExposureKindV1 Kind { get; }

    Task<CounterpartyDeactivationExposureAnswerV1> ReadAsync(
        CounterpartyDeactivationExposureRequestV1 request,
        CancellationToken cancellationToken);
}

/// <summary>Stable wire spellings of the warning list (the frontend decoder accepts exactly these).</summary>
public static class CounterpartyDeactivationExposureWireV1
{
    public const string FinanceOpenBalance = "finance-open-balance";
    public const string SalesOpenDocuments = "sales-open-documents";
    public const string PurchasingOpenDocuments = "purchasing-open-documents";

    public const string Unavailable = "unavailable";
    public const string Denied = "denied";
    public const string Clear = "clear";
    public const string Open = "open";

    public static string Kind(CounterpartyDeactivationExposureKindV1 kind) => kind switch
    {
        CounterpartyDeactivationExposureKindV1.FinanceOpenBalance => FinanceOpenBalance,
        CounterpartyDeactivationExposureKindV1.SalesOpenDocuments => SalesOpenDocuments,
        CounterpartyDeactivationExposureKindV1.PurchasingOpenDocuments => PurchasingOpenDocuments,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Bilinmeyen pasife alma uyarı türü."),
    };

    public static string Outcome(CounterpartyDeactivationExposureOutcomeV1 outcome) => outcome switch
    {
        CounterpartyDeactivationExposureOutcomeV1.Unavailable => Unavailable,
        CounterpartyDeactivationExposureOutcomeV1.Denied => Denied,
        CounterpartyDeactivationExposureOutcomeV1.Clear => Clear,
        CounterpartyDeactivationExposureOutcomeV1.Open => Open,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Bilinmeyen pasife alma uyarı sonucu."),
    };
}

/// <summary>
/// One line of the warning list. <c>OpenItemCount</c> is null unless <c>Outcome</c> is <c>open</c>.
/// </summary>
public sealed record CounterpartyDeactivationExposureItemView(
    string Kind,
    string Outcome,
    int? OpenItemCount);

/// <summary>
/// The ALS-D99 warning for one company's account. <c>AcknowledgementRequired</c> is true when any line is not
/// <c>clear</c> (open, unavailable or denied): the deactivation then needs <c>exposureWarningAcknowledged</c>.
/// <c>AccountVersion</c> and <c>IsActive</c> are the account as read, for the command's expected version; the version is
/// a lossless decimal string, the same wire form every Cari view uses for an int64 version.
/// </summary>
public sealed record CounterpartyDeactivationExposureView(
    Guid CompanyId,
    Guid PartyId,
    string AccountVersion,
    bool IsActive,
    IReadOnlyList<CounterpartyDeactivationExposureItemView> Items,
    bool AcknowledgementRequired);

/// <summary>Reads the ALS-D99 warning list for one company's account of a party.</summary>
public interface ICounterpartyDeactivationExposureReader
{
    /// <summary>Null when the company holds no account for the party.</summary>
    Task<CounterpartyDeactivationExposureView?> ReadAsync(
        Guid companyId,
        string actorId,
        Guid partyId,
        CancellationToken cancellationToken);
}
