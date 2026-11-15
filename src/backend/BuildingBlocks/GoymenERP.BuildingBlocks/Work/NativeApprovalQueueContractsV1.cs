using System.Text.RegularExpressions;

namespace GoymenERP.BuildingBlocks.Work;

/// <summary>
/// The caller of one Work queue read (B-25, F-021, native approval queue contract v1). The host captures it from the
/// resolved request (company context, authenticated actor, correlation) and hands it to every source unchanged. A
/// source answers only for <see cref="CompanyId"/> and only what <see cref="UserId"/> may approve under the owning
/// module's own authority; it never trusts a company id taken from anywhere else.
/// </summary>
public sealed record NativeApprovalQueueContextV1(
    Guid CompanyId,
    string UserId,
    string CorrelationId,
    string? CausationId);

/// <summary>
/// One pending approval as a module lists it for the Work queue. <see cref="Module"/> and <see cref="Kind"/> are the
/// registered source's own names; <see cref="ItemId"/> is the owner's record id; <see cref="CreatedAtUtc"/> is when the
/// record began waiting (UTC, offset zero); <see cref="Link"/> is the owner's own screen for this record as a relative
/// application path (see <see cref="NativeApprovalQueueRulesV1.IsValidLink"/>). The queue only lists: the decision is
/// always given on the owner's screen with the owner's authority, separation of duties and version rules.
/// </summary>
public sealed record NativeApprovalQueueItemV1(
    string Module,
    Guid ItemId,
    string Kind,
    DateTimeOffset CreatedAtUtc,
    string Link);

/// <summary>How a source answered one queue read.</summary>
public enum NativeApprovalQueueOutcomeV1
{
    /// <summary>The source read its pending approvals for the caller; the list may be empty.</summary>
    Found,

    /// <summary>The caller may not see this source's approvals (owner authority); nothing is disclosed.</summary>
    Denied,

    /// <summary>The source cannot answer now; the queue shows it as unavailable, never as empty.</summary>
    Unavailable,
}

/// <summary>
/// A source's answer. <see cref="Items"/> and <see cref="CompanyId"/> are set only for
/// <see cref="NativeApprovalQueueOutcomeV1.Found"/>; <see cref="HasMore"/> says the owner holds more pending records
/// than it returned (the owner's own screen lists them all).
/// </summary>
public sealed record NativeApprovalQueueResultV1(
    NativeApprovalQueueOutcomeV1 Outcome,
    Guid CompanyId,
    IReadOnlyList<NativeApprovalQueueItemV1>? Items,
    bool HasMore)
{
    public static NativeApprovalQueueResultV1 Found(
        Guid companyId,
        IReadOnlyList<NativeApprovalQueueItemV1> items,
        bool hasMore) =>
        new(NativeApprovalQueueOutcomeV1.Found, companyId, items, hasMore);

    public static NativeApprovalQueueResultV1 Denied() =>
        new(NativeApprovalQueueOutcomeV1.Denied, Guid.Empty, null, false);

    public static NativeApprovalQueueResultV1 Unavailable() =>
        new(NativeApprovalQueueOutcomeV1.Unavailable, Guid.Empty, null, false);
}

/// <summary>
/// Read-only port a module implements over its own pending approvals so the Work queue can list them (B-25). Work owns
/// no copy: each read asks the owner afresh, and the owner applies its own permission and company rules. A source
/// serves exactly one <see cref="Kind"/>, which starts with its <see cref="Module"/> name and ends with a version
/// (<c>module.subject.v1</c>); one kind is served by one source only, or the whole queue is unavailable. It returns at
/// most <c>maxCount</c> items of <see cref="NativeApprovalQueueContextV1.CompanyId"/>, never writes, and never reads
/// another module's tables.
/// </summary>
public interface INativeApprovalQueueAdapterV1
{
    string Module { get; }

    string Kind { get; }

    Task<NativeApprovalQueueResultV1> ListPendingAsync(
        NativeApprovalQueueContextV1 context,
        int maxCount,
        CancellationToken cancellationToken);
}

/// <summary>Name, link and size rules of the native approval queue contract v1, shared by the host and tests.</summary>
public static class NativeApprovalQueueRulesV1
{
    /// <summary>Longest module name.</summary>
    public const int MaxModuleLength = 64;

    /// <summary>Longest approval kind name.</summary>
    public const int MaxKindLength = 160;

    /// <summary>Longest owner link.</summary>
    public const int MaxLinkLength = 512;

    /// <summary>Most items one source returns to one queue read.</summary>
    public const int MaxItemsPerSource = 50;

    private static readonly Regex ModulePattern = new(
        "^[a-z][a-z0-9]*(?:-[a-z0-9]+)*\\z", RegexOptions.CultureInvariant);

    private static readonly Regex KindPattern = new(
        "^[a-z][a-z0-9]*(?:-[a-z0-9]+)*(?:\\.[a-z0-9]+(?:-[a-z0-9]+)*)+\\.v[1-9][0-9]*\\z",
        RegexOptions.CultureInvariant);

    private static readonly Regex LinkPattern = new(
        "^/(?!/)[A-Za-z0-9\\-._~/%?=&+]*\\z", RegexOptions.CultureInvariant);

    public static bool IsValidModule(string? module) =>
        module is { Length: > 0 and <= MaxModuleLength } && ModulePattern.IsMatch(module);

    /// <summary>A kind is <c>{module}.{subject}.v{n}</c>: lowercase, owned by the module it names.</summary>
    public static bool IsValidKind(string? module, string? kind) =>
        IsValidModule(module)
        && kind is { Length: > 0 and <= MaxKindLength }
        && KindPattern.IsMatch(kind)
        && kind.StartsWith(module + ".", StringComparison.Ordinal);

    /// <summary>
    /// A link is a relative application path: one leading slash, no scheme or host, no backslash, no "//" anywhere, no
    /// fragment, no "." or ".." segment (also percent-encoded), only URL-safe characters.
    /// </summary>
    public static bool IsValidLink(string? link)
    {
        if (link is not { Length: > 0 and <= MaxLinkLength }
            || !LinkPattern.IsMatch(link)
            || link.Contains("//", StringComparison.Ordinal))
        {
            return false;
        }

        var queryStart = link.IndexOf('?', StringComparison.Ordinal);
        var path = queryStart < 0 ? link : link[..queryStart];
        foreach (var segment in path.Split('/'))
        {
            // A browser resolves "%2e%2e" like "..", and "%2f" or "%5c" would smuggle a separator: judge the decoded segment.
            var decoded = Uri.UnescapeDataString(segment);
            if (decoded is "." or ".."
                || decoded.Contains('/', StringComparison.Ordinal)
                || decoded.Contains('\\', StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }
}
