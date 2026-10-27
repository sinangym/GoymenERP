using System.Globalization;
using GoymenERP.DocumentGeneration.Pdf;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.Extensions.Localization;

namespace GoymenERP.Api.SalesRuntime;

/// <summary>
/// MOD-07: lays the sales order out for the PDF from the two Sales reads it is given — the enriched detail (the party
/// and people as their owners show them) and the print pricing (the price read's decision). It prints stored values
/// verbatim and computes nothing: no line amount, total, tax or exchange exists yet (API-03, T28), and the totals
/// section says so. Prices appear ONLY when the pricing answer is <see cref="SalesPriceDisclosure.Visible"/> AND every
/// detail line says the same; otherwise the price columns are not drawn at all (never as empty cells). Every user text
/// comes from <c>Resources/SalesRuntime.SalesOrderPdfComposer.resx</c>; a missing text is a server fault, never a key
/// printed in its place. Not a corporate template (DEC-DOC-010): no logo, letterhead or company name.
/// </summary>
public sealed class SalesOrderPdfComposer(IStringLocalizer<SalesOrderPdfComposer> texts, TimeZoneInfo? businessTimeZone)
{
    private const string RevisionPrefix = "Rev.";
    // DEC-DOC-004 file-name component; the PDF itself prints the full legal name.
    private const int MaximumFileNamePartyTextElements = 40;
    private const string DateFormat = "dd.MM.yyyy";
    private const string InstantFormat = "dd.MM.yyyy HH:mm";
    // SAT-D10: the business time zone. Instants are stored in UTC and printed in Turkish local time, labelled as such.
    private const string BusinessTimeZoneId = "Europe/Istanbul";
    private static readonly Lazy<TimeZoneInfo?> SystemZone = new(FindBusinessTimeZone);

    /// <summary>The host's rules for the SAT-D10 zone, or null when the host has none (then nothing with a time is printed).</summary>
    public static TimeZoneInfo? SystemBusinessTimeZone => SystemZone.Value;

    // Column weights of the line table: layout proportions only.
    private const double NarrowColumn = 0.7;
    private const double CodeColumn = 1.6;
    private const double NameColumn = 3.2;
    private const double NumberColumn = 1.4;
    private const double UnitColumn = 0.9;
    private const double DateColumn = 1.9;
    private const double PriceColumn = 1.9;
    private const double NoteColumn = 2.4;

    /// <summary>The document request and whether its prices are printed (the price-less variant otherwise).</summary>
    public (DocumentPdfRequest Request, bool PricesVisible) Compose(
        Guid technicalId, SalesOrderDraftReadDetailViewV1 detail, SalesOrderPrintPricingV1 pricing)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(pricing);
        var draft = detail.Draft;
        if (!string.Equals(pricing.Version, draft.Version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The print pricing does not belong to the detail's version.");
        }

        var pricesVisible = pricing.PriceDisclosure == SalesPriceDisclosure.Visible
            && detail.Lines.All(line => line.PriceDisclosure == SalesPriceDisclosure.Visible);
        var linePricing = pricesVisible ? PricingByLine(detail, pricing) : null;
        var currency = pricesVisible ? pricing.CurrencyCode! : null;

        var title = Format(draft.CommercialStage == SalesCommercialStageV1.Quote ? "TitleQuote" : "TitleOrder", draft.DocumentNo);
        var blocks = new List<DocumentPdfBlock>
        {
            new DocumentPdfHeading(T("HeadingOrder")),
            new DocumentPdfFields(OrderFields(detail, currency)),
            new DocumentPdfHeading(T("HeadingApproval")),
            new DocumentPdfFields(ApprovalFields(detail)),
            new DocumentPdfHeading(T("HeadingLines")),
        };
        if (detail.Lines.Count == 0)
        {
            blocks.Add(new DocumentPdfParagraph(T("NoLines")));
        }
        else
        {
            blocks.Add(LineTable(detail, linePricing, currency));
        }

        blocks.Add(new DocumentPdfHeading(T("HeadingTotals")));
        blocks.Add(new DocumentPdfParagraph(pricesVisible ? T("TotalsNotAvailable") : T("PricesNotShown")));

        var request = new DocumentPdfRequest(
            technicalId,
            new PdfDocumentFileNameParts(
                pricesVisible ? T("DocumentType") : T("DocumentTypeWithoutPrices"),
                draft.DocumentNo,
                FileNameParty(detail.PartyDisplay),
                draft.BusinessDate,
                FileNameVersion(draft.Revision),
                StatusText(draft)),
            title,
            blocks,
            Format("Footer", draft.DocumentNo, draft.Revision),
            T("PageLabel"),
            IsApprovedRevision(draft) ? null : T("DraftWatermark"));
        return (request, pricesVisible);
    }

    private static Dictionary<Guid, SalesDraftLinePricingV1> PricingByLine(
        SalesOrderDraftReadDetailViewV1 detail, SalesOrderPrintPricingV1 pricing)
    {
        if (string.IsNullOrWhiteSpace(pricing.CurrencyCode) || pricing.Lines is null
            || pricing.Lines.Count != detail.Lines.Count)
        {
            throw new InvalidOperationException("The visible print pricing is incomplete.");
        }

        var byLine = new Dictionary<Guid, SalesDraftLinePricingV1>();
        foreach (var line in pricing.Lines)
        {
            if (line is null || !byLine.TryAdd(line.LineId, line))
            {
                throw new InvalidOperationException("The visible print pricing repeats or omits a line.");
            }
        }

        if (detail.Lines.Any(line => !byLine.TryGetValue(line.LineId, out var priced) || priced.LineNo != line.LineNo))
        {
            throw new InvalidOperationException("The visible print pricing does not answer the detail's lines.");
        }

        return byLine;
    }

    private List<DocumentPdfField> OrderFields(SalesOrderDraftReadDetailViewV1 detail, string? currency)
    {
        var draft = detail.Draft;
        var fields = new List<DocumentPdfField>
        {
            new(T("FieldDocumentNo"), draft.DocumentNo),
            new(T("FieldRevision"), draft.Revision),
            new(T("FieldState"), StateText(draft)),
            new(T("FieldCommercialStage"), draft.CommercialStage switch
            {
                SalesCommercialStageV1.Quote => T("StageQuote"),
                SalesCommercialStageV1.Order => T("StageOrder"),
                _ => T("None"),
            }),
            new(T("FieldBusinessDate"), Date(draft.BusinessDate)),
            new(T("FieldCustomer"), CustomerText(detail.PartyDisplay)),
            new(T("FieldRequestedDeliveryDate"), detail.RequestedDeliveryDate is { } due ? Date(due) : T("NotGiven")),
        };
        if (currency is not null)
        {
            fields.Add(new(T("FieldCurrency"), currency));
        }

        fields.Add(new(T("FieldNote"), string.IsNullOrWhiteSpace(detail.Note) ? T("None") : detail.Note));
        return fields;
    }

    private List<DocumentPdfField> ApprovalFields(SalesOrderDraftReadDetailViewV1 detail)
    {
        var approval = detail.Approval;
        var acceptance = detail.CustomerAcceptance;
        return
        [
            new(T("FieldSubmittedBy"), Actor(detail, approval.SubmittedBy, approval.SubmittedAtUtc)),
            new(T("FieldDecidedBy"), Actor(detail, approval.DecidedBy, approval.DecidedAtUtc)),
            new(T("FieldCustomerAcceptance"), !acceptance.Required
                ? T("AcceptanceNotRequired")
                : acceptance.AcceptedRevision is null
                    ? T("AcceptancePending")
                    : Format("AcceptanceRecorded", acceptance.AcceptedRevision,
                        Actor(detail, acceptance.AcceptedBy, acceptance.AcceptedAtUtc))),
        ];
    }

    private DocumentPdfTable LineTable(SalesOrderDraftReadDetailViewV1 detail,
        IReadOnlyDictionary<Guid, SalesDraftLinePricingV1>? pricing, string? currency)
    {
        var columns = new List<DocumentPdfColumn>
        {
            new(T("ColumnLineNo"), NarrowColumn, DocumentPdfAlignment.End),
            new(T("ColumnProductCode"), CodeColumn),
            new(T("ColumnProductName"), NameColumn),
            new(T("ColumnQuantity"), NumberColumn, DocumentPdfAlignment.End),
            new(T("ColumnUnit"), UnitColumn),
            new(T("ColumnDelivery"), DateColumn),
        };
        if (pricing is not null)
        {
            columns.Add(new(T("ColumnUnitPrice"), PriceColumn, DocumentPdfAlignment.End));
            columns.Add(new(T("ColumnDiscount"), NumberColumn, DocumentPdfAlignment.End));
            columns.Add(new(T("ColumnSurcharge"), NumberColumn, DocumentPdfAlignment.End));
            columns.Add(new(T("ColumnNetUnitPrice"), PriceColumn, DocumentPdfAlignment.End));
        }

        columns.Add(new(T("ColumnLineNote"), NoteColumn));
        var rows = new List<IReadOnlyList<string>>(detail.Lines.Count);
        foreach (var line in detail.Lines.OrderBy(line => line.LineNo))
        {
            var unitCode = UnitCode(detail, line.OrderUnitId, line.OrderUnitCode);
            var row = new List<string>
            {
                line.LineNo.ToString(CultureInfo.InvariantCulture),
                line.ProductCode,
                line.ProductName,
                line.Quantity,
                unitCode,
                DeliveryText(line),
            };
            if (pricing is not null)
            {
                var priced = pricing[line.LineId];
                var priceUnit = priced.PriceUnitId is { } priceUnitId ? UnitCode(detail, priceUnitId, null) : unitCode;
                row.Add(priced.UnitPrice is null ? T("NotPriced") : Format("Price", priced.UnitPrice, currency!, priceUnit));
                row.Add(Adjustment(priced.DiscountPercent, priced.DiscountAmount, currency!));
                row.Add(Adjustment(priced.SurchargePercent, priced.SurchargeAmount, currency!));
                row.Add(priced.NetUnitPrice is null ? T("None") : Format("Price", priced.NetUnitPrice, currency!, priceUnit));
            }

            row.Add(string.IsNullOrWhiteSpace(line.Note) ? T("None") : line.Note);
            rows.Add(row);
        }

        return new DocumentPdfTable(columns, rows);
    }

    private string Adjustment(string? percent, string? amount, string currency) =>
        percent is not null ? Format("Percent", percent)
        : amount is not null ? Format("Amount", amount, currency)
        : T("None");

    private string DeliveryText(SalesOrderDraftLineDetail line)
    {
        if (line.RequestedDeliverySlices is { Count: > 0 } slices)
        {
            return string.Join('\n', slices.OrderBy(slice => slice.RequestedDeliveryDate)
                .Select(slice => Format("SliceDelivery", Date(slice.RequestedDeliveryDate), slice.Quantity)));
        }

        return line.RequestedDeliveryDate is { } due ? Date(due) : T("NotGiven");
    }

    /// <summary>The unit's code from the line or the record's product snapshots; the raw id only when neither names it.</summary>
    private static string UnitCode(SalesOrderDraftReadDetailViewV1 detail, Guid unitId, string? lineCode)
    {
        if (!string.IsNullOrWhiteSpace(lineCode))
        {
            return lineCode;
        }

        var fromLine = detail.Lines.FirstOrDefault(line => line.OrderUnitId == unitId && !string.IsNullOrWhiteSpace(line.OrderUnitCode));
        if (fromLine is not null)
        {
            return fromLine.OrderUnitCode!;
        }

        var snapshot = detail.Draft.ProductSnapshots?.FirstOrDefault(product => product.OrderUnitId == unitId);
        return snapshot?.OrderUnitCode ?? unitId.ToString("D");
    }

    private string Actor(SalesOrderDraftReadDetailViewV1 detail, string? actorId, DateTimeOffset? at)
    {
        if (actorId is null)
        {
            return T("None");
        }

        var name = Guid.TryParse(actorId, out var userId)
            ? detail.UserDisplays.FirstOrDefault(user => user.UserId == userId)?.DisplayName ?? actorId
            : actorId;
        return at is { } instant ? Format("ActorAt", name, Instant(instant)) : name;
    }

    private string CustomerText(SalesPartyDisplayV1 party) => string.IsNullOrWhiteSpace(party.TradeName)
        ? Format("Customer", party.Code, party.LegalName)
        : Format("CustomerWithTradeName", party.Code, party.LegalName, party.TradeName);

    private static string FileNameParty(SalesPartyDisplayV1 party)
    {
        var name = string.IsNullOrWhiteSpace(party.TradeName) ? party.LegalName : party.TradeName;
        var elements = StringInfo.GetTextElementEnumerator(name.Trim());
        var builder = new System.Text.StringBuilder();
        var count = 0;
        while (elements.MoveNext() && count < MaximumFileNamePartyTextElements)
        {
            builder.Append(elements.GetTextElement());
            count++;
        }

        return builder.ToString();
    }

    /// <summary>DEC-DOC-004 "sürüm": the n-th approved state of the order, so Rev.0 is v01 and Rev.N is v(N+1).</summary>
    private static int FileNameVersion(string revision) =>
        revision.StartsWith(RevisionPrefix, StringComparison.Ordinal)
        && int.TryParse(revision.AsSpan(RevisionPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
        && number < int.MaxValue
            ? number + 1
            : throw new InvalidOperationException("The order revision is not a revision text.");

    /// <summary>
    /// SAT-D56 (Sinan, 2026-10-27, "Evet, ama 'TASLAK' filigranıyla"): an order whose current revision is not approved
    /// (preparing, pending approval, rejected, withdrawn) is printed with the draft watermark on every page; only an
    /// approved revision prints without it. The lifecycle (cancelled/closed) does not change what was approved.
    /// </summary>
    private static bool IsApprovedRevision(SalesOrderDraftSummaryView draft) =>
        string.Equals(draft.ApprovalState, "Approved", StringComparison.Ordinal);

    private string StateText(SalesOrderDraftSummaryView draft) => Format("StateWithApproval", LifecycleText(draft.State), ApprovalText(draft.ApprovalState));

    /// <summary>The file name's status: the terminal lifecycle when there is one, otherwise the internal approval state.</summary>
    private string StatusText(SalesOrderDraftSummaryView draft) =>
        draft.State is "Cancelled" or "Closed" ? LifecycleText(draft.State) : ApprovalText(draft.ApprovalState);

    private string LifecycleText(string state) => state switch
    {
        "Draft" => T("LifecycleDraft"),
        "Cancelled" => T("LifecycleCancelled"),
        "Closed" => T("LifecycleClosed"),
        _ => throw new InvalidOperationException("The order lifecycle state has no text."),
    };

    private string ApprovalText(string state) => state switch
    {
        "Preparing" => T("ApprovalPreparing"),
        "PendingApproval" => T("ApprovalPendingApproval"),
        "Approved" => T("ApprovalApproved"),
        "Rejected" => T("ApprovalRejected"),
        "Withdrawn" => T("ApprovalWithdrawn"),
        _ => throw new InvalidOperationException("The order approval state has no text."),
    };

    private static string Date(DateOnly value) => value.ToString(DateFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// An instant in the SAT-D10 business time zone with its label. Without the zone's rules on the host the PDF is not
    /// printed (a server fault), never printed in another zone.
    /// </summary>
    private string Instant(DateTimeOffset value)
    {
        var zone = businessTimeZone
            ?? throw new InvalidOperationException("The business time zone is not available on this host.");
        return Format("InstantIstanbul",
            TimeZoneInfo.ConvertTime(value, zone).ToString(InstantFormat, CultureInfo.InvariantCulture));
    }

    private static TimeZoneInfo? FindBusinessTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(BusinessTimeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    private string Format(string key, params object[] values) =>
        string.Format(CultureInfo.InvariantCulture, T(key), values);

    private string T(string key)
    {
        var text = texts[key];
        return text.ResourceNotFound || string.IsNullOrEmpty(text.Value)
            ? throw new InvalidOperationException($"The sales order PDF text '{key}' is missing.")
            : text.Value;
    }
}
