using System.Globalization;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.SalesRuntime;

public sealed partial class SalesOrderOwnerAxesHostV1
{
    private static bool TryFinancialScope(Guid companyId, string invoiceKind, SalesOrderInvoicedQuantityReadV1 owner,
        IReadOnlyDictionary<Guid, (Guid AdmissionId, string Version)> active,
        out Dictionary<Guid, (Guid AdmissionId, string Version, bool Cancelled)> invoices)
    {
        invoices = active.ToDictionary(x => x.Key, x => (x.Value.AdmissionId, x.Value.Version, false));
        var history = owner.CancelledInvoices;
        if (owner.HasOfficiallyCancelledInvoices is null
            || owner.HasOfficiallyCancelledInvoices == true && (history is null || history.Count == 0)
            || owner.HasOfficiallyCancelledInvoices == false && history is { Count: > 0 }
            || history is { Count: > MaximumShares }) return false;
        foreach (var proof in history ?? [])
        {
            if (proof.CompanyId != companyId || proof.InvoiceKind != invoiceKind || proof.InvoiceId == Guid.Empty
                || proof.AdmissionId == Guid.Empty || proof.OriginalFinalizedVersion < 1
                || proof.CancellationInvoiceVersion <= proof.OriginalFinalizedVersion
                || proof.FinalizedEventId == Guid.Empty || proof.CancellationId == Guid.Empty || proof.CancellationEventId == Guid.Empty
                || !Digest(proof.FinalizedPayloadSha256) || !Digest(proof.CancellationPayloadSha256)
                || proof.CancelledAtUtc == default || proof.CancelledAtUtc.Offset != TimeSpan.Zero
                || !invoices.TryAdd(proof.InvoiceId, (proof.AdmissionId, proof.OriginalFinalizedVersion.ToString(CultureInfo.InvariantCulture), true))) return false;
        }
        return invoices.Count <= MaximumShares;
    }

    private static bool ValidSettlement(FinanceSalesInvoiceSettlementV1 row,
        IReadOnlyDictionary<Guid, (Guid AdmissionId, string Version, bool Cancelled)> invoices) =>
        invoices.TryGetValue(row.InvoiceId, out var proof) && row.ScopeId == proof.AdmissionId && row.SourceVersion == proof.Version
        && (proof.Cancelled ? row.Status == FinanceSalesInvoiceSettlementStatusV1.OfficiallyReversed
            : row.Status is FinanceSalesInvoiceSettlementStatusV1.Open or FinanceSalesInvoiceSettlementStatusV1.Settled);

    private static bool Digest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
}
