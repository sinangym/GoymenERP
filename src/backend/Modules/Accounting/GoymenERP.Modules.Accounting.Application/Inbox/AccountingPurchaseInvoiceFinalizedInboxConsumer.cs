using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application.Contracts;

namespace GoymenERP.Modules.Accounting.Application.Inbox;

/// <summary>
/// FIN-S13 / FNS-D105: frozen supplier invoice amounts enter Accounting once through its own admission port.
/// The explicit purchase line purpose selects an inventory or expense mapping; no purpose, currency, date or actor is guessed.
/// The envelope, payload and ambient company agree. Transient authority/mapping gaps retry the same immutable key.
/// </summary>
public sealed class AccountingPurchaseInvoiceFinalizedInboxConsumer(
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
        if (!companyContext.IsResolved || companyContext.CompanyId != request.CompanyId)
        {
            throw new AccountingInvoiceJournalRetryException("company_context_mismatch", "Muhasebe çağrısı sırasında şirket bağlamı değişti.");
        }

        switch (answer.Outcome)
        {
            case AccountingSourceDocumentAdmissionOutcomeV1.Admitted:
                return;
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
        if (!string.Equals(inboxEvent.SourceName, AccountingPurchaseInvoiceFinalizedInboxV1.SourceName, StringComparison.Ordinal)
            || !string.Equals(inboxEvent.EventType, AccountingPurchaseInvoiceFinalizedInboxV1.EventType, StringComparison.Ordinal)
            || inboxEvent.Version != AccountingPurchaseInvoiceFinalizedInboxV1.Version)
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

        AccountingPurchaseInvoiceFinalizedEventV1? payload;
        try
        {
            payload = JsonSerializer.Deserialize<AccountingPurchaseInvoiceFinalizedEventV1>(inboxEvent.PayloadJson, Json);
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
        var sourceLineIds = new HashSet<Guid>();
        foreach (var line in payload.Lines)
        {
            var mapped = line is null ? null : Line(line, payload.Rate is not null);
            if (mapped is null || !sourceLineIds.Add(line!.LineId))
            {
                throw Poison("invalid_payload", "Olay satırı satır numarasını, KDV oranını ve tutarlarını eksiksiz taşımalıdır.");
            }

            lines.Add(mapped);
        }

        return new AccountingSourceDocumentAdmissionRequestV1(
            inboxEvent.CompanyId,
            AccountingPurchaseInvoiceFinalizedInboxV1.ActorId,
            inboxEvent.CorrelationId.Trim(),
            inboxEvent.EventId.ToString("D"),
            AccountingSourceDocumentKindsV1.PurchaseInvoiceKey(payload.InvoiceId, payload.Version),
            AccountingSourceDocumentKindsV1.PurchaseInvoice,
            payload.AdmissionId,
            payload.InvoiceId,
            payload.Version,
            sourceActorId,
            payload.PartyId,
            payload.CurrencyId,
            payload.Rate is { } rate ? Text(rate) : null,
            invoiceDate,
            payload.DueDate,
            document,
            functional,
            lines);
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

    private static AccountingSourceDocumentLineV1? Line(AccountingPurchaseInvoiceFinalizedLineV1 line, bool foreignCurrency)
    {
        var net = foreignCurrency ? line.FunctionalNet : line.Net;
        var vat = foreignCurrency ? line.FunctionalVat : line.Vat;
        var withholding = foreignCurrency ? line.FunctionalWithholding : line.Withholding;
        if (line.LineId == Guid.Empty || !AccountingPurchaseLinePurposesV1.IsKnown(line.LinePurpose)
            || line.LineNo < 1 || line.VatPercent is not { } vatPercent || net is not { } n || vat is not { } v
            || withholding is not { } w || line.Net is null || line.Vat is null || line.Withholding is null
            || (!foreignCurrency && (line.FunctionalNet is not null || line.FunctionalVat is not null
                || line.FunctionalWithholding is not null)))
        {
            return null;
        }

        return new AccountingSourceDocumentLineV1(
            line.LineNo,
            string.IsNullOrWhiteSpace(line.ItemType) ? null : line.ItemType.Trim(),
            line.LinePurpose,
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
