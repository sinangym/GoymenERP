using System.Text.RegularExpressions;

namespace GoymenERP.BuildingBlocks.Work;

/// <summary>
/// The duration class of one approval kind (YOL-D17, YOL-D18). <see cref="Hours"/> is the hour-scale class;
/// <see cref="BusinessDays"/> is the business-day class. Which class a kind is in is NOT declared by its source: it is a
/// row of Work's versioned approval rule table (WP1), whose decided default puts the dispatch note, over-plan shipment,
/// weighing difference and manufacturing plan in <see cref="Hours"/> and every other kind in <see cref="BusinessDays"/>.
/// The length of each class is a versioned Settings policy. A kind never carries its own number of hours or days.
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
    string? CausationId,
    WorkInboxGrantScopeV1? GrantScope = null);

/// <summary>
/// D11 (WP1): the scope of the caller's approve grant when it is NOT company-wide — the sites, locations and work centers
/// the grant names. Null on <see cref="WorkInboxSourceContextV1.GrantScope"/> means a company-wide grant. Work passes a
/// scope only to a source that declares <see cref="IWorkInboxSourceV1.AcceptsScopedApprovers"/>; such a source answers only
/// the records inside the scope (and Work drops any item whose <see cref="WorkInboxItemV1.Scope"/> is outside it).
/// </summary>
public sealed record WorkInboxGrantScopeV1(
    IReadOnlySet<Guid> SiteIds,
    IReadOnlySet<Guid> LocationIds,
    IReadOnlySet<Guid> WorkCenterIds)
{
    /// <summary>True when the grant names at least one of the item's scope keys (an item without a scope is never inside).</summary>
    public bool Covers(WorkInboxItemScopeV1? scope) =>
        scope is not null
        && (scope.SiteId is { } site && SiteIds.Contains(site)
            || scope.LocationId is { } location && LocationIds.Contains(location)
            || scope.WorkCenterId is { } workCenter && WorkCenterIds.Contains(workCenter));
}

/// <summary>
/// D11 (WP1): where an item's record lives, as its owner records it (any of site, location, work center). A scoped approver
/// sees the item only when the grant covers one of them.
/// </summary>
public sealed record WorkInboxItemScopeV1(Guid? SiteId, Guid? LocationId, Guid? WorkCenterId);

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
/// record's maker as the owner's own maker ≠ checker rule names it, or null when the owner's rule names no maker for the
/// record (then the owner refuses no one as its maker). <see cref="ExcludedDeciders"/> is the rest of the owner's
/// "may not decide" set for this record (D4, WP1: e.g. ROL-D10 — the approver of the invoice's purchase order; the
/// recorder of an invoice besides its matcher). Work hides the item from every user in either (YOL-D23: no one else is
/// excluded), so a source returns every pending record of the company, its own makers' included.
/// <see cref="WaitingSinceUtc"/> is when the record began waiting for this decision (UTC, offset zero); a record that
/// waits again after a resubmission carries the new time. <see cref="Scope"/> is where the record lives (D11); a source
/// that accepts scoped approvers must set it.
/// </summary>
public sealed record WorkInboxItemV1(
    string Module,
    string Kind,
    Guid ItemId,
    string DocumentLabel,
    DateTimeOffset WaitingSinceUtc,
    string Link,
    string? PreparedBy,
    IReadOnlyList<WorkApprovalRuleResultV1> Rules,
    IReadOnlyList<WorkApprovalReferenceV1> References,
    IReadOnlyList<string>? ExcludedDeciders = null,
    WorkInboxItemScopeV1? Scope = null)
{
    /// <summary>True when the owner's rule refuses <paramref name="userId"/> as a decider of this record.</summary>
    public bool Excludes(string userId) =>
        WorkInboxRulesV1.SameUser(PreparedBy, userId)
        || ExcludedDeciders is { } excluded && excluded.Any(candidate => WorkInboxRulesV1.SameUser(candidate, userId));
}

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
/// The owner checks its own <see cref="ApproverPermission"/> and answers <see cref="NativeApprovalQueueOutcomeV1.Denied"/>
/// without items when the caller lacks it; Work checks the same permission before asking. The decision is never given
/// from the inbox: <see cref="WorkInboxItemV1.Link"/> opens the owner's own screen, where the owner's command, authority,
/// separation of duties and version rules apply. The kind's duration class and escalation permission are not the
/// source's: they are rows of Work's versioned approval rule table (WP1), which also pins <see cref="ApproverPermission"/>.
/// </summary>
public interface IWorkInboxSourceV1
{
    string Module { get; }

    string Kind { get; }

    /// <summary>The owner's permission name that may decide this kind (shown as "kimde bekliyor").</summary>
    string ApproverPermission { get; }

    /// <summary>
    /// D11 (WP1): true when the owner's own decision command accepts a site/location/work-center scoped grant of
    /// <see cref="ApproverPermission"/>. Only then does Work ask the source for a scoped caller (with
    /// <see cref="WorkInboxSourceContextV1.GrantScope"/> set); otherwise a scoped caller is not asked (company-wide only).
    /// </summary>
    bool AcceptsScopedApprovers => false;

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
    public const int MaxExcludedDecidersPerItem = 16;
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

    /// <summary>
    /// The owner's identity comparison for "the same user": equal ordinal text, or two Guid texts naming the same Guid
    /// (an owner that keeps the actor as a Guid compares that way, e.g. Manufacturing's preparer). Null or blank is nobody.
    /// </summary>
    public static bool SameUser(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var a = left.Trim();
        var b = right.Trim();
        return string.Equals(a, b, StringComparison.Ordinal)
            || Guid.TryParse(a, out var first) && Guid.TryParse(b, out var second) && first == second;
    }

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
