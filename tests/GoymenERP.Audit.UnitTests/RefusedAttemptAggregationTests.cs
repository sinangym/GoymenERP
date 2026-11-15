using System.Diagnostics.Metrics;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Audit.UnitTests;

/// <summary>
/// F-023 (F023-4; KIM-D11): the volume control of the separate short refused attempt writes. Shipped disabled, so
/// every refusal is its own row; once window and threshold are both set, the refusals above the threshold inside one
/// window become one append-only summary row.
/// </summary>
public sealed class RefusedAttemptAggregationTests
{
    private static readonly Guid CompanyA = Guid.Parse("0f5e3c1a-6b0d-4c8e-9a51-000000000a11");
    private static readonly Guid CompanyB = Guid.Parse("0f5e3c1a-6b0d-4c8e-9a51-000000000b12");
    private static readonly DateTimeOffset Start = new(2026, 11, 15, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    [Fact]
    public void Empty_window_and_threshold_disable_the_aggregation()
    {
        var options = RefusedAttemptAggregationOptions.FromConfiguration(Configuration(new Dictionary<string, string?>
        {
            ["Audit:RefusedAttempts:Aggregation:Window"] = "",
            ["Audit:RefusedAttempts:Aggregation:Threshold"] = " ",
        }));

        Assert.False(options.IsEnabled);
        Assert.Null(options.Window);
        Assert.Null(options.Threshold);
        Assert.False(RefusedAttemptAggregationOptions.FromConfiguration(
            Configuration(new Dictionary<string, string?>())).IsEnabled);
        Assert.False(RefusedAttemptAggregationOptions.Disabled.IsEnabled);
    }

    [Fact]
    public void Window_and_threshold_set_together_enable_the_aggregation()
    {
        var options = RefusedAttemptAggregationOptions.FromConfiguration(Configuration(new Dictionary<string, string?>
        {
            ["Audit:RefusedAttempts:Aggregation:Window"] = "00:05:00",
            ["Audit:RefusedAttempts:Aggregation:Threshold"] = "3",
        }));

        Assert.True(options.IsEnabled);
        Assert.Equal(Window, options.Window);
        Assert.Equal(3, options.Threshold);
    }

    [Theory]
    [InlineData("00:05:00", "")]
    [InlineData("", "3")]
    [InlineData("00:00:00", "3")]
    [InlineData("-00:01:00", "3")]
    [InlineData("00:05:00", "0")]
    [InlineData("00:05:00", "-1")]
    [InlineData("five minutes", "3")]
    [InlineData("00:05:00", "3.5")]
    public void A_half_set_non_positive_or_unreadable_aggregation_setting_fails_closed(string window, string threshold)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Audit:RefusedAttempts:Aggregation:Window"] = window,
            ["Audit:RefusedAttempts:Aggregation:Threshold"] = threshold,
        });

        Assert.Throws<InvalidOperationException>(() => RefusedAttemptAggregationOptions.FromConfiguration(configuration));
    }

    [Fact]
    public void The_shipped_aggregation_setting_is_empty_so_every_refusal_is_its_own_row()
    {
        using var shipped = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "src", "backend", "GoymenERP.Api", "appsettings.json")));
        var aggregation = shipped.RootElement
            .GetProperty("Audit").GetProperty("RefusedAttempts").GetProperty("Aggregation");
        var options = RefusedAttemptAggregationOptions.FromConfiguration(Configuration(new Dictionary<string, string?>
        {
            ["Audit:RefusedAttempts:Aggregation:Window"] = aggregation.GetProperty("Window").GetString(),
            ["Audit:RefusedAttempts:Aggregation:Threshold"] = aggregation.GetProperty("Threshold").GetString(),
        }));

        Assert.False(options.IsEnabled);
    }

    [Fact]
    public void A_disabled_aggregator_admits_every_refusal_and_holds_no_summary()
    {
        var time = new ManualTime(Start);
        var aggregator = new RefusedAttemptAggregator(RefusedAttemptAggregationOptions.Disabled, time);

        for (var index = 0; index < 20; index++)
        {
            Assert.True(aggregator.Admit(Attempt(CompanyA, "corr-" + index)));
        }

        time.Advance(TimeSpan.FromDays(1));
        Assert.Empty(aggregator.TakeEnded(CompanyA));
    }

    [Fact]
    public void Above_the_threshold_refusals_are_counted_and_become_one_summary_after_the_window_ends()
    {
        var time = new ManualTime(Start);
        var aggregator = new RefusedAttemptAggregator(new RefusedAttemptAggregationOptions(Window, 2), time);

        Assert.True(aggregator.Admit(Attempt(CompanyA, "corr-1")));
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.True(aggregator.Admit(Attempt(CompanyA, "corr-2")));
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.False(aggregator.Admit(Attempt(CompanyA, "corr-3")));
        Assert.False(aggregator.Admit(Attempt(CompanyA, "corr-4")));
        Assert.False(aggregator.Admit(Attempt(CompanyA, "corr-5")));

        Assert.Empty(aggregator.TakeEnded(CompanyA));

        time.Advance(Window);
        var summary = Assert.Single(aggregator.TakeEnded(CompanyA));
        Assert.Equal(3, summary.Count);
        Assert.Equal(Start, summary.WindowStartUtc);
        Assert.Equal(Start + Window, summary.WindowEndUtc);
        Assert.Equal("corr-3", summary.Template.CorrelationId);
        Assert.Empty(aggregator.TakeEnded(CompanyA));
    }

    [Fact]
    public void Each_company_actor_command_and_problem_has_its_own_window()
    {
        var time = new ManualTime(Start);
        var aggregator = new RefusedAttemptAggregator(new RefusedAttemptAggregationOptions(Window, 1), time);
        var first = Attempt(CompanyA, "corr-1");

        Assert.True(aggregator.Admit(first));
        Assert.True(aggregator.Admit(Attempt(CompanyB, "corr-2")));
        Assert.True(aggregator.Admit(first with { CorrelationId = "corr-3", ActorId = "actor-2" }));
        Assert.True(aggregator.Admit(first with
        {
            CorrelationId = "corr-4",
            CommandName = "purchasing.purchase-request.submit.v1",
        }));
        Assert.True(aggregator.Admit(first with { CorrelationId = "corr-5", ProblemCode = "validation_failed" }));
        Assert.False(aggregator.Admit(first with { CorrelationId = "corr-6" }));

        time.Advance(Window);
        Assert.Empty(aggregator.TakeEnded(CompanyB));
        var summary = Assert.Single(aggregator.TakeEnded(CompanyA));
        Assert.Equal(1, summary.Count);
        Assert.Equal(CompanyA, summary.Template.CompanyId);
        Assert.Equal("corr-6", summary.Template.CorrelationId);
    }

    [Fact]
    public void Ai_refusals_stay_outside_the_aggregation()
    {
        var time = new ManualTime(Start);
        var aggregator = new RefusedAttemptAggregator(new RefusedAttemptAggregationOptions(Window, 1), time);
        var aiActor = Attempt(CompanyA, "corr-1") with { ActorType = AuditActorTypes.Ai };
        var aiAssisted = Attempt(CompanyA, "corr-2") with { AiInvolvement = AuditAiInvolvement.Assisted };

        for (var index = 0; index < 5; index++)
        {
            Assert.True(aggregator.Admit(aiActor));
            Assert.True(aggregator.Admit(aiAssisted));
        }

        time.Advance(Window);
        Assert.Empty(aggregator.TakeEnded(CompanyA));
    }

    [Fact]
    public void A_window_that_stayed_within_the_threshold_writes_no_summary_and_a_new_window_starts_after_it()
    {
        var time = new ManualTime(Start);
        var aggregator = new RefusedAttemptAggregator(new RefusedAttemptAggregationOptions(Window, 2), time);

        Assert.True(aggregator.Admit(Attempt(CompanyA, "corr-1")));
        Assert.True(aggregator.Admit(Attempt(CompanyA, "corr-2")));
        time.Advance(Window);

        // The ended window is closed by the next refusal of the same key; it folded nothing, so no summary.
        Assert.True(aggregator.Admit(Attempt(CompanyA, "corr-3")));
        Assert.True(aggregator.Admit(Attempt(CompanyA, "corr-4")));
        Assert.False(aggregator.Admit(Attempt(CompanyA, "corr-5")));
        Assert.Empty(aggregator.TakeEnded(CompanyA));

        time.Advance(Window);
        var summary = Assert.Single(aggregator.TakeEnded(CompanyA));
        Assert.Equal(1, summary.Count);
        Assert.Equal(Start + Window, summary.WindowStartUtc);
        Assert.Equal(Start + Window + Window, summary.WindowEndUtc);
    }

    [Fact]
    public void An_ended_window_keeps_its_own_end_and_waits_for_its_own_company()
    {
        var time = new ManualTime(Start);
        var aggregator = new RefusedAttemptAggregator(new RefusedAttemptAggregationOptions(Window, 1), time);

        Assert.True(aggregator.Admit(Attempt(CompanyA, "corr-1")));
        Assert.False(aggregator.Admit(Attempt(CompanyA, "corr-2")));
        Assert.True(aggregator.Admit(Attempt(CompanyB, "corr-3")));
        time.Advance(Window + TimeSpan.FromMinutes(1));
        Assert.True(aggregator.Admit(Attempt(CompanyA, "corr-4")));
        Assert.False(aggregator.Admit(Attempt(CompanyA, "corr-5")));
        time.Advance(TimeSpan.FromMinutes(1));

        // Company B's request never takes company A's summary; B's own window folded nothing.
        Assert.Empty(aggregator.TakeEnded(CompanyB));
        var first = Assert.Single(aggregator.TakeEnded(CompanyA));
        Assert.Equal(Start, first.WindowStartUtc);
        Assert.Equal(Start + Window, first.WindowEndUtc);
        Assert.Equal("corr-2", first.Template.CorrelationId);

        time.Advance(Window);
        var second = Assert.Single(aggregator.TakeEnded(CompanyA));
        Assert.Equal(Start + Window + TimeSpan.FromMinutes(1), second.WindowStartUtc);
        Assert.Equal(Start + Window + Window + TimeSpan.FromMinutes(1), second.WindowEndUtc);
        Assert.Equal("corr-5", second.Template.CorrelationId);
    }

    [Fact]
    public async Task With_aggregation_disabled_the_recorder_writes_every_standalone_refusal_as_its_own_row()
    {
        using var services = CreateServices(RefusedAttemptAggregationOptions.Disabled, new ManualTime(Start));
        await using (var scope = services.CreateAsyncScope())
        {
            var recorder = Resolve(scope, CompanyA);
            for (var index = 0; index < 4; index++)
            {
                var result = await recorder.RecordStandaloneAsync(Attempt(CompanyA, "corr-" + index));
                Assert.True(result.Recorded);
                Assert.False(result.IsAggregated);
            }
        }

        Assert.Equal(4, (await ReadRows(services, CompanyA)).Count);
    }

    [Fact]
    public async Task With_aggregation_enabled_the_folded_refusals_become_one_hash_verifiable_summary_row()
    {
        var time = new ManualTime(Start);
        using var services = CreateServices(new RefusedAttemptAggregationOptions(Window, 1), time);
        await using (var scope = services.CreateAsyncScope())
        {
            var recorder = Resolve(scope, CompanyA);

            var first = await recorder.RecordStandaloneAsync(Attempt(CompanyA, "corr-1"));
            var second = await recorder.RecordStandaloneAsync(Attempt(CompanyA, "corr-2"));
            var third = await recorder.RecordStandaloneAsync(Attempt(CompanyA, "corr-3"));

            Assert.True(first.Recorded);
            Assert.False(first.IsAggregated);
            Assert.True(second.Recorded);
            Assert.True(second.IsAggregated);
            Assert.Null(second.FailureKind);
            Assert.True(third.IsAggregated);
        }

        Assert.Single(await ReadRows(services, CompanyA));

        // Another company's refusal after the window end does not write company A's summary.
        time.Advance(Window);
        await using (var scope = services.CreateAsyncScope())
        {
            var result = await Resolve(scope, CompanyB).RecordStandaloneAsync(Attempt(CompanyB, "corr-b1"));
            Assert.False(result.IsAggregated);
        }

        Assert.Single(await ReadRows(services, CompanyA));
        Assert.Single(await ReadRows(services, CompanyB));

        // Company A's next refusal opens a new window (its own row) and appends the ended window's summary.
        await using (var scope = services.CreateAsyncScope())
        {
            var result = await Resolve(scope, CompanyA).RecordStandaloneAsync(Attempt(CompanyA, "corr-4"));
            Assert.False(result.IsAggregated);
        }

        var rows = await ReadRows(services, CompanyA);
        Assert.Equal(3, rows.Count);
        var summary = Assert.Single(rows, row =>
            row.CorrelationId.StartsWith(RefusedAttemptAuditDraftMapper.AggregationSummaryCorrelationPrefix, StringComparison.Ordinal));
        Assert.Equal(CompanyA, summary.CompanyId);
        Assert.Equal(AuditOutcomes.Refused, summary.Outcome);
        Assert.Equal("purchasing.purchase-request.approve.v1", summary.EventType);
        Assert.Equal("actor-1", summary.ActorId);
        Assert.Equal(RefusedAttemptV1.NoTarget, summary.EntityType);
        Assert.Equal(RefusedAttemptV1.NoTarget, summary.EntityId);
        Assert.Null(summary.EntityVersionBefore);
        Assert.Equal("corr-2", summary.CausationId);
        Assert.Equal("{}", summary.BeforeJson);
        Assert.Null(summary.ClientIpAddress);
        Assert.Null(summary.UserAgent);
        Assert.Equal(AuditEventIntegrity.Compute(summary.ToIntegrityEnvelope()), summary.IntegrityHash);

        using var after = JsonDocument.Parse(summary.AfterJson);
        Assert.Equal(
            new[] { "count", "problemCode", "refusalClass", "windowEndUtc", "windowStartUtc" },
            after.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(2, after.RootElement.GetProperty("count").GetInt64());
        Assert.Equal(RefusedAttemptClassesV1.BusinessRule, after.RootElement.GetProperty("refusalClass").GetString());
        Assert.Equal("approval_state_changed", after.RootElement.GetProperty("problemCode").GetString());
        Assert.Equal(Start, after.RootElement.GetProperty("windowStartUtc").GetDateTimeOffset());
        Assert.Equal(Start + Window, after.RootElement.GetProperty("windowEndUtc").GetDateTimeOffset());

        Assert.DoesNotContain(await ReadRows(services, CompanyB), row =>
            row.CorrelationId.StartsWith(RefusedAttemptAuditDraftMapper.AggregationSummaryCorrelationPrefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_summary_that_cannot_be_written_is_counted_as_lost_and_never_throws()
    {
        long lost = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == RefusedAttemptMetrics.MeterName
                && instrument.Name == RefusedAttemptMetrics.AggregationLostCounterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == RefusedAttemptMetrics.RefusalClassTag
                    && Equals(tag.Value, RefusedAttemptClassesV1.BusinessRule))
                {
                    Interlocked.Add(ref lost, measurement);
                }
            }
        });
        listener.Start();

        // No Audit context in this provider: the summary append fails like an unavailable store.
        var collection = new ServiceCollection();
        collection.AddScoped<ICompanyContext, CompanyContext>();
        using var broken = collection.BuildServiceProvider(validateScopes: true);
        var summary = new RefusedAttemptAggregationSummary(Attempt(CompanyA, "corr-1"), 7, Start, Start + Window);

        var written = await RefusedAttemptRecorder.AppendAggregationSummaryAsync(
            broken.GetRequiredService<IServiceScopeFactory>(),
            new RefusedAttemptStandaloneWriteOptions(TimeSpan.FromSeconds(5)),
            summary,
            CancellationToken.None);

        Assert.False(written);
        Assert.Equal(7, Interlocked.Read(ref lost));
    }

    [Fact]
    public void The_volume_counters_have_their_published_names()
    {
        Assert.Equal("audit_refused_attempt_standalone_observed", RefusedAttemptMetrics.StandaloneObservedCounterName);
        Assert.Equal("audit_refused_attempt_aggregated", RefusedAttemptMetrics.AggregatedCounterName);
        Assert.Equal("audit_refused_attempt_aggregation_lost", RefusedAttemptMetrics.AggregationLostCounterName);
        Assert.Equal("Audit:RefusedAttempts:Aggregation", RefusedAttemptAggregationOptions.SectionName);
    }

    private static RefusedAttemptV1 Attempt(Guid companyId, string correlationId) => new()
    {
        CompanyId = companyId,
        CommandName = "purchasing.purchase-request.approve.v1",
        Operation = "Approve",
        TargetType = "Purchasing.PurchaseRequest",
        TargetId = "4b0c2f5e-1f3a-4f7e-8d61-0000000000c3",
        ExpectedVersion = 7,
        ActorType = AuditActorTypes.User,
        ActorId = "actor-1",
        PermissionCode = "purchasing.requests.approve",
        RoleCodes = ["role-a"],
        HttpStatus = 409,
        ProblemCode = "approval_state_changed",
        RefusalClass = RefusedAttemptClassesV1.BusinessRule,
        CorrelationId = correlationId,
        SourceChannel = SourceChannels.Web,
        IdempotencyKey = "idem-" + correlationId,
    };

    private static ServiceProvider CreateServices(RefusedAttemptAggregationOptions aggregation, TimeProvider time)
    {
        var databaseName = "refused-attempt-aggregation-" + Guid.NewGuid().ToString("N");
        var databaseRoot = new InMemoryDatabaseRoot();
        var collection = new ServiceCollection();
        collection.AddScoped<ICompanyContext, CompanyContext>();
        collection.AddDbContext<AuditDbContext>(options => options
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .EnableServiceProviderCaching(false));
        collection.AddScoped<AuditEventCollector>();
        collection.AddScoped<IAuditEventCollector>(provider => provider.GetRequiredService<AuditEventCollector>());
        collection.AddSingleton(new RefusedAttemptStandaloneWriteOptions(TimeSpan.FromSeconds(5)));
        collection.AddSingleton(aggregation);
        collection.AddSingleton(time);
        collection.AddSingleton<RefusedAttemptAggregator>();
        collection.AddScoped<IRefusedAttemptRecorderV1, RefusedAttemptRecorder>();
        return collection.BuildServiceProvider(validateScopes: true);
    }

    private static IRefusedAttemptRecorderV1 Resolve(AsyncServiceScope scope, Guid companyId)
    {
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
        return scope.ServiceProvider.GetRequiredService<IRefusedAttemptRecorderV1>();
    }

    private static async Task<List<AuditEvent>> ReadRows(ServiceProvider services, Guid companyId)
    {
        await using var read = services.CreateAsyncScope();
        read.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
        return await read.ServiceProvider.GetRequiredService<AuditDbContext>().AuditEvents.ToListAsync();
    }

    private static IConfiguration Configuration(IDictionary<string, string?> values) =>
        new DictionarySection(string.Empty, new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase));

    /// <summary>A flat key/value configuration; the options read only <c>GetSection(name)[key]</c>.</summary>
    private sealed class DictionarySection(string path, IReadOnlyDictionary<string, string?> values) : IConfigurationSection
    {
        public string? this[string key]
        {
            get => values.TryGetValue(Combine(key), out var value) ? value : null;
            set => throw new NotSupportedException();
        }

        public string Key => path.Split(':')[^1];

        public string Path => path;

        public string? Value
        {
            get => values.TryGetValue(path, out var value) ? value : null;
            set => throw new NotSupportedException();
        }

        public IConfigurationSection GetSection(string key) => new DictionarySection(Combine(key), values);

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotSupportedException();

        private string Combine(string key) => path.Length == 0 ? key : path + ":" + key;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GoymenERP.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("GoymenERP repository root was not found.");
    }

    /// <summary>A hand-moved clock: the aggregation windows are counted on <see cref="TimeProvider"/> only.</summary>
    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }
}
