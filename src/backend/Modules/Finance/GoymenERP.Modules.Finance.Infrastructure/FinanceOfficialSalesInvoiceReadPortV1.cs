using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// FIN-S6 (F-005 D3): Finance's own answer to <see cref="IFinanceOfficialSalesInvoiceReadPortV1"/> over the manual official
/// sales invoice records of F-005 D1 (<see cref="FinanceOfficialSalesInvoiceServiceV1"/>), in the server-resolved company only.
/// <list type="bullet">
/// <item>The requested company must be the resolved one; anything else is Unavailable (a caller cannot read another
/// company by naming it). The query also filters on the company explicitly, beside the context's query filter.</item>
/// <item>The key is the official number and the ETTN together, compared exactly (SAT-D89). A number Finance could never
/// have stored (blank, padded, longer than the column) or an ETTN that is not a "D" GUID cannot name a record: NotFound.
/// The ETTN is compared in its stored lowercase form.</item>
/// <item>Only live records are read (pending and approved — the same filter as the unique indexes, FNS-D39): a rejected
/// or withdrawn record frees its number and ETTN and reads as NotFound. A pending record answers PendingApproval without
/// facts; only an approved record carries them (F-005 D1).</item>
/// <item>A stored approved row Finance cannot read back exactly (an amount that is not exact decimal text, a missing
/// approval time, an empty identity), two live rows on one key, or a store fault answer Unavailable — never a guess.</item>
/// </list>
/// Untracked; it creates nothing (no attempt, audit or version effect) and carries no authority.
/// </summary>
internal sealed class FinanceOfficialSalesInvoiceReadPortV1(FinanceDbContext dbContext, ICompanyContext companyContext)
    : IFinanceOfficialSalesInvoiceReadPortV1
{
    public async Task<FinanceOfficialSalesInvoiceReadAnswerV1> LookupAsync(Guid companyId, string officialNo, string ettn,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (companyId == Guid.Empty || !companyContext.IsResolved || companyContext.CompanyId != companyId)
        {
            return FinanceOfficialSalesInvoiceReadAnswerV1.Unavailable;
        }
        if (!IsStorableOfficialNo(officialNo) || NormalizedEttn(ettn) is not { } storedEttn)
        {
            return FinanceOfficialSalesInvoiceReadAnswerV1.NotFound;
        }

        List<FinanceOfficialSalesInvoiceRow> rows;
        try
        {
            var pending = (int)FinanceOfficialSalesInvoiceStatusV1.PendingApproval;
            var approved = (int)FinanceOfficialSalesInvoiceStatusV1.Approved;
            rows = await dbContext.OfficialSalesInvoices.AsNoTracking()
                .Where(row => row.CompanyId == companyId && (row.OfficialNo == officialNo || row.Ettn == storedEttn)
                    && (row.Status == pending || row.Status == approved))
                .Take(3)
                .ToListAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return FinanceOfficialSalesInvoiceReadAnswerV1.Unavailable;
        }

        // The column collation may compare case-insensitively; the key is exact (SAT-D89), so only an exact match counts.
        if (!companyContext.IsResolved || companyContext.CompanyId != companyId)
            return FinanceOfficialSalesInvoiceReadAnswerV1.Unavailable;
        var exact = rows
            .Where(row => string.Equals(row.OfficialNo, officialNo, StringComparison.Ordinal)
                || string.Equals(row.Ettn, storedEttn, StringComparison.Ordinal))
            .ToList();
        var exactPair = exact.Where(row => row.OfficialNo == officialNo && row.Ettn == storedEttn).ToArray();
        if (exactPair.Length > 1) return FinanceOfficialSalesInvoiceReadAnswerV1.Unavailable;
        var approvedRows = exact.Where(row => row.Status == (int)FinanceOfficialSalesInvoiceStatusV1.Approved).ToArray();
        if (exactPair is [var approvedRow] && approvedRow.Status == (int)FinanceOfficialSalesInvoiceStatusV1.Approved)
            return Approved(approvedRow);
        // SAT-D89 still requires the exact pair. KLN-D104 prevents an import from bypassing a manual winner
        // sharing either identity; it does not rewrite the supplied matching key.
        if (approvedRows.Length != 0) return FinanceOfficialSalesInvoiceReadAnswerV1.NotFound;
        try
        {
            // FNS-D90: a pending manual row is a note, never an import lock. Only an admitted, approved upload
            // supplies facts, with its real bytes/checksum; the uploaded file does not pretend to be a manual PDF.
            var imported = await Imported(companyId, officialNo, storedEttn, exact.Count != 0, cancellationToken);
            return imported ?? (exact.Count == 0 ? FinanceOfficialSalesInvoiceReadAnswerV1.NotFound
                : FinanceOfficialSalesInvoiceReadAnswerV1.PendingApproval);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return FinanceOfficialSalesInvoiceReadAnswerV1.Unavailable;
        }
    }

    private async Task<FinanceOfficialSalesInvoiceReadAnswerV1?> Imported(Guid companyId, string officialNo,
        string ettn, bool pendingManual, CancellationToken cancellationToken)
    {
        var claims = await dbContext.OfficialInvoiceIdentityClaims.AsNoTracking()
            .Where(row => row.CompanyId == companyId && (row.IdentityKind == FinanceOfficialInvoiceIdentityKinds.OfficialNo
                && row.IdentityValue == officialNo || row.IdentityKind == FinanceOfficialInvoiceIdentityKinds.Ettn && row.IdentityValue == ettn))
            .ToListAsync(cancellationToken);
        var holders = FinanceOfficialInvoiceIdentityClaimPolicy.CurrentHolders(claims);
        if (holders.Count == 0) return null;
        if (!holders.TryGetValue((FinanceOfficialInvoiceIdentityKinds.OfficialNo, officialNo), out var number)
            || !holders.TryGetValue((FinanceOfficialInvoiceIdentityKinds.Ettn, ettn), out var identity)
            || number.HolderBatchId != identity.HolderBatchId || number.HolderSourceDocumentId != identity.HolderSourceDocumentId)
            return FinanceOfficialSalesInvoiceReadAnswerV1.Unavailable;
        var upload = await dbContext.OfficialInvoiceImportBatches.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == number.HolderBatchId, cancellationToken);
        var approval = await dbContext.OfficialInvoiceImportApprovals.AsNoTracking()
            .SingleOrDefaultAsync(row => row.BatchId == number.HolderBatchId, cancellationToken);
        if (!companyContext.IsResolved || companyContext.CompanyId != companyId || upload is null || approval is null
            || upload.CompanyId != companyId || approval.CompanyId != companyId)
            return FinanceOfficialSalesInvoiceReadAnswerV1.Unavailable;
        var sourceRows = JsonSerializer.Deserialize<ApprovedFinanceOpenItemSourceRow[]>(upload.SourceRowsJson, FinanceJson.CreateOptions());
        if (sourceRows is null) return FinanceOfficialSalesInvoiceReadAnswerV1.Unavailable;
        var context = new FinanceContext(companyId, approval.ApprovedBy, approval.CorrelationId, approval.CausationId);
        var batch = new ApprovedFinanceOpenItemSourceBatch(upload.Id, companyId, upload.BatchVersion, upload.BatchChecksum, sourceRows);
        var documents = FinanceOfficialInvoiceIdentityClaimPolicy.ReadDocuments(context, upload, batch);
        var document = documents?.SingleOrDefault(row => row.SourceDocumentId == number.HolderSourceDocumentId
            && row.OfficialNo == officialNo && row.Ettn == ettn);
        if (document is null) return FinanceOfficialSalesInvoiceReadAnswerV1.Unavailable;
        var imported = FinanceOfficialInvoiceSourceComparison.Imported(batch, [document]).Single();
        if (imported.CustomerPartyId is not { } party || imported.CurrencyId is not { } currency
            || !IsExact(imported.PayableAmount, true) || !IsExact(imported.PayableFunctionalAmount, true))
            return FinanceOfficialSalesInvoiceReadAnswerV1.Unavailable;
        return new(FinanceOfficialSalesInvoiceReadOutcomeV1.Approved, new(document.SourceDocumentId, companyId,
            officialNo, ettn, party, currency, imported.PayableAmount!, imported.PayableFunctionalAmount!,
            document.TaxBaseFunctionalAmount, document.VatFunctionalAmount, document.WithholdingFunctionalAmount,
            null, null, null, upload.BatchVersion, approval.ApprovedAtUtc, document.InvoiceDate,
            "approved-import", upload.Id, upload.BatchChecksum, pendingManual));
    }

    private static FinanceOfficialSalesInvoiceReadAnswerV1 Approved(FinanceOfficialSalesInvoiceRow row)
    {
        if (row.ApprovedAtUtc is not { } approvedAtUtc
            || row.Id == Guid.Empty || row.CustomerPartyId == Guid.Empty || row.CurrencyId == Guid.Empty
            || row.PdfDocumentId == Guid.Empty || row.PdfDocumentVersionNo < 1 || string.IsNullOrEmpty(row.PdfSha256)
            || row.Version < 1
            || !IsExact(row.PayableAmountText, positive: true)
            || !IsExact(row.PayableFunctionalAmountText, positive: true)
            || !IsExact(row.TaxBaseFunctionalAmountText, positive: false)
            || !IsExact(row.VatFunctionalAmountText, positive: false)
            || !IsExact(row.WithholdingFunctionalAmountText, positive: false))
        {
            return FinanceOfficialSalesInvoiceReadAnswerV1.Unavailable;
        }

        return new(FinanceOfficialSalesInvoiceReadOutcomeV1.Approved, new FinanceOfficialSalesInvoiceFactsV1(
            row.Id,
            row.CompanyId,
            row.OfficialNo,
            row.Ettn,
            row.CustomerPartyId,
            row.CurrencyId,
            row.PayableAmountText,
            row.PayableFunctionalAmountText,
            row.TaxBaseFunctionalAmountText,
            row.VatFunctionalAmountText,
            row.WithholdingFunctionalAmountText,
            row.PdfDocumentId,
            row.PdfDocumentVersionNo,
            row.PdfSha256,
            row.Version.ToString(CultureInfo.InvariantCulture),
            approvedAtUtc, row.InvoiceDate));
    }

    /// <summary>
    /// The stored text is the record's validated, normalised exact decimal (DEC-FIN-014): it must parse with the same sign
    /// rule the record applied (payable amounts positive, the tax amounts non-negative) and read back as the same text.
    /// </summary>
    private static bool IsExact(string? text, bool positive)
    {
        var parsed = positive
            ? FinanceExactDecimalText.TryParsePositive(text, out var value)
            : FinanceExactDecimalText.TryParseNonNegative(text, out value);
        return parsed && string.Equals(value.Text, text, StringComparison.Ordinal);
    }

    /// <summary>The record refuses a blank, padded or over-long official number, so no such number can name a record.</summary>
    private static bool IsStorableOfficialNo(string? officialNo) =>
        !string.IsNullOrWhiteSpace(officialNo)
        && officialNo.Length <= FinanceDbContext.OfficialSalesInvoiceMaximumOfficialNoLength
        && string.Equals(officialNo, officialNo.Trim(), StringComparison.Ordinal);

    /// <summary>The record stores the ETTN as the lowercase "D" GUID text; an empty GUID is never stored.</summary>
    private static string? NormalizedEttn(string? ettn) =>
        ettn is { Length: FinanceDbContext.OfficialSalesInvoiceEttnLength }
        && Guid.TryParseExact(ettn, "D", out var parsed) && parsed != Guid.Empty
            ? parsed.ToString("D")
            : null;
}
