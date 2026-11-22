using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32, YOL-D18): the incoming invoices whose three-way match left a price, quantity or exchange-rate variance
/// waiting for approval (ALS-D8), as Purchasing lists them for the Work inbox. An invoice waits while its latest revision is
/// Matched, the owner's match found at least one variance and no variance decision is recorded — exactly what
/// <c>ApprovePurchaseIncomingInvoiceVarianceV1</c> accepts. It is approved on the invoice's own screen by a holder of
/// <c>purchasing.invoices.approve-variance</c> other than its recorder and matcher. Every such invoice of the company is
/// returned, the caller's own included: Work hides a maker's own item.
/// <para>
/// The variances are not stored apart: each invoice is restored through its own history (the domain's
/// <see cref="PurchaseIncomingInvoice.Match"/> recomputes them from the frozen receipt facts and the frozen Ayarlar policy). One rule
/// per variance kind carries the frozen policy tolerance as the threshold (amount tolerance for price, rate tolerance for the
/// exchange rate; quantity has no tolerance, it must match exactly), the invoiced value of the line with the largest difference
/// as the actual value and its signed difference from the expected value as the deviation. Purchasing stores the currency and
/// unit identities, not their codes, so the unit is left empty. The matched invoices are filtered in memory because the
/// variance lives in the revision snapshot; the set is bounded by the invoices matched but not yet finalized.
/// </para>
/// </summary>
internal sealed class PurchaseIncomingInvoiceVarianceWorkInboxSourceV1(
    PurchasingDbContext dbContext,
    PurchasingWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "purchasing";
    public const string KindName = "purchasing.incoming-invoice-variance.v1";

    /// <summary>A line's net amount differs from the matched receipts' expected net by more than the amount tolerance.</summary>
    public const string PriceVarianceRule = "purchasing.invoice-price-variance";

    /// <summary>A line's invoiced quantity differs from the matched receipt quantity (no tolerance: it must match exactly).</summary>
    public const string QuantityVarianceRule = "purchasing.invoice-quantity-variance";

    /// <summary>The official rate differs from a receipt's control rate by more than the rate tolerance.</summary>
    public const string ExchangeRateVarianceRule = "purchasing.invoice-exchange-rate-variance";

    private const string PriceKind = "Price";
    private const string QuantityKind = "Quantity";
    private const string ExchangeRateKind = "ExchangeRate";

    /// <summary>The supplier reference plus at most this many matched orders (the contract's reference limit).</summary>
    private const int MaxOrderReferences = WorkInboxRulesV1.MaxReferencesPerItem - 1;

    // The store's snapshot serializer settings (PurchasingStore.Json): web defaults.
    private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => PurchaseIncomingInvoicePermissionNamesV1.ApproveVariance;

    public WorkApprovalDurationClassV1 DurationClass => WorkApprovalDurationClassV1.BusinessDays;

    public async Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        cancellationToken.ThrowIfCancellationRequested();
        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is { } refused)
        {
            return refused;
        }

        List<Pending> pending;
        Dictionary<Guid, string?> orderNumbers;
        try
        {
            var matched = (int)PurchaseIncomingInvoiceStatus.Matched;
            var revisions = await dbContext.IncomingInvoiceRevisions.AsNoTracking()
                .Where(row => row.CompanyId == context.CompanyId && row.Status == matched
                    && row.Version == dbContext.IncomingInvoiceRevisions
                        .Where(item => item.CompanyId == row.CompanyId && item.InvoiceId == row.InvoiceId)
                        .Max(item => item.Version))
                .ToListAsync(cancellationToken);
            pending = [];
            foreach (var revision in revisions)
            {
                var invoice = Restore(context.CompanyId, revision);
                if (invoice.Variances.Count != 0 && invoice.VarianceDecision is null)
                {
                    pending.Add(new(invoice, revision.RecordedAtUtc.ToUniversalTime()));
                }
            }

            pending = pending.OrderBy(item => item.WaitingSinceUtc).ThenBy(item => item.Invoice.Id).ToList();
            var orderIds = pending.Take(maxCount).SelectMany(item => OrderIds(item.Invoice)).Distinct().ToArray();
            orderNumbers = await dbContext.Orders.AsNoTracking()
                .Where(row => row.CompanyId == context.CompanyId && orderIds.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, row => row.DocumentNumber, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await fence.CheckAsync(context, ApproverPermission, cancellationToken)
                ?? WorkInboxSourceResultV1.Unavailable();
        }

        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

        var items = new List<WorkInboxItemV1>(Math.Min(pending.Count, maxCount));
        foreach (var entry in pending.Take(maxCount))
        {
            if (!TryItem(entry, orderNumbers, out var item))
            {
                return WorkInboxSourceResultV1.Unavailable();
            }

            items.Add(item);
        }

        return WorkInboxSourceResultV1.Found(context.CompanyId, items, pending.Count > maxCount);
    }

    /// <summary>The store's own reading rule (PurchasingStore.FindIncomingInvoiceAsync): an inconsistent history throws.</summary>
    private static PurchaseIncomingInvoice Restore(Guid companyId, PurchaseIncomingInvoiceRevisionRow row)
    {
        var snapshot = JsonSerializer.Deserialize<PurchaseIncomingInvoiceSnapshot>(row.SnapshotJson, SnapshotJson)
            ?? throw new InvalidOperationException("Fatura geçmişi okunamadı.");
        if (snapshot.CompanyId != companyId || snapshot.Id != row.InvoiceId || snapshot.Version != row.Version
            || (int)snapshot.Status != row.Status)
        {
            throw new InvalidOperationException("Fatura geçmişi şirket ve sürüm kaydıyla uyuşmuyor.");
        }

        return snapshot.Restore();
    }

    private static IEnumerable<Guid> OrderIds(PurchaseIncomingInvoice invoice) =>
        invoice.Matches.Select(match => match.OrderId).Concat(invoice.AdditionalCosts.Select(cost => cost.OrderId));

    private static bool TryItem(Pending entry, IReadOnlyDictionary<Guid, string?> orderNumbers, out WorkInboxItemV1 item)
    {
        item = null!;
        var invoice = entry.Invoice;
        var label = invoice.Source.OfficialNumber;
        if (invoice.Id == Guid.Empty || string.IsNullOrWhiteSpace(invoice.MatchedBy) || invoice.Policy is not { } policy
            || string.IsNullOrWhiteSpace(label) || label.Length > WorkInboxRulesV1.MaxDocumentLabelLength)
        {
            return false;
        }

        var rules = new List<WorkApprovalRuleResultV1>(3);
        AddRule(rules, invoice, PriceKind, PriceVarianceRule, policy.AmountTolerance);
        AddRule(rules, invoice, QuantityKind, QuantityVarianceRule, threshold: null);
        AddRule(rules, invoice, ExchangeRateKind, ExchangeRateVarianceRule, policy.RateTolerance);
        if (rules.Count == 0 || rules.Count != invoice.Variances.Select(variance => variance.Kind).Distinct().Count())
        {
            // A variance kind this source does not know: unavailable rather than a silent omission.
            return false;
        }

        var references = new List<WorkApprovalReferenceV1>(WorkInboxRulesV1.MaxReferencesPerItem)
        {
            new("supplier", PurchasingWorkInboxFence.ShortReference(invoice.SupplierPartyId), invoice.SupplierPartyId, null),
        };
        foreach (var orderId in OrderIds(invoice).Distinct().Order().Take(MaxOrderReferences))
        {
            orderNumbers.TryGetValue(orderId, out var number);
            references.Add(new("purchase-order", PurchasingWorkInboxFence.OrderLabel(orderId, number), orderId,
                PurchasingWorkInboxFence.Link(PurchasingWorkInboxFence.OrderScreenPath, orderId)));
        }

        item = new WorkInboxItemV1(ModuleName, KindName, invoice.Id, label, entry.WaitingSinceUtc,
            PurchasingWorkInboxFence.Link(PurchasingWorkInboxFence.InvoiceScreenPath, invoice.Id), invoice.MatchedBy,
            rules, references);
        return true;
    }

    /// <summary>
    /// One rule for one variance kind: the owner's frozen tolerance, and the line with the largest absolute difference
    /// (then the lowest line number) as the actual value and its signed difference — the quantity the domain compared.
    /// </summary>
    private static void AddRule(List<WorkApprovalRuleResultV1> rules, PurchaseIncomingInvoice invoice, string kind, string ruleCode,
        decimal? threshold)
    {
        var lineNumbers = invoice.Lines.ToDictionary(line => line.Id, line => line.LineNo);
        var worst = invoice.Variances.Where(variance => variance.Kind == kind)
            .OrderByDescending(variance => Math.Abs(variance.Actual - variance.Expected))
            .ThenBy(variance => lineNumbers.TryGetValue(variance.InvoiceLineId, out var lineNo) ? lineNo : int.MaxValue)
            .FirstOrDefault();
        if (worst is null)
        {
            return;
        }

        rules.Add(new(ruleCode,
            threshold is { } tolerance ? WorkInboxRulesV1.Decimal(tolerance) : null,
            WorkInboxRulesV1.Decimal(worst.Actual),
            WorkInboxRulesV1.Decimal(worst.Actual - worst.Expected),
            null));
    }

    private sealed record Pending(PurchaseIncomingInvoice Invoice, DateTimeOffset WaitingSinceUtc);
}
