using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Finance.Contracts;

// Preparation contract v1. No endpoint, grant, persistence or financial execution is implemented.
public static class FinanceJson
{
    public static JsonSerializerOptions CreateOptions() => new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
}

// Constructed by the authenticated backend adapter, never bound from a request body.
public sealed record FinanceContext(Guid CompanyId, string ActorId, string CorrelationId, string? CausationId);
public sealed record CommandMetadata(string IdempotencyKey);
public sealed record MoneyText(string Amount, Guid CurrencyId);
public sealed record ProfileReference(string ProfileId, string Version);

public sealed record ReceiptPreparation(Guid PartyId, Guid CompanyAccountId, Guid TreasuryAccountId,
    MoneyText Money, ProfileReference Profile, DateOnly OccurredOn, DateOnly? ValueOn,
    string? BankReference, string? Description);

// Save replaces the complete draft; optional nulls clear fields. No patch semantics.
public sealed record DraftSaveRequest(Guid? DraftId, string? ExpectedVersion, ReceiptPreparation Preparation);
public sealed record RecordRequest(Guid DraftId, string ExpectedVersion);
// Original amount/currency/account are resolved from the source, never supplied for reversal.
public sealed record ReverseRequest(Guid SourceId, string ExpectedVersion, DateOnly OccurredOn, string Reason);
public sealed record SourceListRequest(string? Cursor);
public sealed record SourceDetailRequest(Guid SourceId);

// D4 (#66 5749046401) adds ProposeBankAccountIbanChange and ApproveBankAccountIbanChange. They are
// separate ACTIONS — they carry their own idempotency records and audit envelopes — but they map onto
// the EXISTING finance.bank-account.propose/.approve permission NAMES, because D4 says the changed
// value "Ö4 öneren≠onaylayan sürecine girer": the same dual control, applied to a change. Minting new
// permission names would be granting authority nobody approved.
public enum FinanceAction
{
    ReadBankAccounts, ReadSources, ReadAmount, SaveDraft, Record, Reverse, ProposeBankAccount, ApproveBankAccount,
    ProposeBankAccountIbanChange, ApproveBankAccountIbanChange,
}
public enum PermissionDecision { Unavailable, Denied, Allowed }

public enum ReadOutcome { Unavailable, Denied, NotFound, Found }
// String names on the wire, in the generated OpenAPI schema as in FinanceJson (FIN-T04b; the FE decodes names).
[JsonConverter(typeof(JsonStringEnumConverter<CommandOutcome>))]
public enum CommandOutcome { Unavailable, Denied, ValidationFailed, VersionConflict, IdempotencyConflict, Succeeded }
[JsonConverter(typeof(JsonStringEnumConverter<SourceStage>))]
public enum SourceStage { Draft, Recorded, Reversed }
[JsonConverter(typeof(JsonStringEnumConverter<AmountDisclosure>))]
public enum AmountDisclosure { Unavailable, Redacted, Visible }
[JsonConverter(typeof(JsonStringEnumConverter<AllocationKnowledge>))]
public enum AllocationKnowledge { Unavailable, Known }
public sealed record AmountView(AmountDisclosure Disclosure, MoneyText? Money);
public sealed record UnallocatedView(AllocationKnowledge Knowledge, AmountView Amount);
public sealed record BankAccountView(Guid Id, Guid CompanyId, Guid CurrencyId, string DisplayName,
    bool IsActive, bool IsUsableForReceipt, string Version);
public sealed record PartyAccountEligibility(Guid PartyId, Guid CompanyAccountId, Guid CompanyId, bool IsActive,
    DraftPartyLabel? Label = null, bool IsPartyActive = false);
public sealed record SourceSummary(Guid Id, Guid CompanyId, string SourceNumber, string Version,
    SourceStage Stage, Guid PartyId, Guid CompanyAccountId, Guid TreasuryAccountId, Guid CurrencyId,
    DateOnly OccurredOn, DateOnly? ValueOn, AmountView Amount);
public sealed record SourceDetail(SourceSummary Source, ProfileReference Profile, string? BankReference,
    string? Description, DateTimeOffset? RecordedAtUtc, Guid? ReversalSourceId, Guid? ReversesSourceId,
    UnallocatedView Unallocated);
public sealed record SourcePage(Guid CompanyId, IReadOnlyList<SourceSummary> Items, string? NextCursor);
public sealed record BankAccountPage(Guid CompanyId, IReadOnlyList<BankAccountView> Items);
public sealed record ReadResult<T>(ReadOutcome Outcome, T? Resource) where T : class;
public sealed record ValidationIssue(string Field, string Code);
public sealed record CommandResult(CommandOutcome Outcome, SourceDetail? Resource,
    bool IsReplay, string OriginalCorrelationId, IReadOnlyList<ValidationIssue> Issues);

// Adapters must check fresh company/action authority on every read and command, including replay.
// These are proposed module ports, not published Identity grant names or HTTP status mappings.
public interface IFinancePermissionPort
{
    Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId, CancellationToken cancellationToken);
}
public interface IFinancePartyAccountPort
{
    Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId, Guid companyAccountId, CancellationToken cancellationToken);
}
public interface IFinanceBankAccountReader
{
    Task<ReadResult<BankAccountPage>> ListAuthorizedAsync(FinanceContext context, CancellationToken cancellationToken);
    Task<ReadResult<BankAccountView>> GetAuthorizedAsync(FinanceContext context, Guid accountId, CancellationToken cancellationToken);
}
public interface IManualReceiptService
{
    Task<CommandResult> DraftSaveAsync(FinanceContext context, CommandMetadata metadata, DraftSaveRequest request, CancellationToken cancellationToken);
    Task<CommandResult> RecordAsync(FinanceContext context, CommandMetadata metadata, RecordRequest request, CancellationToken cancellationToken);
    Task<CommandResult> ReverseAsync(FinanceContext context, CommandMetadata metadata, ReverseRequest request, CancellationToken cancellationToken);
    Task<ReadResult<SourcePage>> ListAuthorizedAsync(FinanceContext context, SourceListRequest request, CancellationToken cancellationToken);
    Task<ReadResult<SourceDetail>> DetailAuthorizedAsync(FinanceContext context, SourceDetailRequest request, CancellationToken cancellationToken);
}

public static partial class PreparationValidator
{
    // String inspection avoids decimal/double range loss and never rounds.
    public static IReadOnlyList<ValidationIssue> Validate(ReceiptPreparation request, Guid companyId,
        BankAccountView? bank, PartyAccountEligibility? party, MoneyProfileV1? profile, string operation)
    {
        var issues = new List<ValidationIssue>();
        void Reject(string field, string code) => issues.Add(new(field, code));
        if (companyId == Guid.Empty) Reject("companyId", "company_required");
        if (request.PartyId == Guid.Empty || request.CompanyAccountId == Guid.Empty ||
            request.TreasuryAccountId == Guid.Empty || request.Money.CurrencyId == Guid.Empty)
            Reject("identity", "identity_required");
        if (bank is null) Reject("treasuryAccountId", "bank_account_unavailable");
        else if (bank.Id != request.TreasuryAccountId || bank.CompanyId != companyId ||
            bank.CurrencyId != request.Money.CurrencyId || !bank.IsActive || !bank.IsUsableForReceipt)
            Reject("treasuryAccountId", "bank_account_ineligible");
        if (party is null) Reject("companyAccountId", "party_account_unavailable");
        else if (party.CompanyId != companyId || party.PartyId != request.PartyId ||
            party.CompanyAccountId != request.CompanyAccountId || !party.IsActive)
            Reject("companyAccountId", "party_account_ineligible");
        if (profile is null) Reject("profile", "settings_provider_unavailable");
        else
        {
            if (profile.Profile is null || profile.Profile.ProfileId == Guid.Empty || profile.Profile.Version <= 0)
            {
                Reject("profile", "settings_profile_invalid");
                return issues;
            }
            if (profile.CompanyId != companyId || profile.CurrencyId != request.Money.CurrencyId ||
                profile.Operation != operation || !profile.IsActive ||
                request.Profile is null ||
                profile.Profile.ProfileId.ToString("D") != request.Profile.ProfileId ||
                profile.Profile.Version.ToString(CultureInfo.InvariantCulture) != request.Profile.Version ||
                request.OccurredOn < profile.EffectiveFrom ||
                (profile.EffectiveThrough is { } through && request.OccurredOn > through))
                Reject("profile", "profile_mismatch");
            try { _ = ExactPositiveDecimalV1.Parse(request.Money.Amount, profile.Scale); }
            catch (SettingsPrerequisiteException exception) { Reject("money.amount", exception.Code); }
        }
        return issues;
    }

    public static bool IsValidDraftIdentity(DraftSaveRequest request) => request.DraftId is null
        ? request.ExpectedVersion is null
        : request.DraftId != Guid.Empty && !string.IsNullOrWhiteSpace(request.ExpectedVersion);

    public static bool IsValidReverse(ReverseRequest request) => request.SourceId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(request.ExpectedVersion) && !string.IsNullOrWhiteSpace(request.Reason);
}
