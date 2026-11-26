using System.Globalization;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>
/// D5 (WP1): one native D-05 approval kind (<see cref="NativeApprovalKindsV1"/>: official invoice import, stock count, stock
/// scrap, sales order draft) as a Work inbox source, so the inbox is the one box and the B-25 queue is no longer a second
/// list. Each read is the owner's own first pending page through its typed read adapter, judged by the same strict rules as
/// "Onaylarım" (<see cref="NativeApprovalReadQueryV1.ValidatePage"/>); the owner checks its approve permission
/// (company-wide) and leaves out the caller's own submissions. The item's maker is the owner's submitter, the link opens the
/// existing Work approval detail screen where the owner's own decision command applies, and the rule and numbers are the
/// owner's recorded values (nothing is computed here). The kind's duration class is the rule table's, never this class's.
/// </summary>
internal interface INativeApprovalDecisionReadAdapterV1
{
    bool HasDecisionRead { get; }
    Task<WorkInboxSourceResultV1> ListDecisionPendingAsync(WorkInboxSourceContextV1 context,int maxCount,CancellationToken token);
    Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context,Guid id,CancellationToken token);
}

internal sealed class NativeApprovalWorkInboxSourceV1 : IWorkApprovalDecisionSourceV1
{
    public const string SalesOrderDraftRule = "sales.order-draft-approval";
    public const string OfficialInvoiceImportRule = "finance.official-invoice-import-approval";
    public const string StockCountDifferenceRule = "inventory.stock-count-difference";
    public const string StockScrapRule = "inventory.stock-scrap-request";
    private const string KilogramUnit = "kg";
    private const int ShortReferenceLength = 12;

    private readonly INativeApprovalReadAdapterV1 inner;

    public NativeApprovalWorkInboxSourceV1(INativeApprovalReadAdapterV1 inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        this.inner = inner;
        Kind = inner.Kind;
        var separator = Kind.IndexOf('.', StringComparison.Ordinal);
        Module = separator > 0 ? Kind[..separator] : string.Empty;
        ApproverPermission = ApproverPermissionOf(Kind) ?? string.Empty;
    }

    public string Module { get; }

    public string Kind { get; }

    public string ApproverPermission { get; }

    public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context,Guid itemId,CancellationToken cancellationToken) =>
        inner is INativeApprovalDecisionReadAdapterV1 decision ? decision.ReadDecisionAsync(context,itemId,cancellationToken)
            : Task.FromResult<WorkApprovalDecisionSnapshotV1?>(null);

    /// <summary>The owner's approve permission of each native kind (the name its own read and decision check).</summary>
    internal static string? ApproverPermissionOf(string kind) => kind switch
    {
        NativeApprovalKindsV1.FinanceOfficialInvoiceImport => ErpPermissions.FinanceOfficialInvoiceImportApprove,
        NativeApprovalKindsV1.InventoryStockCount => ErpPermissions.InventoryStockCountApprove,
        NativeApprovalKindsV1.InventoryStockScrap => ErpPermissions.InventoryStockScrapApprove,
        NativeApprovalKindsV1.SalesOrderDraft => ErpPermissions.SalesOrdersApprove,
        _ => null,
    };

    public async Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        cancellationToken.ThrowIfCancellationRequested();
        if(inner is INativeApprovalDecisionReadAdapterV1 { HasDecisionRead:true } decision)
            return await decision.ListDecisionPendingAsync(context,maxCount,cancellationToken);
        if (context.GrantScope is not null || !NativeApprovalKindsV1.IsKnown(Kind))
        {
            // Native owners decide company-wide only; a scoped caller is never asked (AcceptsScopedApprovers is false).
            return WorkInboxSourceResultV1.Denied();
        }

        var readContext = new NativeApprovalReadContextV1(context.CompanyId, context.UserId, context.CorrelationId,
            context.CausationId, IncludeInventoryLabelCode: true);
        var result = NativeApprovalReadQueryV1.ValidatePage(Kind, readContext,
            await inner.ListAsync(readContext, cursor: null, cancellationToken));
        if (result.Outcome == NativeApprovalReadOutcomeV1.Denied)
        {
            return WorkInboxSourceResultV1.Denied();
        }
        if (result is not { Outcome: NativeApprovalReadOutcomeV1.Found, Resource: { } page })
        {
            return WorkInboxSourceResultV1.Unavailable();
        }

        var items = new List<WorkInboxItemV1>(Math.Min(page.Items.Count, maxCount));
        foreach (var native in page.Items.Take(maxCount))
        {
            if (Item(native) is not { } item)
            {
                // A record the owner answered that this source cannot read as its contract says: never a partial list.
                return WorkInboxSourceResultV1.Unavailable();
            }
            items.Add(item);
        }

        return WorkInboxSourceResultV1.Found(page.CompanyId, items, page.NextCursor is not null || page.Items.Count > maxCount);
    }

    private WorkInboxItemV1? Item(NativeApprovalReadItemV1 native)
    {
        if (!string.Equals(native.Kind, Kind, StringComparison.Ordinal) || native.ApprovalId == Guid.Empty)
        {
            return null;
        }

        var link = $"{NativeApprovalTypedQueueSourceV1.WorkApprovalDetailLinkPrefix}{Kind}/{native.ApprovalId:D}";
        var waitingSince = native.SubmittedAtUtc.ToUniversalTime();
        switch (native)
        {
            case SalesOrderDraftNativeApprovalReadItemV1 draft:
                return Build(native.ApprovalId, draft.DocumentNo, waitingSince, link, draft.SubmittedBy,
                    [new WorkApprovalRuleResultV1(SalesOrderDraftRule, null, null, null, null)], []);
            case FinanceInvoiceImportNativeApprovalReadItemV1 import:
                return Build(native.ApprovalId, Short(import.ApprovalId), waitingSince, link, import.SubmittedBy,
                    [new WorkApprovalRuleResultV1(OfficialInvoiceImportRule, null, null, null, null)], []);
            case InventoryStockCountNativeApprovalReadItemV1 count:
                if (Decimal(count.CountedKg) is not { } counted || Decimal(count.DifferenceKg) is not { } difference)
                {
                    return null;
                }
                return Build(native.ApprovalId, count.LabelCode ?? Short(count.LabelId), waitingSince, link, count.SubmittedBy,
                    [new WorkApprovalRuleResultV1(StockCountDifferenceRule, null, counted, difference, KilogramUnit)],
                    InventoryReferences(count.LabelId, count.LabelCode, count.ProductId, count.LocationId));
            case InventoryStockScrapNativeApprovalReadItemV1 scrap:
                if (Decimal(scrap.ScrapKg) is not { } scrapped)
                {
                    return null;
                }
                return Build(native.ApprovalId, scrap.LabelCode ?? Short(scrap.LabelId), waitingSince, link, scrap.SubmittedBy,
                    [new WorkApprovalRuleResultV1(StockScrapRule, null, scrapped, null, KilogramUnit)],
                    InventoryReferences(scrap.LabelId, scrap.LabelCode, scrap.ProductId, scrap.LocationId));
            default:
                return null;
        }
    }

    private WorkInboxItemV1? Build(Guid itemId, string label, DateTimeOffset waitingSince, string link, string submittedBy,
        IReadOnlyList<WorkApprovalRuleResultV1> rules, IReadOnlyList<WorkApprovalReferenceV1> references) =>
        string.IsNullOrWhiteSpace(label) || label.Length > WorkInboxRulesV1.MaxDocumentLabelLength
            || string.IsNullOrWhiteSpace(submittedBy)
            ? null
            : new WorkInboxItemV1(Module, Kind, itemId, label, waitingSince, link,
                submittedBy.Trim(), rules, references);

    private static IReadOnlyList<WorkApprovalReferenceV1> InventoryReferences(Guid labelId, string? labelCode, Guid productId,
        Guid locationId) =>
    [
        new("inventory-label", labelCode ?? Short(labelId), labelId, null),
        new("product", Short(productId), productId, null),
        new("location", Short(locationId), locationId, null),
    ];

    private static string Short(Guid id) => "…" + id.ToString("D", CultureInfo.InvariantCulture)[^ShortReferenceLength..];

    /// <summary>The owner's recorded kilogram text in the contract's invariant form, or null when it is not a decimal.</summary>
    private static string? Decimal(string? value) =>
        value is not null
        && decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
            out var parsed)
            ? WorkInboxRulesV1.Decimal(parsed)
            : null;
}
