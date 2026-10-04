using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Logistics.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Quantities;
using GoymenERP.Modules.Logistics.Domain.Returns;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// Contract-level tests for the owner seams. SQL/host acceptance is deliberately deferred until the
/// Inventory participant and central adapter are owned and ready; these tests prevent either owner
/// from accepting raw label/location/owner/custodian data from a client.
/// </summary>
public sealed class LogisticsCustomerReturnServiceTests
{
    [Fact]
    public void Inventory_request_contains_only_source_identity_and_never_client_selected_stock_position()
    {
        var properties = typeof(LogisticsCustomerReturnStockRequestV1).GetProperties().Select(property => property.Name).ToArray();

        Assert.Contains(nameof(LogisticsCustomerReturnStockRequestV1.SourceShipmentId), properties);
        Assert.Contains(nameof(LogisticsCustomerReturnStockRequestV1.SourceDeliveryId), properties);
        Assert.DoesNotContain("LabelId", properties);
        Assert.DoesNotContain("LocationId", properties);
        Assert.DoesNotContain("OwnerPartyId", properties);
        Assert.DoesNotContain("CustodianPartyId", properties);
    }

    [Fact]
    public void Sales_read_contract_exposes_only_approved_aggregate_not_a_financial_reversal()
    {
        var properties = typeof(GoymenERP.Modules.Sales.Application.Contracts.SalesOrderLineCustomerReturnV1)
            .GetProperties().Select(property => property.Name).ToArray();

        Assert.Equal(new[] { "SalesOrderLineId", "UnitId", "ApprovedReturnedQuantity" }, properties);
        Assert.DoesNotContain("CreditNoteId", properties);
        Assert.DoesNotContain("InvoiceId", properties);
        Assert.DoesNotContain("Amount", properties);
    }

    [Fact]
    public void Pending_and_rejected_are_not_approved_physical_return_totals()
    {
        Assert.NotEqual(CustomerReturnStatus.Pending, CustomerReturnStatus.Approved);
        Assert.NotEqual(CustomerReturnStatus.Rejected, CustomerReturnStatus.Approved);
        Assert.Equal(0m, Quantity.Zero(QuantityUnit.Kilogram).Value);
    }

    [Fact]
    public async Task Same_key_fault_is_rechecked_after_rollback_and_hidden_when_scope_changed()
    {
        var state = new FaultState();
        var store = new FakeStore(state)
        {
            ReplayFault = new LogisticsStoreException(
                LogisticsStoreCodes.SameKeyPayloadMismatch,
                "must-not-leak")
        };
        var scope = new FakeScope(state);
        var runner = new FakeRunner(state, new FakeStock(state));
        var service = CreateService(scope, store, runner, new FakeSource());

        var exception = await Assert.ThrowsAsync<LogisticsCustomerReturnServiceException>(() => service.RejectAsync(
            Guid.NewGuid(), new RejectCustomerReturnV1("1", null), Metadata()));

        Assert.Equal((403, "access_denied"), (exception.Status, exception.Code));
        Assert.True(state.RollbackObserved);
        Assert.Equal(1, scope.FaultChecks);
    }

    [Fact]
    public async Task Inventory_stage_fault_is_rechecked_after_rollback_and_hidden_when_scope_changed()
    {
        var state = new FaultState();
        var pending = PendingReturn();
        var store = new FakeStore(state) { Pending = pending };
        var scope = new FakeScope(state);
        var stock = new FakeStock(state)
        {
            StageFault = new LogisticsCustomerReturnServiceException(
                409,
                "inventory_owner_conflict",
                [new("inventory", "must_not_leak")])
        };
        var runner = new FakeRunner(state, stock);
        var service = CreateService(scope, store, runner, new FakeSource(pending));

        var exception = await Assert.ThrowsAsync<LogisticsCustomerReturnServiceException>(() => service.ApproveAsync(
            pending.Id, new ApproveCustomerReturnV1("1", Guid.NewGuid(), null), Metadata()));

        Assert.Equal((403, "access_denied"), (exception.Status, exception.Code));
        Assert.True(state.RollbackObserved);
        Assert.Equal(1, scope.FaultChecks);
        Assert.Equal(0, store.StoreDecisionCalls);
    }

    [Fact]
    public async Task Cancellation_remains_cancellation_without_fault_disclosure_fencing()
    {
        var state = new FaultState();
        var store = new FakeStore(state) { ReplayFault = new OperationCanceledException("cancelled") };
        var scope = new FakeScope(state);
        var runner = new FakeRunner(state, new FakeStock(state));
        var service = CreateService(scope, store, runner, new FakeSource());

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.RejectAsync(
            Guid.NewGuid(), new RejectCustomerReturnV1("1", null), Metadata()));

        Assert.True(state.RollbackObserved);
        Assert.Equal(0, scope.FaultChecks);
    }

    [Theory]
    [InlineData(QuantityUnit.Kilogram)]
    [InlineData(QuantityUnit.Piece)]
    public async Task Raw_quantity_overprecision_is_refused_before_decimal_conversion_and_writes_nothing(
        QuantityUnit unit)
    {
        var state = new FaultState();
        var source = Source(unit);
        var store = new FakeStore(state);
        var service = CreateService(new FakeScope(state), store,
            new FakeRunner(state, new FakeStock(state)), new FakeSource(response: source));

        var exception = await Assert.ThrowsAsync<LogisticsCustomerReturnServiceException>(() => service.RequestAsync(
            source.LoadingInstructionId,
            source.LoadingInstructionLineId,
            new RequestCustomerReturnV1(Guid.NewGuid(), source.SourceDeliveryId, "1",
                "1.00000000000000000000000000001", "return", DateTimeOffset.UtcNow),
            Metadata()));

        Assert.Equal((422, "validation_failed"), (exception.Status, exception.Code));
        Assert.Equal(0, store.StoreRequestCalls);
    }

    [Theory]
    [InlineData(QuantityUnit.Kilogram)]
    [InlineData(QuantityUnit.Piece)]
    public async Task Raw_quantity_trailing_zeros_are_normalized_without_becoming_precision(QuantityUnit unit)
    {
        var state = new FaultState();
        var source = Source(unit);
        var store = new FakeStore(state);
        var service = CreateService(new FakeScope(state), store,
            new FakeRunner(state, new FakeStock(state)), new FakeSource(response: source));

        var response = await service.RequestAsync(
            source.LoadingInstructionId,
            source.LoadingInstructionLineId,
            new RequestCustomerReturnV1(Guid.NewGuid(), source.SourceDeliveryId, "1",
                "1.00000000000000000000000000000", "return", DateTimeOffset.UtcNow),
            Metadata());

        Assert.Equal("1", response.Return.Quantity);
        Assert.Equal(1, store.StoreRequestCalls);
    }

    private static LogisticsCustomerReturnService CreateService(
        ILogisticsCustomerReturnScopeV1 scope,
        ILogisticsCustomerReturnStoreV1 store,
        ILogisticsCustomerReturnTransactionRunnerV1 runner,
        ILogisticsCustomerReturnSourceReadV1 source) =>
        new(scope, source, store, runner,
            new("logistics.customer-returns.request", "logistics.customer-returns.decide",
                "logistics.customer-returns.read", "inventory.physical-allocation.write"),
            new FixedCorrelation(), TimeProvider.System);

    private static LogisticsCommandMetadata Metadata() =>
        new("fault-key", "fault-correlation", null, DateTimeOffset.UtcNow);

    private static CustomerReturn PendingReturn() => CustomerReturn.Request(
        Guid.NewGuid(), Company, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Quantity.Positive(10m, QuantityUnit.Kilogram),
        Quantity.Positive(20m, QuantityUnit.Kilogram), "return", "requester",
        DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);

    private static LogisticsCustomerReturnSourceV1 Source(QuantityUnit unit) => new(
        Company, Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Quantity.Positive(20m, unit));

    private static readonly Guid Company = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private sealed class FaultState
    {
        public bool Faulted { get; set; }
        public bool RollbackObserved { get; set; }
    }

    private sealed class FakeScope(FaultState state) : ILogisticsCustomerReturnScopeV1
    {
        public int FaultChecks { get; private set; }

        public Task<LogisticsCustomerReturnScopeV1> RequireAsync(
            string permission,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LogisticsCustomerReturnScopeV1(Company, "approver", ["ROLE-A"]));

        public Task RequireStillAsync(
            LogisticsCustomerReturnScopeV1 scope,
            string permission,
            CancellationToken cancellationToken = default)
        {
            if (!state.Faulted)
            {
                return Task.CompletedTask;
            }

            FaultChecks++;
            Assert.True(state.RollbackObserved);
            throw new LogisticsCustomerReturnServiceException(403, "access_denied");
        }
    }

    private sealed class FakeRunner(FaultState state, ILogisticsCustomerReturnStockV1 stock)
        : ILogisticsCustomerReturnTransactionRunnerV1
    {
        public async Task<T> ExecuteLogisticsAsync<T>(Guid companyId, Func<CancellationToken, Task<T>> work,
            Func<CancellationToken, Task> beforeCommit, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await work(cancellationToken);
                await beforeCommit(cancellationToken);
                return result;
            }
            catch
            {
                state.RollbackObserved = true;
                throw;
            }
        }

        public async Task<T> ExecuteApprovalAsync<T>(Guid companyId,
            Func<ILogisticsCustomerReturnStockV1, CancellationToken, Task<T>> work,
            Func<ILogisticsCustomerReturnStockV1, CancellationToken, Task> beforeCommit,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await work(stock, cancellationToken);
                await beforeCommit(stock, cancellationToken);
                return result;
            }
            catch
            {
                state.RollbackObserved = true;
                throw;
            }
        }
    }

    private sealed class FakeStock(FaultState state) : ILogisticsCustomerReturnStockV1
    {
        public Exception? StageFault { get; init; }

        public Task<LogisticsCustomerReturnStockResultV1> StageAsync(
            LogisticsCustomerReturnStockRequestV1 request,
            LogisticsWriteEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            if (StageFault is not null)
            {
                state.Faulted = true;
                return Task.FromException<LogisticsCustomerReturnStockResultV1>(StageFault);
            }

            throw new NotSupportedException();
        }

        public Task RequireStillAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSource(
        CustomerReturn? pending = null,
        LogisticsCustomerReturnSourceV1? response = null) : ILogisticsCustomerReturnSourceReadV1
    {
        public Task<LogisticsCustomerReturnSourceV1?> FindDeliveryAsync(
            Guid instructionId,
            Guid lineId,
            Guid deliveryId,
            CancellationToken cancellationToken = default)
        {
            if (response is not null)
            {
                return Task.FromResult<LogisticsCustomerReturnSourceV1?>(response);
            }

            if (pending is null)
            {
                return Task.FromResult<LogisticsCustomerReturnSourceV1?>(null);
            }

            return Task.FromResult<LogisticsCustomerReturnSourceV1?>(new(
                pending.CompanyId, pending.LoadingInstructionId, 1, pending.LoadingInstructionLineId,
                pending.SourceDeliveryId, pending.SourceShipmentId, pending.SourceOrderId,
                pending.SourceOrderLineId, pending.UnitId, pending.SourceDeliveredQuantity));
        }
    }

    private sealed class FakeStore(FaultState state) : ILogisticsCustomerReturnStoreV1
    {
        public Exception? ReplayFault { get; init; }
        public CustomerReturn? Pending { get; init; }
        public int StoreDecisionCalls { get; private set; }
        public int StoreRequestCalls { get; private set; }

        public Task<CustomerReturnReplayV1?> FindReplayAsync(LogisticsWriteEnvelope envelope, string requestHash,
            string command, CancellationToken cancellationToken = default)
        {
            if (ReplayFault is not null)
            {
                state.Faulted = true;
                return Task.FromException<CustomerReturnReplayV1?>(ReplayFault);
            }

            return Task.FromResult<CustomerReturnReplayV1?>(null);
        }

        public Task<CustomerReturn?> FindAsync(Guid customerReturnId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Pending);

        public Task<IReadOnlyList<CustomerReturn>> ListOrderReturnsAsync(
            Guid salesOrderId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CustomerReturn>>([]);

        public Task<Quantity> SumApprovedForDeliveryAsync(Guid sourceDeliveryId, QuantityUnit unit,
            CancellationToken cancellationToken = default) => Task.FromResult(Quantity.Zero(unit));

        public Task<CustomerReturnStoreResultV1> StoreDecisionAsync(LogisticsWriteEnvelope envelope,
            string requestHash, string command, CustomerReturn customerReturn,
            CancellationToken cancellationToken = default)
        {
            StoreDecisionCalls++;
            throw new NotSupportedException();
        }

        public Task<CustomerReturnReplayStoreResultV1> RecordRefusalAsync(LogisticsWriteEnvelope envelope,
            string requestHash, string command, CustomerReturnTerminalRefusalV1 refusal,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<CustomerReturnStoreResultV1> StoreRequestAsync(LogisticsWriteEnvelope envelope,
            string requestHash, CustomerReturn customerReturn,
            CancellationToken cancellationToken = default)
        {
            StoreRequestCalls++;
            return Task.FromResult(new CustomerReturnStoreResultV1(
                customerReturn,
                LogisticsWriteOutcome.Recorded,
                envelope.CorrelationId));
        }
    }

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "fault-correlation";
        public string? CausationId => null;
        public string SourceChannel => "Test";
        public string? ClientIpAddress => "127.0.0.1";
        public string? UserAgent => "SENTETIK-test";

        public void Set(string correlationId, string? causationId, string sourceChannel,
            string? clientIpAddress, string? userAgent)
        {
        }
    }
}
