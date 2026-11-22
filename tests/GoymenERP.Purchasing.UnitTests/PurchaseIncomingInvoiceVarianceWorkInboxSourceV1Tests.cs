using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.WorkInbox;
using static GoymenERP.Purchasing.UnitTests.PurchasingWorkInboxTestWorld;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// W1-A (AI-D31/AI-D32, ALS-D8): the incoming invoice variance Work inbox source lists the invoices whose LATEST revision is
/// Matched with an undecided variance (what the variance approval accepts), oldest first, the matcher's own included; the rule
/// numbers are the frozen Ayarlar tolerance and the variance the owner's restored match computed. SENTETIK / NOT_ENV.
/// </summary>
public sealed class PurchaseIncomingInvoiceVarianceWorkInboxSourceV1Tests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Matcher = "SENTETIK-matcher";

    [Fact]
    public async Task Only_matched_invoices_with_an_undecided_variance_at_their_latest_revision_are_listed_oldest_first()
    {
        var world = new PurchasingWorkInboxTestWorld();
        var order = Order(CompanyA, "SENTETIK-PO-INV");
        var (olderInvoice, olderRevisions) = Matched(CompanyA, "SENTETIK-INV-OLD", order.Id, expectedNet: 90m, matchedAt: Start);
        var (newerInvoice, newerRevisions) = Matched(CompanyA, "SENTETIK-INV-NEW", order.Id, expectedNet: 110m, matchedAt: Start.AddHours(1));
        var (_, noVariance) = Matched(CompanyA, "SENTETIK-INV-EXACT", order.Id, expectedNet: 100m, matchedAt: Start.AddMinutes(-30));
        var (decided, decidedRevisions) = Matched(CompanyA, "SENTETIK-INV-DECIDED", order.Id, expectedNet: 90m, matchedAt: Start.AddMinutes(-40));
        decided.ApproveVariance(2, Approver, "SENTETIK onay", Start.AddMinutes(-35));
        decidedRevisions.Add(Revision(decided, Start.AddMinutes(-35)));
        var recorded = Invoice(CompanyA, "SENTETIK-INV-RECORDED");
        await world.SeedAsync(CompanyA, db =>
        {
            db.Orders.Add(order);
            db.IncomingInvoiceRevisions.AddRange(olderRevisions.Concat(newerRevisions).Concat(noVariance).Concat(decidedRevisions));
            db.IncomingInvoiceRevisions.Add(Revision(recorded, Start.AddMinutes(-50)));
        });
        var foreignOrder = Order(CompanyB, "SENTETIK-PO-B");
        var (_, foreign) = Matched(CompanyB, "SENTETIK-INV-B", foreignOrder.Id, expectedNet: 90m, matchedAt: Start.AddDays(-1));
        await world.SeedAsync(CompanyB, db =>
        {
            db.Orders.Add(foreignOrder);
            db.IncomingInvoiceRevisions.AddRange(foreign);
        });

        var result = await Source(world).ListPendingAsync(world.Caller(CompanyA), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        Assert.False(result.HasMore);
        Assert.Equal([olderInvoice.Id, newerInvoice.Id], result.Items!.Select(item => item.ItemId));
        var first = result.Items![0];
        AssertContractShape("purchasing", first);
        Assert.Equal("purchasing.incoming-invoice-variance.v1", first.Kind);
        Assert.Equal("SENTETIK-INV-OLD", first.DocumentLabel);
        Assert.Equal("/satinalma/faturalar/" + olderInvoice.Id.ToString("D"), first.Link);
        Assert.Equal(Start, first.WaitingSinceUtc);
        Assert.Contains(first.References, reference => reference.Role == "purchase-order" && reference.Label == "SENTETIK-PO-INV"
            && reference.RecordId == order.Id);
        Assert.Equal(PurchaseIncomingInvoicePermissionNamesV1.ApproveVariance, world.LastPermission);
    }

    [Fact]
    public async Task The_rules_carry_the_frozen_tolerance_and_the_owner_variance_and_the_matchers_own_invoice_is_returned()
    {
        var world = new PurchasingWorkInboxTestWorld { UserId = Matcher };
        var order = Order(CompanyA, "SENTETIK-PO-RULE");
        var (_, revisions) = Matched(CompanyA, "SENTETIK-INV-RULE", order.Id, expectedNet: 90m, matchedAt: Start, matchedQuantity: 8m);
        await world.SeedAsync(CompanyA, db =>
        {
            db.Orders.Add(order);
            db.IncomingInvoiceRevisions.AddRange(revisions);
        });

        var item = Assert.Single((await Source(world).ListPendingAsync(world.Caller(CompanyA), 5, CancellationToken.None)).Items!);

        Assert.Equal(Matcher, item.PreparedBy);
        Assert.Equal(
            [
                new WorkApprovalRuleResultV1(PurchaseIncomingInvoiceVarianceWorkInboxSourceV1.PriceVarianceRule, "2.5", "100", "10", null),
                new WorkApprovalRuleResultV1(PurchaseIncomingInvoiceVarianceWorkInboxSourceV1.QuantityVarianceRule, null, "10", "2", null),
            ],
            item.Rules);
    }

    [Fact]
    public async Task More_pending_invoices_than_requested_set_has_more()
    {
        var world = new PurchasingWorkInboxTestWorld();
        var order = Order(CompanyA, "SENTETIK-PO-MANY");
        var invoices = Enumerable.Range(0, 3)
            .Select(index => Matched(CompanyA, "SENTETIK-INV-" + index, order.Id, expectedNet: 90m, matchedAt: Start.AddMinutes(index)))
            .ToArray();
        await world.SeedAsync(CompanyA, db =>
        {
            db.Orders.Add(order);
            db.IncomingInvoiceRevisions.AddRange(invoices.SelectMany(invoice => invoice.Revisions));
        });

        var result = await Source(world).ListPendingAsync(world.Caller(CompanyA), 2, CancellationToken.None);

        Assert.True(result.HasMore);
        Assert.Equal([invoices[0].Invoice.Id, invoices[1].Invoice.Id], result.Items!.Select(item => item.ItemId));
    }

    [Fact]
    public async Task Without_the_variance_approval_permission_the_source_is_denied_without_items()
    {
        var world = new PurchasingWorkInboxTestWorld { CompanyWide = false };
        var (_, revisions) = Matched(CompanyA, "SENTETIK-INV", Guid.NewGuid(), expectedNet: 90m, matchedAt: Start);
        await world.SeedAsync(CompanyA, db => db.IncomingInvoiceRevisions.AddRange(revisions));

        var result = await Source(world).ListPendingAsync(world.Caller(CompanyA), 5, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, result.Outcome);
        Assert.Null(result.Items);
    }

    private static PurchaseIncomingInvoiceVarianceWorkInboxSourceV1 Source(PurchasingWorkInboxTestWorld world) =>
        new(world.Context(CompanyA), world.Fence(CompanyA));

    private static (PurchaseIncomingInvoice Invoice, List<PurchaseIncomingInvoiceRevisionRow> Revisions) Matched(Guid company,
        string number, Guid orderId, decimal expectedNet, DateTimeOffset matchedAt, decimal matchedQuantity = 10m)
    {
        var invoice = Invoice(company, number);
        var revisions = new List<PurchaseIncomingInvoiceRevisionRow> { Revision(invoice, matchedAt.AddHours(-1)) };
        var line = invoice.Lines[0];
        invoice.Match(1,
            [new PurchaseInvoiceReceiptMatch(company, line.Id, Guid.NewGuid(), orderId, Guid.NewGuid(), 3, invoice.SupplierPartyId,
                invoice.CompanyAccountId, invoice.CurrencyId, line.UnitId, matchedQuantity, expectedNet, null, new DateOnly(2026, 11, 1))],
            new PurchaseInvoiceVariancePolicy(Guid.NewGuid(), 1, 2.5m, 0m), Matcher);
        revisions.Add(Revision(invoice, matchedAt));
        return (invoice, revisions);
    }

    private static PurchaseIncomingInvoice Invoice(Guid company, string number)
    {
        var currency = Guid.NewGuid();
        return PurchaseIncomingInvoice.Record(Guid.NewGuid(), company, Guid.NewGuid(), Guid.NewGuid(), currency, currency, 2, 2, null,
            new PurchaseInvoiceSource(Guid.NewGuid(), 1, new string('a', 64), number, Guid.NewGuid(), new DateOnly(2026, 11, 3)),
            new PurchaseInvoiceAmounts(100m, 20m, 0m), null,
            [new PurchaseInvoiceLine(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), 10m, "material", "resale", 20m,
                new PurchaseInvoiceAmounts(100m, 20m, 0m), null)],
            [new PurchaseInvoiceInstallment(Guid.NewGuid(), Guid.NewGuid(), 1, new DateOnly(2026, 12, 3), 120m, null)],
            "SENTETIK-recorder", Start.AddDays(-1));
    }

    private static PurchaseIncomingInvoiceRevisionRow Revision(PurchaseIncomingInvoice invoice, DateTimeOffset recordedAt)
    {
        var snapshot = PurchaseIncomingInvoiceSnapshot.Capture(invoice);
        return new PurchaseIncomingInvoiceRevisionRow
        {
            CompanyId = invoice.CompanyId, InvoiceId = invoice.Id, Version = invoice.Version, Status = (int)invoice.Status,
            SnapshotJson = JsonSerializer.Serialize(snapshot, Json), RecordedAtUtc = recordedAt,
        };
    }

    private static PurchaseOrderRow Order(Guid company, string number) => new()
    {
        Id = Guid.NewGuid(), CompanyId = company, SupplierPartyId = Guid.NewGuid(), CompanyAccountId = Guid.NewGuid(),
        Status = (int)PurchaseOrderStatus.Approved, Version = 2, RowVersion = [1], EffectiveOn = new DateOnly(2026, 11, 1),
        CreatedAtUtc = Start.AddDays(-5), UpdatedAtUtc = Start.AddDays(-5), CreatedBy = "SENTETIK-buyer",
        CorrelationId = "SENTETIK-correlation", DocumentNumber = number,
    };
}
