using System.Text.Json;
using GoymenERP.Modules.Idempotency.Domain.Retention;
using GoymenERP.Modules.Idempotency.Infrastructure;
using GoymenERP.Modules.Idempotency.Infrastructure.Retention;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace GoymenERP.Idempotency.UnitTests;

/// <summary>OPEN-021 / NS-12: idempotency record retention read from settings and the coded, default-off purge job.</summary>
public sealed class IdempotencyRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 10, 30, 0, TimeSpan.Zero);
    private static readonly Guid CompanyA = Guid.Parse("7d0c3f8e-4b1a-4f36-9a51-2a6f0e1b9c01");
    private static readonly Guid CompanyB = Guid.Parse("7d0c3f8e-4b1a-4f36-9a51-2a6f0e1b9c02");

    [Theory]
    [InlineData("P90D", 0, 0, 90, "P90D")]
    [InlineData(" P1Y2M3D ", 1, 2, 3, "P1Y2M3D")]
    [InlineData("P6M", 0, 6, 0, "P6M")]
    public void The_period_parses_iso_8601_date_periods_and_prints_them_canonically(
        string text, int years, int months, int days, string canonical)
    {
        Assert.True(IdempotencyRetentionPeriod.TryParse(text, out var period));

        Assert.Equal((years, months, days), (period.Years, period.Months, period.Days));
        Assert.True(period.IsDefined);
        Assert.Equal(canonical, period.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("P")]
    [InlineData("P0D")]
    [InlineData("90")]
    [InlineData("P90")]
    [InlineData("PT1H")]
    [InlineData("P-1D")]
    [InlineData("p90d")]
    [InlineData("P1W")]
    public void The_period_refuses_empty_zero_time_negative_and_malformed_values(string? text)
    {
        Assert.False(IdempotencyRetentionPeriod.TryParse(text, out var period));
        Assert.False(period.IsDefined);
        Assert.Throws<InvalidOperationException>(() => period.CutoffAt(Now));
    }

    [Fact]
    public void The_cutoff_is_now_minus_the_period_in_utc_and_only_strictly_older_records_are_expired()
    {
        Assert.True(IdempotencyRetentionPeriod.TryParse("P90D", out var period));
        var localNow = Now.ToOffset(TimeSpan.FromHours(3));

        var cutoff = period.CutoffAt(localNow);

        Assert.Equal(Now.AddDays(-90), cutoff);
        Assert.Equal(TimeSpan.Zero, cutoff.Offset);
        Assert.True(period.IsExpired(cutoff.AddTicks(-1), Now));
        Assert.False(period.IsExpired(cutoff, Now));
        Assert.False(period.IsExpired(cutoff.AddTicks(1), Now));
    }

    [Fact]
    public void The_api_settings_start_with_the_decided_ninety_day_period_and_the_purge_job_off()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "backend", "GoymenERP.Api", "appsettings.json")));
        var retention = document.RootElement.GetProperty("Idempotency").GetProperty("Retention");

        Assert.Equal("P90D", retention.GetProperty("Period").GetString());
        Assert.False(string.IsNullOrWhiteSpace(retention.GetProperty("PolicyRef").GetString()));
        Assert.False(retention.GetProperty("Purge").GetProperty("Enabled").GetBoolean());
    }

    [Fact]
    public void Settings_with_the_purge_off_are_read_without_purge_values_and_cannot_be_resolved_for_a_job()
    {
        var options = IdempotencyRetentionOptions.FromConfiguration(Configuration(new Dictionary<string, string?>
        {
            ["Idempotency:Retention:Period"] = "P90D",
            ["Idempotency:Retention:PolicyRef"] = "policy-ref-1",
        }));

        Assert.Equal("P90D", options.Period?.ToString());
        Assert.Equal("policy-ref-1", options.PolicyRef);
        Assert.False(options.PurgeEnabled);
        Assert.Throws<InvalidOperationException>(() => options.ResolvePurge());
    }

    [Fact]
    public void An_enabled_purge_resolves_every_configured_value()
    {
        var settings = IdempotencyRetentionOptions.FromConfiguration(Configuration(EnabledValues())).ResolvePurge();

        Assert.Equal("P90D", settings.Period.ToString());
        Assert.Equal("policy-ref-1", settings.PolicyRef);
        Assert.Equal(250, settings.BatchSize);
        Assert.Equal(TimeSpan.FromHours(6), settings.PollInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.CommandTimeout);
    }

    [Theory]
    [InlineData("Idempotency:Retention:Period")]
    [InlineData("Idempotency:Retention:PolicyRef")]
    [InlineData("Idempotency:Retention:Purge:BatchSize")]
    [InlineData("Idempotency:Retention:Purge:PollInterval")]
    [InlineData("Idempotency:Retention:Purge:CommandTimeout")]
    public void An_enabled_purge_with_a_missing_value_fails_closed_and_names_it(string missingKey)
    {
        var values = EnabledValues();
        values.Remove(missingKey);
        var options = IdempotencyRetentionOptions.FromConfiguration(Configuration(values));

        var refused = Assert.Throws<InvalidOperationException>(() => options.ResolvePurge());

        Assert.Contains(missingKey, refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Idempotency:Retention:Period", "90 gün")]
    [InlineData("Idempotency:Retention:Period", "P0D")]
    [InlineData("Idempotency:Retention:Purge:Enabled", "evet")]
    [InlineData("Idempotency:Retention:Purge:BatchSize", "0")]
    [InlineData("Idempotency:Retention:Purge:BatchSize", "-5")]
    [InlineData("Idempotency:Retention:Purge:PollInterval", "00:00:00")]
    [InlineData("Idempotency:Retention:Purge:CommandTimeout", "-00:00:05")]
    public void A_present_but_malformed_value_is_refused_at_start_up(string key, string value)
    {
        var values = EnabledValues();
        values[key] = value;

        Assert.Throws<InvalidOperationException>(() => IdempotencyRetentionOptions.FromConfiguration(Configuration(values)));
    }

    [Fact]
    public void A_policy_reference_longer_than_the_evidence_column_is_refused()
    {
        var values = EnabledValues();
        values["Idempotency:Retention:PolicyRef"] = new string('R', IdempotencyRetentionPurgeRecord.PolicyRefMaxLength + 1);

        Assert.Throws<InvalidOperationException>(() => IdempotencyRetentionOptions.FromConfiguration(Configuration(values)));
    }

    [Fact]
    public void The_module_registers_no_purge_job_while_the_purge_is_off()
    {
        var services = new ServiceCollection();

        services.AddIdempotencyModule(Configuration(new Dictionary<string, string?>
        {
            ["DB_CONNECTION_STRING"] = "Server=unused.invalid;Database=unused;Connect Timeout=1;",
            ["Idempotency:Retention:Period"] = "P90D",
            ["Idempotency:Retention:PolicyRef"] = "policy-ref-1",
            ["Idempotency:Retention:Purge:Enabled"] = "false",
        }));

        Assert.DoesNotContain(services, service => service.ServiceType == typeof(IHostedService));
        Assert.DoesNotContain(services, service => service.ServiceType == typeof(IdempotencyRetentionPurgeJob));
        Assert.DoesNotContain(services, service => service.ServiceType == typeof(IIdempotencyRetentionPurgeStore));
    }

    [Fact]
    public void The_module_registers_the_purge_job_only_when_enabled_and_refuses_an_enabled_job_without_its_values()
    {
        var values = EnabledValues();
        values["DB_CONNECTION_STRING"] = "Server=unused.invalid;Database=unused;Connect Timeout=1;";
        var services = new ServiceCollection();

        services.AddIdempotencyModule(Configuration(values));

        Assert.Single(services, service => service.ServiceType == typeof(IHostedService));
        Assert.Single(services, service => service.ServiceType == typeof(IdempotencyRetentionPurgeJob));
        Assert.Single(services, service => service.ServiceType == typeof(IIdempotencyRetentionPurgeStore));

        values.Remove("Idempotency:Retention:Purge:BatchSize");
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddIdempotencyModule(Configuration(values)));
    }

    [Fact]
    public async Task A_cycle_drains_each_company_in_batches_with_one_cutoff_and_one_run_id()
    {
        var store = new RecordingStore(new Dictionary<Guid, int> { [CompanyA] = 5, [CompanyB] = 2 });
        var job = Job(store, batchSize: 2);

        var summary = await job.RunOnceAsync(CancellationToken.None);

        Assert.Equal(Now.AddDays(-90), summary.CutoffUtc);
        Assert.Equal(2, summary.PurgedCompanies);
        Assert.Equal(7, summary.PurgedRecords);
        Assert.Equal([Now.AddDays(-90)], store.ListedCutoffs);
        // Company A: 2 + 2 + 1 (the short batch ends it); company B: 2 + 0 (an exact batch needs one more look).
        Assert.Equal([CompanyA, CompanyA, CompanyA, CompanyB, CompanyB], store.Batches.Select(batch => batch.CompanyId));
        Assert.All(store.Batches, batch =>
        {
            Assert.Equal(summary.RunId, batch.RunId);
            Assert.Equal(summary.CutoffUtc, batch.CutoffUtc);
            Assert.Equal("P90D", batch.Period.ToString());
            Assert.Equal("policy-ref-1", batch.PolicyRef);
            Assert.True(batch.CutoffUtc < batch.RecordedAtUtc);
        });
        Assert.Equal(0, store.Remaining(CompanyA));
        Assert.Equal(0, store.Remaining(CompanyB));
    }

    [Fact]
    public async Task A_cycle_without_expired_records_deletes_nothing()
    {
        var store = new RecordingStore(new Dictionary<Guid, int>());

        var summary = await Job(store, batchSize: 10).RunOnceAsync(CancellationToken.None);

        Assert.Equal((0, 0L), (summary.PurgedCompanies, summary.PurgedRecords));
        Assert.Empty(store.Batches);
    }

    [Fact]
    public async Task A_store_that_reports_more_than_the_batch_stops_the_cycle()
    {
        var store = new RecordingStore(new Dictionary<Guid, int> { [CompanyA] = 3 }) { OverReport = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Job(store, batchSize: 2).RunOnceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_cancelled_cycle_stops_before_the_next_batch()
    {
        var store = new RecordingStore(new Dictionary<Guid, int> { [CompanyA] = 5 });
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Job(store, batchSize: 2).RunOnceAsync(cancellation.Token));
        Assert.Empty(store.Batches);
    }

    [Fact]
    public void The_evidence_record_refuses_an_empty_company_a_zero_count_a_local_time_and_a_cutoff_after_the_record()
    {
        Assert.True(IdempotencyRetentionPeriod.TryParse("P90D", out var period));
        var cutoff = period.CutoffAt(Now);

        var record = IdempotencyRetentionPurgeRecord.Record(
            Guid.NewGuid(), Guid.NewGuid(), CompanyA, cutoff, period, " policy-ref-1 ", 3, Now);
        Assert.Equal(("P90D", "policy-ref-1", 3), (record.RetentionPeriod, record.PolicyRef, record.DeletedCount));

        Assert.Throws<ArgumentException>(() => IdempotencyRetentionPurgeRecord.Record(
            Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, cutoff, period, "policy-ref-1", 3, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => IdempotencyRetentionPurgeRecord.Record(
            Guid.NewGuid(), Guid.NewGuid(), CompanyA, cutoff, period, "policy-ref-1", 0, Now));
        Assert.Throws<ArgumentException>(() => IdempotencyRetentionPurgeRecord.Record(
            Guid.NewGuid(), Guid.NewGuid(), CompanyA, cutoff.ToOffset(TimeSpan.FromHours(3)), period, "policy-ref-1", 3, Now));
        Assert.Throws<ArgumentException>(() => IdempotencyRetentionPurgeRecord.Record(
            Guid.NewGuid(), Guid.NewGuid(), CompanyA, Now, period, "policy-ref-1", 3, Now));
        Assert.Throws<ArgumentException>(() => IdempotencyRetentionPurgeRecord.Record(
            Guid.NewGuid(), Guid.NewGuid(), CompanyA, cutoff, default, "policy-ref-1", 3, Now));
        Assert.Throws<ArgumentException>(() => IdempotencyRetentionPurgeRecord.Record(
            Guid.NewGuid(), Guid.NewGuid(), CompanyA, cutoff, period, " ", 3, Now));
    }

    private static IdempotencyRetentionPurgeJob Job(IIdempotencyRetentionPurgeStore store, int batchSize)
    {
        Assert.True(IdempotencyRetentionPeriod.TryParse("P90D", out var period));
        return new IdempotencyRetentionPurgeJob(
            store,
            new IdempotencyRetentionPurgeSettings(period, "policy-ref-1", batchSize, TimeSpan.FromHours(6), TimeSpan.FromSeconds(30)),
            new FixedTime(Now),
            NullLogger<IdempotencyRetentionPurgeJob>.Instance);
    }

    private static Dictionary<string, string?> EnabledValues() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Idempotency:Retention:Period"] = "P90D",
        ["Idempotency:Retention:PolicyRef"] = "policy-ref-1",
        ["Idempotency:Retention:Purge:Enabled"] = "true",
        ["Idempotency:Retention:Purge:BatchSize"] = "250",
        ["Idempotency:Retention:Purge:PollInterval"] = "06:00:00",
        ["Idempotency:Retention:Purge:CommandTimeout"] = "00:00:30",
    };

    private static IConfiguration Configuration(IDictionary<string, string?> values) =>
        new DictionarySection(string.Empty, new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase));

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

    private sealed class RecordingStore(Dictionary<Guid, int> expired) : IIdempotencyRetentionPurgeStore
    {
        public List<DateTimeOffset> ListedCutoffs { get; } = [];

        public List<IdempotencyRetentionPurgeBatch> Batches { get; } = [];

        public bool OverReport { get; init; }

        public int Remaining(Guid companyId) => expired.GetValueOrDefault(companyId);

        public Task<IReadOnlyList<Guid>> ListCompaniesWithExpiredRecordsAsync(
            DateTimeOffset cutoffUtc,
            CancellationToken cancellationToken)
        {
            ListedCutoffs.Add(cutoffUtc);
            return Task.FromResult<IReadOnlyList<Guid>>(expired.Where(x => x.Value > 0).Select(x => x.Key).ToList());
        }

        public Task<int> PurgeBatchAsync(
            IdempotencyRetentionPurgeBatch evidence,
            int batchSize,
            CancellationToken cancellationToken)
        {
            Batches.Add(evidence);
            var deleted = Math.Min(batchSize, expired.GetValueOrDefault(evidence.CompanyId));
            expired[evidence.CompanyId] = expired.GetValueOrDefault(evidence.CompanyId) - deleted;
            return Task.FromResult(OverReport ? batchSize + 1 : deleted);
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>A flat key/value configuration; the options read only <c>GetSection(name)[key]</c> and <c>[key]</c>.</summary>
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
}
