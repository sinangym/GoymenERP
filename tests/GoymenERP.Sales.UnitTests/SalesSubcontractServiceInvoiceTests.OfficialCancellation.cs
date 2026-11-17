using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Invoices;
namespace GoymenERP.Sales.UnitTests;
public sealed partial class SalesSubcontractServiceInvoiceTests
{
    [Fact]
    public void Official_subcontract_cancellation_retains_original_weighing_match_amounts_approval_and_finalization()
    {
        var invoice=LifecycleInvoice(); ApprovedService(invoice); invoice.RecordMatch(3,"SENTETIK-matcher",Official(invoice,Day),Try,null,0.05m,false,null,Now);
        var final=invoice.Finalize(4,"SENTETIK-finalizer",Guid.NewGuid,Now); var totals=invoice.Totals; var facts=invoice.LifecycleFacts;
        var proof=new SalesOfficialInvoiceCancellationEvidenceV1(Company,invoice.Id,invoice.Match!.OfficialNo,invoice.Match.Ettn,"SENTETIK-status","record","2",new string('a',64),Now.AddMinutes(1));
        Assert.Throws<SalesRuleException>(()=>invoice.RecordOfficialCancellation(5,"SENTETIK-cancel","SENTETIK",proof with {InvoiceId=Guid.NewGuid()},Now.AddMinutes(2)));
        Assert.Equal(5,invoice.Version);
        invoice.RecordOfficialCancellation(5,"SENTETIK-cancel","SENTETIK",proof,Now.AddMinutes(2));
        Assert.Equal(SalesSubcontractServiceInvoiceState.OfficiallyCancelled,invoice.State);Assert.Equal(6,invoice.Version);
        Assert.Equal(totals,invoice.Totals);Assert.Equal(final,invoice.Finalization);Assert.Equal(facts.Match,invoice.Match);Assert.Equal(facts.Decision.DecidedBy,invoice.Decision.DecidedBy);
        var restored=SalesSubcontractServiceInvoice.Restore(invoice.Id,Company,invoice.DocumentNo,invoice.Header,invoice.Source,invoice.State,invoice.Version,invoice.CreatedBy,invoice.CreatedAtUtc,invoice.Lines,invoice.LifecycleFacts);
        Assert.Equal(final,restored.Finalization);Assert.Equal(invoice.Decision,restored.Decision);
        Assert.Throws<SalesRuleException>(()=>invoice.RecordOfficialCancellation(6,"SENTETIK-cancel","SENTETIK",proof,Now.AddMinutes(3)));
    }
}
