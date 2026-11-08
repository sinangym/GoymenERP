using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Work.Infrastructure;
using GoymenERP.Modules.Work.Infrastructure.Outbox;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GoymenERP.Work.UnitTests;

public sealed class WorkOutboxDispatcherTests
{
    private const string SourceName = "sales";
    private const string EventType = "sales.invoice.finalized.v1";
    private static readonly Guid CompanyA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid CompanyB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");

    private static readonly WorkOutboxDispatcherOptions Defaults = new()
    {
        Enabled = true,
        BatchSize = 10,
        MaxAttempts = 3,
        InitialBackoff = TimeSpan.FromSeconds(10),
        MaxBackoff = TimeSpan.FromSeconds(40),
        PollInterval = TimeSpan.FromSeconds(1),
    };

    [Fact]
    public void The_dispatcher_is_off_by_default_and_refuses_an_enabled_section_without_explicit_values()
    {
        var off = new ServiceCollection();
        off.AddWorkOutboxDispatcher(new ConfigurationBuilder().Build());
        Assert.DoesNotContain(off, d => d.ServiceType == typeof(IHostedService));

        var explicitOff = new ServiceCollection();
        explicitOff.AddWorkOutboxDispatcher(Config(("Enabled", "false")));
        Assert.DoesNotContain(explicitOff, d => d.ServiceType == typeof(IHostedService));

        var incomplete = new ServiceCollection();
        var failure = Assert.Throws<InvalidOperationException>(
            () => incomplete.AddWorkOutboxDispatcher(Config(("Enabled", "true"), ("BatchSize", "5"))));
        Assert.Contains("MaxAttempts", failure.Message);
        Assert.Contains("PollInterval", failure.Message);
        Assert.DoesNotContain("BatchSize", failure.Message);

        var on = new ServiceCollection();
        on.AddWorkOutboxDispatcher(Config(
            ("Enabled", "true"), ("BatchSize", "5"), ("MaxAttempts", "3"), ("InitialBackoff", "00:00:05"),
            ("MaxBackoff", "00:01:00"), ("PollInterval", "00:00:02")));
        Assert.Contains(on, d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void Backoff_doubles_from_the_configured_start_and_never_passes_the_configured_cap()
    {
        var settings = new WorkOutboxDispatcherSettings(1, 9, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(10), settings.BackoffAfter(1));
        Assert.Equal(TimeSpan.FromSeconds(20), settings.BackoffAfter(2));
        Assert.Equal(TimeSpan.FromSeconds(40), settings.BackoffAfter(3));
        Assert.Equal(TimeSpan.FromSeconds(40), settings.BackoffAfter(4));
        Assert.Equal(TimeSpan.FromSeconds(40), settings.BackoffAfter(int.MaxValue));
    }

    [Fact]
    public async Task Messages_are_delivered_in_order_once_to_matching_consumers_and_the_cursor_moves_on()
    {
        var harness = new Harness(Defaults);
        harness.Source.Add(CompanyA, Message(CompanyA, 1), Message(CompanyA, 2));
        var consumer = harness.AddConsumer("finance");
        var other = harness.AddConsumer("other", new OutboxSubscriptionV1(SourceName, "sales.invoice.cancelled.v1", 1));
        var wrongVersion = harness.AddConsumer("v2", new OutboxSubscriptionV1(SourceName, EventType, 2));

        var first = await harness.RunAsync();
        var second = await harness.RunAsync();

        Assert.Equal(2, first.Delivered);
        Assert.Equal(0, second.Delivered);
        Assert.Equal([1, 2], consumer.Handled.Select(m => m.Position).Select(int.Parse).ToArray());
        Assert.Empty(other.Handled);
        Assert.Empty(wrongVersion.Handled);
        Assert.Equal("2", (await harness.Db(CompanyA).OutboxSourceCursors.SingleAsync()).Position);
        Assert.All(await harness.Db(CompanyA).OutboxDeliveries.ToListAsync(),
            row => Assert.Equal((int)WorkOutboxDeliveryStatus.Delivered, row.Status));
    }

    [Fact]
    public async Task A_message_the_source_sends_again_at_a_later_position_is_deduplicated_not_redelivered()
    {
        var harness = new Harness(Defaults);
        harness.Source.Add(CompanyA, Message(CompanyA, 1));
        var consumer = harness.AddConsumer("finance");
        await harness.RunAsync();

        harness.Source.Replace(CompanyA, Message(CompanyA, 1) with { Position = "5" });

        var replay = await harness.RunAsync();

        Assert.Equal(0, replay.Delivered);
        Assert.Equal(1, replay.Duplicates);
        Assert.Single(consumer.Handled);
        Assert.Equal("5", (await harness.Db(CompanyA).OutboxSourceCursors.SingleAsync()).Position);
    }

    [Fact]
    public async Task A_failing_consumer_is_retried_after_backoff_and_holds_later_messages_in_order()
    {
        var harness = new Harness(Defaults);
        harness.Source.Add(CompanyA, Message(CompanyA, 1), Message(CompanyA, 2));
        var consumer = harness.AddConsumer("finance");
        consumer.FailTimes = 1;

        var first = await harness.RunAsync();
        Assert.Equal(1, first.Retrying);
        Assert.Equal(0, first.Delivered);
        Assert.Empty(await harness.Db(CompanyA).OutboxSourceCursors.ToListAsync());
        var failing = await harness.Db(CompanyA).OutboxDeliveries.SingleAsync();
        Assert.Equal((int)WorkOutboxDeliveryStatus.Failing, failing.Status);
        Assert.Equal(WorkOutboxDispatcher.ConsumerExceptionCode, failing.LastErrorCode);
        Assert.Equal(harness.Time.GetUtcNow() + TimeSpan.FromSeconds(10), failing.NextAttemptAtUtc);

        harness.Time.Advance(TimeSpan.FromSeconds(9));
        var early = await harness.RunAsync();
        Assert.Equal(0, early.Delivered);
        Assert.Equal(1, consumer.Calls);

        harness.Time.Advance(TimeSpan.FromSeconds(1));
        var later = await harness.RunAsync();
        Assert.Equal(2, later.Delivered);
        Assert.Equal(3, consumer.Calls);
        Assert.Equal("2", (await harness.Db(CompanyA).OutboxSourceCursors.SingleAsync()).Position);
        var rows = (await harness.Db(CompanyA).OutboxDeliveries.ToListAsync()).OrderBy(r => r.AttemptCount).ToList();
        Assert.Equal([1, 2], rows.Select(r => r.AttemptCount).ToArray());
    }

    [Fact]
    public async Task A_message_that_keeps_failing_becomes_poison_after_the_configured_attempts_and_releases_the_cursor()
    {
        var harness = new Harness(Defaults);
        harness.Source.Add(CompanyA, Message(CompanyA, 1), Message(CompanyA, 2));
        var consumer = harness.AddConsumer("finance");
        consumer.FailOn.Add(Guid.Parse("00000000-0000-0000-0000-000000000001"));

        await harness.RunAsync();
        harness.Time.Advance(TimeSpan.FromSeconds(10));
        await harness.RunAsync();
        harness.Time.Advance(TimeSpan.FromSeconds(20));
        var last = await harness.RunAsync();

        Assert.Equal(1, last.Poisoned);
        Assert.Equal(1, last.Delivered);
        var rows = (await harness.Db(CompanyA).OutboxDeliveries.ToListAsync()).OrderBy(r => r.MessageId).ToList();
        Assert.Equal((int)WorkOutboxDeliveryStatus.Poisoned, rows[0].Status);
        Assert.Equal(3, rows[0].AttemptCount);
        Assert.Null(rows[0].NextAttemptAtUtc);
        Assert.Equal((int)WorkOutboxDeliveryStatus.Delivered, rows[1].Status);
        Assert.Equal("2", (await harness.Db(CompanyA).OutboxSourceCursors.SingleAsync()).Position);

        var calls = consumer.Calls;
        await harness.RunAsync();
        Assert.Equal(calls, consumer.Calls);
    }

    [Fact]
    public async Task A_permanent_refusal_is_poison_at_once_with_the_consumer_code()
    {
        var harness = new Harness(Defaults);
        harness.Source.Add(CompanyA, Message(CompanyA, 1));
        var consumer = harness.AddConsumer("finance");
        consumer.Permanent = true;

        var run = await harness.RunAsync();

        Assert.Equal(1, run.Poisoned);
        var row = await harness.Db(CompanyA).OutboxDeliveries.SingleAsync();
        Assert.Equal(1, row.AttemptCount);
        Assert.Equal("schema_unsupported", row.LastErrorCode);
        Assert.Equal("1", (await harness.Db(CompanyA).OutboxSourceCursors.SingleAsync()).Position);
    }

    [Fact]
    public async Task Each_company_is_walked_in_its_own_scope_and_a_foreign_company_envelope_is_never_delivered()
    {
        var harness = new Harness(Defaults);
        harness.Source.Add(CompanyA, Message(CompanyA, 1));
        harness.Source.Add(CompanyB, Message(CompanyB, 1));
        var consumer = harness.AddConsumer("finance");

        var run = await harness.RunAsync();

        Assert.Equal(2, run.Companies);
        Assert.Equal(2, run.Delivered);
        Assert.Equal(consumer.Handled.Select(m => m.CompanyId).ToArray(), consumer.SeenCompanies.ToArray());
        Assert.Equal(1, await harness.Db(CompanyA).OutboxDeliveries.CountAsync());
        Assert.Equal(1, await harness.Db(CompanyB).OutboxDeliveries.CountAsync());

        var leaky = new Harness(Defaults);
        leaky.Source.Add(CompanyA, Message(CompanyB, 1));
        var leakyConsumer = leaky.AddConsumer("finance");
        var leakyRun = await leaky.RunAsync();
        Assert.Equal(0, leakyRun.Delivered);
        Assert.Empty(leakyConsumer.Handled);
        Assert.Equal(1, leakyRun.InvalidEnvelopes);
    }

    [Fact]
    public async Task A_message_whose_payload_changed_after_a_failed_first_attempt_is_poisoned_without_calling_the_consumer_again()
    {
        var harness = new Harness(Defaults);
        harness.Source.Add(CompanyA, Message(CompanyA, 1));
        var consumer = harness.AddConsumer("finance");
        consumer.FailTimes = 1;
        await harness.RunAsync();

        harness.Source.Replace(CompanyA, Message(CompanyA, 1) with { PayloadJson = "{\"changed\":true}" });
        harness.Time.Advance(TimeSpan.FromSeconds(10));
        var run = await harness.RunAsync();

        Assert.Equal(1, run.Poisoned);
        Assert.Equal(1, consumer.Calls);
        var row = await harness.Db(CompanyA).OutboxDeliveries.SingleAsync();
        Assert.Equal(WorkOutboxDispatcher.PayloadMismatchCode, row.LastErrorCode);
        Assert.Equal((int)WorkOutboxDeliveryStatus.Poisoned, row.Status);
    }

    [Fact]
    public async Task Without_a_company_directory_nothing_is_read_or_delivered()
    {
        var harness = new Harness(Defaults, registerDirectory: false);
        harness.Source.Add(CompanyA, Message(CompanyA, 1));
        var consumer = harness.AddConsumer("finance");

        var run = await harness.RunAsync();

        Assert.Equal(new WorkOutboxDispatchSummary(0, 0, 0, 0, 0), run);
        Assert.Empty(consumer.Handled);
    }

    [Fact]
    public async Task An_invalid_envelope_is_skipped_with_an_alarm_and_the_cursor_moves_past_it_keeping_earlier_progress()
    {
        var harness = new Harness(Defaults);
        harness.Source.Add(CompanyA, Message(CompanyA, 1), Message(CompanyA, 2) with { EventType = new string('e', 161) }, Message(CompanyA, 3));
        var consumer = harness.AddConsumer("finance");

        var run = await harness.RunAsync();

        Assert.Equal(1, run.InvalidEnvelopes);
        Assert.Equal(2, run.Delivered);
        Assert.Equal([1, 3], consumer.Handled.Select(m => int.Parse(m.Position)).ToArray());
        Assert.Equal("3", (await harness.Db(CompanyA).OutboxSourceCursors.SingleAsync()).Position);
        Assert.Equal(0, (await harness.RunAsync()).InvalidEnvelopes);
    }

    [Fact]
    public async Task An_envelope_with_an_unusable_position_fails_the_source_instead_of_moving_the_cursor()
    {
        var harness = new Harness(Defaults);
        harness.Source.Add(CompanyA, Message(CompanyA, 1));
        harness.Source.RawPositions[CompanyA] = new string('p', 300);
        var consumer = harness.AddConsumer("finance");

        var run = await harness.RunAsync();

        Assert.Equal(0, run.Delivered);
        Assert.Empty(consumer.Handled);
        Assert.Empty(await harness.Db(CompanyA).OutboxSourceCursors.ToListAsync());
    }

    [Fact]
    public async Task A_consumer_name_longer_than_the_stored_limit_never_runs_and_writes_nothing()
    {
        var harness = new Harness(Defaults);
        harness.Source.Add(CompanyA, Message(CompanyA, 1));
        var consumer = harness.AddConsumer(new string('c', 129));

        var run = await harness.RunAsync();

        Assert.Equal(0, run.Delivered);
        Assert.Equal(0, consumer.Calls);
        Assert.Empty(await harness.Db(CompanyA).OutboxDeliveries.ToListAsync());
        Assert.Empty(await harness.Db(CompanyA).OutboxSourceCursors.ToListAsync());
    }

    [Fact]
    public async Task A_persistent_storage_error_does_not_repeat_the_handler_every_poll()
    {
        var harness = new Harness(Defaults);
        harness.Source.Add(CompanyA, Message(CompanyA, 1));
        var consumer = harness.AddConsumer("finance");
        harness.Saves.Failure = () => new DbUpdateException("disk full");

        await harness.RunAsync();
        await harness.RunAsync();
        await harness.RunAsync();
        Assert.Equal(1, consumer.Calls);

        harness.Time.Advance(TimeSpan.FromSeconds(10));
        await harness.RunAsync();
        Assert.Equal(2, consumer.Calls);

        harness.Time.Advance(TimeSpan.FromSeconds(10));
        await harness.RunAsync();
        Assert.Equal(2, consumer.Calls);

        harness.Saves.Failure = null;
        harness.Time.Advance(TimeSpan.FromSeconds(10));
        var recovered = await harness.RunAsync();
        Assert.Equal(1, recovered.Delivered);
        Assert.Equal("1", (await harness.Db(CompanyA).OutboxSourceCursors.SingleAsync()).Position);
    }

    [Fact]
    public async Task A_concurrency_conflict_holds_the_cursor_without_failing_the_source_and_keeps_earlier_progress()
    {
        var harness = new Harness(Defaults);
        harness.Source.Add(CompanyA, Message(CompanyA, 1), Message(CompanyA, 2));
        var consumer = harness.AddConsumer("finance");
        await harness.RunAsync();
        harness.Source.Add(CompanyA, Message(CompanyA, 3), Message(CompanyA, 4));
        var saves = 0;
        harness.Saves.Failure = () => ++saves == 2 ? new DbUpdateConcurrencyException("lost race") : null;

        var run = await harness.RunAsync();

        Assert.Equal(1, run.Delivered);
        Assert.Equal("3", (await harness.Db(CompanyA).OutboxSourceCursors.SingleAsync()).Position);
        harness.Saves.Failure = null;
        var next = await harness.RunAsync();
        Assert.Equal(1, next.Delivered);
        Assert.Equal("4", (await harness.Db(CompanyA).OutboxSourceCursors.SingleAsync()).Position);
        Assert.Equal(4, consumer.Handled.Select(m => m.Position).Distinct().Count());
    }

    [Fact]
    public void Only_unique_key_violations_and_concurrency_exceptions_count_as_conflicts()
    {
        Assert.True(WorkOutboxDispatcher.IsConflict(new DbUpdateConcurrencyException("race")));
        Assert.True(WorkOutboxDispatcher.IsConflict(new DbUpdateException("dup", new SqlException(2627))));
        Assert.True(WorkOutboxDispatcher.IsConflict(new DbUpdateException("dup", new SqlException(2601))));
        Assert.False(WorkOutboxDispatcher.IsConflict(new DbUpdateException("fk", new SqlException(547))));
        Assert.False(WorkOutboxDispatcher.IsConflict(new DbUpdateException("other")));
        Assert.False(WorkOutboxDispatcher.IsConflict(new InvalidOperationException("other")));
    }

    [Fact]
    public async Task Cancellation_while_a_handler_runs_propagates_and_records_nothing()
    {
        var harness = new Harness(Defaults);
        harness.Source.Add(CompanyA, Message(CompanyA, 1));
        var consumer = harness.AddConsumer("finance");
        using var cancellation = new CancellationTokenSource();
        consumer.OnHandle = () =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.RunAsync(cancellation.Token));

        Assert.Empty(await harness.Db(CompanyA).OutboxDeliveries.ToListAsync());
        Assert.Empty(await harness.Db(CompanyA).OutboxSourceCursors.ToListAsync());
    }

    private static OutboxMessageEnvelopeV1 Message(Guid companyId, int n) => new(
        SourceName,
        Guid.Parse($"00000000-0000-0000-0000-{n:D12}"),
        companyId,
        EventType,
        1,
        "{\"n\":" + n + "}",
        new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.Zero),
        "corr-" + n,
        null,
        n.ToString());

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            values.Select(v => KeyValuePair.Create($"{WorkOutboxDispatcherOptions.SectionName}:{v.Key}", (string?)v.Value))).Build();

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now += by;
    }

    private sealed class FakeSource : IOutboxSourceV1
    {
        private readonly Dictionary<Guid, List<OutboxMessageEnvelopeV1>> byCompany = [];
        public Func<Guid> ActiveCompany { get; set; } = () => Guid.Empty;
        public Dictionary<Guid, string> RawPositions { get; } = [];
        public string SourceName => WorkOutboxDispatcherTests.SourceName;

        public void Add(Guid company, params OutboxMessageEnvelopeV1[] messages)
        {
            if (!byCompany.TryGetValue(company, out var list))
            {
                byCompany[company] = list = [];
            }

            list.AddRange(messages);
        }

        public void Replace(Guid company, OutboxMessageEnvelopeV1 message) =>
            byCompany[company][byCompany[company].FindIndex(m => m.MessageId == message.MessageId)] = message;

        public Task<IReadOnlyList<OutboxMessageEnvelopeV1>> ReadAfterAsync(string? afterPosition, int maxCount, CancellationToken cancellationToken)
        {
            var after = afterPosition is null ? 0 : int.Parse(afterPosition);
            IReadOnlyList<OutboxMessageEnvelopeV1> page = byCompany.GetValueOrDefault(ActiveCompany(), [])
                .Where(m => int.Parse(m.Position) > after).OrderBy(m => int.Parse(m.Position)).Take(maxCount)
                .Select(m => RawPositions.TryGetValue(ActiveCompany(), out var raw) ? m with { Position = raw } : m).ToList();
            return Task.FromResult(page);
        }
    }

    private sealed class FakeConsumer(string name, IReadOnlyCollection<OutboxSubscriptionV1> subscriptions, Func<Guid> activeCompany)
        : IOutboxConsumerV1
    {
        public string ConsumerName => name;
        public IReadOnlyCollection<OutboxSubscriptionV1> Subscriptions => subscriptions;
        public List<OutboxMessageEnvelopeV1> Handled { get; } = [];
        public List<Guid> SeenCompanies { get; } = [];
        public HashSet<Guid> FailOn { get; } = [];
        public int FailTimes { get; set; }
        public bool Permanent { get; set; }
        public int Calls { get; private set; }
        public Action? OnHandle { get; set; }

        public Task HandleAsync(OutboxMessageEnvelopeV1 message, CancellationToken cancellationToken)
        {
            Calls++;
            OnHandle?.Invoke();
            if (Permanent)
            {
                throw new OutboxPermanentFailureException("schema_unsupported", "refused");
            }

            if (FailTimes > 0)
            {
                FailTimes--;
                throw new InvalidOperationException("transient");
            }

            if (FailOn.Contains(message.MessageId))
            {
                throw new InvalidOperationException("transient");
            }

            Handled.Add(message);
            SeenCompanies.Add(activeCompany());
            return Task.CompletedTask;
        }
    }

    private sealed class Directory(Guid[] companies) : IOutboxCompanyDirectoryV1
    {
        public Task<IReadOnlyList<Guid>> ListCompanyIdsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(companies);
    }

    private sealed class Harness
    {
        private readonly ServiceProvider provider;
        private readonly WorkOutboxDispatcher dispatcher;
        private readonly string database = "work-outbox-" + Guid.NewGuid().ToString("N");
        private readonly List<FakeConsumer> consumers = [];
        private Guid active;

        public Harness(WorkOutboxDispatcherOptions options, bool registerDirectory = true)
        {
            Time = new ManualTime();
            Source = new FakeSource { ActiveCompany = () => active };
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(Time);
            services.AddScoped<ICompanyContext>(sp =>
            {
                var context = new CompanyContext();
                return new TrackingCompanyContext(context, id => active = id);
            });
            services.AddSingleton(Saves);
            services.AddDbContext<WorkDbContext>((sp, o) => o.UseInMemoryDatabase(database).AddInterceptors(sp.GetRequiredService<FailingSaves>()));
            services.AddSingleton<IOutboxSourceV1>(Source);
            services.AddSingleton<IEnumerable<IOutboxConsumerV1>>(consumers);
            if (registerDirectory)
            {
                services.AddSingleton<IOutboxCompanyDirectoryV1>(new Directory([CompanyA, CompanyB]));
            }

            provider = services.BuildServiceProvider();
            dispatcher = new WorkOutboxDispatcher(
                provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(options), Time,
                NullLogger<WorkOutboxDispatcher>.Instance);
        }

        public ManualTime Time { get; }
        public FakeSource Source { get; }
        public FailingSaves Saves { get; } = new();

        public FakeConsumer AddConsumer(string name, params OutboxSubscriptionV1[] subscriptions)
        {
            var consumer = new FakeConsumer(
                name, subscriptions.Length == 0 ? [new OutboxSubscriptionV1(SourceName, EventType, 1)] : subscriptions, () => active);
            consumers.Add(consumer);
            return consumer;
        }

        public Task<WorkOutboxDispatchSummary> RunAsync(CancellationToken cancellationToken = default) =>
            dispatcher.RunOnceAsync(cancellationToken);

        public WorkDbContext Db(Guid companyId)
        {
            var company = new CompanyContext();
            company.Set(companyId);
            return new WorkDbContext(
                new DbContextOptionsBuilder<WorkDbContext>().UseInMemoryDatabase(database).Options, company);
        }
    }

    // Shaped like the provider's exception (type name and Number) so the classifier can be tested without a database.
    private sealed class SqlException(int number) : Exception("sql")
    {
        public int Number { get; } = number;
    }

    private sealed class FailingSaves : SaveChangesInterceptor
    {
        public Func<Exception?>? Failure { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var failure = Failure?.Invoke();
            return failure is null ? ValueTask.FromResult(result) : throw failure;
        }
    }

    private sealed class TrackingCompanyContext(CompanyContext inner, Action<Guid> onSet) : ICompanyContext
    {
        public bool IsResolved => inner.IsResolved;
        public Guid CompanyId => inner.CompanyId;

        public void Set(Guid companyId)
        {
            inner.Set(companyId);
            onSet(companyId);
        }
    }
}
