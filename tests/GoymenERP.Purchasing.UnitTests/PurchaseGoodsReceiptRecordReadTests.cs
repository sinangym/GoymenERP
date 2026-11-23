using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>YOL-D42 (E-81, WP9): the goods receipt by acceptance id alone. SENTETIK ids only.</summary>
public sealed class PurchaseGoodsReceiptRecordReadTests
{
    private static readonly Guid Company = Guid.Parse("c4000000-0000-4000-8000-000000000001");
    private static readonly Guid Order = Guid.Parse("04000000-0000-4000-8000-000000000001");
    private static readonly Guid Acceptance = Guid.Parse("a4000000-0000-4000-8000-000000000001");

    [Fact]
    public async Task A_stored_receipt_answers_its_company_order_and_acceptance_through_the_authorized_receipt_read()
    {
        var receipts = new FakeReceipts();
        var answer = await new PurchaseGoodsReceiptRecordReadV1(receipts, new FakeLookup(Order)).ReadByAcceptanceAsync(Acceptance);
        Assert.Equal(new PurchaseGoodsReceiptRecordRefV1(Company, Order, Acceptance), answer);
        Assert.Equal(new[] { (Order, Acceptance) }, receipts.Asked);
    }

    [Fact]
    public async Task An_unknown_acceptance_still_goes_through_the_receipt_read_so_the_permission_is_decided_first()
    {
        // Unauthorized: the receipt read answers 403 before anything about the record, found or not.
        var forbidden = new FakeReceipts { Refusal = 403 };
        foreach (var lookup in new[] { new FakeLookup(null), new FakeLookup(Order) })
        {
            var error = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
                new PurchaseGoodsReceiptRecordReadV1(forbidden, lookup).ReadByAcceptanceAsync(Acceptance));
            Assert.Equal(403, error.Status);
        }

        Assert.Equal(new[] { (Guid.Empty, Acceptance), (Order, Acceptance) }, forbidden.Asked);
        // Authorized and missing: the read itself answers 404 for the empty order id.
        var missing = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            new PurchaseGoodsReceiptRecordReadV1(new FakeReceipts(), new FakeLookup(null)).ReadByAcceptanceAsync(Acceptance));
        Assert.Equal((404, "not_found"), (missing.Status, missing.Code));
        var empty = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            new PurchaseGoodsReceiptRecordReadV1(new FakeReceipts(), new FakeLookup(Order)).ReadByAcceptanceAsync(Guid.Empty));
        Assert.Equal(404, empty.Status);
    }

    [Fact]
    public async Task A_receipt_read_answering_another_order_or_acceptance_is_not_found()
    {
        var other = new FakeReceipts { AnswerOrder = Guid.NewGuid() };
        var error = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            new PurchaseGoodsReceiptRecordReadV1(other, new FakeLookup(Order)).ReadByAcceptanceAsync(Acceptance));
        Assert.Equal(404, error.Status);
    }

    private sealed class FakeLookup(Guid? order) : IPurchaseAcceptanceOrderLookupV1
    {
        public Task<Guid?> FindOrderIdAsync(Guid acceptanceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(acceptanceId == Acceptance ? order : null);
    }

    private sealed class FakeReceipts : IPurchaseGoodsReceiptServiceV1
    {
        public int? Refusal { get; init; }
        public Guid? AnswerOrder { get; init; }
        public List<(Guid, Guid)> Asked { get; } = [];

        public Task<PurchaseGoodsReceiptDetailV1> GetGoodsReceiptAsync(Guid orderId, Guid acceptanceId, CancellationToken cancellationToken = default)
        {
            Asked.Add((orderId, acceptanceId));
            if (Refusal is { } status) throw new PurchasingServiceException(status, "forbidden");
            if (orderId == Guid.Empty || acceptanceId == Guid.Empty) throw new PurchasingServiceException(404, "not_found");
            return Task.FromResult(new PurchaseGoodsReceiptDetailV1(new PurchaseGoodsReceiptViewV1(Company, acceptanceId, AnswerOrder ?? orderId,
                Guid.NewGuid(), "3", Guid.NewGuid(), "1", Guid.NewGuid(), "2", "0", Guid.NewGuid(), Guid.NewGuid(), "SENTETIK-ETIKET", "1",
                Guid.NewGuid(), Guid.NewGuid(), "2", "Free", DateTimeOffset.UnixEpoch)));
        }

        public Task<PurchaseGoodsReceiptResponseV1> RecordGoodsReceiptAsync(Guid orderId, Guid lineId, RecordPurchaseGoodsReceiptV1 request,
            PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PurchaseGoodsReceiptOptionsV1> GetGoodsReceiptOptionsAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PurchaseGoodsReceiptPageV1> GetGoodsReceiptsAsync(Guid orderId, int limit = 100, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
