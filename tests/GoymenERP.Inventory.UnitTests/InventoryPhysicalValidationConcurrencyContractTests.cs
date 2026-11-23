using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryPhysicalValidationConcurrencyContractTests
{
    [Theory]
    [InlineData("manufacturing")]
    [InlineData("definite-shipment")]
    [InlineData("customer-return")]
    [InlineData("supplier-return")]
    [InlineData("subcontract-delivery")]
    [InlineData("receipt-reversal-batch")]
    public async Task An_awaited_owner_guard_conflict_becomes_only_the_existing_typed_409_with_its_original_cause(string participant)
    {
        using var cancellation = new CancellationTokenSource();
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new InventoryStoreException(InventoryStoreCodes.ConcurrentChange, "Synthetic guard collision.",
            new DbUpdateConcurrencyException("Synthetic original storage cause."));
        var calls = 0;
        var pending = Acquire(participant)(async token =>
        {
            Assert.Equal(cancellation.Token, token);
            calls++;
            await released.Task;
            throw source;
        }, cancellation.Token);

        Assert.Equal(1, calls);
        Assert.False(pending.IsCompleted);
        released.SetResult();
        var refused = await Assert.ThrowsAnyAsync<Exception>(() => pending);
        Assert.Equal((409, ExpectedCode(participant)), ReadRefusal(participant, refused));
        Assert.Same(source, refused.InnerException);
        Assert.Same(source.InnerException, refused.InnerException!.InnerException);
        Assert.Equal(1, calls); // The borrowed transaction owner decides rollback/retry; the participant never retries.
        if (refused is InventoryManufacturingRealizationException manufacturing)
        {
            Assert.Null(manufacturing.StockShortfall);
            Assert.Null(manufacturing.PlanAllocationShortfall);
        }
    }

    [Theory]
    [InlineData("manufacturing")]
    [InlineData("definite-shipment")]
    [InlineData("customer-return")]
    [InlineData("supplier-return")]
    [InlineData("subcontract-delivery")]
    [InlineData("receipt-reversal-batch")]
    public async Task Another_store_failure_raw_storage_failure_or_cancellation_is_never_reclassified_as_a_guard_conflict(string participant)
    {
        using var cancellation = new CancellationTokenSource();
        Exception[] failures =
        [
            new InventoryStoreException(InventoryStoreCodes.LabelNotRegistered, "Synthetic different owner failure."),
            new InventoryStoreException(InventoryStoreCodes.ConcurrentChange.ToUpperInvariant(), "Synthetic differently cased code."),
            new DbUpdateConcurrencyException("An unclassified storage concurrency failure."),
            new DbUpdateException("An unclassified storage write failure."),
            new InvalidOperationException("An unclassified transaction failure."),
            new OperationCanceledException(cancellation.Token),
        ];
        foreach (var source in failures)
        {
            var calls = 0;
            var actual = await Assert.ThrowsAnyAsync<Exception>(() => Acquire(participant)(token =>
            {
                Assert.Equal(cancellation.Token, token);
                calls++;
                return Task.FromException(source);
            }, cancellation.Token));
            Assert.Same(source, actual);
            Assert.Equal(1, calls);
        }
    }

    [Theory]
    [InlineData("manufacturing")]
    [InlineData("definite-shipment")]
    [InlineData("customer-return")]
    [InlineData("supplier-return")]
    [InlineData("subcontract-delivery")]
    [InlineData("receipt-reversal-batch")]
    public async Task A_successful_guard_is_awaited_once_before_the_participant_can_continue(string participant)
    {
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var pending = Acquire(participant)(_ => { calls++; return released.Task; }, CancellationToken.None);
        Assert.Equal(1, calls);
        Assert.False(pending.IsCompleted);
        released.SetResult();
        await pending;
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Subcontract_release_keeps_the_existing_conflict_identity_without_losing_its_original_cause()
    {
        var source = new InventoryStoreException(InventoryStoreCodes.ConcurrentChange, "Synthetic guard collision.");
        var refused = Assert.IsType<InventoryAllocationException>(
            InventorySubcontractAllocationReleaseStoreV1.PhysicalValidationConflict(source));
        Assert.Equal((409, InventoryAllocationCodesV1.ConcurrentChange), (refused.Status, refused.Code));
        Assert.Same(source, refused.InnerException);
        Assert.False(refused.IsReplay);
        Assert.Null(refused.OriginalCorrelationId);
    }

    [Fact]
    public void Subcontract_release_leaves_other_failures_to_its_existing_rollback_error_handling()
    {
        Exception[] failures =
        [
            new InventoryStoreException(InventoryStoreCodes.LabelNotRegistered, "Synthetic different owner failure."),
            new InventoryStoreException(InventoryStoreCodes.ConcurrentChange.ToUpperInvariant(), "Synthetic differently cased code."),
            new DbUpdateConcurrencyException("An existing rollback classifier handles this storage failure."),
            new DbUpdateException("An existing rollback classifier handles this storage failure."),
            new OperationCanceledException(),
        ];
        foreach (var source in failures)
            Assert.Null(InventorySubcontractAllocationReleaseStoreV1.PhysicalValidationConflict(source));
    }

    private static Func<Func<CancellationToken, Task>, CancellationToken, Task> Acquire(string participant) => participant switch
    {
        "manufacturing" => InventoryManufacturingRealizationParticipant.AcquirePhysicalValidationAsync,
        "definite-shipment" => InventoryDefiniteShipmentParticipant.AcquirePhysicalValidationAsync,
        "customer-return" => InventoryCustomerReturnParticipant.AcquirePhysicalValidationAsync,
        "supplier-return" => InventorySupplierReturnExitParticipant.AcquirePhysicalValidationAsync,
        "subcontract-delivery" => InventorySubcontractDeliveryParticipant.AcquirePhysicalValidationAsync,
        "receipt-reversal-batch" => InventoryGoodsReceiptReversalParticipant.AcquirePhysicalValidationAsync,
        _ => throw new ArgumentOutOfRangeException(nameof(participant)),
    };

    private static (int Status, string Code) ReadRefusal(string participant, Exception refusal) => participant switch
    {
        "manufacturing" => Read(Assert.IsType<InventoryManufacturingRealizationException>(refusal)),
        "definite-shipment" or "subcontract-delivery" => Read(Assert.IsType<InventoryDefiniteShipmentException>(refusal)),
        "customer-return" => Read(Assert.IsType<InventoryCustomerReturnException>(refusal)),
        "supplier-return" => Read(Assert.IsType<InventorySupplierReturnExitException>(refusal)),
        "receipt-reversal-batch" => Read(Assert.IsType<InventoryGoodsReceiptReversalException>(refusal)),
        _ => throw new ArgumentOutOfRangeException(nameof(participant)),
    };

    private static (int Status, string Code) Read(InventoryManufacturingRealizationException refusal) => (refusal.Status, refusal.Code);
    private static (int Status, string Code) Read(InventoryDefiniteShipmentException refusal) => (refusal.Status, refusal.Code);
    private static (int Status, string Code) Read(InventoryCustomerReturnException refusal) => (refusal.Status, refusal.Code);
    private static (int Status, string Code) Read(InventorySupplierReturnExitException refusal) => (refusal.Status, refusal.Code);
    private static (int Status, string Code) Read(InventoryGoodsReceiptReversalException refusal) => (refusal.Status, refusal.Code);

    private static string ExpectedCode(string participant) => participant switch
    {
        "manufacturing" => InventoryManufacturingRealizationCodesV1.ConcurrentChange,
        "definite-shipment" or "subcontract-delivery" => InventoryDefiniteShipmentCodesV1.ConcurrentChange,
        "customer-return" => InventoryCustomerReturnCodesV1.ConcurrentChange,
        "supplier-return" => InventorySupplierReturnExitCodesV1.ConcurrentChange,
        "receipt-reversal-batch" => InventoryGoodsReceiptReversalCodesV1.ConcurrentChange,
        _ => throw new ArgumentOutOfRangeException(nameof(participant)),
    };
}
