using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>
/// R47 first-release manual outgoing bank payment. Preparation and approval do not move money;
/// only the separately authorized final record creates the outgoing money event.
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
    string? Description);

/// <summary>Creates or completely replaces a draft. Null optional values clear their fields.</summary>
public sealed record ManualBankPaymentDraftSaveRequest(
    Guid? PaymentId,
    string? ExpectedVersion,
    ManualBankPaymentPreparation Preparation);

/// <summary>The approver must be a different actor from the draft preparer.</summary>
public sealed record ApproveManualBankPaymentRequest(Guid PaymentId, string ExpectedVersion);

/// <summary>
/// A-now requires an explicit human confirmation that the bank result occurred. A missing bank
/// reference or document is not a refusal in this version; an unknown result is never recordable.
/// </summary>
public sealed record RecordManualBankPaymentRequest(
    Guid PaymentId,
    string ExpectedVersion,
    bool BankResultConfirmed);

[JsonConverter(typeof(JsonStringEnumConverter<ManualBankPaymentStatus>))]
public enum ManualBankPaymentStatus
{
    Draft = 0,
    Approved = 1,
    Recorded = 2
}

public sealed record ManualBankPaymentView(
    Guid Id,
    Guid CompanyId,
    ManualBankPaymentStatus Status,
    ManualBankPaymentPreparation Preparation,
    string PreparedBy,
    DateTimeOffset PreparedAtUtc,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAtUtc,
    string? RecordedBy,
    DateTimeOffset? RecordedAtUtc,
    bool BankResultConfirmed,
    string Version);

public sealed record ManualBankPaymentPage(
    Guid CompanyId,
    IReadOnlyList<ManualBankPaymentView> Items,
    string? NextCursor);

public sealed record ManualBankPaymentCommandResult(
    CommandOutcome Outcome,
    ManualBankPaymentView? Resource,
    bool IsReplay,
    string OriginalCorrelationId,
    IReadOnlyList<ValidationIssue> Issues);

/// <summary>
/// Finance-owned R47 contract. Implementations must fence company/action authority after awaited
/// owner reads and immediately before persistence; successful Record is the sole money effect.
/// </summary>
public interface IManualBankPaymentService
{
    Task<ManualBankPaymentCommandResult> DraftSaveAsync(
        FinanceContext context,
        CommandMetadata metadata,
        ManualBankPaymentDraftSaveRequest request,
        CancellationToken cancellationToken);

    Task<ManualBankPaymentCommandResult> ApproveAsync(
        FinanceContext context,
        CommandMetadata metadata,
        ApproveManualBankPaymentRequest request,
        CancellationToken cancellationToken);

    Task<ManualBankPaymentCommandResult> RecordAsync(
        FinanceContext context,
        CommandMetadata metadata,
        RecordManualBankPaymentRequest request,
        CancellationToken cancellationToken);

    Task<ReadResult<ManualBankPaymentPage>> ListAuthorizedAsync(
        FinanceContext context,
        string? cursor,
        CancellationToken cancellationToken);

    Task<ReadResult<ManualBankPaymentView>> DetailAuthorizedAsync(
        FinanceContext context,
        Guid paymentId,
        CancellationToken cancellationToken);
}

public static class ManualBankPaymentValidator
{
    public static bool IsValidDraftIdentity(ManualBankPaymentDraftSaveRequest request) =>
        request.PaymentId is null
            ? request.ExpectedVersion is null
            : request.PaymentId != Guid.Empty && IsVersion(request.ExpectedVersion);

    public static bool CanApprove(
        ManualBankPaymentStatus status,
        string preparedBy,
        string actorId,
        ApproveManualBankPaymentRequest request) =>
        status == ManualBankPaymentStatus.Draft
        && request.PaymentId != Guid.Empty
        && IsVersion(request.ExpectedVersion)
        && !string.IsNullOrWhiteSpace(preparedBy)
        && !string.IsNullOrWhiteSpace(actorId)
        && !string.Equals(preparedBy, actorId, StringComparison.Ordinal);

    public static IReadOnlyList<ValidationIssue> ValidateRecord(
        ManualBankPaymentStatus status,
        RecordManualBankPaymentRequest request)
    {
        var issues = new List<ValidationIssue>();
        if (request.PaymentId == Guid.Empty)
        {
            issues.Add(new ValidationIssue("paymentId", "identity_required"));
        }

        if (!IsVersion(request.ExpectedVersion))
        {
            issues.Add(new ValidationIssue("expectedVersion", "expected_version_invalid"));
        }

        if (status != ManualBankPaymentStatus.Approved)
        {
            issues.Add(new ValidationIssue("status", "manual_bank_payment_not_approved"));
        }

        if (!request.BankResultConfirmed)
        {
            issues.Add(new ValidationIssue("bankResultConfirmed", "bank_result_not_confirmed"));
        }

        return issues;
    }

    private static bool IsVersion(string? value) =>
        long.TryParse(value, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var version)
        && version > 0
        && string.Equals(version.ToString(System.Globalization.CultureInfo.InvariantCulture), value,
            StringComparison.Ordinal);
}
