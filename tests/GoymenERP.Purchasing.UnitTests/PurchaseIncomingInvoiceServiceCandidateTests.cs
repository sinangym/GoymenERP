using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Xunit;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// K3 review (purchasing P2-3): service receipt candidates of an incoming invoice line are judged one by one. A source whose stored
/// service-order snapshot is unreadable or no longer matches the invoice is not offered (fail-closed for that candidate only); the
/// other candidates on the page stay offered instead of the whole page becoming 503. SENTETIK.
/// </summary>
public sealed class PurchaseIncomingInvoiceServiceCandidateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_matching_approved_service_acceptance_is_offered_with_its_available_quantity()
    {
        var (order, fact, invoice) = World();

        var candidate = PurchaseIncomingInvoiceWorkspace.ServiceCandidate(order, fact, 3m, invoice, invoice.Lines[0]);

        Assert.NotNull(candidate);
        Assert.Equal((order.OrderId, fact.LineId, fact.AcceptanceId, Guid.Empty, invoice.Lines[0].ServiceTypeId),
            (candidate!.PurchaseOrderId, candidate.PurchaseOrderLineId, candidate.AcceptanceId, candidate.ProductId, candidate.ServiceTypeId));
    }

    [Theory]
    [InlineData("unreadable")]
    [InlineData("supplier")]
    [InlineData("account")]
    [InlineData("status")]
    [InlineData("line")]
    [InlineData("acceptance")]
    [InlineData("unit")]
    [InlineData("order")]
    [InlineData("zero")]
    public void A_source_that_cannot_be_offered_is_skipped_never_offered_and_never_a_page_failure(string fault)
    {
        var (order, fact, invoice) = World();
        PurchaseServiceOrderState? source = fault switch
        {
            "unreadable" => null,
            "supplier" => order with { SupplierPartyId = Guid.NewGuid() },
            "account" => order with { CompanyAccountId = Guid.NewGuid() },
            "status" => order with { Status = PurchaseServiceOrderStatus.Cancelled },
            "line" => order with { Lines = [] },
            "acceptance" => order with { Acceptances = [] },
            "unit" => order with { Lines = [order.Lines[0] with { UnitId = Guid.NewGuid() }] },
            "order" => order with { OrderId = Guid.NewGuid() },
            _ => order,
        };

        Assert.Null(PurchaseIncomingInvoiceWorkspace.ServiceCandidate(source, fact, fault == "zero" ? 0m : 3m, invoice, invoice.Lines[0]));
    }

    private static (PurchaseServiceOrderState Order, PurchaseServiceAcceptanceRow Fact, PurchaseIncomingInvoiceSnapshot Invoice) World()
    {
        var company = Guid.NewGuid(); var supplier = Guid.NewGuid(); var account = Guid.NewGuid(); var currency = Guid.NewGuid();
        var type = Guid.NewGuid(); var unit = Guid.NewGuid(); var day = new DateOnly(2026, 11, 17);
        var draft = PurchaseServiceOrder.Create(new(company, Guid.NewGuid(), 1, PurchaseServiceOrderKind.ServicePurchase,
            PurchaseServiceOrderStatus.Draft, supplier, account, "1", "1", day, "SENTETIK-maker", null, Now, Now,
            new(currency, "TRY", Guid.NewGuid(), 1, 2, 4),
            [new(Guid.NewGuid(), type, 1, "SENTETIK hizmet", unit, "kg", Guid.NewGuid(), 1, 3, 10m, 2m, 0m)], [], [], []));
        var approved = PurchaseServiceOrder.Approve(draft, 1, "SENTETIK-checker", Now);
        var acceptanceId = Guid.NewGuid();
        var accepted = PurchaseServiceOrder.Accept(approved, 2, acceptanceId, draft.Lines[0].LineId, 3m, day, "SENTETIK-receiver",
            "SENTETIK hizmet kabulü", Now);
        var fact = new PurchaseServiceAcceptanceRow { CompanyId = company, OrderId = accepted.OrderId, LineId = accepted.Lines[0].LineId,
            AcceptanceId = acceptanceId, Quantity = 3m, OrderVersionAfter = accepted.Version, SupplierPartyId = supplier,
            CompanyAccountId = account, CurrencyId = currency, ServiceTypeId = type, UnitId = unit };
        var invoice = PurchaseIncomingInvoice.Record(Guid.NewGuid(), company, supplier, account, currency, currency, 2, 2, null,
            new(Guid.NewGuid(), 1, new string('a', 64), "SENTETIK-HIZ-1", Guid.NewGuid(), day), new(8, 0, 0), null,
            [new(Guid.NewGuid(), 1, Guid.Empty, unit, 3, "Service", "service", 0, new(8, 0, 0), null, type)],
            [new(Guid.NewGuid(), Guid.NewGuid(), 1, day, 8, null)], "SENTETIK-recorder", Now);
        return (accepted, fact, PurchaseIncomingInvoiceSnapshot.Capture(invoice));
    }
}
