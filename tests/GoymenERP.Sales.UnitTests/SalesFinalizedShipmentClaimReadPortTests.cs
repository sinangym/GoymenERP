using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Reader = GoymenERP.Modules.Sales.Infrastructure.Persistence.SalesFinalizedShipmentClaimReadPortV1;

namespace GoymenERP.Sales.UnitTests;

public sealed class SalesFinalizedShipmentClaimReadPortTests
{
    [Fact]
    public void Frozen_whole_share_and_event_hash_are_returned_without_prices_and_revision_zero_is_valid()
    {
        var f = new Fixture();
        var answer = Reader.Project(f.Company, [f.Line.ShipmentId], f.Snapshot);
        var item = Assert.Single(answer.Items);
        Assert.Equal(SalesShipmentClaimReadOutcomeV1.Available, item.Outcome);
        var claim = Assert.IsType<SalesFinalizedShipmentClaimV1>(item.Claim);
        Assert.Equal(f.Source, claim.ShipmentSource);
        Assert.Equal(0, claim.ShipmentSource.PriceBasisRevision);
        Assert.Equal(f.Finalization.AdmissionId, claim.AdmissionId);
        Assert.Equal(f.Event.Id, claim.FinalizedEventId);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(f.Event.PayloadJson))), claim.FinalizedPayloadSha256);
        Assert.Equal("Finalized", claim.Lifecycle.State);
        Assert.Null(claim.Lifecycle.ReleasedAtUtc);
        var wire = JsonSerializer.Serialize(answer, Json);
        Assert.DoesNotContain("unitPrice", wire);
        Assert.DoesNotContain("vatAmount", wire);
        Assert.DoesNotContain("900.99", wire);
    }

    [Theory]
    [InlineData("event-missing")]
    [InlineData("event-duplicate")]
    [InlineData("ledger-missing")]
    [InlineData("ledger-version")]
    [InlineData("legacy")]
    [InlineData("source-product")]
    [InlineData("source-quantity")]
    [InlineData("line-duplicate")]
    [InlineData("second-active")]
    [InlineData("foreign-company")]
    [InlineData("released-final")]
    [InlineData("duplicate-json-property")]
    public void Corrupt_ambiguous_or_legacy_proof_is_unavailable_never_not_invoiced(string fault)
    {
        var f = new Fixture(); var source = f.Snapshot;
        source = fault switch
        {
            "event-missing" => source with { Events = [] },
            "event-duplicate" => source with { Events = [f.Event, f.Event with { Id = Guid.NewGuid() }] },
            "ledger-missing" => source with { Finalizations = [] },
            "ledger-version" => source with { Finalizations = [f.Finalization with { Version = 5 }] },
            "legacy" => source with { Events = [f.Event with { PayloadJson = f.Payload(null) }] },
            "source-product" => source with { Events = [f.Event with { PayloadJson = f.Payload(f.Source with { ProductId = Guid.NewGuid() }) }] },
            "source-quantity" => source with { Events = [f.Event with { PayloadJson = f.Payload(f.Source with { InvoicedQuantity = "8" }) }] },
            "line-duplicate" => source with { Lines = [f.Line, f.Line with { LineId = Guid.NewGuid(), LineNo = 2 }] },
            "second-active" => source with { Lines = [f.Line, f.Line with { InvoiceId = Guid.NewGuid(), LineId = Guid.NewGuid() }] },
            "foreign-company" => source with { Lines = [f.Line with { CompanyId = Guid.NewGuid() }] },
            "released-final" => source with { Lines = [f.Line with { ReleasedAtUtc = f.Time }] },
            "duplicate-json-property" => source with { Events = [f.Event with { PayloadJson = f.Event.PayloadJson.Insert(1, "\"version\":4,") }] },
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };
        var item = Assert.Single(Reader.Project(f.Company, [f.Line.ShipmentId], source).Items);
        Assert.Equal(SalesShipmentClaimReadOutcomeV1.Unavailable, item.Outcome);
        Assert.Null(item.Claim);
        Assert.Null(item.SourceVersion);
    }

    [Fact]
    public void Unfinalized_and_cancelled_history_remains_explicit_and_changes_the_read_version()
    {
        var f = new Fixture();
        var draft = new Reader.Snapshot([f.Line], [f.Header with { Version = 1, State = (int)SalesInvoiceState.Draft }], [], []);
        var before = Assert.Single(Reader.Project(f.Company, [f.Line.ShipmentId], draft).Items);
        Assert.Equal(SalesShipmentClaimReadOutcomeV1.NotInvoiced, before.Outcome);
        Assert.Equal("Draft", Assert.Single(before.Lifecycles).State);
        var cancelled = draft with { Lines = [f.Line with { ReleasedAtUtc = f.Time }], Headers = [f.Header with {
            Version = 2, State = (int)SalesInvoiceState.Cancelled, CancelledBy = "SENTETIK-cancel", CancelledAtUtc = f.Time }] };
        var after = Assert.Single(Reader.Project(f.Company, [f.Line.ShipmentId], cancelled).Items);
        Assert.Equal(SalesShipmentClaimReadOutcomeV1.NotInvoiced, after.Outcome);
        Assert.Equal(f.Time, Assert.Single(after.Lifecycles).ReleasedAtUtc);
        Assert.NotEqual(before.SourceVersion, after.SourceVersion);
        var unknown = Assert.Single(Reader.Project(f.Company, [Guid.NewGuid()], cancelled).Items);
        Assert.Equal(SalesShipmentClaimReadOutcomeV1.NotInvoiced, unknown.Outcome);
        Assert.Empty(unknown.Lifecycles);
    }

    [Fact]
    public async Task Invalid_batch_or_missing_consumer_fence_is_refused_without_a_database_provider()
    {
        var company = new MutableCompany();
        await using var db = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>().Options, company);
        var reader = new Reader(db, company);
        var id = Guid.NewGuid();
        foreach (var ids in new IReadOnlyCollection<Guid>[] { [Guid.Empty], [id, id], Enumerable.Range(0, 129).Select(_ => Guid.NewGuid()).ToArray() })
        {
            var result = await reader.ReadAsync(company.CompanyId, ids, _ => throw new InvalidOperationException("No fence for invalid input."));
            Assert.Equal(SalesShipmentClaimReadOutcomeV1.Unavailable, result.Outcome);
            Assert.Equal("shipment_request_invalid", result.Reason);
            Assert.Empty(result.Items);
        }
        Assert.Equal("consumer_context_unavailable", (await reader.ReadAsync(company.CompanyId, [], null!)).Reason);
        Assert.Equal("consumer_context_unavailable", (await reader.ReadAsync(Guid.NewGuid(), [], _ => Task.CompletedTask)).Reason);
    }

    [Fact]
    public async Task Empty_batch_still_measures_real_consumer_fence_and_company_on_return()
    {
        var company = new MutableCompany(); var id = company.CompanyId;
        await using var db = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>().Options, company);
        var reader = new Reader(db, company); var calls = 0;
        var result = await reader.ReadAsync(id, [], _ => { calls++; return Task.CompletedTask; });
        Assert.Equal(SalesShipmentClaimReadOutcomeV1.Available, result.Outcome);
        Assert.Empty(result.Items); Assert.Equal(2, calls);
        calls = 0;
        var denied = await reader.ReadAsync(id, [], _ => { if (++calls == 2) throw new UnauthorizedAccessException(); return Task.CompletedTask; });
        Assert.Equal(SalesShipmentClaimReadOutcomeV1.Unavailable, denied.Outcome); Assert.Empty(denied.Items);
        var changed = await reader.ReadAsync(id, [], _ => { company.Set(Guid.NewGuid()); return Task.CompletedTask; });
        Assert.Equal(SalesShipmentClaimReadOutcomeV1.Unavailable, changed.Outcome); Assert.Empty(changed.Items);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed class Fixture
    {
        public Guid Company { get; } = Guid.NewGuid();
        public DateTimeOffset Time { get; } = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
        public Reader.Line Line { get; }
        public Reader.Header Header { get; }
        public Reader.Finalization Finalization { get; }
        public Reader.Event Event { get; }
        public Reader.Snapshot Snapshot => new([Line], [Header], [Finalization], [Event]);
        public SalesInvoiceShipmentSourceV1 Source => new(Line.ShipmentId, Line.DispatchNoteId, Line.NoteLineNo,
            Line.SalesOrderId, Line.SalesOrderLineId, Line.PriceBasisRevision, Line.ProductId, Line.UnitId, Line.QuantityText);

        public Fixture()
        {
            Line = new(Company, Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), 1,
                Guid.NewGuid(), Guid.NewGuid(), 0, Guid.NewGuid(), Guid.NewGuid(), "5", null);
            Header = new(Company, Line.InvoiceId, 4, (int)SalesInvoiceState.Finalized, null, null, null);
            Finalization = new(Company, Line.InvoiceId, Guid.NewGuid(), 4, "SENTETIK-finalizer", Time, "SENTETIK-correlation");
            Event = new(Company, Guid.NewGuid(), Line.InvoiceId, "Sales.Invoice", 4, 1, 1, Payload(Source), Time, Finalization.CorrelationId);
        }
        public string Payload(SalesInvoiceShipmentSourceV1? source) => JsonSerializer.Serialize(new {
            companyId = Company, invoiceId = Line.InvoiceId, version = 4, admissionId = Finalization.AdmissionId,
            finalizedBy = Finalization.FinalizedBy, net = 900.99m,
            lines = new[] { new { lineId = Line.LineId, lineNo = Line.LineNo, productId = Line.ProductId,
                unitId = Line.UnitId, quantity = 5m, shipmentSource = source, unitPrice = 180.198m, vatAmount = 180.198m } } }, Json);
    }
    private sealed class MutableCompany : ICompanyContext
    {
        public Guid CompanyId { get; private set; } = Guid.NewGuid();
        public bool IsResolved => CompanyId != Guid.Empty;
        public void Set(Guid id) => CompanyId = id;
    }
}
