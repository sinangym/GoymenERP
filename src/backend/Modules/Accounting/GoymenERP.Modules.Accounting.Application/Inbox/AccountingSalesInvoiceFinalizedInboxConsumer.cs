using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application.Contracts;

namespace GoymenERP.Modules.Accounting.Application.Inbox;

/// <summary>
/// FIN-S13 (B-04; KLN-D6..D19): the Accounting inbox consumer of <c>sales.invoice.finalized.v1</c>. It turns one finalised sales
/// invoice version into ONE admission offer to Accounting's own B-04b port, which writes one draft voucher (or records why not)
/// in Accounting's tables. Delivery is at-least-once: the idempotency key is
/// <see cref="AccountingSourceDocumentKindsV1.SalesInvoiceKey"/> of the invoice and its finalised version, so a second delivery
/// replays the first answer and never creates a second voucher.
/// <list type="bullet">
/// <item>The company is the envelope's, the dispatcher's ambient scope and the payload's at once; any disagreement is poison.</item>
/// <item>Nothing is recomputed (KLN-D14, SAT-D223): the producer's frozen amounts are passed as exact text; the voucher lines use the
/// TL amounts at the invoice's own fixed rate (KLN-D19, SAT-D226). A TL invoice has no rate and its amounts are the TL amounts.</item>
/// <item>The voucher date is the official invoice date (KLN-D8) and the source actor is the finalising user (KLN-D10/D17): both are
/// read from the event only. An event without them is poison; neither is guessed (<c>businessDate</c> is not the invoice date).</item>
/// <item>The export selector (KLN-D18) is not derived: <c>buyerSituation</c> "ihraç kayıtlı" is not an export sale, and the event has
/// no scenario field, so <see cref="AccountingSourceDocumentLineV1.IsExport"/> stays null.</item>
/// <item>The VAT-rate group is the line's VAT percent as exact text (the producer's VAT group key, SAT-D223).</item>
/// <item>"Unavailable" and a waiting mapping ("eşleme bekliyor") are retried (<see cref="AccountingInvoiceJournalRetryException"/>);
/// a content refusal or an identity conflict is final for the key: <see cref="OutboxPermanentFailureException"/>.</item>
/// </list>
/// Not registered with the dispatcher yet (see <see cref="AccountingSalesInvoiceFinalizedInboxV1"/>).
/// </summary>
public sealed class AccountingSalesInvoiceFinalizedInboxConsumer(
    IAccountingSourceDocumentAdmissionPortV1 admission,
    ICompanyContext companyContext)
    : IWorkInboxConsumerV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task HandleAsync(WorkInboxEventV1 inboxEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inboxEvent);
        cancellationToken.ThrowIfCancellationRequested();
        var request = Read(inboxEvent, companyContext);
        var answer = await admission.AdmitAsync(request, cancellationToken);
        switch (answer.Outcome)
        {
            case AccountingSourceDocumentAdmissionOutcomeV1.Admitted:
                return;
            case AccountingSourceDocumentAdmissionOutcomeV1.Refused
                when answer.Reason == AccountingSourceDocumentAdmissionReasonsV1.SourceCancelled:
                return; // Committed cancellation wins over an out-of-order finalized delivery.
            case AccountingSourceDocumentAdmissionOutcomeV1.Unavailable:
                throw new AccountingInvoiceJournalRetryException(answer.Reason,
                    "Muhasebe faturayı şu an kabul edemedi; aynı anahtarla yeniden denenecek.");
            case AccountingSourceDocumentAdmissionOutcomeV1.Refused
                when answer.Retryable || answer.Reason == AccountingSourceDocumentAdmissionReasonsV1.AuthorityDenied:
                throw new AccountingInvoiceJournalRetryException(answer.Reason,
                    "Muhasebe faturayı yapılandırma bekleyerek reddetti (eşleme ya da yetki bekliyor); aynı anahtarla yeniden denenecek.");
            case AccountingSourceDocumentAdmissionOutcomeV1.Refused:
                throw Poison(PoisonCode(answer.Reason), "Muhasebe bu fatura sürümünü kesin olarak reddetti; aynı anahtar yeniden denenmez.");
            default:
                throw new InvalidOperationException("Muhasebe fatura kabulünün bilinmeyen sonucu.");
        }
    }

    /// <summary>
    /// Validates the envelope and decodes the payload into the admission offer. Every refusal is permanent: the same event never
    /// becomes readable by retrying it.
    /// </summary>
    public static AccountingSourceDocumentAdmissionRequestV1 Read(WorkInboxEventV1 inboxEvent, ICompanyContext companyContext)
    {
        ArgumentNullException.ThrowIfNull(inboxEvent);
        ArgumentNullException.ThrowIfNull(companyContext);
        if (!string.Equals(inboxEvent.SourceName, AccountingSalesInvoiceFinalizedInboxV1.SourceName, StringComparison.Ordinal)
            || !string.Equals(inboxEvent.EventType, AccountingSalesInvoiceFinalizedInboxV1.EventType, StringComparison.Ordinal)
            || inboxEvent.Version != AccountingSalesInvoiceFinalizedInboxV1.Version)
        {
            throw Poison("unsupported_event", "Muhasebe bu kaynağın bu olay türünü ya da sürümünü dinlemiyor.");
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || inboxEvent.CompanyId != companyContext.CompanyId)
        {
            throw Poison("company_context_mismatch", "Olayın şirketi etkin şirket bağlamıyla aynı değil.");
        }

        if (inboxEvent.EventId == Guid.Empty || string.IsNullOrWhiteSpace(inboxEvent.CorrelationId))
        {
            throw Poison("invalid_envelope", "Olay zarfı kimlik ve correlation taşımalıdır.");
        }

        AccountingSalesInvoiceFinalizedEventV1? payload;
        try
        {
            payload = JsonSerializer.Deserialize<AccountingSalesInvoiceFinalizedEventV1>(inboxEvent.PayloadJson, Json);
        }
        catch (JsonException)
        {
            throw Poison("invalid_payload", "Olay içeriği okunamadı.");
        }
        catch (NotSupportedException)
        {
            throw Poison("invalid_payload", "Olay içeriği okunamadı.");
        }

        if (payload is null || payload.CompanyId != inboxEvent.CompanyId || payload.InvoiceId == Guid.Empty
            || payload.AdmissionId == Guid.Empty || payload.Version < 1 || payload.PartyId == Guid.Empty
            || payload.CurrencyId == Guid.Empty)
        {
            throw Poison("invalid_payload", "Olay içeriği şirket, fatura, kabul, cari ve para birimi kimliklerini ve sürümü eksiksiz taşımalıdır.");
        }

        if (payload.InvoiceDate is not { } invoiceDate || invoiceDate == default)
        {
            throw Poison("invoice_date_missing", "Olay resmî fatura tarihini taşımıyor; fiş dönemi tahmin edilmez (KLN-D8).");
        }

        if (string.IsNullOrWhiteSpace(payload.FinalizedBy)
            || !Guid.TryParse(payload.FinalizedBy.Trim(), out var sourceActorId) || sourceActorId == Guid.Empty)
        {
            throw Poison("finalized_by_missing", "Olay faturayı kesinleştiren kullanıcıyı taşımıyor; kaynak aktör tahmin edilmez (KLN-D10).");
        }

        var document = Totals(payload.Net, payload.Vat, payload.Withholding, payload.Payable);
        AccountingSourceDocumentTotalsV1? functional;
        if (payload.Rate is null)
        {
            // A TL invoice: its invoice-currency amounts are the TL amounts and its functional fields are null.
            functional = payload.FunctionalNet is null && payload.FunctionalVat is null && payload.FunctionalWithholding is null
                && payload.FunctionalPayable is null
                    ? document
                    : null;
        }
        else
        {
            functional = Totals(payload.FunctionalNet, payload.FunctionalVat, payload.FunctionalWithholding, payload.FunctionalPayable);
        }

        if (document is null || functional is null || payload.Lines is null || payload.Lines.Count == 0)
        {
            throw Poison("invalid_payload", "Olay içeriği fatura ve TL toplamlarını ve satırlarını eksiksiz taşımalıdır.");
        }

        var lines = new List<AccountingSourceDocumentLineV1>(payload.Lines.Count);
        foreach (var line in payload.Lines)
        {
            var mapped = line is null ? null : Line(line, payload.Rate is not null);
            if (mapped is null)
            {
                throw Poison("invalid_payload", "Olay satırı satır numarasını, KDV oranını ve tutarlarını eksiksiz taşımalıdır.");
            }

            lines.Add(mapped);
        }

        return new AccountingSourceDocumentAdmissionRequestV1(
            inboxEvent.CompanyId,
            AccountingSalesInvoiceFinalizedInboxV1.ActorId,
            inboxEvent.CorrelationId.Trim(),
            inboxEvent.EventId.ToString("D"),
            AccountingSourceDocumentKindsV1.SalesInvoiceKey(payload.InvoiceId, payload.Version),
            AccountingSourceDocumentKindsV1.SalesInvoice,
            payload.AdmissionId,
            payload.InvoiceId,
            payload.Version,
            sourceActorId,
            payload.PartyId,
            payload.CurrencyId,
            payload.Rate is { } rate ? Text(rate) : null,
            invoiceDate,
            DueDate: null,
            document,
            functional,
            lines,
            payload.OfficialInvoiceNo);
    }

    /// <summary>The exact decimal text of a frozen amount. Trailing zeros of the scale are dropped; the value is never rounded.</summary>
    public static string Text(decimal value)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        return text.Contains('.', StringComparison.Ordinal) ? text.TrimEnd('0').TrimEnd('.') : text;
    }

    private static AccountingSourceDocumentTotalsV1? Totals(decimal? net, decimal? vat, decimal? withholding, decimal? payable) =>
        net is { } n && vat is { } v && withholding is { } w && payable is { } p
            ? new AccountingSourceDocumentTotalsV1(Text(n), Text(v), Text(w), Text(p))
            : null;

    private static AccountingSourceDocumentLineV1? Line(AccountingSalesInvoiceFinalizedLineV1 line, bool foreignCurrency)
    {
        var net = foreignCurrency ? line.FunctionalNet : line.Net;
        var vat = foreignCurrency ? line.FunctionalVat : line.Vat;
        var withholding = foreignCurrency ? line.FunctionalWithholding : line.Withholding;
        if (line.LineNo < 1 || line.VatPercent is not { } vatPercent || net is not { } n || vat is not { } v
            || withholding is not { } w)
        {
            return null;
        }

        return new AccountingSourceDocumentLineV1(
            line.LineNo,
            string.IsNullOrWhiteSpace(line.ItemType) ? null : line.ItemType.Trim(),
            LinePurpose: null,
            IsExport: null,
            Text(vatPercent),
            Text(n),
            Text(v),
            Text(w));
    }

    private static string PoisonCode(string? reason) =>
        OutboxPermanentFailureException.IsValidCode(reason) ? reason! : "accounting_admission_refused";

    private static OutboxPermanentFailureException Poison(string code, string message) => new(code, message);
}
