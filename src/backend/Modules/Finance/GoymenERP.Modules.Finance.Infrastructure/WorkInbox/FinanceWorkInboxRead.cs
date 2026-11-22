using GoymenERP.BuildingBlocks.Work;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31): the one read sequence every Finance Work inbox source follows — the fence before the read, the read of
/// at most <c>maxCount + 1</c> pending rows of the caller's company (oldest first) with their lookups, the fence again after
/// the read and in the failure path, and the mapping of every returned row. A row Finance cannot read as its own
/// invariants say makes the whole answer Unavailable: never a partial list. Nothing is written.
/// </summary>
internal static class FinanceWorkInboxRead
{
    /// <summary>
    /// <paramref name="read"/> receives the caller's company and <c>maxCount</c>; it must filter by that company, order
    /// oldest first, take at most <c>maxCount + 1</c> rows and read untracked. <paramref name="item"/> answers null for a
    /// row that violates the owner's invariants.
    /// </summary>
    public static async Task<WorkInboxSourceResultV1> ListAsync<TRow, TLookup>(
        FinanceWorkInboxFence fence,
        WorkInboxSourceContextV1 context,
        string permission,
        int maxCount,
        Func<Guid, int, CancellationToken, Task<(List<TRow> Rows, TLookup Lookup)>> read,
        Func<TRow, TLookup, WorkInboxItemV1?> item,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        cancellationToken.ThrowIfCancellationRequested();
        if (await fence.CheckAsync(context, permission, cancellationToken) is { } refused)
        {
            return refused;
        }

        List<TRow> rows;
        TLookup lookup;
        try
        {
            (rows, lookup) = await read(context.CompanyId, maxCount, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await fence.CheckAsync(context, permission, cancellationToken)
                ?? WorkInboxSourceResultV1.Unavailable();
        }

        if (await fence.CheckAsync(context, permission, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

        var items = new List<WorkInboxItemV1>(Math.Min(rows.Count, maxCount));
        foreach (var row in rows.Take(maxCount))
        {
            if (item(row, lookup) is not { } mapped || !Valid(mapped))
            {
                // A record Finance cannot read as its own invariants say: unavailable, never a partial list.
                return WorkInboxSourceResultV1.Unavailable();
            }

            items.Add(mapped);
        }

        return WorkInboxSourceResultV1.Found(context.CompanyId, items, rows.Count > maxCount);
    }

    /// <summary>The owner's document number when it has one, otherwise the short reference the Finance screens show.</summary>
    public static string Label(string? documentNumber, Guid id) =>
        string.IsNullOrWhiteSpace(documentNumber) ? FinanceWorkInboxFence.ShortReference(id) : documentNumber;

    /// <summary>A stored display name fit for a reference label, otherwise the short reference (never dropped).</summary>
    public static string Name(string? name, Guid id) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= WorkInboxRulesV1.MaxReferenceLabelLength
            ? name
            : FinanceWorkInboxFence.ShortReference(id);

    /// <summary>The stored amount text as contract decimal text, or null when it is not the owner's exact decimal.</summary>
    public static string? Amount(string? text) =>
        FinanceWorkInboxFence.TryAmount(text, out var value) ? WorkInboxRulesV1.Decimal(value) : null;

    public static WorkApprovalReferenceV1 Reference(string role, Guid id, string? label = null, string? link = null) =>
        new(role, label ?? FinanceWorkInboxFence.ShortReference(id), id, link);

    private static bool Valid(WorkInboxItemV1 item) =>
        item.ItemId != Guid.Empty
        && !string.IsNullOrWhiteSpace(item.DocumentLabel)
        && item.DocumentLabel.Length <= WorkInboxRulesV1.MaxDocumentLabelLength
        && NativeApprovalQueueRulesV1.IsValidLink(item.Link)
        && item.Rules.Count is > 0 and <= WorkInboxRulesV1.MaxRulesPerItem
        && item.Rules.All(rule => WorkInboxRulesV1.IsValidRule(item.Module, rule))
        && item.References.Count <= WorkInboxRulesV1.MaxReferencesPerItem
        && item.References.All(WorkInboxRulesV1.IsValidReference);
}
