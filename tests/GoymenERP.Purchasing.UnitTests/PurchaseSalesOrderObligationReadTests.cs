using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Requests;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseSalesOrderObligationReadTests
{
    private static readonly Guid Company = Guid.NewGuid(), Sales = Guid.NewGuid(), SalesLine = Guid.NewGuid(),
        RequestId = Guid.NewGuid(), RequestLine = Guid.NewGuid(), Product = Guid.NewGuid(), Unit = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(PurchaseRequestStatus.Draft)]
    [InlineData(PurchaseRequestStatus.Submitted)]
    [InlineData(PurchaseRequestStatus.Approved)]
    public void A_request_without_a_purchase_order_remains_open(PurchaseRequestStatus status)
    {
        Assert.Equal(PurchaseSalesOrderObligationStatusV1.Open, Project(Proof(status)));
    }

    [Theory]
    [InlineData(PurchaseRequestStatus.Rejected)]
    [InlineData(PurchaseRequestStatus.Withdrawn)]
    public void Properly_terminated_requests_have_no_remaining_obligation(PurchaseRequestStatus status)
    {
        Assert.Equal(PurchaseSalesOrderObligationStatusV1.Complete, Project(Proof(status)));
    }

    [Fact]
    public void No_request_for_this_sales_order_is_complete_and_public_response_has_no_amounts()
    {
        Assert.Equal(PurchaseSalesOrderObligationStatusV1.Complete,
            PurchaseSalesOrderObligationReadPortV1.Project(Company, Guid.NewGuid(), Proof(PurchaseRequestStatus.Approved)));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new PurchaseSalesOrderObligationReadV1(Company, Sales,
            PurchaseSalesOrderObligationStatusV1.Complete)));
        Assert.Equal(new[] { "CompanyId", "SalesOrderId", "Status" }, json.RootElement.EnumerateObject().Select(x => x.Name));
    }

    [Fact]
    public void Draft_po_or_unsourced_request_share_stays_open_even_after_another_share_finishes()
    {
        var draft = WithOrder(Proof(PurchaseRequestStatus.Approved), 10, 0, PurchaseOrderStatus.Draft);
        Assert.Equal(PurchaseSalesOrderObligationStatusV1.Open, Project(draft));
        var partial = WithOrder(Proof(PurchaseRequestStatus.Approved), 4, 4);
        Assert.Equal(PurchaseSalesOrderObligationStatusV1.Open, Project(partial));
        var split = WithOrder(partial, 6, 6);
        Assert.Equal(PurchaseSalesOrderObligationStatusV1.Complete, Project(split));
        var over = WithOrder(split, 1, 1);
        Assert.Throws<InvalidOperationException>(() => Project(over));
    }

    [Fact]
    public void Accepted_cancelled_and_explicitly_closed_shares_finish_but_missing_history_does_not()
    {
        Assert.Equal(PurchaseSalesOrderObligationStatusV1.Complete,
            Project(WithOrder(Proof(PurchaseRequestStatus.Approved), 10, 10)));
        var cancelled = WithOrder(Proof(PurchaseRequestStatus.Approved), 10, 0, PurchaseOrderStatus.Cancelled);
        Assert.Equal(PurchaseSalesOrderObligationStatusV1.Complete, Project(cancelled)); // ALS-D68: request share is not reopened.
        var closed = WithOrder(Proof(PurchaseRequestStatus.Approved), 10, 0);
        closed.Lines[0].Closed = 10;
        closed.Orders[0].Version++;
        closed = closed with { Closures = [new() { CompanyId = Company, OrderId = closed.Orders[0].Id,
            LineId = closed.Lines[0].LineId, Closed = true, OrderVersionAfter = 4, RecordedBy = "closer", Reason = "SENTETIK",
            RecordedAtUtc = Now, CorrelationId = "SENTETIK" }] };
        Assert.Equal(PurchaseSalesOrderObligationStatusV1.Complete, Project(closed));
        Assert.Throws<InvalidOperationException>(() => Project(closed with { Closures = [] }));
    }

    [Fact]
    public void Shared_order_waits_only_for_this_sales_orders_request_share()
    {
        var proof = WithOrder(Proof(PurchaseRequestStatus.Approved), 10, 10);
        var otherRequestLine = Guid.NewGuid();
        proof.Requests[0].Version++;
        proof.Orders[0].Version++;
        proof.Acceptances[0].OrderVersionAfter++;
        proof = proof with
        {
            RequestLines = [.. proof.RequestLines, new() { CompanyId = Company, RequestId = RequestId,
                LineId = otherRequestLine, LineNo = 2, ProductId = Product, UnitId = Unit, UnitIsKilogram = true,
                Quantity = 20, SourceKind = (int)PurchaseRequestSourceKind.Sales, SourceDocumentId = Guid.NewGuid(),
                SourceDocumentLineId = Guid.NewGuid() }],
            Lines = [.. proof.Lines, new() { CompanyId = Company, OrderId = proof.Orders[0].Id, LineId = Guid.NewGuid(),
                LineNo = 2, Kind = (int)PurchaseOrderLineKind.General, SourceRequestLineId = otherRequestLine,
                ProductId = Product, UnitId = Unit, UnitIsKilogram = true, Ordered = 20 }],
        };
        Assert.Equal(PurchaseSalesOrderObligationStatusV1.Complete, Project(proof));
    }

    [Fact]
    public void Technical_slice_is_folded_once_and_mismatched_source_cannot_disappear()
    {
        var proof = WithOrder(Proof(PurchaseRequestStatus.Approved), 10, 6);
        proof.Lines[0].Technical = 4;
        proof.Orders[0].Version++;
        proof.Acceptances[0].OrderVersionAfter++;
        var slice = new PurchaseOrderLineRow { CompanyId = Company, OrderId = proof.Orders[0].Id, LineId = Guid.NewGuid(),
            LineNo = 2, Kind = (int)PurchaseOrderLineKind.Technical, ParentLineId = proof.Lines[0].LineId,
            SourceRequestLineId = RequestLine, ProductId = Guid.NewGuid(), UnitId = Unit, UnitIsKilogram = true, Ordered = 4 };
        proof = proof with { Lines = [.. proof.Lines, slice] };
        Assert.Equal(PurchaseSalesOrderObligationStatusV1.Open, Project(proof));
        slice.Accepted = 4;
        proof.Orders[0].Version++;
        proof = proof with { Acceptances = [.. proof.Acceptances, Acceptance(proof.Orders[0].Id, slice.LineId, 4, 6)] };
        Assert.Equal(PurchaseSalesOrderObligationStatusV1.Complete, Project(proof));
        slice.SourceRequestLineId = null;
        Assert.Throws<InvalidOperationException>(() => Project(proof));
    }

    [Fact]
    public void Missing_headers_wrong_company_and_wrong_source_binding_fail_closed()
    {
        var proof = WithOrder(Proof(PurchaseRequestStatus.Approved), 10, 10);
        Assert.Throws<InvalidOperationException>(() => Project(proof with { Requests = [] }));
        Assert.Throws<InvalidOperationException>(() => Project(proof with { Orders = [] }));
        proof.Orders[0].SourceRequestId = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => Project(proof));
        proof.Orders[0].SourceRequestId = RequestId;
        proof.RequestLines[0].CompanyId = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => Project(proof));
    }

    [Fact]
    public async Task Company_and_required_consumer_fence_are_checked_before_database_access()
    {
        var company = new CompanyContext(); company.Set(Company);
        await using var db = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>().Options, company);
        var port = new PurchaseSalesOrderObligationReadPortV1(db, company);
        var calls = 0;
        Task Reject(CancellationToken _) { calls++; throw new UnauthorizedAccessException(); }
        Assert.Equal(PurchaseSalesOrderObligationStatusV1.Unavailable,
            (await port.ReadAsync(Guid.NewGuid(), Sales, Reject)).Status);
        Assert.Equal(0, calls);
        Assert.Equal(PurchaseSalesOrderObligationStatusV1.Unavailable, (await port.ReadAsync(Company, Sales, Reject)).Status);
        Assert.Equal(1, calls);
        Assert.Equal(PurchaseSalesOrderObligationStatusV1.Unavailable, (await port.ReadAsync(Company, Sales, null!)).Status);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => port.ReadAsync(Company, Sales, Reject, cancellation.Token));
    }

    private static PurchaseSalesOrderObligationStatusV1 Project(PurchaseSalesOrderObligationReadPortV1.Snapshot proof) =>
        PurchaseSalesOrderObligationReadPortV1.Project(Company, Sales, proof);
    private static PurchaseSalesOrderObligationReadPortV1.Snapshot Proof(PurchaseRequestStatus status)
    {
        var row = new PurchaseRequestRow { Id = RequestId, CompanyId = Company, Status = (int)status,
            Version = status == PurchaseRequestStatus.Draft ? 2 : status == PurchaseRequestStatus.Submitted ? 3 : 4,
            CreatedBy = "maker", SubmittedBy = status == PurchaseRequestStatus.Draft ? null : "maker",
            CreatedAtUtc = Now, UpdatedAtUtc = Now };
        if (status == PurchaseRequestStatus.Approved) { row.ApprovedBy = "manager"; row.ApprovedAtUtc = Now; }
        if (status == PurchaseRequestStatus.Rejected) { row.RejectedBy = "manager"; row.RejectedAtUtc = Now; row.RejectionReason = "SENTETIK"; }
        if (status == PurchaseRequestStatus.Withdrawn) { row.WithdrawnBy = "maker"; row.WithdrawnAtUtc = Now; row.WithdrawalReason = "SENTETIK"; }
        var line = new PurchaseRequestLineRow { CompanyId = Company, RequestId = RequestId, LineId = RequestLine,
            LineNo = 1, ProductId = Product, UnitId = Unit, UnitIsKilogram = true, Quantity = 10,
            SourceKind = (int)PurchaseRequestSourceKind.Sales, SourceDocumentId = Sales, SourceDocumentLineId = SalesLine };
        return new([row], [line], [], [], [], [], [], [], [], [], []);
    }
    private static PurchaseSalesOrderObligationReadPortV1.Snapshot WithOrder(PurchaseSalesOrderObligationReadPortV1.Snapshot proof,
        decimal quantity, decimal accepted, PurchaseOrderStatus status = PurchaseOrderStatus.Approved)
    {
        var id = Guid.NewGuid(); var lineId = Guid.NewGuid();
        var order = new PurchaseOrderRow { Id = id, CompanyId = Company, SourceRequestId = RequestId,
            SupplierPartyId = Guid.NewGuid(), CreatedBy = "maker", CreatedAtUtc = Now, UpdatedAtUtc = Now,
            Status = (int)status, Version = status == PurchaseOrderStatus.Draft ? 2 : accepted > 0 || status == PurchaseOrderStatus.Cancelled ? 4 : 3 };
        if (status != PurchaseOrderStatus.Draft) { order.ApprovedBy = "manager"; order.ApprovedAtUtc = Now; }
        if (status == PurchaseOrderStatus.Cancelled) { order.CancelledBy = "closer"; order.CancelledAtUtc = Now; order.CancellationReason = "SENTETIK"; }
        var line = new PurchaseOrderLineRow { CompanyId = Company, OrderId = id, LineId = lineId, LineNo = 1,
            Kind = (int)PurchaseOrderLineKind.General, SourceRequestLineId = RequestLine,
            ProductId = Product, UnitId = Unit, UnitIsKilogram = true, Ordered = quantity, Accepted = accepted,
            Cancelled = status == PurchaseOrderStatus.Cancelled ? quantity - accepted : 0 };
        return proof with { Orders = [.. proof.Orders, order], Lines = [.. proof.Lines, line],
            Acceptances = accepted > 0 ? [.. proof.Acceptances, Acceptance(id, lineId, accepted, 4)] : proof.Acceptances };
    }
    private static PurchaseAcceptanceRow Acceptance(Guid orderId, Guid lineId, decimal quantity, long version) => new()
    {
        AcceptanceId = Guid.NewGuid(), CompanyId = Company, OrderId = orderId, LineId = lineId, Quantity = quantity,
        OrderVersionAfter = version, RecordedAtUtc = Now, ActorId = "SENTETIK", CorrelationId = "SENTETIK",
        IdempotencyKeyHash = new string('a', 64), RequestHash = new string('b', 64), ResultJson = "{}",
    };
}
