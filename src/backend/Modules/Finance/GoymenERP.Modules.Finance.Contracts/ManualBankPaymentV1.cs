using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>
/// R47 first-release manual outgoing bank payment. This contract does not assert that a bank
/// integration exists: only a separately authorized human may confirm that the result occurred.
/// </summary>
public sealed record ManualBankPaymentPreparation(
    Guid PartyId,
    Guid CompanyAccountId,
    Guid TreasuryAccountId,
    MoneyText Money,
    ProfileReference Profile,
    DateOnly OccurredOn,
    DateOnly? ValueOn,
    string? BankReference,
    Guid? DocumentId,
    string? Description);

[JsonConverter(typeof(JsonStringEnumConverter<ManualBankPaymentStage>))]
public enum ManualBankPaymentStage
{
    Draft = 0,
    Approved = 1,
    Recorded = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter<ManualBankResultConfirmation>))]
public enum ManualBankResultConfirmation
{
    Unknown = 0,
    Occurred = 1,
}

public sealed record SaveManualBankPaymentDraftRequest(
    Guid? PaymentId,
    string? ExpectedVersion,
    ManualBankPaymentPreparation Preparation);

public sealed record ApproveManualBankPaymentRequest(Guid PaymentId, string ExpectedVersion);

public sealed record RecordManualBankPaymentRequest(
    Guid PaymentId,
    string ExpectedVersion,
    ManualBankResultConfirmation Result);

public sealed record ManualBankPaymentView(
    Guid Id,
    Guid CompanyId,
    string Version,
    ManualBankPaymentStage Stage,
    ManualBankPaymentPreparation Preparation,
    string PreparedBy,
    DateTimeOffset PreparedAtUtc,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAtUtc,
    string? RecordedBy,
    DateTimeOffset? RecordedAtUtc,
    Guid? MoneyEventId);

public sealed record ManualBankPaymentCommandResult(
    CommandOutcome Outcome,
    ManualBankPaymentView? Resource,
    bool IsReplay,
    string OriginalCorrelationId,
    IReadOnlyList<ValidationIssue> Issues);

