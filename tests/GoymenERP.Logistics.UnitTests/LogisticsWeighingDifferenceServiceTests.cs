using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Logistics.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.WeighingDifferences;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// STK-S24 owner service: permission fence, fail-closed Settings and stock reads, replay and the final authority fence. The rate
/// and bands are synthetic Settings answers of a fake port. SENTETIK / NOT_ENV.
/// </summary>
public sealed class LogisticsWeighingDifferenceServiceTests
{
    private static readonly Guid Company = Guid.Parse("24200000-0000-4000-8000-000000000001");
    private static readonly Guid Record = Guid.Parse("24200000-0000-4000-8000-000000000002");
    private static readonly Guid Note = Guid.Parse("24200000-0000-4000-8000-000000000003");
    private static readonly Guid Instruction = Guid.Parse("24200000-0000-4000-8000-000000000004");
    private static readonly Guid Product = Guid.Parse("24200000-0000-4000-8000-000000000005");
    private static readonly Guid OtherProduct = Guid.Parse("24200000-0000-4000-8000-000000000006");
    private static readonly Guid Shipment = Guid.Parse("24200000-0000-4000-8000-000000000007");
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_excess_above_the_rate_is_recorded_pending_under_the_record_permission_and_the_final_fence()
    {
        var scope = new FakeScope();
        var store = new FakeStore(Source());
        var stock = new FakeStock(Stock(0m));
        var service = Create(scope, store, Readable(), stock);

        var response = await service.RecordAsync(Request(1008m), Metadata("record-key"));

        var view = response.Record;
        Assert.False(response.IsReplay);
        Assert.Equal(("ApprovalRequired", "PendingApproval", 1), (view.Decision, view.Status, view.Version));
        Assert.Equal(("None", 8m, 8m), (view.BandLevel, view.DifferenceKg, view.TotalExcessKg));
        Assert.Equal(0.5m, view.ApprovalRatePercent);
        Assert.Equal(1, view.Products.Single().TargetLineNo);
        Assert.Equal(["actor", "shipper"], view.EnteredBy.ToArray());
        Assert.Equal([LogisticsPermissionManifestV1.WeighingDifferencesRecord], scope.InitialPermissions);
        Assert.Equal([LogisticsPermissionManifestV1.WeighingDifferencesRecord], scope.FreshPermissions);
        Assert.Equal([Product], stock.RequestedProducts.ToArray());
        Assert.Same(store.Source, store.ExpectedSource);
        Assert.True(store.Stored);
    }

    [Fact]
    public async Task Physical_stock_that_covers_the_scale_leaves_a_recorded_snapshot_without_approval()
    {
        var store = new FakeStore(Source());
        var service = Create(new FakeScope(), store, Readable(), new FakeStock(Stock(8m)));

        var response = await service.RecordAsync(Request(1008m), Metadata("covered-key"));

        Assert.Equal(("NoExcess", "Recorded"), (response.Record.Decision, response.Record.Status));
        Assert.Equal(8m, response.Record.Products.Single().PhysicallyCoveredKg);
        Assert.Null(response.Record.Products.Single().TargetLineNo);
    }

    [Theory]
    [InlineData("0.12345")]
    [InlineData("0")]
    [InlineData("100.5")]
    public async Task A_rate_outside_its_range_or_precision_is_unreadable_and_the_record_waits(string rate)
    {
        var policy = new LogisticsWeighingDifferencePolicyReadV1(decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture),
            1m, 2m, 3m);
        var service = Create(new FakeScope(), new FakeStore(Source()), new FakePolicy(policy), new FakeStock(Stock(0m)));

        var response = await service.RecordAsync(Request(1000.001m), Metadata("rate-key"));

        Assert.Equal(("ApprovalRequired", "PendingApproval"), (response.Record.Decision, response.Record.Status));
        Assert.Null(response.Record.ApprovalRatePercent);
        Assert.Equal("None", response.Record.BandLevel);
    }

    [Fact]
    public async Task Bands_out_of_order_leave_the_band_level_not_evaluated_and_the_default_port_is_unreadable()
    {
        var disorder = Create(new FakeScope(), new FakeStore(Source()),
            new FakePolicy(new LogisticsWeighingDifferencePolicyReadV1(0.5m, 2m, 1m, 3m)), new FakeStock(Stock(0m)));
        var unavailable = Create(new FakeScope(), new FakeStore(Source()), new UnavailableLogisticsWeighingDifferencePolicyReadPort(),
            new FakeStock(Stock(0m)));

        var first = await disorder.RecordAsync(Request(1100m), Metadata("bands-key"));
        var second = await unavailable.RecordAsync(Request(1004m), Metadata("default-key"));

        Assert.Equal("NotEvaluated", first.Record.BandLevel);
        Assert.Null(first.Record.WarningBandPercent);
        Assert.Equal(("NotEvaluated", "ApprovalRequired"), (second.Record.BandLevel, second.Record.Decision));
    }

    [Fact]
    public async Task A_missing_or_issued_dispatch_note_is_refused_before_anything_is_stored()
    {
        var missingStore = new FakeStore(null);
        var issuedStore = new FakeStore(Source() with { IsDraft = false });

        var missing = await Assert.ThrowsAsync<LogisticsWeighingDifferenceServiceException>(() =>
            Create(new FakeScope(), missingStore, Readable(), new FakeStock(Stock(0m)))
                .RecordAsync(Request(1008m), Metadata("missing-key")));
        var issued = await Assert.ThrowsAsync<LogisticsWeighingDifferenceServiceException>(() =>
            Create(new FakeScope(), issuedStore, Readable(), new FakeStock(Stock(0m)))
                .RecordAsync(Request(1008m), Metadata("issued-key")));

        Assert.Equal((404, LogisticsWeighingDifferenceCodesV1.DispatchNoteNotFound), (missing.Status, missing.Code));
        Assert.Equal((409, LogisticsWeighingDifferenceCodesV1.DispatchNoteNotDraft), (issued.Status, issued.Code));
        Assert.False(missingStore.Stored);
        Assert.False(issuedStore.Stored);
    }

    [Fact]
    public async Task A_note_line_that_is_not_in_kilograms_is_refused()
    {
        var store = new FakeStore(Source() with { Lines = [new WeighingDifferenceSourceLineV1(1, Shipment, Product, 10m, false)] });

        var error = await Assert.ThrowsAsync<LogisticsWeighingDifferenceServiceException>(() =>
            Create(new FakeScope(), store, Readable(), new FakeStock(Stock(0m))).RecordAsync(Request(10m), Metadata("piece-key")));

        Assert.Equal((409, WeighingDifferenceRuleCodes.UnitNotKilogram), (error.Status, error.Code));
        Assert.False(store.Stored);
    }

    [Fact]
    public async Task An_unreadable_physical_stock_refuses_the_record_instead_of_assuming_any_cover()
    {
        var cases = new[]
        {
            LogisticsWeighingDifferenceStockReadV1.Unreadable,
            new LogisticsWeighingDifferenceStockReadV1(true, new Dictionary<Guid, decimal>()),
            new LogisticsWeighingDifferenceStockReadV1(true, new Dictionary<Guid, decimal> { [Product] = -1m }),
            new LogisticsWeighingDifferenceStockReadV1(true, new Dictionary<Guid, decimal> { [Product] = 0.0001m }),
        };
        foreach (var read in cases)
        {
            var store = new FakeStore(Source());
            var error = await Assert.ThrowsAsync<LogisticsWeighingDifferenceServiceException>(() =>
                Create(new FakeScope(), store, Readable(), new FakeStock(read)).RecordAsync(Request(1008m), Metadata("stock-key")));

            Assert.Equal((503, LogisticsWeighingDifferenceCodesV1.StockUnavailable), (error.Status, error.Code));
            Assert.False(store.Stored);
        }

        var unavailable = await Assert.ThrowsAsync<LogisticsWeighingDifferenceServiceException>(() =>
            Create(new FakeScope(), new FakeStore(Source()), Readable(), new UnavailableLogisticsWeighingDifferenceStockReadPort())
                .RecordAsync(Request(1008m), Metadata("default-stock-key")));
        Assert.Equal(503, unavailable.Status);
    }

    [Fact]
    public async Task Every_note_product_needs_one_scale_entry_and_a_bad_request_is_a_validation_failure()
    {
        var service = Create(new FakeScope(), new FakeStore(Source()), Readable(), new FakeStock(Stock(0m)));

        var unexpected = await Assert.ThrowsAsync<LogisticsWeighingDifferenceServiceException>(() => service.RecordAsync(
            new RecordWeighingDifferenceV1(Record, Note, [new(Product, 1000m), new(OtherProduct, 1m)]), Metadata("extra-key")));
        var empty = await Assert.ThrowsAsync<LogisticsWeighingDifferenceServiceException>(() => service.RecordAsync(
            new RecordWeighingDifferenceV1(Record, Note, []), Metadata("empty-key")));
        var negative = await Assert.ThrowsAsync<LogisticsWeighingDifferenceServiceException>(() => service.RecordAsync(
            Request(-1m), Metadata("negative-key")));

        Assert.Equal((409, WeighingDifferenceRuleCodes.MeasurementUnexpected), (unexpected.Status, unexpected.Code));
        Assert.Equal((422, LogisticsWeighingDifferenceCodesV1.ValidationFailed), (empty.Status, empty.Code));
        Assert.Equal((422, LogisticsWeighingDifferenceCodesV1.ValidationFailed), (negative.Status, negative.Code));
    }

    [Fact]
    public async Task A_replay_returns_the_stored_result_without_reading_the_note_again()
    {
        var stored = Pending();
        var store = new FakeStore(Source()) { Replay = new(stored, LogisticsWriteOutcome.Replayed, "first-corr") };
        var service = Create(new FakeScope(), store, Readable(), new FakeStock(Stock(0m)));

        var response = await service.RecordAsync(Request(1008m), Metadata("record-key"));

        Assert.True(response.IsReplay);
        Assert.Equal("first-corr", response.OriginalCorrelationId);
        Assert.Equal(0, store.SourceReads);
        Assert.False(store.Stored);
    }

    [Fact]
    public async Task Approval_uses_the_approve_permission_and_moves_the_record_to_approved()
    {
        var scope = new FakeScope { Actor = "manager" };
        var store = new FakeStore(Source()) { Existing = Pending() };
        var service = Create(scope, store, Readable(), new FakeStock(Stock(0m)));

        var response = await service.ApproveAsync(Record, new ApproveWeighingDifferenceV1(1), Metadata("approve-key"));

        Assert.Equal(("Approved", 2, "manager"), (response.Record.Status, response.Record.Version, response.Record.ApprovedBy));
        Assert.Equal([LogisticsPermissionManifestV1.WeighingDifferencesApprove], scope.InitialPermissions);
        Assert.Equal([LogisticsPermissionManifestV1.WeighingDifferencesApprove], scope.FreshPermissions);
    }

    [Fact]
    public async Task The_person_who_entered_a_shipment_cannot_approve_and_lost_authority_stops_the_approval()
    {
        var self = await Assert.ThrowsAsync<LogisticsWeighingDifferenceServiceException>(() =>
            Create(new FakeScope { Actor = "shipper" }, new FakeStore(Source()) { Existing = Pending() }, Readable(),
                new FakeStock(Stock(0m))).ApproveAsync(Record, new(1), Metadata("self-key")));
        var store = new FakeStore(Source()) { Existing = Pending() };
        var lost = await Assert.ThrowsAsync<LogisticsWeighingDifferenceServiceException>(() =>
            Create(new FakeScope { Actor = "manager", DenyFresh = true }, store, Readable(), new FakeStock(Stock(0m)))
                .ApproveAsync(Record, new(1), Metadata("lost-key")));
        var stale = await Assert.ThrowsAsync<LogisticsWeighingDifferenceServiceException>(() =>
            Create(new FakeScope { Actor = "manager" }, new FakeStore(Source()) { Existing = Pending() }, Readable(),
                new FakeStock(Stock(0m))).ApproveAsync(Record, new(2), Metadata("stale-key")));

        Assert.Equal((409, WeighingDifferenceRuleCodes.SelfApproval), (self.Status, self.Code));
        Assert.Equal((403, "forbidden"), (lost.Status, lost.Code));
        Assert.False(store.Stored);
        Assert.Equal((409, LogisticsWeighingDifferenceCodesV1.VersionConflict), (stale.Status, stale.Code));
    }

    [Theory]
    [InlineData(LogisticsStoreCodes.ConcurrentChange, 409, LogisticsWeighingDifferenceCodesV1.VersionConflict)]
    [InlineData(LogisticsStoreCodes.SameKeyPayloadMismatch, 409, LogisticsStoreCodes.SameKeyPayloadMismatch)]
    [InlineData(LogisticsWeighingDifferenceCodesV1.AlreadyRecorded, 409, LogisticsWeighingDifferenceCodesV1.AlreadyRecorded)]
    [InlineData(LogisticsStoreCodes.ValueDoesNotFit, 503, LogisticsWeighingDifferenceCodesV1.Unavailable)]
    public async Task Store_refusals_map_to_stable_problem_codes(string storeCode, int status, string code)
    {
        var store = new FakeStore(Source()) { Refusal = storeCode };

        var error = await Assert.ThrowsAsync<LogisticsWeighingDifferenceServiceException>(() =>
            Create(new FakeScope(), store, Readable(), new FakeStock(Stock(0m))).RecordAsync(Request(1008m), Metadata("store-key")));

        Assert.Equal((status, code), (error.Status, error.Code));
    }

    [Fact]
    public async Task Reading_by_dispatch_note_uses_the_read_permission_and_a_missing_record_is_not_found()
    {
        var scope = new FakeScope();
        var service = Create(scope, new FakeStore(Source()), Readable(), new FakeStock(Stock(0m)));

        var error = await Assert.ThrowsAsync<LogisticsWeighingDifferenceServiceException>(() => service.GetByDispatchNoteAsync(Note));

        Assert.Equal((404, LogisticsWeighingDifferenceCodesV1.NotFound), (error.Status, error.Code));
        Assert.Equal([LogisticsPermissionManifestV1.WeighingDifferencesRead], scope.InitialPermissions);
    }

    private static LogisticsWeighingDifferenceService Create(FakeScope scope, FakeStore store,
        ILogisticsWeighingDifferencePolicyReadPortV1 policies, ILogisticsWeighingDifferenceStockReadPortV1 stock) => new(
        scope, store,
        new(LogisticsPermissionManifestV1.WeighingDifferencesRecord, LogisticsPermissionManifestV1.WeighingDifferencesApprove,
            LogisticsPermissionManifestV1.WeighingDifferencesRead),
        policies, stock, new FixedCorrelation(), new FixedTimeProvider(Now));

    private static FakePolicy Readable() => new(new LogisticsWeighingDifferencePolicyReadV1(0.5m, 1m, 2m, 3m));

    private static LogisticsWeighingDifferenceStockReadV1 Stock(decimal physicalKg) =>
        new(true, new Dictionary<Guid, decimal> { [Product] = physicalKg });

    private static RecordWeighingDifferenceV1 Request(decimal scaleKg) => new(Record, Note, [new(Product, scaleKg)]);

    private static WeighingDifferenceSourceV1 Source() => new(Note, Instruction, true,
        [new WeighingDifferenceSourceLineV1(1, Shipment, Product, 1000m, true)], ["shipper"], true);

    private static WeighingDifferenceRecord Pending() => WeighingDifferenceRecord.Create(Company, Record, Note, Instruction,
        WeighingDifferenceCalculator.Evaluate(
            [new WeighingDifferenceNoteLine(1, Shipment, Product, Quantity.Of(1000m, QuantityUnit.Kilogram))],
            [new WeighingDifferenceProductMeasurement(Product, Quantity.Of(1008m, QuantityUnit.Kilogram),
                Quantity.Zero(QuantityUnit.Kilogram))],
            new WeighingDifferencePolicy(0.5m, null)),
        ["shipper"], true, "actor", Now);

    private static LogisticsCommandMetadata Metadata(string key) => new(key, "corr", null, Now);

    private sealed class FakeScope : ILogisticsWeighingDifferenceScopeV1
    {
        public string Actor { get; init; } = "actor";
        public bool DenyFresh { get; init; }
        public List<string> InitialPermissions { get; } = [];
        public List<string> FreshPermissions { get; } = [];

        public Task<LogisticsWeighingDifferenceScopeV1> RequireAsync(string permission, CancellationToken cancellationToken = default)
        {
            InitialPermissions.Add(permission);
            return Task.FromResult(new LogisticsWeighingDifferenceScopeV1(Company, Actor, ["ROLE"]));
        }

        public Task RequireStillAsync(LogisticsWeighingDifferenceScopeV1 scope, string permission,
            CancellationToken cancellationToken = default)
        {
            FreshPermissions.Add(permission);
            if (DenyFresh) throw new LogisticsWeighingDifferenceServiceException(403, "forbidden");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStore(WeighingDifferenceSourceV1? source) : ILogisticsWeighingDifferenceStoreV1
    {
        public WeighingDifferenceSourceV1? Source { get; } = source;
        public WeighingDifferenceSourceV1? ExpectedSource { get; private set; }
        public WeighingDifferenceStoreResultV1? Replay { get; init; }
        public WeighingDifferenceRecord? Existing { get; init; }
        public string? Refusal { get; init; }
        public int SourceReads { get; private set; }
        public bool Stored { get; private set; }

        public Task<WeighingDifferenceStoreResultV1?> FindReplayAsync(LogisticsWriteEnvelope envelope, string commandName,
            string requestHash, CancellationToken cancellationToken = default) => Task.FromResult(Replay);

        public Task<WeighingDifferenceSourceV1?> ReadSourceAsync(Guid dispatchNoteId, CancellationToken cancellationToken = default)
        {
            SourceReads++;
            return Task.FromResult(Source);
        }

        public async Task<WeighingDifferenceStoreResultV1> StoreAsync(LogisticsWriteEnvelope envelope, string requestHash,
            WeighingDifferenceRecord record, WeighingDifferenceSourceV1 expectedSource, Func<CancellationToken, Task> beforeCommit,
            CancellationToken cancellationToken = default)
        {
            if (Refusal is not null) throw new LogisticsStoreException(Refusal, "SENTETIK store refusal");
            ExpectedSource = expectedSource;
            await beforeCommit(cancellationToken);
            Stored = true;
            return new(record, LogisticsWriteOutcome.Recorded, envelope.CorrelationId);
        }

        public async Task<WeighingDifferenceStoreResultV1> MutateAsync(LogisticsWriteEnvelope envelope, string requestHash,
            string command, Guid recordId, Action<WeighingDifferenceRecord, DateTimeOffset> mutate,
            Func<CancellationToken, Task> beforeCommit, CancellationToken cancellationToken = default)
        {
            if (Refusal is not null) throw new LogisticsStoreException(Refusal, "SENTETIK store refusal");
            var record = Existing ?? throw new LogisticsStoreException(LogisticsWeighingDifferenceCodesV1.NotFound, "SENTETIK");
            mutate(record, Now.AddMinutes(1));
            await beforeCommit(cancellationToken);
            Stored = true;
            return new(record, LogisticsWriteOutcome.Recorded, envelope.CorrelationId);
        }

        public Task<WeighingDifferenceRecord?> FindByDispatchNoteAsync(Guid dispatchNoteId,
            CancellationToken cancellationToken = default) => Task.FromResult(Existing);
    }

    private sealed class FakePolicy(LogisticsWeighingDifferencePolicyReadV1 read) : ILogisticsWeighingDifferencePolicyReadPortV1
    {
        public Task<LogisticsWeighingDifferencePolicyReadV1> ReadAsync(Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(read);
    }

    private sealed class FakeStock(LogisticsWeighingDifferenceStockReadV1 read) : ILogisticsWeighingDifferenceStockReadPortV1
    {
        public List<Guid> RequestedProducts { get; } = [];

        public Task<LogisticsWeighingDifferenceStockReadV1> ReadAsync(Guid companyId, IReadOnlyCollection<Guid> productIds,
            CancellationToken cancellationToken = default)
        {
            RequestedProducts.AddRange(productIds);
            return Task.FromResult(read);
        }
    }

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "corr";
        public string? CausationId => null;
        public string SourceChannel => "Test";
        public string? ClientIpAddress => "127.0.0.1";
        public string? UserAgent => "SENTETIK-test";
        public void Set(string correlationId, string? causationId, string sourceChannel,
            string? clientIpAddress, string? userAgent) { }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
