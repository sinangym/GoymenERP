using System.Text.Json;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Reader = GoymenERP.Modules.Sales.Infrastructure.Persistence.SalesFinalizedInvoiceIdentityReadPortV1;

namespace GoymenERP.Sales.UnitTests;

public sealed class SalesFinalizedInvoiceIdentityReadPortTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Only_original_native_receipt_and_finalized_event_release_price_free_identity(bool subcontract)
    {
        var f = new Fixture(subcontract);
        var result = Reader.Project(f.Company, [new(f.Invoice, 4)], f.Snapshot);
        Assert.True(result.Available);
        var item = Assert.Single(result.Items);
        Assert.Equal(f.Admission, item.AdmissionId);
        Assert.Equal("SENTETIK-LEGAL", item.OfficialInvoiceNo);
        Assert.Equal(f.Ettn, item.Ettn);
        Assert.Equal(64, item.SourceVersion.Length);
        var wire = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("987654.32", wire);
        Assert.DoesNotContain("FunctionalAmount", wire);
        Assert.DoesNotContain("PayloadJson", wire);
    }

    [Theory]
    [InlineData(false, "version")]
    [InlineData(false, "foreign")]
    [InlineData(false, "receipt-missing")]
    [InlineData(false, "duplicate-json")]
    [InlineData(false, "event-missing")]
    [InlineData(false, "source-missing")]
    [InlineData(true, "version")]
    [InlineData(true, "foreign")]
    [InlineData(true, "sequence")]
    [InlineData(true, "receipt-missing")]
    [InlineData(true, "duplicate-json")]
    [InlineData(true, "event-missing")]
    [InlineData(true, "source-missing")]
    public void Missing_ambiguous_foreign_or_wrong_original_version_never_returns_partial_identity(bool subcontract, string fault)
    {
        var f = new Fixture(subcontract);
        var snapshot = f.Snapshot;
        switch (fault)
        {
            case "receipt-missing": snapshot = snapshot with { Finalizations = [], Attempts = [] }; break;
            case "sequence": snapshot.Attempts[0].LifecycleSequence = 7; break;
            case "event-missing": snapshot = snapshot with { Events = [] }; break;
            case "duplicate-json": snapshot.Events[0].PayloadJson = snapshot.Events[0].PayloadJson.Insert(1, "\"version\":4,"); break;
            case "source-missing": snapshot.Events[0].PayloadJson = snapshot.Events[0].PayloadJson.Replace("\"sourceProof\":", "\"unrecognizedSource\":"); break;
        }
        var result = Reader.Project(fault == "foreign" ? Guid.NewGuid() : f.Company,
            [new(f.Invoice, fault == "version" ? 5 : 4)], snapshot);
        Assert.False(result.Available); Assert.Empty(result.Items);
    }

    private sealed class Fixture
    {
        public Guid Company { get; } = Guid.NewGuid();
        public Guid Invoice { get; } = Guid.NewGuid();
        public Guid Admission { get; } = Guid.NewGuid();
        public string Ettn { get; } = Guid.NewGuid().ToString("D");
        public Reader.IdentitySnapshot Snapshot { get; }
        public Fixture(bool subcontract)
        {
            var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var time = DateTimeOffset.UnixEpoch;
            var proof = new SalesOfficialInvoiceSourceProofV1(Company, Guid.NewGuid(), "manual", "2", time,
                Guid.NewGuid(), 1, new string('a', 64), null, null, false);
            var match = new SalesInvoiceMatchFacts("SENTETIK-LEGAL", Ettn, 987654.32m, 987654.32m, 0m, 0m,
                "SENTETIK-matcher", time, null, null, null, new DateOnly(2026, 11, 27), proof);
            var final = new SalesInvoiceFinalization(Admission, 4, "SENTETIK-finalizer", time,
                SalesInvoiceFinanceStatus.Waiting, null, 0, null, null, []);
            var payload = JsonSerializer.Serialize(new { companyId = Company, invoiceId = Invoice, version = 4,
                admissionId = Admission, partyId = Guid.NewGuid(), companyAccountId = Guid.NewGuid(),
                officialInvoiceNo = match.OfficialNo, final.FinalizedBy,
                match = new { match.OfficialNo, match.Ettn, match.InvoiceDate, match.SourceProof }, amount = 987654.32m }, json);
            var envelope = new SalesOutboxRow { Id = Guid.NewGuid(), CompanyId = Company, AggregateId = Invoice,
                AggregateType = subcontract ? "Sales.SubcontractServiceInvoice" : "Sales.Invoice", AggregateVersion = 4,
                EventType = SalesStore.InvoiceFinalizedEventType, SchemaVersion = 1, Sequence = 1,
                PayloadJson = payload, OccurredAtUtc = time, CorrelationId = "SENTETIK-original" };
            var normal = new Reader.Finalization(Company, Invoice, Admission, 4, final.FinalizedBy, time, envelope.CorrelationId);
            var attempt = new SubcontractInvoiceCommandAttemptRow { CompanyId = Company, InvoiceId = Invoice,
                CommandName = SalesSubcontractInvoiceLifecycleCommandsV1.Finalize, LifecycleSequence = 5,
                ActorId = final.FinalizedBy, RecordedAtUtc = time, CorrelationId = envelope.CorrelationId,
                RequestHash = new string('b', 64), IdempotencyKeyHash = new string('c', 64) };
            var history = new SubcontractInvoiceLifecycleRow { CompanyId = Company, InvoiceId = Invoice, Sequence = 5,
                InvoiceVersion = 4, State = (int)SalesSubcontractServiceInvoiceState.Finalized,
                CorrelationId = envelope.CorrelationId, RecordedAtUtc = time,
                FactsJson = JsonSerializer.Serialize(new SalesSubcontractInvoiceLifecycleFacts(SalesInvoiceDecisionFacts.None,
                    [], null, [], match, final), json) };
            Snapshot = subcontract ? new([], [attempt], [history], [envelope]) : new([normal], [], [], [envelope]);
        }
    }
}
