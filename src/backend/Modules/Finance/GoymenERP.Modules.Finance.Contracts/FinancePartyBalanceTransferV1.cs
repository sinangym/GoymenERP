using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>KLN-D72/D76: registration only; one authorized command, no automatic role/grant or second approval.</summary>
public static class FinancePartyBalanceTransferPermissionsV1
{
    public const string Record = "finance.party-balance-transfers.record";
}

[JsonConverter(typeof(JsonStringEnumConverter<FinancePartyBalanceTransferLinkKindV1>))]
public enum FinancePartyBalanceTransferLinkKindV1 { Succession = 1, DuplicateMerge = 2 }

public enum FinancePartyBalanceTransferOwnerOutcomeV1 { Unavailable, NotFound, InvalidRelation, Available }

/// <summary>
/// Counterparty-owned, freshly authorized legal link and exact company accounts. No historical invoice identity is
/// changed. A duplicate merge has no exposed effective date/legal-basis text in the owner read, so those stay null;
/// the actual party/account versions still identify the frozen owner proof. No synthetic business value is filled in.
/// </summary>
public sealed record FinancePartyBalanceTransferOwnerProofV1(
    Guid CompanyId,
    Guid PredecessorPartyId,
    Guid PredecessorAccountId,
    string PredecessorPartyVersion,
    string PredecessorAccountVersion,
    string PredecessorCode,
    string PredecessorName,
    Guid SuccessorPartyId,
    Guid SuccessorAccountId,
    string SuccessorPartyVersion,
    string SuccessorAccountVersion,
    string SuccessorCode,
    string SuccessorName,
    FinancePartyBalanceTransferLinkKindV1 LinkKind,
    DateOnly? EffectiveOn,
    string? LegalBasis,
    DateTimeOffset? LinkRecordedAtUtc);

public sealed record FinancePartyBalanceTransferOwnerAnswerV1(
    FinancePartyBalanceTransferOwnerOutcomeV1 Outcome,
    FinancePartyBalanceTransferOwnerProofV1? Proof);

/// <summary>Actual owner read: company membership, passive predecessor and active legal successor; never a name match.</summary>
public interface IFinancePartyBalanceTransferOwnerPortV1
{
    Task<FinancePartyBalanceTransferOwnerAnswerV1> ReadAsync(FinanceContext context,
        Guid predecessorPartyId, Guid predecessorAccountId, Guid successorPartyId, Guid successorAccountId,
        CancellationToken cancellationToken);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FinancePartyBalanceTransferSelectionV1(
    [property: JsonRequired] Guid PredecessorPartyId,
    [property: JsonRequired] Guid PredecessorAccountId,
    [property: JsonRequired] Guid SuccessorPartyId,
    [property: JsonRequired] Guid SuccessorAccountId);

/// <summary>One currency's full balance, read exactly from the Finance ledger; never an FX conversion or partial amount.</summary>
public sealed record FinancePartyBalanceTransferCurrencyV1(
    Guid CurrencyId, PartyLedgerBalanceSide Side, string Amount,
    string OpenReceivable, string OpenPayable);

public sealed record FinancePartyBalanceTransferPreviewV1(
    Guid CompanyId, FinancePartyBalanceTransferOwnerProofV1 OwnerProof, string SnapshotVersion,
    IReadOnlyList<FinancePartyBalanceTransferCurrencyV1> Currencies, int OpenItemCount,
    int ReceiptCount, int PaymentCount, bool CanRecord);

/// <summary>
/// The client selects no amount, currency or subset. The server rereads the full balance under its transaction and
/// compares the exact preview fingerprint. Later corrections stay with the original party and require another explicit transfer.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FinancePartyBalanceTransferEvidenceV1(Guid DocumentId, int VersionNo, string Sha256);

public interface IFinancePartyBalanceTransferEvidencePortV1
{
    Task<ReadOutcome> CheckAsync(FinanceContext context, FinancePartyBalanceTransferOwnerProofV1 owner,
        FinancePartyBalanceTransferEvidenceV1 evidence, CancellationToken cancellationToken);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecordFinancePartyBalanceTransferV1(
    [property: JsonRequired] Guid TransferId,
    [property: JsonRequired] FinancePartyBalanceTransferSelectionV1 Selection,
    [property: JsonRequired] string ExpectedSnapshotVersion,
    [property: JsonRequired] DateOnly OccurredOn,
    [property: JsonRequired] string Reason,
    [property: JsonRequired] FinancePartyBalanceTransferEvidenceV1 Evidence);

public sealed record FinancePartyBalanceTransferViewV1(
    Guid Id, Guid CompanyId, string Version, FinancePartyBalanceTransferOwnerProofV1 OwnerProof,
    IReadOnlyList<FinancePartyBalanceTransferCurrencyV1> Currencies, DateOnly OccurredOn,
    string Reason, string RecordedBy, DateTimeOffset RecordedAtUtc, FinancePartyBalanceTransferEvidenceV1 Evidence);

public sealed record FinancePartyBalanceTransferReadResultV1(
    [property: JsonConverter(typeof(JsonStringEnumConverter<ReadOutcome>))] ReadOutcome Outcome, FinancePartyBalanceTransferPreviewV1? Preview, FinancePartyBalanceTransferViewV1? Transfer);

public sealed record FinancePartyBalanceTransferCommandResultV1(
    CommandOutcome Outcome, FinancePartyBalanceTransferViewV1? Resource, string? Code, bool Replayed = false,
    [property: JsonIgnore] string? OriginalCorrelationId = null);

public interface IFinancePartyBalanceTransferServiceV1
{
    Task<FinancePartyBalanceTransferReadResultV1> PreviewAuthorizedAsync(FinanceContext context,
        FinancePartyBalanceTransferSelectionV1 selection, CancellationToken cancellationToken);
    Task<FinancePartyBalanceTransferReadResultV1> GetAuthorizedAsync(FinanceContext context,
        Guid transferId, CancellationToken cancellationToken);
    Task<FinancePartyBalanceTransferCommandResultV1> RecordAsync(FinanceContext context,
        CommandMetadata metadata, RecordFinancePartyBalanceTransferV1 request, CancellationToken cancellationToken);
}
