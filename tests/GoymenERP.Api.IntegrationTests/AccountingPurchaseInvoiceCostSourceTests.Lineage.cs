using GoymenERP.Api.AccountingRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingPurchaseInvoiceCostSourceTests
{
    [Fact]
    public async Task The_actual_purchase_lineage_adapter_keeps_each_receipt_and_exact_candidate_under_the_original_envelope()
    {
        var owner = FirstSource(.01m); var envelope = Envelope(owner);
        var source = AccountingPurchaseInvoiceCostSourceV1.Read(envelope, owner.CompanyId);
        var company = new MutableCompany(owner.CompanyId);
        var reader = new PurchaseFirstCostLineageReaderV1(company); var calls = 0;
        var lineage = await reader.ReadAsync(envelope, source, source.Components[0], _ => { calls++; return Task.CompletedTask; });
        Assert.True(calls >= 2); Assert.Equal(owner.CostDifferenceEventId, lineage.SourceEventId);
        Assert.Equal(owner.InvoiceId, lineage.InvoiceId); Assert.Equal(2, lineage.Receipts.Count);
        var candidates = PurchaseFirstCostCandidatesV1.Build(lineage);
        Assert.Equal(2, candidates.Count); Assert.All(candidates, x => Assert.Equal(new("1", "200"), x.ExactTlAmount));
        Assert.Equal(owner.InitialRecognitions![0].Receipts.Select(x => x.AcceptanceId).Order(), candidates.Select(x => x.AcceptanceId).Order());
        await Assert.ThrowsAsync<AccountingStoreException>(() => reader.ReadAsync(envelope, source, source.Components[0] with {
            SourceProofSha256 = new string('F', 64) }, _ => Task.CompletedTask));
        await Assert.ThrowsAsync<AccountingStoreException>(() => reader.ReadAsync(envelope, source, source.Components[0],
            _ => { company.Set(Guid.NewGuid()); return Task.CompletedTask; }));
    }

    // Failure injection at the fresh fence; the real CompanyContext rejects request-time changes.
    private sealed class MutableCompany(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid value) => CompanyId = value;
    }
}
