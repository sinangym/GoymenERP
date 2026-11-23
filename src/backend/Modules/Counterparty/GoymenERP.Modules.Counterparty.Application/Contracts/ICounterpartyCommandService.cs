using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;

using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Counterparty.Application.Contracts;

public interface ICounterpartyCommandService
{
    Task<CounterpartyCommandExecution> CreateAsync(
        CreateCounterparty command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken);

    Task<CounterpartyCommandExecution> SetCompanyAccountActiveStateAsync(
        Guid partyId,
        SetCompanyPartyAccountActiveState command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken);
}

public interface ICounterpartyCompanyAccountLinkService
{
    Task<CounterpartyCommandExecution> CreateCompanyAccountAsync(
        Guid partyId,
        CreateCompanyPartyAccount command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken);
}

public sealed record CreateCounterparty(
    PartyKind Kind,
    string LegalName,
    string? TradeName = null,
    PartyRole Roles = PartyRole.None);

public sealed record CreateCompanyPartyAccount(
    PartyRole Roles = PartyRole.None);

/// <summary>
/// <c>exposureWarningAcknowledged</c> (ALS-D99): deactivating an active account first shows the owner modules' open
/// balance/documents warning; when the server's own re-read finds anything open, unavailable or denied, the command needs
/// this acknowledgement (else 422 <c>deactivation_warning_acknowledgement_required</c>, nothing recorded). Ignored for a
/// reactivation or for an account that is already passive.
/// </summary>
public sealed record SetCompanyPartyAccountActiveState(
    [property: JsonRequired] bool IsActive,
    [property: JsonRequired] long ExpectedVersion,
    string Reason,
    bool ExposureWarningAcknowledged = false);

public sealed record CounterpartyCommandMetadata(
    Guid CompanyId,
    string ActorId,
    string CorrelationId,
    string? CausationId,
    string SourceChannel,
    string? ClientIpAddress,
    string? UserAgent,
    string IdempotencyKey);

public enum CounterpartyCommandResult
{
    Created = 1,
    Succeeded = 2,
    NotFound = 3,
    VersionConflict = 4,
    IdempotencyConflict = 5,
    AlreadyLinked = 6,

    /// <summary>AT-009: the party already has a number; replacing it is a change request (409).</summary>
    TaxIdentityChangeRequiresApproval = 7,

    /// <summary>AT-009: a change was proposed for a party that has no number yet (409).</summary>
    TaxIdentityNotSet = 8,

    /// <summary>AT-009: the proposal equals the current number (409).</summary>
    TaxIdentityUnchanged = 9,

    /// <summary>AT-009: a change of this party is already pending in this company (409).</summary>
    TaxIdentityChangePending = 10,

    /// <summary>AT-009: the named request is no longer pending (409).</summary>
    TaxIdentityChangeNotPending = 11,

    /// <summary>AT-009: the party's number moved after the request was made; it cannot be approved (409).</summary>
    TaxIdentityChangeStale = 12,

    /// <summary>AT-009: the proposer tried to decide their own request (409, maker-checker).</summary>
    TaxIdentitySelfDecision = 13,

    /// <summary>SAT-D57: a customer group with this id already exists (409).</summary>
    CustomerGroupExists = 14,

    /// <summary>SAT-D57: another group of the company already carries this name (409).</summary>
    CustomerGroupNameTaken = 15,

    /// <summary>SAT-D57: the group to assign is not one of the active company's groups (422).</summary>
    CustomerGroupNotFound = 16,

    /// <summary>SAT-D57: the group to assign is passive; a passive group is never newly assigned (409).</summary>
    CustomerGroupInactive = 17,

    /// <summary>
    /// SAT-D102: Settings has no e-Fatura scenario code list in force for the company ("kaynak yok"), so no customer
    /// override can be chosen (422).
    /// </summary>
    EInvoiceScenarioCodeListMissing = 18,

    /// <summary>SAT-D102: the scenario code is not in the Settings list in force for the company (422).</summary>
    EInvoiceScenarioNotListed = 19,

    /// <summary>SAT-D119: the party has no VKN/TCKN, so no VKN-keyed e-belge profile can exist (422).</summary>
    EDocumentTaxIdentityRequired = 20,

    /// <summary>
    /// SAT-D119 / ASSUMPTION-CARI-EDOC-4: the acting company's account is passive, so it does not change the group-shared
    /// e-belge profile (422).
    /// </summary>
    EDocumentAccountInactive = 21,

    /// <summary>EK-18 #76: the duplicate was already merged into a party; a merge happens once, never again (409).</summary>
    PartyAlreadyMerged = 22,

    /// <summary>EK-18 #76: the surviving party was itself merged away, so it cannot receive a merge (409).</summary>
    PartyMergeTargetMerged = 23,

    /// <summary>EK-18 #76: both parties carry a VKN/TCKN and the numbers differ, so they are not duplicates (409).</summary>
    PartyMergeTaxIdentityMismatch = 24,

    /// <summary>KLN-D38: a cari cannot succeed itself (422).</summary>
    SuccessionSameParty = 25,

    /// <summary>
    /// KLN-D38 rule 3: the successor carries the predecessor's VKN/TCKN, so it is the same legal person; a type change
    /// with the same VKN keeps the same cari and records the dated legal name instead (422).
    /// </summary>
    SuccessionSameTaxIdentity = 26,

    /// <summary>KLN-D38: the successor's account in the active company is passive; a passive cari takes over nothing (409).</summary>
    SuccessionSuccessorInactive = 27,

    /// <summary>KLN-D38: the successor already (transitively) succeeds-from the predecessor's chain; a cycle is refused (409).</summary>
    SuccessionCycle = 28,

    /// <summary>KLN-D38: a successor of this cari is already recorded in the active company (409).</summary>
    SuccessionAlreadyRecorded = 29,

    /// <summary>
    /// ALS-D99: deactivation found an open, unavailable or denied warning line and the command did not acknowledge the
    /// warning (422). Never stored as an idempotent answer: the warning is re-read live on the next attempt.
    /// </summary>
    DeactivationWarningAcknowledgementRequired = 30,

    /// <summary>ALS-D111: the proposal equals the stored legal name (409).</summary>
    LegalNameUnchanged = 31,

    /// <summary>ALS-D111: a legal-name change of this party is already pending in this company (409).</summary>
    LegalNameChangePending = 32,

    /// <summary>ALS-D111: the named legal-name request is no longer pending (409).</summary>
    LegalNameChangeNotPending = 33,

    /// <summary>ALS-D111: the party moved after the legal-name request was made; it cannot be approved (409).</summary>
    LegalNameChangeStale = 34,

    /// <summary>ALS-D113: the proposer tried to decide their own legal-name request (409, maker-checker).</summary>
    LegalNameSelfDecision = 35,
}

public static class CounterpartyProblemCodesV1
{
    public const string ValidationFailed = "validation_failed";

    public const string CommandMetadataInvalid = "command_metadata_invalid";

    public const string InvalidCursor = "invalid_cursor";

    public const string NotFound = "not_found";

    public const string AlreadyLinked = "already_linked";

    public const string VersionConflict = "version_conflict";

    public const string IdempotencyConflict = "idempotency_conflict";

    /// <summary>AT-009: the number fails the VKN/TCKN format or check digits (400, never stored).</summary>
    public const string TaxIdentityInvalid = "tax_identity_invalid";

    public const string TaxIdentityChangeRequiresApproval = "tax_identity_change_requires_approval";

    public const string TaxIdentityNotSet = "tax_identity_not_set";

    public const string TaxIdentityUnchanged = "tax_identity_unchanged";

    public const string TaxIdentityChangePending = "tax_identity_change_pending";

    public const string TaxIdentityChangeNotPending = "tax_identity_change_not_pending";

    public const string TaxIdentityChangeStale = "tax_identity_change_stale";

    public const string TaxIdentitySelfDecision = "tax_identity_self_decision";

    /// <summary>SAT-D57 customer groups.</summary>
    public const string CustomerGroupExists = "customer_group_exists";

    public const string CustomerGroupNameTaken = "customer_group_name_taken";

    public const string CustomerGroupNotFound = "customer_group_not_found";

    public const string CustomerGroupInactive = "customer_group_inactive";

    /// <summary>SAT-D102 e-belge profile.</summary>
    public const string EInvoiceScenarioCodeListMissing = "einvoice_scenario_code_list_missing";

    public const string EInvoiceScenarioNotListed = "einvoice_scenario_not_listed";

    /// <summary>SAT-D119: no VKN/TCKN on the party, so the shared e-belge profile cannot be recorded.</summary>
    public const string EDocumentTaxIdentityRequired = "e_document_tax_identity_required";

    /// <summary>ASSUMPTION-CARI-EDOC-4: a passive account does not change the shared e-belge profile.</summary>
    public const string EDocumentAccountInactive = "e_document_account_inactive";

    /// <summary>EK-18 #76 party merge refusals.</summary>
    public const string PartyAlreadyMerged = "party_already_merged";

    public const string PartyMergeTargetMerged = "party_merge_target_merged";

    public const string PartyMergeTaxIdentityMismatch = "party_merge_tax_identity_mismatch";

    /// <summary>KLN-D38 party succession (predecessor → successor in one company).</summary>
    public const string SuccessionSameParty = "succession_same_party";

    public const string SuccessionSameTaxIdentity = "succession_same_tax_identity";

    public const string SuccessionSuccessorInactive = "succession_successor_inactive";

    public const string SuccessionCycle = "succession_cycle";

    public const string SuccessionAlreadyRecorded = "succession_already_recorded";

    /// <summary>ALS-D99: the deactivation warning must be shown and acknowledged before the account becomes passive.</summary>
    public const string DeactivationWarningAcknowledgementRequired = "deactivation_warning_acknowledgement_required";

    /// <summary>ALS-D111/D113 legal-name maker-checker refusals.</summary>
    public const string LegalNameUnchanged = "legal_name_unchanged";

    public const string LegalNameChangePending = "legal_name_change_pending";

    public const string LegalNameChangeNotPending = "legal_name_change_not_pending";

    public const string LegalNameChangeStale = "legal_name_change_stale";

    public const string LegalNameSelfDecision = "legal_name_self_decision";

    public static string RequiredForProblem(CounterpartyCommandResult result)
    {
        return result switch
        {
            CounterpartyCommandResult.NotFound => NotFound,
            CounterpartyCommandResult.VersionConflict => VersionConflict,
            CounterpartyCommandResult.AlreadyLinked => AlreadyLinked,
            CounterpartyCommandResult.IdempotencyConflict => IdempotencyConflict,
            CounterpartyCommandResult.TaxIdentityChangeRequiresApproval => TaxIdentityChangeRequiresApproval,
            CounterpartyCommandResult.TaxIdentityNotSet => TaxIdentityNotSet,
            CounterpartyCommandResult.TaxIdentityUnchanged => TaxIdentityUnchanged,
            CounterpartyCommandResult.TaxIdentityChangePending => TaxIdentityChangePending,
            CounterpartyCommandResult.TaxIdentityChangeNotPending => TaxIdentityChangeNotPending,
            CounterpartyCommandResult.TaxIdentityChangeStale => TaxIdentityChangeStale,
            CounterpartyCommandResult.TaxIdentitySelfDecision => TaxIdentitySelfDecision,
            CounterpartyCommandResult.CustomerGroupExists => CustomerGroupExists,
            CounterpartyCommandResult.CustomerGroupNameTaken => CustomerGroupNameTaken,
            CounterpartyCommandResult.CustomerGroupNotFound => CustomerGroupNotFound,
            CounterpartyCommandResult.CustomerGroupInactive => CustomerGroupInactive,
            CounterpartyCommandResult.EInvoiceScenarioCodeListMissing => EInvoiceScenarioCodeListMissing,
            CounterpartyCommandResult.EInvoiceScenarioNotListed => EInvoiceScenarioNotListed,
            CounterpartyCommandResult.EDocumentTaxIdentityRequired => EDocumentTaxIdentityRequired,
            CounterpartyCommandResult.EDocumentAccountInactive => EDocumentAccountInactive,
            CounterpartyCommandResult.PartyAlreadyMerged => PartyAlreadyMerged,
            CounterpartyCommandResult.PartyMergeTargetMerged => PartyMergeTargetMerged,
            CounterpartyCommandResult.PartyMergeTaxIdentityMismatch => PartyMergeTaxIdentityMismatch,
            CounterpartyCommandResult.SuccessionSameParty => SuccessionSameParty,
            CounterpartyCommandResult.SuccessionSameTaxIdentity => SuccessionSameTaxIdentity,
            CounterpartyCommandResult.SuccessionSuccessorInactive => SuccessionSuccessorInactive,
            CounterpartyCommandResult.SuccessionCycle => SuccessionCycle,
            CounterpartyCommandResult.SuccessionAlreadyRecorded => SuccessionAlreadyRecorded,
            CounterpartyCommandResult.DeactivationWarningAcknowledgementRequired =>
                DeactivationWarningAcknowledgementRequired,
            CounterpartyCommandResult.LegalNameUnchanged => LegalNameUnchanged,
            CounterpartyCommandResult.LegalNameChangePending => LegalNameChangePending,
            CounterpartyCommandResult.LegalNameChangeNotPending => LegalNameChangeNotPending,
            CounterpartyCommandResult.LegalNameChangeStale => LegalNameChangeStale,
            CounterpartyCommandResult.LegalNameSelfDecision => LegalNameSelfDecision,
            _ => throw new InvalidOperationException(
                $"Cari terminal sonucu stable ProblemDetails koduyla eşleşmiyor: {result}."),
        };
    }
}

public sealed record CounterpartyVersionConflict(
    long ExpectedVersion,
    long CurrentVersion);

public sealed record CounterpartyCommandResponse(
    CounterpartyCommandResult Result,
    int StatusCode,
    string? Title,
    string? Detail,
    PartyCoreView? Resource,
    CounterpartyVersionConflict? Conflict);

public sealed record CounterpartyCommandExecution(
    CounterpartyCommandResponse Response,
    bool IsReplay,
    string OriginalCorrelationId);
