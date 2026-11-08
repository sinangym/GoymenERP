using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Work.Infrastructure;
using GoymenERP.Modules.Work.Infrastructure.Outbox;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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
        Assert.Empty(await leaky.Db(CompanyA).OutboxSourceCursors.ToListAsync());
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
                .Where(m => int.Parse(m.Position) > after).OrderBy(m => int.Parse(m.Position)).Take(maxCount).ToList();
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

        public Task HandleAsync(OutboxMessageEnvelopeV1 message, CancellationToken cancellationToken)
        {
            Calls++;
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
            services.AddDbContext<WorkDbContext>(o => o.UseInMemoryDatabase(database));
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

        public FakeConsumer AddConsumer(string name, params OutboxSubscriptionV1[] subscriptions)
        {
            var consumer = new FakeConsumer(
                name, subscriptions.Length == 0 ? [new OutboxSubscriptionV1(SourceName, EventType, 1)] : subscriptions, () => active);
            consumers.Add(consumer);
            return consumer;
        }

        public Task<WorkOutboxDispatchSummary> RunAsync() => dispatcher.RunOnceAsync(CancellationToken.None);

        public WorkDbContext Db(Guid companyId)
        {
            var company = new CompanyContext();
            company.Set(companyId);
            return new WorkDbContext(
                new DbContextOptionsBuilder<WorkDbContext>().UseInMemoryDatabase(database).Options, company);
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
