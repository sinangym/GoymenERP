using System.Text.RegularExpressions;

namespace GoymenERP.BuildingBlocks.Work;

/// <summary>
/// The duration class of one approval kind (YOL-D17, YOL-D18). <see cref="Hours"/> is the hour-scale class (dispatch
/// note, over-plan shipment, weighing difference, manufacturing plan); <see cref="BusinessDays"/> is every other kind.
/// The class is the owner's declaration; the length of each class is a versioned Settings policy that Work reads. A
/// kind never carries its own number of hours or days.
/// </summary>
public enum WorkApprovalDurationClassV1
{
    Hours,
    BusinessDays,
}

/// <summary>
/// The caller of one Work inbox read (W1-A, AI-D31). The host captures it from the resolved request and hands it to
/// every source unchanged. A source answers only for <see cref="CompanyId"/> and only under its own authority.
/// </summary>
public sealed record WorkInboxSourceContextV1(
    Guid CompanyId,
    string UserId,
    string CorrelationId,
    string? CausationId);

/// <summary>
/// One deterministic rule result that put the record into approval (AI-D32): which owner rule fired
/// (<see cref="RuleCode"/>, <c>{module}.{rule}</c>, lowercase), and, where the rule has numbers, the threshold, the
/// actual value and the deviation as invariant-culture decimal text the owner computed with its own calculator, with
/// their unit (<c>%</c>, <c>kg</c>, a currency code, ...). Work and the screen never compute these; a rule without
/// numbers leaves all four null.
/// </summary>
public sealed record WorkApprovalRuleResultV1(
    string RuleCode,
    string? Threshold,
    string? Actual,
    string? Deviation,
    string? Unit);

/// <summary>
/// One record the approval rests on (AI-D32 "dayandığı kayıtlar"): its role in English code form
/// (<c>sales-order-draft</c>, <c>customer</c>, <c>purchase-order</c>, ...), the owner's display label (document number,
/// name), the record id when it has one, and the owner's screen for it when one exists (relative application path).
/// </summary>
public sealed record WorkApprovalReferenceV1(
    string Role,
    string Label,
    Guid? RecordId,
    string? Link);

/// <summary>
/// One pending approval as its owner lists it for the Work inbox. <see cref="PreparedBy"/> is the user id of the
/// record's maker: Work hides the item from that user (maker ≠ checker), so a source returns every pending record of the
/// company, its own makers' included. <see cref="WaitingSinceUtc"/> is when the record began waiting for this decision
/// (UTC, offset zero); a record that waits again after a resubmission carries the new time.
/// </summary>
public sealed record WorkInboxItemV1(
    string Module,
    string Kind,
    Guid ItemId,
    string DocumentLabel,
    DateTimeOffset WaitingSinceUtc,
    string Link,
    string PreparedBy,
    IReadOnlyList<WorkApprovalRuleResultV1> Rules,
    IReadOnlyList<WorkApprovalReferenceV1> References);

/// <summary>
/// A source's answer. <see cref="Items"/> and <see cref="CompanyId"/> are set only for
/// <see cref="NativeApprovalQueueOutcomeV1.Found"/>; <see cref="HasMore"/> says the owner holds more pending records
/// than it returned. Oldest first.
/// </summary>
public sealed record WorkInboxSourceResultV1(
    NativeApprovalQueueOutcomeV1 Outcome,
    Guid CompanyId,
    IReadOnlyList<WorkInboxItemV1>? Items,
    bool HasMore)
{
    public static WorkInboxSourceResultV1 Found(Guid companyId, IReadOnlyList<WorkInboxItemV1> items, bool hasMore) =>
        new(NativeApprovalQueueOutcomeV1.Found, companyId, items, hasMore);

    public static WorkInboxSourceResultV1 Denied() =>
        new(NativeApprovalQueueOutcomeV1.Denied, Guid.Empty, null, false);

    public static WorkInboxSourceResultV1 Unavailable() =>
        new(NativeApprovalQueueOutcomeV1.Unavailable, Guid.Empty, null, false);
}

/// <summary>
/// Read-only port a module implements over one kind of its own pending approvals so the Work inbox can show them with
/// the reason they wait (W1-A). Work owns no copy and never reads the owner's tables: each read asks the owner afresh.
/// The owner checks its own <see cref="ApproverPermission"/> (company-wide) and answers
/// <see cref="NativeApprovalQueueOutcomeV1.Denied"/> without items when the caller lacks it; Work checks the same
/// permission before asking. The decision is never given from the inbox: <see cref="WorkInboxItemV1.Link"/> opens the
/// owner's own screen, where the owner's command, authority, separation of duties and version rules apply.
/// </summary>
public interface IWorkInboxSourceV1
{
    string Module { get; }

    string Kind { get; }

    /// <summary>The owner's permission name that may decide this kind (shown as "kimde bekliyor").</summary>
    string ApproverPermission { get; }

    WorkApprovalDurationClassV1 DurationClass { get; }

    Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken);
}

/// <summary>Shape rules of the Work inbox source contract v1, shared by the host, the sources and tests.</summary>
public static class WorkInboxRulesV1
{
    public const int MaxDocumentLabelLength = 200;
    public const int MaxRuleCodeLength = 120;
    public const int MaxRulesPerItem = 8;
    public const int MaxReferencesPerItem = 12;
    public const int MaxRoleLength = 64;
    public const int MaxReferenceLabelLength = 200;
    public const int MaxUnitLength = 16;
    public const int MaxDecimalTextLength = 40;
    public const int MaxPermissionLength = 128;

    private static readonly Regex RuleCodePattern = new(
        "^[a-z][a-z0-9]*(?:-[a-z0-9]+)*(?:\\.[a-z0-9]+(?:-[a-z0-9]+)*)+\\z", RegexOptions.CultureInvariant);

    private static readonly Regex RolePattern = new(
        "^[a-z][a-z0-9]*(?:-[a-z0-9]+)*\\z", RegexOptions.CultureInvariant);

    private static readonly Regex DecimalPattern = new(
        "^-?(?:0|[1-9][0-9]*)(?:\\.[0-9]+)?\\z", RegexOptions.CultureInvariant);

    private static readonly Regex UnitPattern = new(
        "^(?:%|[A-Za-z]{1,16})\\z", RegexOptions.CultureInvariant);

    private static readonly Regex PermissionPattern = new(
        "^[a-z][a-z0-9]*(?:-[a-z0-9]+)*(?:\\.[a-z0-9]+(?:-[a-z0-9]+)*)+\\z", RegexOptions.CultureInvariant);

    public static bool IsValidPermission(string? permission) =>
        permission is { Length: > 0 and <= MaxPermissionLength } && PermissionPattern.IsMatch(permission);

    /// <summary>A rule code is <c>{module}.{rule}</c>, owned by the module that names it.</summary>
    public static bool IsValidRuleCode(string module, string? ruleCode) =>
        ruleCode is { Length: > 0 and <= MaxRuleCodeLength }
        && RuleCodePattern.IsMatch(ruleCode)
        && ruleCode.StartsWith(module + ".", StringComparison.Ordinal);

    public static bool IsValidDecimalText(string? value) =>
        value is null || value.Length <= MaxDecimalTextLength && DecimalPattern.IsMatch(value);

    public static bool IsValidRule(string module, WorkApprovalRuleResultV1? rule) =>
        rule is not null
        && IsValidRuleCode(module, rule.RuleCode)
        && IsValidDecimalText(rule.Threshold)
        && IsValidDecimalText(rule.Actual)
        && IsValidDecimalText(rule.Deviation)
        && (rule.Unit is null || UnitPattern.IsMatch(rule.Unit))
        && (rule.Unit is null || rule.Threshold is not null || rule.Actual is not null || rule.Deviation is not null);

    public static bool IsValidReference(WorkApprovalReferenceV1? reference) =>
        reference is not null
        && reference.Role is { Length: > 0 and <= MaxRoleLength }
        && RolePattern.IsMatch(reference.Role)
        && !string.IsNullOrWhiteSpace(reference.Label)
        && reference.Label.Length <= MaxReferenceLabelLength
        && reference.RecordId != Guid.Empty
        && (reference.Link is null || NativeApprovalQueueRulesV1.IsValidLink(reference.Link));

    /// <summary>Formats an owner-computed decimal as invariant text without trailing zeros (contract form).</summary>
    public static string Decimal(decimal value)
    {
        var text = value.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture);
        return text == "-0" ? "0" : text;
    }
}
