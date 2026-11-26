using System.Diagnostics.Metrics;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Work.Infrastructure;
using GoymenERP.Modules.Work.Infrastructure.Outbox;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GoymenERP.Work.UnitTests;

/// <summary>
/// PLT-S6 (B-24, F-021): outbox source / inbox consumer registration contract v1, the (company, consumer, event id)
/// dedup key and the dispatcher metrics. The dispatcher stays OFF unless configured; registration never turns it on.
/// </summary>
public sealed class WorkOutboxRegistrationTests
{
    private const string SalesSource = "sales";
    private const string LegacySource = "sales-legacy";
    private const string InvoiceFinalized = "sales.invoice.finalized.v1";
    private const string FinanceConsumer = "finance.invoice-projection";
    private const string MetricsProbe = "metrics-probe";
    private static readonly Guid CompanyA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid CompanyB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");

    private static readonly WorkOutboxDispatcherOptions Enabled = new()
    {
        Enabled = true,
        BatchSize = 10,
        MaxAttempts = 3,
        InitialBackoff = TimeSpan.FromSeconds(10),
        MaxBackoff = TimeSpan.FromSeconds(40),
        PollInterval = TimeSpan.FromSeconds(1),
    };

    [Fact]
    public void Registration_refuses_invalid_names_duplicates_and_empty_lists_at_once()
    {
        var services = new ServiceCollection();
        services.AddWorkOutboxEventSource<RecordingSource>(SalesSource, new WorkOutboxEventTypeV1(InvoiceFinalized, 1));

        Assert.Throws<InvalidOperationException>(() =>
            services.AddWorkOutboxEventSource<RecordingSource>(SalesSource, new WorkOutboxEventTypeV1(InvoiceFinalized, 1)));
        Assert.Throws<ArgumentException>(() =>
            services.AddWorkOutboxEventSource<RecordingSource>("Sales", new WorkOutboxEventTypeV1(InvoiceFinalized, 1)));
        Assert.Throws<ArgumentException>(() =>
            services.AddWorkOutboxEventSource<RecordingSource>("sales\n", new WorkOutboxEventTypeV1(InvoiceFinalized, 1)));
        Assert.Throws<ArgumentException>(() =>
            services.AddWorkOutboxEventSource<RecordingSource>(new string('s', WorkOutboxRegistrationNamesV1.MaxNameLength + 1),
                new WorkOutboxEventTypeV1(InvoiceFinalized, 1)));
        Assert.Throws<ArgumentException>(() => services.AddWorkOutboxEventSource<RecordingSource>("purchasing"));
        Assert.Throws<ArgumentException>(() =>
            services.AddWorkOutboxEventSource<RecordingSource>("purchasing", new WorkOutboxEventTypeV1(InvoiceFinalized, 0)));
        Assert.Throws<ArgumentException>(() => services.AddWorkOutboxEventSource<RecordingSource>(
            "purchasing", new WorkOutboxEventTypeV1(InvoiceFinalized, 1), new WorkOutboxEventTypeV1(InvoiceFinalized, 1)));

        services.AddWorkInboxConsumer<RecordingConsumer>(FinanceConsumer, new WorkInboxSubscriptionV1(SalesSource, InvoiceFinalized, 1));
        Assert.Throws<InvalidOperationException>(() =>
            services.AddWorkInboxConsumer<RecordingConsumer>(FinanceConsumer, new WorkInboxSubscriptionV1(SalesSource, InvoiceFinalized, 1)));
        Assert.Throws<ArgumentException>(() => services.AddWorkInboxConsumer<RecordingConsumer>("finance.other"));
        Assert.Throws<ArgumentException>(() => services.AddWorkInboxConsumer<RecordingConsumer>(
            "finance.other", new WorkInboxSubscriptionV1(SalesSource, "Sales.Invoice", 1)));
        Assert.Throws<ArgumentException>(() => services.AddWorkInboxConsumer<RecordingConsumer>(
            "finance.other",
            new WorkInboxSubscriptionV1(SalesSource, InvoiceFinalized, 1),
            new WorkInboxSubscriptionV1(SalesSource, InvoiceFinalized, 1)));

        // Only the accepted registrations are visible to the dispatcher. The scoped source and consumer resolve their
        // test dependencies (the shared in-memory outbox, the handled list and the company context) as the harness does.
        services.AddSingleton(new EventStore());
        services.AddSingleton(new List<WorkInboxEventV1>());
        services.AddScoped<ICompanyContext, CompanyContext>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Equal(new[] { SalesSource }, scope.ServiceProvider.GetServices<IOutboxSourceV1>().Select(s => s.SourceName));
        Assert.Equal(new[] { FinanceConsumer }, scope.ServiceProvider.GetServices<IOutboxConsumerV1>().Select(c => c.ConsumerName));
        Assert.Single(services, d => d.ServiceType == typeof(WorkOutboxRegistry));
    }

    [Fact]
    public void A_subscription_to_an_unregistered_source_or_an_undeclared_event_fails_host_start_validation_in_any_order()
    {
        var valid = new ServiceCollection();
        valid.AddWorkInboxConsumer<RecordingConsumer>(FinanceConsumer, new WorkInboxSubscriptionV1(SalesSource, InvoiceFinalized, 1));
        valid.AddWorkOutboxEventSource<RecordingSource>(SalesSource, new WorkOutboxEventTypeV1(InvoiceFinalized, 1));
        using (var provider = valid.BuildServiceProvider())
        {
            Assert.NotNull(provider.GetRequiredService<IOptions<WorkOutboxRegistrationOptions>>().Value);
        }

        var broken = new ServiceCollection();
        broken.AddWorkOutboxEventSource<RecordingSource>(SalesSource, new WorkOutboxEventTypeV1(InvoiceFinalized, 1));
        broken.AddWorkInboxConsumer<RecordingConsumer>(
            FinanceConsumer,
            new WorkInboxSubscriptionV1("inventory", "inventory.definite-shipment.recorded.v1", 1),
            new WorkInboxSubscriptionV1(SalesSource, InvoiceFinalized, 2));
        using var brokenProvider = broken.BuildServiceProvider();
        var failure = Assert.Throws<OptionsValidationException>(
            () => brokenProvider.GetRequiredService<IOptions<WorkOutboxRegistrationOptions>>().Value);
        Assert.Contains(failure.Failures, f => f.Contains("unregistered source 'inventory'", StringComparison.Ordinal));
        Assert.Contains(failure.Failures, f => f.Contains("v2", StringComparison.Ordinal) && f.Contains("does not declare", StringComparison.Ordinal));
        Assert.Equal(2, failure.Failures.Count());
    }

    [Fact]
    public void Registering_sources_and_consumers_never_enables_the_dispatcher()
    {
        var services = new ServiceCollection();
        services.AddWorkOutboxEventSource<RecordingSource>(SalesSource, new WorkOutboxEventTypeV1(InvoiceFinalized, 1));
        services.AddWorkInboxConsumer<RecordingConsumer>(FinanceConsumer, new WorkInboxSubscriptionV1(SalesSource, InvoiceFinalized, 1));
        services.AddWorkOutboxDispatcher(new ConfigurationBuilder().Build());

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IHostedService));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(WorkOutboxDispatcher));
    }

    [Fact]
    public async Task A_registered_source_and_consumer_deliver_once_per_company_with_every_field_mapped()
    {
        var harness = new Harness(services =>
        {
            services.AddWorkOutboxEventSource<RecordingSource>(SalesSource, new WorkOutboxEventTypeV1(InvoiceFinalized, 1));
            services.AddWorkInboxConsumer<RecordingConsumer>(FinanceConsumer, new WorkInboxSubscriptionV1(SalesSource, InvoiceFinalized, 1));
        });
        var first = Event(CompanyA, 1) with { CausationId = "cause-1" };
        harness.Events.Add(SalesSource, first, Event(CompanyB, 1));

        var run = await harness.RunAsync();
        var again = await harness.RunAsync();

        Assert.Equal(2, run.Delivered);
        Assert.Equal(0, again.Delivered);
        var handled = Assert.Single(harness.Handled, e => e.CompanyId == CompanyA);
        Assert.Equal(
            new WorkInboxEventV1(SalesSource, first.EventId, CompanyA, InvoiceFinalized, 1, first.PayloadJson, first.OccurredAtUtc,
                first.CorrelationId, "cause-1"),
            handled);
        Assert.Equal(2, harness.Handled.Count);
        await using var db = harness.Db(CompanyA);
        var row = await db.OutboxDeliveries.SingleAsync();
        Assert.Equal((SalesSource, FinanceConsumer, first.EventId), (row.SourceName, row.ConsumerName, row.MessageId));
        Assert.Equal("1", (await db.OutboxSourceCursors.SingleAsync()).Position);
    }

    [Fact]
    public async Task An_event_id_already_recorded_for_a_consumer_from_one_source_is_not_handed_to_it_again_from_another()
    {
        var harness = new Harness(services =>
        {
            services.AddWorkOutboxEventSource<RecordingSource>(SalesSource, new WorkOutboxEventTypeV1(InvoiceFinalized, 1));
            services.AddWorkOutboxEventSource<LegacySalesSource>(LegacySource, new WorkOutboxEventTypeV1(InvoiceFinalized, 1));
            services.AddWorkInboxConsumer<RecordingConsumer>(
                FinanceConsumer,
                new WorkInboxSubscriptionV1(SalesSource, InvoiceFinalized, 1),
                new WorkInboxSubscriptionV1(LegacySource, InvoiceFinalized, 1));
        });
        var shared = Event(CompanyA, 1);
        harness.Events.Add(SalesSource, shared);
        harness.Events.Add(LegacySource, shared with { Position = "7" });

        var run = await harness.RunAsync();

        Assert.Equal(1, run.Delivered);
        Assert.Equal(1, run.CrossSourceDuplicates);
        Assert.Single(harness.Handled);
        await using var db = harness.Db(CompanyA);
        var row = await db.OutboxDeliveries.SingleAsync();
        Assert.Equal(SalesSource, row.SourceName);
        // Both cursors move: the duplicate is resolved, not held.
        Assert.Equal(2, await db.OutboxSourceCursors.CountAsync());
    }

    [Fact]
    public async Task Two_sources_under_one_name_fail_without_moving_any_cursor_and_others_still_run()
    {
        var first = new RawSource(SalesSource);
        var second = new RawSource(SalesSource);
        var harness = new Harness(services =>
        {
            services.AddSingleton<IOutboxSourceV1>(first);
            services.AddSingleton<IOutboxSourceV1>(second);
            services.AddWorkOutboxEventSource<LegacySalesSource>(LegacySource, new WorkOutboxEventTypeV1(InvoiceFinalized, 1));
            services.AddWorkInboxConsumer<RecordingConsumer>(
                FinanceConsumer,
                new WorkInboxSubscriptionV1(SalesSource, InvoiceFinalized, 1),
                new WorkInboxSubscriptionV1(LegacySource, InvoiceFinalized, 1));
        });
        harness.Events.Add(LegacySource, Event(CompanyA, 1));

        var run = await harness.RunAsync();

        Assert.Equal(0, first.Reads + second.Reads);
        Assert.Equal(1, run.Delivered);
        await using var db = harness.Db(CompanyA);
        Assert.Equal(new[] { LegacySource }, await db.OutboxSourceCursors.Select(c => c.SourceName).ToListAsync());
    }

    [Fact]
    public async Task Two_consumers_under_one_name_fail_the_source_and_record_nothing()
    {
        var harness = new Harness(services =>
        {
            services.AddWorkOutboxEventSource<RecordingSource>(SalesSource, new WorkOutboxEventTypeV1(InvoiceFinalized, 1));
            services.AddWorkInboxConsumer<RecordingConsumer>(FinanceConsumer, new WorkInboxSubscriptionV1(SalesSource, InvoiceFinalized, 1));
            services.AddSingleton<IOutboxConsumerV1>(new RawConsumer(FinanceConsumer));
        });
        harness.Events.Add(SalesSource, Event(CompanyA, 1));

        var run = await harness.RunAsync();

        Assert.Equal(0, run.Delivered);
        Assert.Empty(harness.Handled);
        await using var db = harness.Db(CompanyA);
        Assert.Empty(await db.OutboxDeliveries.ToListAsync());
        Assert.Empty(await db.OutboxSourceCursors.ToListAsync());
    }

    [Fact]
    public async Task An_event_nobody_subscribes_to_is_counted_unrouted_and_releases_the_cursor()
    {
        var harness = new Harness(services =>
            services.AddWorkOutboxEventSource<RecordingSource>(SalesSource, new WorkOutboxEventTypeV1(InvoiceFinalized, 1)));
        harness.Events.Add(SalesSource, Event(CompanyA, 1), Event(CompanyA, 2));

        var run = await harness.RunAsync();

        Assert.Equal(2, run.Unrouted);
        await using var db = harness.Db(CompanyA);
        Assert.Equal("2", (await db.OutboxSourceCursors.SingleAsync()).Position);
        Assert.Empty(await db.OutboxDeliveries.ToListAsync());
    }

    [Fact]
    public async Task Metrics_carry_source_consumer_and_event_type_tags_and_never_a_company_value()
    {
        const string source = MetricsProbe;
        const string consumer = "metrics-probe.consumer";
        var harness = new Harness(services =>
        {
            services.AddWorkOutboxEventSource<MetricsProbeSource>(source, new WorkOutboxEventTypeV1(InvoiceFinalized, 1));
            services.AddWorkInboxConsumer<RecordingConsumer>(consumer, new WorkInboxSubscriptionV1(source, InvoiceFinalized, 1));
        });
        harness.Events.Add(source, Event(CompanyA, 1));

        var measurements = new List<(string Instrument, Dictionary<string, object?> Tags)>();
        var lockObject = new object();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == WorkOutboxDispatcherMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        void Capture(Instrument instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                copy[tag.Key] = tag.Value;
            }

            lock (lockObject)
            {
                measurements.Add((instrument.Name, copy));
            }
        }

        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => Capture(instrument, tags));
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => Capture(instrument, tags));
        listener.Start();

        await harness.RunAsync();
        // The source sends the same message again at a later position: the second cycle counts one duplicate.
        harness.Events.Add(source, Event(CompanyA, 1) with { Position = "2" });
        await harness.RunAsync();
        listener.Dispose();

        List<(string Instrument, Dictionary<string, object?> Tags)> mine;
        lock (lockObject)
        {
            mine = measurements.Where(m => Equals(m.Tags.GetValueOrDefault(WorkOutboxDispatcherMetrics.SourceTag), source)).ToList();
        }

        var delivered = Assert.Single(mine, m => m.Instrument == WorkOutboxDispatcherMetrics.DeliveredCounterName);
        Assert.Equal(consumer, delivered.Tags[WorkOutboxDispatcherMetrics.ConsumerTag]);
        Assert.Equal(InvoiceFinalized, delivered.Tags[WorkOutboxDispatcherMetrics.EventTypeTag]);
        Assert.Single(mine, m => m.Instrument == WorkOutboxDispatcherMetrics.DuplicateCounterName);
        var handler = Assert.Single(mine, m => m.Instrument == WorkOutboxDispatcherMetrics.HandlerDurationHistogramName);
        Assert.Equal(WorkOutboxDispatcherMetrics.OutcomeSucceeded, handler.Tags[WorkOutboxDispatcherMetrics.OutcomeTag]);
        lock (lockObject)
        {
            Assert.Contains(measurements, m => m.Instrument == WorkOutboxDispatcherMetrics.CycleDurationHistogramName);
            Assert.All(measurements, m => Assert.DoesNotContain(m.Tags.Values, v =>
                Equals(v, CompanyA) || Equals(v, CompanyA.ToString()) || Equals(v, CompanyB) || Equals(v, CompanyB.ToString())));
        }
    }

    [Fact]
    public void The_inbox_dedup_key_is_a_unique_company_consumer_message_index_added_by_a_pure_DDL_migration()
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        using var context = new WorkDbContext(
            new DbContextOptionsBuilder<WorkDbContext>()
                .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;",
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", WorkDbContext.Schema))
                .Options,
            company);

        Assert.Equal(new[] { "AddPersonalNotifications", "AddWorkCollaboration", "AddWorkApprovalInboxEvents", "WidenWorkRecordTypesAndNoteReads", "AddWorkAssistantMemory", "AddWorkApprovalRuleVersions", "Wp2SingleNotificationStoreAndApprovalFollowUp", "AddWorkGoodsReceiptRecordType", "AddWorkApprovalDelegations", "AddStaleCostProposalNotification", "AddStaleCostNotificationCostKind" },
            context.Database.GetMigrations().TakeLast(11).Select(name => name.Split('_', 2)[1]));
        Assert.False(context.Database.HasPendingModelChanges());

        var deliveries = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(WorkOutboxDeliveryRow))!;
        var index = Assert.Single(deliveries.GetIndexes(), i => i.GetDatabaseName() == WorkDbContext.ConsumerMessageIndexName);
        Assert.True(index.IsUnique);
        Assert.Null(index.GetFilter());
        Assert.Equal(
            new[] { nameof(WorkOutboxDeliveryRow.CompanyId), nameof(WorkOutboxDeliveryRow.ConsumerName), nameof(WorkOutboxDeliveryRow.MessageId) },
            index.Properties.Select(p => p.Name));

        var migration = new AddWorkInboxConsumerDedup();
        var create = Assert.IsType<CreateIndexOperation>(Assert.Single(migration.UpOperations));
        Assert.True(create.IsUnique);
        Assert.Equal(WorkDbContext.ConsumerMessageIndexName, create.Name);
        Assert.Equal(WorkDbContext.Schema, create.Schema);
        // Dropping the index loses no row, so Down needs no refusal guard.
        var drop = Assert.IsType<DropIndexOperation>(Assert.Single(migration.DownOperations));
        Assert.Equal(WorkDbContext.ConsumerMessageIndexName, drop.Name);
        Assert.DoesNotContain(migration.UpOperations.Concat(migration.DownOperations), o => o is SqlOperation);
    }

    private static WorkOutboxEventV1 Event(Guid companyId, int n) => new(
        Guid.Parse($"00000000-0000-0000-0000-{n:D12}"),
        companyId,
        InvoiceFinalized,
        1,
        "{\"n\":" + n + "}",
        new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.Zero),
        "corr-" + n,
        null,
        n.ToString());

    /// <summary>Shared in-memory outboxes keyed by registered source name and company.</summary>
    private sealed class EventStore
    {
        private readonly Dictionary<(string Source, Guid Company), List<WorkOutboxEventV1>> events = [];

        public void Add(string source, params WorkOutboxEventV1[] items)
        {
            foreach (var item in items)
            {
                var key = (source, item.CompanyId);
                if (!events.TryGetValue(key, out var list))
                {
                    events[key] = list = [];
                }

                list.Add(item);
            }
        }

        public IReadOnlyList<WorkOutboxEventV1> Read(string source, Guid company, string? after, int max)
        {
            var from = after is null ? 0 : int.Parse(after);
            return events.GetValueOrDefault((source, company), [])
                .Where(e => int.Parse(e.Position) > from)
                .OrderBy(e => int.Parse(e.Position))
                .Take(max)
                .ToList();
        }
    }

    private sealed class Harness
    {
        private readonly ServiceProvider provider;
        private readonly WorkOutboxDispatcher dispatcher;
        private readonly string database = "work-registration-" + Guid.NewGuid().ToString("N");

        public Harness(Action<IServiceCollection> register)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(Events);
            services.AddSingleton(Handled);
            services.AddScoped<ICompanyContext, CompanyContext>();
            services.AddDbContext<WorkDbContext>(o => o.UseInMemoryDatabase(database));
            services.AddSingleton<IOutboxCompanyDirectoryV1>(new Directory([CompanyA, CompanyB]));
            services.AddSingleton<ICompanyLifecycleReadPortV1>(new TestCompanyLifecycleReader());
            register(services);
            provider = services.BuildServiceProvider();
            dispatcher = new WorkOutboxDispatcher(
                provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(Enabled), TimeProvider.System,
                NullLogger<WorkOutboxDispatcher>.Instance);
        }

        public EventStore Events { get; } = new();

        public List<WorkInboxEventV1> Handled { get; } = [];

        public Task<WorkOutboxDispatchSummary> RunAsync() => dispatcher.RunOnceAsync(CancellationToken.None);

        public WorkDbContext Db(Guid companyId)
        {
            var company = new CompanyContext();
            company.Set(companyId);
            return new WorkDbContext(new DbContextOptionsBuilder<WorkDbContext>().UseInMemoryDatabase(database).Options, company);
        }
    }

    // Scoped implementations resolved through the registration; the source name comes from the registration.
    private sealed class RecordingSource(EventStore store, ICompanyContext company) : IWorkOutboxEventSourceV1
    {
        public Task<IReadOnlyList<WorkOutboxEventV1>> ReadAfterAsync(string? afterPosition, int maxCount, CancellationToken cancellationToken) =>
            Task.FromResult(store.Read(SalesSource, company.CompanyId, afterPosition, maxCount));
    }

    private sealed class MetricsProbeSource(EventStore store, ICompanyContext company) : IWorkOutboxEventSourceV1
    {
        public Task<IReadOnlyList<WorkOutboxEventV1>> ReadAfterAsync(string? afterPosition, int maxCount, CancellationToken cancellationToken) =>
            Task.FromResult(store.Read(MetricsProbe, company.CompanyId, afterPosition, maxCount));
    }

    private sealed class LegacySalesSource(EventStore store, ICompanyContext company) : IWorkOutboxEventSourceV1
    {
        public Task<IReadOnlyList<WorkOutboxEventV1>> ReadAfterAsync(string? afterPosition, int maxCount, CancellationToken cancellationToken) =>
            Task.FromResult(store.Read(LegacySource, company.CompanyId, afterPosition, maxCount));
    }

    private sealed class RecordingConsumer(List<WorkInboxEventV1> handled) : IWorkInboxConsumerV1
    {
        public Task HandleAsync(WorkInboxEventV1 inboxEvent, CancellationToken cancellationToken)
        {
            handled.Add(inboxEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class RawSource(string name) : IOutboxSourceV1
    {
        public int Reads { get; private set; }

        public string SourceName => name;

        public Task<IReadOnlyList<OutboxMessageEnvelopeV1>> ReadAfterAsync(string? afterPosition, int maxCount, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<OutboxMessageEnvelopeV1>>([]);
        }
    }

    private sealed class RawConsumer(string name) : IOutboxConsumerV1
    {
        public string ConsumerName => name;

        public IReadOnlyCollection<OutboxSubscriptionV1> Subscriptions { get; } = [new OutboxSubscriptionV1(SalesSource, InvoiceFinalized, 1)];

        public Task HandleAsync(OutboxMessageEnvelopeV1 message, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A duplicate consumer name must never be called.");
    }

    private sealed class Directory(Guid[] companies) : IOutboxCompanyDirectoryV1
    {
        public Task<IReadOnlyList<Guid>> ListCompanyIdsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(companies);
    }
}
