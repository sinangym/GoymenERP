using System.Globalization;
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
                .Where(row => row.CompanyId == companyId && row.OfficialNo == officialNo && row.Ettn == storedEttn
                    && (row.Status == pending || row.Status == approved))
                .Take(2)
                .ToListAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return FinanceOfficialSalesInvoiceReadAnswerV1.Unavailable;
        }

        // The column collation may compare case-insensitively; the key is exact (SAT-D89), so only an exact match counts.
        var exact = rows
            .Where(row => string.Equals(row.OfficialNo, officialNo, StringComparison.Ordinal)
                && string.Equals(row.Ettn, storedEttn, StringComparison.Ordinal))
            .ToList();
        return exact switch
        {
            [] => FinanceOfficialSalesInvoiceReadAnswerV1.NotFound,
            [var row] when row.CompanyId != companyId => FinanceOfficialSalesInvoiceReadAnswerV1.Unavailable,
            [var row] when row.Status == (int)FinanceOfficialSalesInvoiceStatusV1.PendingApproval =>
                FinanceOfficialSalesInvoiceReadAnswerV1.PendingApproval,
            [var row] => Approved(row),
            _ => FinanceOfficialSalesInvoiceReadAnswerV1.Unavailable,
        };
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
            approvedAtUtc));
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
