using System.Text.RegularExpressions;
using GoymenERP.BuildingBlocks.Work;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

/// <summary>
/// W1-A: the shapes the Sales Work inbox sources share — the owner's order screen link (where the approval controls live),
/// the reference roles and the bounded labels. A stored value that breaks the Work contract shape (an empty number, a
/// foreign company) is an internal error: the source answers Unavailable, never a repaired item.
/// </summary>
internal static class SalesWorkInboxItemsV1
{
    public const string OrderDraftRole = "sales-order-draft";
    public const string PriceListRole = "price-list";
    public const string ProductRole = "product";
    public const string RequestRole = "sales-request";
    public const string InvoiceRole = "sales-invoice";
    public const string SubcontractInvoiceRole = "sales-subcontract-invoice";
    public const string CustomerReturnRole = "sales-customer-return";
    public const string ContractRole = "sales-contract";
    public const string QuoteRole = "sales-quote";

    private static readonly Regex CurrencyUnitPattern = new("^[A-Za-z]{1,16}\\z", RegexOptions.CultureInvariant);

    /// <summary>The Sales order 360 screen (<c>/satis/siparisler/:draftId</c>): the price-floor, discount and risk exception panels.</summary>
    public static string OrderLink(Guid draftId) => $"/satis/siparisler/{draftId:D}";

    /// <summary>WP4: S-01 one request (<c>/satis/talepler/:requestId</c>): the request approval and rejection controls.</summary>
    public static string RequestLink(Guid requestId) => $"/satis/talepler/{requestId:D}";

    /// <summary>WP4: S-09 Satış faturaları (<c>/satis/faturalar</c>): the invoice approval and rejection controls of the list.</summary>
    public const string InvoicesLink = "/satis/faturalar";

    /// <summary>
    /// WP4: S-11 Satış sözleşmeleri, the screen's planned route. The screen is not routed yet (its shell capability is not
    /// published), so the link opens the shell's not-found page until it is; the decision is never given from the inbox.
    /// </summary>
    public const string ContractsLink = "/satis/sozlesmeler";

    /// <summary>WP4 (SAT-D476): S-01 one quote (<c>/satis/teklifler/:quoteId</c>): the request list and the expired-conversion approval.</summary>
    public static string QuoteLink(Guid quoteId) => $"/satis/teklifler/{quoteId:D}";

    /// <summary>WP4: S-10 one customer return (<c>/satis/iadeler/:returnId</c>): the approval control.</summary>
    public static string CustomerReturnLink(Guid returnId) => $"/satis/iadeler/{returnId:D}";

    /// <summary>WP4: the owner's "may not decide" set beyond the maker: the other named actors, distinct from the maker and each other.</summary>
    public static IReadOnlyList<string>? Others(string maker, params string?[] actors)
    {
        var others = actors
            .Where(actor => !string.IsNullOrWhiteSpace(actor))
            .Select(actor => actor!.Trim())
            .Where(actor => !WorkInboxRulesV1.SameUser(actor, maker))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return others.Length == 0 ? null : others;
    }

    public static DateTimeOffset RequireTime(DateTimeOffset? value) => value is { } time
        ? time.ToUniversalTime()
        : throw new InvalidOperationException("Satış kaydının onaya gönderilme zamanı boş.");

    public static string RequireLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label) || label.Trim().Length > WorkInboxRulesV1.MaxDocumentLabelLength)
        {
            throw new InvalidOperationException("Satış kaydının belge numarası Work sözleşmesine uymuyor.");
        }

        return label.Trim();
    }

    public static string RequireActor(string? actor) => string.IsNullOrWhiteSpace(actor)
        ? throw new InvalidOperationException("Satış kaydının hazırlayanı boş.")
        : actor.Trim();

    public static void RequireCompany(Guid expected, Guid actual)
    {
        if (expected == Guid.Empty || actual != expected)
        {
            throw new InvalidOperationException("Satış kaydı istenen şirkete ait değil.");
        }
    }

    /// <summary>The stored currency code as the rule's unit, or null when it is not a plain code.</summary>
    public static string? CurrencyUnit(string? currencyCode) =>
        currencyCode is not null && CurrencyUnitPattern.IsMatch(currencyCode) ? currencyCode : null;

    public static WorkApprovalReferenceV1 OrderReference(Guid draftId, string documentNo) =>
        new(OrderDraftRole, documentNo, draftId, OrderLink(draftId));

    /// <summary>A reference with a bounded label, or null when the stored label is empty.</summary>
    public static WorkApprovalReferenceV1? Reference(string role, string? label, Guid? recordId)
    {
        var text = label?.Trim();
        if (string.IsNullOrEmpty(text) || recordId == Guid.Empty)
        {
            return null;
        }

        return new(role, text.Length > WorkInboxRulesV1.MaxReferenceLabelLength
            ? text[..WorkInboxRulesV1.MaxReferenceLabelLength].TrimEnd()
            : text, recordId, null);
    }

    public static string ProductLabel(string? code, string? name) =>
        string.Join(' ', new[] { code?.Trim(), name?.Trim() }.Where(part => !string.IsNullOrEmpty(part)));

    /// <summary>Oldest first, then id; the first <paramref name="take"/> and whether more wait.</summary>
    public static (IReadOnlyList<WorkInboxItemV1> Items, bool HasMore) Page(IEnumerable<WorkInboxItemV1> items, int take)
    {
        var ordered = items.OrderBy(item => item.WaitingSinceUtc).ThenBy(item => item.ItemId).Take(take + 1).ToArray();
        return (ordered.Take(take).ToArray(), ordered.Length > take);
    }

    /// <summary>The distinct references in order, at most the contract's bound.</summary>
    public static IReadOnlyList<WorkApprovalReferenceV1> References(IEnumerable<WorkApprovalReferenceV1?> references) =>
        [.. references
            .OfType<WorkApprovalReferenceV1>()
            .DistinctBy(reference => (reference.Role, reference.RecordId))
            .Take(WorkInboxRulesV1.MaxReferencesPerItem)];
}
