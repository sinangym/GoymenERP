using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31): the fence every Accounting Work inbox source applies before its read, after its read and when the read
/// fails. The server-resolved company and the authenticated user must be the context's, the user must still reach the
/// company, and the source's approver permission must be granted company-wide as it stands now (every Accounting decision
/// command requires a company-wide grant: <c>IsGranted &amp;&amp; IsCompanyWide</c>). Anything else is a refusal without items.
/// </summary>
internal sealed class AccountingWorkInboxFence(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority)
{
    /// <summary>The short reference the Accounting screens show for an identity (first eight characters, lowercase).</summary>
    internal const int ShortReferenceLength = 8;

    /// <summary>Null when the caller may read; otherwise the outcome to return.</summary>
    public async Task<WorkInboxSourceResultV1?> CheckAsync(
        WorkInboxSourceContextV1 context,
        string permission,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return WorkInboxSourceResultV1.Denied();
        }

        try
        {
            var scope = await authority.RevalidatePermissionScopeAsync(permission, context.CompanyId, cancellationToken);
            return scope.IsGranted && scope.IsCompanyWide && SameContext(context) ? null : WorkInboxSourceResultV1.Denied();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return WorkInboxSourceResultV1.Unavailable();
        }
    }

    /// <summary>
    /// The one read shape of every Accounting source: fence, read (no tracking, company filtered, oldest first, at most
    /// <paramref name="maxCount"/> + 1 records), fence again, then build. A failed read answers the fence's refusal or
    /// Unavailable; a record the owner cannot read as its own invariants say (<paramref name="build"/> returns null items)
    /// is Unavailable, never a partial list.
    /// </summary>
    public async Task<WorkInboxSourceResultV1> ListAsync<TSnapshot>(
        WorkInboxSourceContextV1 context,
        string permission,
        int maxCount,
        Func<CancellationToken, Task<TSnapshot>> read,
        Func<TSnapshot, (IReadOnlyList<WorkInboxItemV1>? Items, bool HasMore)> build,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        cancellationToken.ThrowIfCancellationRequested();
        if (await CheckAsync(context, permission, cancellationToken) is { } refused)
        {
            return refused;
        }

        TSnapshot snapshot;
        try
        {
            snapshot = await read(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await CheckAsync(context, permission, cancellationToken) ?? WorkInboxSourceResultV1.Unavailable();
        }

        if (await CheckAsync(context, permission, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

        var (items, hasMore) = build(snapshot);
        return items is null
            ? WorkInboxSourceResultV1.Unavailable()
            : WorkInboxSourceResultV1.Found(context.CompanyId, items, hasMore);
    }

    public bool SameContext(WorkInboxSourceContextV1 context) =>
        context.CompanyId != Guid.Empty
        && !string.IsNullOrWhiteSpace(context.UserId)
        && companyContext.IsResolved
        && companyContext.CompanyId == context.CompanyId
        && currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, context.UserId, StringComparison.Ordinal)
        && currentUser.CanAccessCompany(context.CompanyId);

    public static string ShortReference(Guid id) =>
        id.ToString("D", CultureInfo.InvariantCulture)[..ShortReferenceLength];

    public static string Link(string basePath, Guid id) =>
        basePath + "/" + id.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>Accounting keeps actors as Guid; Work compares Guid texts (<see cref="WorkInboxRulesV1.SameUser"/>).</summary>
    public static string Actor(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>A period as the Accounting screens name it: its first and last day.</summary>
    public static string PeriodLabel(DateOnly startsOn, DateOnly endsOn) =>
        startsOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " / "
        + endsOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Exact stored decimal text (Accounting stores these amounts as invariant text, never rounded).</summary>
    public static bool TryAmount(string? text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value);

    /// <summary>A stored document number when the owner has one and it fits; otherwise the record's short reference.</summary>
    public static string Label(string? documentNumber, Guid id) =>
        string.IsNullOrWhiteSpace(documentNumber) || documentNumber.Length > WorkInboxRulesV1.MaxDocumentLabelLength
            ? ShortReference(id)
            : documentNumber;

    /// <summary>Builds the page: null items when any row breaks the owner's invariants.</summary>
    public static (IReadOnlyList<WorkInboxItemV1>? Items, bool HasMore) Page<TRow>(
        IReadOnlyList<TRow> rows,
        int maxCount,
        Func<TRow, WorkInboxItemV1?> map)
    {
        var items = new List<WorkInboxItemV1>(Math.Min(rows.Count, maxCount));
        foreach (var row in rows.Take(maxCount))
        {
            if (map(row) is not { } item)
            {
                return (null, false);
            }

            items.Add(item);
        }

        return (items, rows.Count > maxCount);
    }
}
