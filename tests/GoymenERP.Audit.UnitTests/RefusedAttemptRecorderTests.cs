using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

/// <summary>F-023 (F023-1): the refused attempt contract, its restricted draft, the recorder and its settings.</summary>
public sealed class RefusedAttemptRecorderTests
{
    private static readonly Guid CompanyA = Guid.Parse("0f5e3c1a-6b0d-4c8e-9a51-000000000a01");
    private static readonly Guid CompanyB = Guid.Parse("0f5e3c1a-6b0d-4c8e-9a51-000000000b02");

    [Fact]
    public void The_attempt_contract_carries_only_the_spec_fields_and_no_free_text_or_payload()
    {
        Assert.Equal(
            new[]
            {
                "ActorId", "ActorType", "AiContext", "AiInvolvement", "CausationId", "CommandName", "CompanyId",
                "CorrelationId", "ExpectedVersion", "FieldErrors", "HttpStatus", "IdempotencyKey", "Operation",
                "PermissionCode", "ProblemCode", "RefusalClass", "RoleCodes", "SourceChannel", "TargetId", "TargetType",
            },
            typeof(RefusedAttemptV1).GetProperties()
                .Select(property => property.Name)
                .Where(name => name != "EqualityContract")
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.Equal(
            new[] { "Code", "Field" },
            typeof(RefusedAttemptFieldErrorV1).GetProperties()
                .Select(property => property.Name)
                .Where(name => name != "EqualityContract")
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void Record_leaves_one_restricted_refused_draft_on_the_collector_for_the_modules_own_save()
    {
        var (recorder, collector, _) = CreateRecorder(CompanyA);

        recorder.Record(Attempt(CompanyA) with
        {
            FieldErrors = [new RefusedAttemptFieldErrorV1("lines[0].quantity", "max_length")],
            CausationId = "cause-1",
        });

        var draft = Assert.Single(collector.Pending);
        Assert.Equal(CompanyA, draft.CompanyId);
        Assert.Equal(AuditOutcomes.Refused, draft.Outcome);
        Assert.Equal("purchasing.purchase-request.approve.v1", draft.EventType);
        Assert.Equal("Purchasing.PurchaseRequest", draft.EntityType);
        Assert.Equal("4b0c2f5e-1f3a-4f7e-8d61-0000000000c3", draft.EntityId);
        Assert.Equal("Approve", draft.Operation);
        Assert.Equal(7, draft.EntityVersionBefore);
        Assert.Null(draft.EntityVersionAfter);
        Assert.Equal(AuditActorTypes.User, draft.ActorType);
        Assert.Equal("purchasing.requests.approve", draft.PermissionCode);
        Assert.Equal(new[] { "role-a", "role-b" }, draft.RoleCodes);
        Assert.Equal("{}", draft.BeforeJson);
        Assert.Null(draft.ApprovalContextJson);
        Assert.Null(draft.ClientIpAddress);
        Assert.Null(draft.UserAgent);
        Assert.Equal(AuditAiInvolvement.None, draft.AiInvolvement);
        Assert.Null(draft.AiContextJson);
        Assert.Equal("corr-1", draft.CorrelationId);
        Assert.Equal("cause-1", draft.CausationId);
        Assert.Equal("idem-1", draft.IdempotencyKey);
        Assert.Equal(RefusedAttemptReasonTextsV1.For(RefusedAttemptClassesV1.BusinessRule), draft.Reason);

        using var after = JsonDocument.Parse(draft.AfterJson);
        Assert.Equal(
            new[] { "fieldErrors", "httpStatus", "problemCode", "refusalClass" },
            after.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(409, after.RootElement.GetProperty("httpStatus").GetInt32());
        Assert.Equal("approval_state_changed", after.RootElement.GetProperty("problemCode").GetString());
        Assert.Equal(RefusedAttemptClassesV1.BusinessRule, after.RootElement.GetProperty("refusalClass").GetString());
        var fieldError = Assert.Single(after.RootElement.GetProperty("fieldErrors").EnumerateArray().ToArray());
        Assert.Equal(
            new[] { "code", "field" },
            fieldError.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("lines[0].quantity", fieldError.GetProperty("field").GetString());
        Assert.Equal("max_length", fieldError.GetProperty("code").GetString());
    }

    [Fact]
    public void Record_refuses_an_attempt_of_another_company_or_without_a_resolved_company()
    {
        var (recorder, collector, _) = CreateRecorder(CompanyA);
        Assert.Throws<InvalidOperationException>(() => recorder.Record(Attempt(CompanyB)));

        var (unresolved, unresolvedCollector, _) = CreateRecorder(companyId: null);
        Assert.Throws<InvalidOperationException>(() => unresolved.Record(Attempt(CompanyA)));

        Assert.Empty(collector.Pending);
        Assert.Empty(unresolvedCollector.Pending);
    }

    [Fact]
    public void A_refusal_without_an_idempotency_key_takes_the_technical_digest_of_its_correlation()
    {
        var (recorder, collector, _) = CreateRecorder(CompanyA);

        recorder.Record(Attempt(CompanyA) with { IdempotencyKey = null });

        Assert.Equal("refused-attempt:no-idempotency-key:corr-1", Assert.Single(collector.Pending).IdempotencyKey);
    }

    [Fact]
    public void The_factory_accepts_the_refused_outcome_and_keeps_it_in_the_integrity_hash()
    {
        var auditEvent = AuditEventFactory.Create(RefusedAttemptAuditDraftMapper.ToDraft(Attempt(CompanyA)));

        Assert.Equal(AuditOutcomes.Refused, auditEvent.Outcome);
        Assert.Equal(AuditEvent.CompanySubject, auditEvent.SubjectKind);
        Assert.Equal(AuditEventIntegrity.Compute(auditEvent.ToIntegrityEnvelope()), auditEvent.IntegrityHash);
        Assert.Equal(Sha256Hex("idem-1"), auditEvent.IdempotencyKeyHash, ignoreCase: true);
        Assert.Throws<ArgumentOutOfRangeException>(() => AuditEventFactory.Create(
            RefusedAttemptAuditDraftMapper.ToDraft(Attempt(CompanyA)) with { Outcome = "Failed" }));
    }

    public static TheoryData<string> InvalidAttempts => new()
    {
        "status-401",
        "status-403",
        "status-500",
        "status-302",
        "problem-with-space",
        "actor-with-slash",
        "target-with-space",
        "command-without-version",
        "unknown-class",
        "unknown-actor-type",
        "unknown-source-channel",
        "negative-version",
        "empty-company",
        "empty-role",
        "field-with-value-text",
        "ai-involvement-without-context",
    };

    [Theory]
    [MemberData(nameof(InvalidAttempts))]
    public void An_attempt_outside_the_closed_shape_is_refused_by_the_mapper(string invalid)
    {
        var attempt = Invalid(invalid);

        Assert.ThrowsAny<ArgumentException>(() => RefusedAttemptAuditDraftMapper.ToDraft(attempt));
    }

    [Theory]
    [MemberData(nameof(InvalidAttempts))]
    public async Task The_standalone_path_returns_invalid_attempt_without_throwing(string invalid)
    {
        using var services = CreateServices();
        await using var scope = services.CreateAsyncScope();
        var recorder = Resolve(scope, CompanyA);

        var result = await recorder.RecordStandaloneAsync(Invalid(invalid));

        Assert.False(result.Recorded);
        Assert.Equal(RefusedAttemptRecordFailureKindsV1.InvalidAttempt, result.FailureKind);
    }

    [Fact]
    public void Too_many_field_errors_are_refused()
    {
        var errors = Enumerable.Range(0, RefusedAttemptAuditDraftMapper.MaxFieldErrors + 1)
            .Select(index => new RefusedAttemptFieldErrorV1("field" + index.ToString(CultureInfo.InvariantCulture), "required"))
            .ToArray();

        Assert.ThrowsAny<ArgumentException>(() =>
            RefusedAttemptAuditDraftMapper.ToDraft(Attempt(CompanyA) with { FieldErrors = errors }));
    }

    [Fact]
    public async Task The_standalone_path_writes_one_hash_verifiable_row_in_a_fresh_scope_of_the_same_company()
    {
        using var services = CreateServices();
        await using (var scope = services.CreateAsyncScope())
        {
            var recorder = Resolve(scope, CompanyA);

            var result = await recorder.RecordStandaloneAsync(Attempt(CompanyA));

            Assert.True(result.Recorded);
            Assert.Null(result.FailureKind);
            // The request scope's own audit collector stays untouched: the standalone row is already written.
            Assert.Empty(scope.ServiceProvider.GetRequiredService<AuditEventCollector>().Pending);
        }

        await using (var read = services.CreateAsyncScope())
        {
            read.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            var rows = await read.ServiceProvider.GetRequiredService<AuditDbContext>().AuditEvents.ToListAsync();
            var row = Assert.Single(rows);
            Assert.Equal(CompanyA, row.CompanyId);
            Assert.Equal(AuditOutcomes.Refused, row.Outcome);
            Assert.Equal("{}", row.BeforeJson);
            Assert.Null(row.ClientIpAddress);
            Assert.Null(row.UserAgent);
            Assert.Equal(AuditEventIntegrity.Compute(row.ToIntegrityEnvelope()), row.IntegrityHash);
        }

        await using (var other = services.CreateAsyncScope())
        {
            other.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyB);
            Assert.Empty(await other.ServiceProvider.GetRequiredService<AuditDbContext>().AuditEvents.ToListAsync());
        }
    }

    [Fact]
    public async Task The_standalone_path_refuses_another_company_without_writing_or_throwing()
    {
        using var services = CreateServices();
        await using (var scope = services.CreateAsyncScope())
        {
            var result = await Resolve(scope, CompanyA).RecordStandaloneAsync(Attempt(CompanyB));

            Assert.False(result.Recorded);
            Assert.Equal(RefusedAttemptRecordFailureKindsV1.CompanyContextMismatch, result.FailureKind);
        }

        await using (var unresolved = services.CreateAsyncScope())
        {
            var result = await Resolve(unresolved, companyId: null).RecordStandaloneAsync(Attempt(CompanyA));

            Assert.False(result.Recorded);
            Assert.Equal(RefusedAttemptRecordFailureKindsV1.CompanyContextMismatch, result.FailureKind);
        }

        await using var read = services.CreateAsyncScope();
        read.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyB);
        Assert.Empty(await read.ServiceProvider.GetRequiredService<AuditDbContext>().AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task A_store_failure_returns_store_unavailable_and_never_throws_the_refusal_away()
    {
        // No Audit context is registered: the fresh scope cannot reach the store.
        var collection = new ServiceCollection();
        collection.AddScoped<ICompanyContext, CompanyContext>();
        collection.AddScoped<AuditEventCollector>();
        collection.AddScoped<IAuditEventCollector>(provider => provider.GetRequiredService<AuditEventCollector>());
        collection.AddSingleton(new RefusedAttemptStandaloneWriteOptions(TimeSpan.FromSeconds(5)));
        using var services = collection.BuildServiceProvider(validateScopes: true);
        await using var scope = services.CreateAsyncScope();
        var recorder = Resolve(scope, CompanyA);

        var result = await recorder.RecordStandaloneAsync(Attempt(CompanyA));

        Assert.False(result.Recorded);
        Assert.Equal(RefusedAttemptRecordFailureKindsV1.StoreUnavailable, result.FailureKind);
    }

    [Fact]
    public async Task The_callers_cancellation_is_not_swallowed()
    {
        using var services = CreateServices();
        await using var scope = services.CreateAsyncScope();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Resolve(scope, CompanyA).RecordStandaloneAsync(Attempt(CompanyA), cancellation.Token));
    }

    [Fact]
    public async Task The_standalone_append_refuses_a_succeeded_or_foreign_row()
    {
        using var services = CreateServices();
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        var dbContext = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var succeeded = AuditEventFactory.Create(
            RefusedAttemptAuditDraftMapper.ToDraft(Attempt(CompanyA)) with { Outcome = AuditOutcomes.Succeeded });
        var foreign = AuditEventFactory.Create(RefusedAttemptAuditDraftMapper.ToDraft(Attempt(CompanyB)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dbContext.AppendRefusedAttemptAsync(succeeded, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dbContext.AppendRefusedAttemptAsync(foreign, CancellationToken.None));
        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    [Fact]
    public void Every_refusal_class_has_a_fixed_turkish_reason_text_and_an_unknown_class_has_none()
    {
        var reasons = RefusedAttemptClassesV1.All.Select(RefusedAttemptReasonTextsV1.For).ToArray();

        Assert.All(reasons, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
        Assert.Equal(reasons.Length, reasons.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("reddedildi", reasons[0], StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => RefusedAttemptReasonTextsV1.For("Unknown"));
    }

    [Fact]
    public void A_not_written_result_names_only_a_closed_failure_kind()
    {
        Assert.All(RefusedAttemptRecordFailureKindsV1.All, kind =>
            Assert.Equal(kind, RefusedAttemptRecordResultV1.NotWritten(kind).FailureKind));
        Assert.Throws<ArgumentOutOfRangeException>(() => RefusedAttemptRecordResultV1.NotWritten("other"));
    }

    [Fact]
    public void The_retention_is_read_from_settings_two_years_with_automatic_disposal_off()
    {
        var options = RefusedAttemptRetentionOptions.FromConfiguration(Configuration(new Dictionary<string, string?>
        {
            ["Audit:RefusedAttempts:Retention:Period"] = "P2Y",
            ["Audit:RefusedAttempts:Retention:PolicyRef"] = "policy-ref-1",
            ["Audit:RefusedAttempts:Retention:AutoDisposalEnabled"] = "false",
        }));

        Assert.Equal(new RefusedAttemptRetentionPeriod(2, 0, 0), options.Period);
        Assert.Equal("P2Y", options.Period.ToString());
        Assert.Equal("policy-ref-1", options.PolicyRef);
        Assert.False(options.AutoDisposalEnabled);
        var written = new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);
        Assert.Equal(written.AddYears(2), options.Period.RetainUntil(written));
        Assert.False(options.IsExpired(written, written.AddYears(2).AddTicks(-1)));
        Assert.True(options.IsExpired(written, written.AddYears(2)));
    }

    [Fact]
    public void The_shipped_settings_bind_and_carry_the_kim_d8_start_values()
    {
        using var shipped = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "src", "backend", "GoymenERP.Api", "appsettings.json")));
        var refusedAttempts = shipped.RootElement.GetProperty("Audit").GetProperty("RefusedAttempts");
        var retentionSection = refusedAttempts.GetProperty("Retention");
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Audit:RefusedAttempts:Retention:Period"] = retentionSection.GetProperty("Period").GetString(),
            ["Audit:RefusedAttempts:Retention:PolicyRef"] = retentionSection.GetProperty("PolicyRef").GetString(),
            ["Audit:RefusedAttempts:Retention:AutoDisposalEnabled"] =
                retentionSection.GetProperty("AutoDisposalEnabled").GetBoolean() ? "true" : "false",
            ["Audit:RefusedAttempts:StandaloneWrite:Timeout"] =
                refusedAttempts.GetProperty("StandaloneWrite").GetProperty("Timeout").GetString(),
        });

        var retention = RefusedAttemptRetentionOptions.FromConfiguration(configuration);
        var standalone = RefusedAttemptStandaloneWriteOptions.FromConfiguration(configuration);

        Assert.Equal(new RefusedAttemptRetentionPeriod(2, 0, 0), retention.Period);
        Assert.False(retention.AutoDisposalEnabled);
        Assert.True(standalone.Timeout > TimeSpan.Zero);
    }

    [Theory]
    [InlineData(null, "policy-ref-1", null)]
    [InlineData("", "policy-ref-1", null)]
    [InlineData("2", "policy-ref-1", null)]
    [InlineData("P0Y", "policy-ref-1", null)]
    [InlineData("P", "policy-ref-1", null)]
    [InlineData("PT2H", "policy-ref-1", null)]
    [InlineData("-P2Y", "policy-ref-1", null)]
    [InlineData("P2Y", null, null)]
    [InlineData("P2Y", " ", null)]
    [InlineData("P2Y", "policy-ref-1", "true")]
    [InlineData("P2Y", "policy-ref-1", "yes")]
    public void A_missing_or_unsafe_retention_setting_fails_closed(string? period, string? policyRef, string? autoDisposal)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Audit:RefusedAttempts:Retention:Period"] = period,
            ["Audit:RefusedAttempts:Retention:PolicyRef"] = policyRef,
            ["Audit:RefusedAttempts:Retention:AutoDisposalEnabled"] = autoDisposal,
        });

        Assert.Throws<InvalidOperationException>(() => RefusedAttemptRetentionOptions.FromConfiguration(configuration));
    }

    [Theory]
    [InlineData("P1Y6M", 1, 6, 0)]
    [InlineData("P30D", 0, 0, 30)]
    [InlineData("P3Y2M1D", 3, 2, 1)]
    public void The_retention_period_is_an_iso_8601_date_duration(string text, int years, int months, int days)
    {
        Assert.True(RefusedAttemptRetentionPeriod.TryParse(text, out var period));
        Assert.Equal(new RefusedAttemptRetentionPeriod(years, months, days), period);
        Assert.Equal(text, period.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("00:00:00")]
    [InlineData("-00:00:02")]
    public void A_missing_or_non_positive_standalone_timeout_fails_closed(string? timeout)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Audit:RefusedAttempts:StandaloneWrite:Timeout"] = timeout,
        });

        Assert.Throws<InvalidOperationException>(() => RefusedAttemptStandaloneWriteOptions.FromConfiguration(configuration));
    }

    [Fact]
    public void The_alarm_metrics_are_named_and_tagged_only_by_closed_values()
    {
        Assert.Equal("audit_refused_attempt_write_failed", RefusedAttemptMetrics.WriteFailedCounterName);
        Assert.Equal("audit_refused_attempt_replayed", RefusedAttemptMetrics.ReplayedCounterName);
        Assert.Equal("unknown", RefusedAttemptMetrics.UnknownRefusalClass);
    }

    private static RefusedAttemptV1 Attempt(Guid companyId) => new()
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
        RoleCodes = ["role-b", "role-a", "role-b"],
        HttpStatus = 409,
        ProblemCode = "approval_state_changed",
        RefusalClass = RefusedAttemptClassesV1.BusinessRule,
        CorrelationId = "corr-1",
        SourceChannel = SourceChannels.Web,
        IdempotencyKey = "idem-1",
    };

    private static RefusedAttemptV1 Invalid(string invalid)
    {
        var attempt = Attempt(CompanyA);
        return invalid switch
        {
            "status-401" => attempt with { HttpStatus = 401 },
            "status-403" => attempt with { HttpStatus = 403 },
            "status-500" => attempt with { HttpStatus = 500 },
            "status-302" => attempt with { HttpStatus = 302 },
            "problem-with-space" => attempt with { ProblemCode = "approval state changed" },
            "actor-with-slash" => attempt with { ActorId = "actor/1" },
            "target-with-space" => attempt with { TargetId = "free text" },
            "command-without-version" => attempt with { CommandName = "purchasing.purchase-request.approve" },
            "unknown-class" => attempt with { RefusalClass = "Forbidden" },
            "unknown-actor-type" => attempt with { ActorType = "Robot" },
            "unknown-source-channel" => attempt with { SourceChannel = "Fax" },
            "negative-version" => attempt with { ExpectedVersion = -1 },
            "empty-company" => attempt with { CompanyId = Guid.Empty },
            "empty-role" => attempt with { RoleCodes = [" "] },
            "field-with-value-text" => attempt with
            {
                FieldErrors = [new RefusedAttemptFieldErrorV1("quantity = 12", "max_length")],
            },
            "ai-involvement-without-context" => attempt with { AiInvolvement = AuditAiInvolvement.Assisted },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid), invalid, null),
        };
    }

    private static (RefusedAttemptRecorder Recorder, AuditEventCollector Collector, ICompanyContext Company) CreateRecorder(
        Guid? companyId)
    {
        var company = new CompanyContext();
        if (companyId.HasValue)
        {
            company.Set(companyId.Value);
        }

        var collector = new AuditEventCollector();
        var recorder = new RefusedAttemptRecorder(
            collector,
            company,
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new RefusedAttemptStandaloneWriteOptions(TimeSpan.FromSeconds(5)));
        return (recorder, collector, company);
    }

    private static ServiceProvider CreateServices()
    {
        var databaseName = "refused-attempts-" + Guid.NewGuid().ToString("N");
        // Without provider caching each context builds its own internal provider; the shared root keeps every scope
        // (the request scope, the standalone write scope and the read scope) on the same in-memory store.
        var databaseRoot = new InMemoryDatabaseRoot();
        var collection = new ServiceCollection();
        collection.AddScoped<ICompanyContext, CompanyContext>();
        collection.AddDbContext<AuditDbContext>(options => options
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .EnableServiceProviderCaching(false));
        collection.AddScoped<AuditEventCollector>();
        collection.AddScoped<IAuditEventCollector>(provider => provider.GetRequiredService<AuditEventCollector>());
        collection.AddSingleton(new RefusedAttemptStandaloneWriteOptions(TimeSpan.FromSeconds(5)));
        collection.AddScoped<IRefusedAttemptRecorderV1, RefusedAttemptRecorder>();
        return collection.BuildServiceProvider(validateScopes: true);
    }

    private static IRefusedAttemptRecorderV1 Resolve(AsyncServiceScope scope, Guid? companyId)
    {
        if (companyId.HasValue)
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId.Value);
        }

        return scope.ServiceProvider.GetService<IRefusedAttemptRecorderV1>()
            ?? new RefusedAttemptRecorder(
                scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(),
                scope.ServiceProvider.GetRequiredService<ICompanyContext>(),
                scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                scope.ServiceProvider.GetRequiredService<RefusedAttemptStandaloneWriteOptions>());
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

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

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
}
