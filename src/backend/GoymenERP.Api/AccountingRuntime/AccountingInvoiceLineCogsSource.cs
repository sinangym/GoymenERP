using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Api.AccountingRuntime;

/// <summary>Narrow actual owner reads for the already-authorized Accounting line workflow; no Sales price-read grant.</summary>
public sealed class AccountingInvoiceLineCogsSource(ISalesInvoiceStore invoices, ICompanyContext company, ICurrentUser user)
    : IAccountingFinalizedInvoiceLineSourceV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<FinalizedInvoiceCogsSelectionReadV1> FindAsync(Guid companyId, string officialInvoiceNumber,
        CancellationToken cancellationToken = default)
    {
        if (!Current(companyId)) return new(FinalizedInvoiceLineSourceOutcomeV1.Unavailable, companyId, null);
        try
        {
            // The existing dual-key owner requires two nonempty lookup texts. This is the same actual input, not a
            // fabricated ETTN; after the read we admit only its official-number match and never an unrelated ETTN hit.
            var id = await invoices.FindMatchedInvoiceIdAsync(officialInvoiceNumber, officialInvoiceNumber, cancellationToken);
            if (!Current(companyId)) return new(FinalizedInvoiceLineSourceOutcomeV1.Unavailable, companyId, null);
            if (id is null) return new(FinalizedInvoiceLineSourceOutcomeV1.NotFound, companyId, null);
            var invoice = await invoices.FindInvoiceAsync(id.Value, cancellationToken);
            if (!Current(companyId)) return new(FinalizedInvoiceLineSourceOutcomeV1.Unavailable, companyId, null);
            if (invoice is null || invoice.CompanyId != companyId || invoice.State != SalesInvoiceState.Finalized
                || invoice.Match is null || !string.Equals(invoice.Match.OfficialNo, officialInvoiceNumber, StringComparison.Ordinal))
                return new(FinalizedInvoiceLineSourceOutcomeV1.NotFound, companyId, null);
            var lines = new List<FinalizedInvoiceLineReferenceV1>();
            foreach (var line in invoice.Lines.OrderBy(x => x.LineNo))
            {
                var facts = Build(invoice, line);
                if (facts is null)
                    return new(FinalizedInvoiceLineSourceOutcomeV1.Unavailable, companyId, null);
                lines.Add(FinalizedInvoiceLineReferenceV1.From(facts));
            }
            if (lines.Count == 0 || !Current(companyId)) return new(FinalizedInvoiceLineSourceOutcomeV1.Unavailable, companyId, null);
            return new(FinalizedInvoiceLineSourceOutcomeV1.Available, companyId,
                new(companyId, invoice.Id, invoice.Match.OfficialNo, lines[0].Ettn, lines));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return new(FinalizedInvoiceLineSourceOutcomeV1.Unavailable, companyId, null); }
    }
    public async Task<FinalizedInvoiceLineSourceReadV1> GetAsync(Guid companyId, Guid invoiceId, Guid invoiceLineId,
        CancellationToken cancellationToken = default)
    {
        FinalizedInvoiceLineSourceReadV1 Reply(FinalizedInvoiceLineSourceOutcomeV1 outcome,
            FinalizedInvoiceLineSourceV1? source = null) => new(outcome, companyId, invoiceId, invoiceLineId, source);
        if (!Current(companyId)) return Reply(FinalizedInvoiceLineSourceOutcomeV1.Unavailable);
        try
        {
            var invoice = await invoices.FindInvoiceAsync(invoiceId, cancellationToken);
            if (!Current(companyId)) return Reply(FinalizedInvoiceLineSourceOutcomeV1.Unavailable);
            if (invoice is null || invoice.CompanyId != companyId || invoice.State != SalesInvoiceState.Finalized)
                return Reply(FinalizedInvoiceLineSourceOutcomeV1.NotFound);
            var line = invoice.Lines.SingleOrDefault(x => x.Id == invoiceLineId);
            if (line is null) return Reply(FinalizedInvoiceLineSourceOutcomeV1.NotFound);
            var facts = Build(invoice, line);
            return facts is null ? Reply(FinalizedInvoiceLineSourceOutcomeV1.Unavailable)
                : Reply(FinalizedInvoiceLineSourceOutcomeV1.Available, facts);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Reply(FinalizedInvoiceLineSourceOutcomeV1.Unavailable); }
    }
    private static FinalizedInvoiceLineSourceV1? Build(SalesInvoice invoice, SalesInvoiceLine line)
    {
        if (invoice.Finalization is not { } finalized || finalized.FinalizedVersion < 1
            || finalized.AdmissionId == Guid.Empty || invoice.Match is not { } match
            || !match.HasCompleteSourceProof(invoice.CompanyId) || !Guid.TryParseExact(match.Ettn, "D", out var ettn) || ettn == Guid.Empty) return null;
        var proof = JsonSerializer.Serialize(new { invoiceId = invoice.Id, invoice.CompanyId,
            finalized.AdmissionId, finalized.FinalizedVersion, match.OfficialNo, match.Ettn, match.InvoiceDate,
            invoice.Header, line.Id, line.LineNo, line.Share, line.NetUnitPrice, line.VatPercent,
            line.WithholdingShare, line.Amounts, line.Functional }, Json);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(proof)));
        return new(invoice.CompanyId, invoice.Id, line.Id, finalized.AdmissionId, finalized.FinalizedVersion,
            line.Share.ProductId, line.Share.ShipmentId, line.Share.DispatchNoteId, line.Share.NoteLineNo, line.LineNo,
            match.InvoiceDate!.Value, match.OfficialNo, ettn, proof, fingerprint);
    }
    private bool Current(Guid id) => id != Guid.Empty && company.IsResolved && company.CompanyId == id
        && user.IsAuthenticated && user.CanAccessCompany(id);
}

public sealed class AccountingInvoiceLineProductReference(IProductCogsClassificationReadPortV1 products,
    ICompanyContext company, ICurrentUser user) : IAccountingInvoiceLineProductReferenceV1
{
    public async Task<InvoiceLineCogsProductReferenceV1> GetAsync(Guid companyId, Guid productId,
        CancellationToken cancellationToken = default)
    {
        InvoiceLineCogsProductReferenceV1 Unavailable() => new(companyId, productId, "Unavailable", null, null, null);
        if (!Current(companyId)) return Unavailable();
        try
        {
            var result = await products.GetAsync(productId, cancellationToken);
            if (!Current(companyId) || result.CompanyId != companyId || result.ProductId != productId) return Unavailable();
            if (result.Outcome != ProductCogsClassificationReadOutcomeV1.Available)
                return new(companyId, productId, result.Outcome.ToString(), null, null, null);
            if (result.Snapshot is not { } snapshot || snapshot.ProductId != productId || snapshot.Classification is not { } classification)
                return Unavailable();
            return new(companyId, productId, "Available", (InvoiceLineCogsClassificationV1)(int)classification,
                snapshot.ProductVersion, snapshot.ClassificationVersion);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Unavailable(); }
    }
    private bool Current(Guid id) => id != Guid.Empty && company.IsResolved && company.CompanyId == id
        && user.IsAuthenticated && user.CanAccessCompany(id);
}

public static class AccountingInvoiceLineCogsRuntimeComposition
{
    public static IServiceCollection AddAccountingInvoiceLineCogsRuntime(this IServiceCollection services)
    {
        services.AddScoped<IAccountingFinalizedInvoiceLineSourceV1, AccountingInvoiceLineCogsSource>();
        services.AddScoped<IAccountingInvoiceLineProductReferenceV1, AccountingInvoiceLineProductReference>();
        return services;
    }
}
