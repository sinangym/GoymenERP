using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>
/// WP15 (1.16): duplicate candidates for <see cref="PurchaseInvoiceDuplicateRule"/>. The rule decides; this read only narrows:
/// the ETTN (unique index), the supplier's official numbers (identity columns only, normalized in memory because letter case,
/// spaces and dashes are ignored) and the supplier's invoices on the invoice date (index <c>IX_IncomingInvoices_DuplicateDate</c>).
/// A record written before the duplicate facts were stored takes its facts from its first revision (the recorded source and
/// amounts never change after recording). Company query filter applies; nothing is written.
/// </summary>
public sealed partial class PurchasingStore : IPurchasingInvoiceDuplicateCandidateSourceV1
{
    public async Task<IReadOnlyList<PurchaseInvoiceDuplicateCandidate>> ListDuplicateCandidatesAsync(PurchaseInvoiceDuplicateProbe probe,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(probe);
        var company = RequireResolvedCompany();
        var number = PurchaseInvoiceDuplicateRule.NormalizeNumber(probe.OfficialNumber);
        var byEttn = probe.Ettn == Guid.Empty
            ? []
            : await dbContext.IncomingInvoices.AsNoTracking().Where(row => row.Ettn == probe.Ettn).ToListAsync(cancellationToken);
        var supplierNumbers = probe.SupplierPartyId == Guid.Empty || number.Length == 0
            ? []
            : await dbContext.IncomingInvoices.AsNoTracking().Where(row => row.SupplierPartyId == probe.SupplierPartyId)
                .Select(row => new { row.InvoiceId, row.OfficialNumber }).ToListAsync(cancellationToken);
        var numberIds = supplierNumbers.Where(row => PurchaseInvoiceDuplicateRule.NormalizeNumber(row.OfficialNumber) == number)
            .Select(row => row.InvoiceId).ToArray();
        var probeDate = probe.InvoiceDate;
        var byDateOrNumber = probe.SupplierPartyId == Guid.Empty
            ? []
            : await dbContext.IncomingInvoices.AsNoTracking()
                .Where(row => row.SupplierPartyId == probe.SupplierPartyId
                    && (row.InvoiceDate == probeDate || row.InvoiceDate == null || numberIds.Contains(row.InvoiceId)))
                .ToListAsync(cancellationToken);
        var rows = byEttn.Concat(byDateOrNumber).DistinctBy(row => row.InvoiceId).OrderBy(row => row.InvoiceId).ToArray();
        if (rows.Any(row => row.CompanyId != company)) throw new InvalidOperationException("Fatura kaydı şirket kapsamıyla uyuşmuyor.");

        var legacyIds = rows.Where(row => row.InvoiceDate is null).Select(row => row.InvoiceId).ToArray();
        var legacy = legacyIds.Length == 0
            ? new Dictionary<Guid, PurchaseIncomingInvoiceSnapshot>()
            : (await dbContext.IncomingInvoiceRevisions.AsNoTracking()
                    .Where(row => legacyIds.Contains(row.InvoiceId) && row.Version == 1).ToListAsync(cancellationToken))
                .Select(row => (row, snapshot: ReadInvoiceSnapshot(row.SnapshotJson)))
                .Select(item => item.snapshot.CompanyId == company && item.snapshot.Id == item.row.InvoiceId && item.snapshot.Version == 1
                    ? item.snapshot
                    : throw new InvalidOperationException("Fatura geçmişi şirket ve sürüm kaydıyla uyuşmuyor."))
                .ToDictionary(snapshot => snapshot.Id);

        return rows.Select(row =>
        {
            if (row.InvoiceDate is { } date)
                return new PurchaseInvoiceDuplicateCandidate(row.InvoiceId, row.SupplierPartyId, row.OfficialNumber, row.Ettn, date,
                    row.CurrencyId, row.PayableAmount);
            if (!legacy.TryGetValue(row.InvoiceId, out var snapshot))
                throw new InvalidOperationException("Fatura ilk sürümü bulunamadı.");
            return new PurchaseInvoiceDuplicateCandidate(row.InvoiceId, row.SupplierPartyId, row.OfficialNumber, row.Ettn,
                snapshot.Source.InvoiceDate, snapshot.CurrencyId, snapshot.Amounts.Payable);
        }).ToArray();
    }
}
