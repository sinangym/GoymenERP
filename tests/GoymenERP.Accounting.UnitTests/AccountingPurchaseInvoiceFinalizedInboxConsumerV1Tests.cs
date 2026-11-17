using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Application.Inbox;

namespace GoymenERP.Accounting.UnitTests;

public sealed class AccountingPurchaseInvoiceFinalizedInboxConsumerV1Tests
{
    private static readonly Guid Company = Guid.Parse("b0430000-0000-4000-8000-000000000001");
    private static readonly Guid Invoice = Guid.Parse("b0430000-0000-4000-8000-000000000002");
    private static readonly Guid Actor = Guid.Parse("b0430000-0000-4000-8000-000000000003");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Purchase_source_preserves_official_date_actor_purpose_and_frozen_foreign_amounts()
    {
        var payload = Payload() with
        {
            Rate = 40m, FunctionalNet = 4000m, FunctionalVat = 800m, FunctionalWithholding = 400m, FunctionalPayable = 4400m,
            Lines = [Payload().Lines![0]! with { FunctionalNet = 4000m, FunctionalVat = 800m, FunctionalWithholding = 400m }],
        };
        var request = AccountingPurchaseInvoiceFinalizedInboxConsumer.Read(Event(payload), Context());
        Assert.Equal("purchase-invoice", request.DocumentKind);
        Assert.Equal(AccountingSourceDocumentKindsV1.PurchaseInvoiceKey(Invoice, 2), request.IdempotencyKey);
        Assert.Equal(Actor, request.SourceActorId);
        Assert.Equal(new DateOnly(2026, 11, 17), request.DocumentDate);
        Assert.Equal("production-input", Assert.Single(request.Lines).LinePurpose);
        Assert.Equal("4000", Assert.Single(request.Lines).Net);
        Assert.Equal("40", request.ExchangeRate);
        Assert.Equal("110", request.DocumentTotals.Payable);
        Assert.Equal("4400", request.FunctionalTotals.Payable);
    }

    [Fact]
    public void Missing_source_purpose_actor_or_foreign_amounts_is_permanent_and_no_defaults_are_inserted()
    {
        var payload = Payload();
        foreach (var invalid in new[]
        {
            payload with { Lines = [payload.Lines![0]! with { LinePurpose = null }] },
            payload with { FinalizedBy = null },
            payload with { InvoiceDate = null },
            payload with { Rate = 40m },
        })
        {
            Assert.Throws<OutboxPermanentFailureException>(() => AccountingPurchaseInvoiceFinalizedInboxConsumer.Read(Event(invalid), Context()));
        }
    }

    [Fact]
    public async Task Company_mismatch_never_reaches_admission_and_mapping_gap_retries_the_same_purchase_key()
    {
        var port = new Port();
        var consumer = new AccountingPurchaseInvoiceFinalizedInboxConsumer(port, Context());
        await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => consumer.HandleAsync(Event(Payload() with { CompanyId = Guid.NewGuid() }), CancellationToken.None));
        Assert.Empty(port.Requests);
        await Assert.ThrowsAsync<AccountingInvoiceJournalRetryException>(() => consumer.HandleAsync(Event(Payload()), CancellationToken.None));
        await Assert.ThrowsAsync<AccountingInvoiceJournalRetryException>(() => consumer.HandleAsync(Event(Payload()), CancellationToken.None));
        Assert.Equal(2, port.Requests.Count);
        Assert.Equal(port.Requests[0].IdempotencyKey, port.Requests[1].IdempotencyKey);
    }

    private static AccountingPurchaseInvoiceFinalizedEventV1 Payload() => new(
        Invoice, 2, Guid.Parse("b0430000-0000-4000-8000-000000000004"),
        Guid.Parse("b0430000-0000-4000-8000-000000000005"), Guid.Parse("b0430000-0000-4000-8000-000000000006"),
        null, 110m, null, Company, 100m, 20m, 10m, null, null, null,
        [new(Guid.Parse("b0430000-0000-4000-8000-000000000007"), 1, "Goods", "production-input", 20m, 100m, 20m, 10m, null, null, null)],
        new DateOnly(2026, 11, 17), Actor.ToString("D"));

    private static WorkInboxEventV1 Event(AccountingPurchaseInvoiceFinalizedEventV1 payload) => new(
        "purchasing", Guid.Parse("b0430000-0000-4000-8000-000000000008"), Company, "purchasing.invoice.finalized.v1", 1,
        JsonSerializer.Serialize(payload, Json), new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero), "synthetic-purchase-journal", null);

    private static CompanyContext Context()
    {
        var context = new CompanyContext();
        context.Set(Company);
        return context;
    }

    private sealed class Port : IAccountingSourceDocumentAdmissionPortV1
    {
        public List<AccountingSourceDocumentAdmissionRequestV1> Requests { get; } = [];
        public Task<AccountingSourceDocumentAdmissionAnswerV1> AdmitAsync(AccountingSourceDocumentAdmissionRequestV1 request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(AccountingSourceDocumentAdmissionAnswerV1.Refused(AccountingSourceDocumentAdmissionReasonsV1.MappingPending, retryable: true));
        }
    }
}
