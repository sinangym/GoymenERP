using System.Text.Json;
using GoymenERP.Api.Ai;
using GoymenERP.Api.Ai.SecurityWatch;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Ai;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Reading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP-C10 <c>security.tech-watch.summary.v1</c> with no model, network or database: the WP16 dependency scan report is
/// read through its strict file contract (the SENTETIK sample in <c>contracts/security/samples</c>), the company's
/// high-severity security events come only from the SecurityEvents owner digest with actors masked, an unauthorized
/// caller or another company's row releases nothing, and the emergency step list is always empty and never executable.
/// </summary>
public sealed class AiSecurityTechWatchToolTests
{
    private const string SamplePath = "contracts/security/samples/dependency-scan-report-v1.sample.json";
    private const string SchemaPath = "contracts/security/dependency-scan-report-v1.schema.json";
    private const string EmergencySchemaPath = "contracts/security/emergency-step-list-v1.schema.json";
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000c10e1");
    private static readonly Guid OtherCompany = Guid.Parse("bbbbbbbb-0000-4000-8000-0000000c10e2");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);
    private const string ActorOne = "11111111-1111-4111-8111-0000000c10a1";
    private const string ActorTwo = "11111111-1111-4111-8111-0000000c10a2";

    [Fact]
    public async Task The_sample_scan_report_is_counted_by_severity_and_listed_most_severe_first()
    {
        var query = new FakeQuery();
        var result = await Tool(query, SampleReport()).ReadAsync(new AiToolInputV1("{}"), Context(Company));

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var document = JsonDocument.Parse(result.ContentJson!);
        var dependencies = document.RootElement.GetProperty("dependencies");
        Assert.Equal("ok", dependencies.GetProperty("status").GetString());
        Assert.Equal("0000000000000000000000000000000000000001", dependencies.GetProperty("sourceCommit").GetString());
        Assert.Equal(3, dependencies.GetProperty("findingCount").GetInt32());
        Assert.Equal(
            new[] { ("critical", 1), ("high", 0), ("moderate", 1), ("low", 1) },
            dependencies.GetProperty("severityCounts").EnumerateArray()
                .Select(item => (item.GetProperty("severity").GetString()!, item.GetProperty("count").GetInt32())).ToArray());
        Assert.Equal(new[] { "SENTETIK-0002", "SENTETIK-0001", "SENTETIK-0003" },
            dependencies.GetProperty("findings").EnumerateArray().Select(item => item.GetProperty("advisoryId").GetString()).ToArray());
        Assert.Equal(JsonValueKind.Null, dependencies.GetProperty("findings")[0].GetProperty("fixedVersion").ValueKind);
        Assert.Equal(AiDataClassV1.Confidential, result.Source!.DataClass);
        Assert.Equal(Company, result.Source.TargetId);
    }

    [Fact]
    public async Task The_emergency_step_list_is_schema_only_always_empty_and_never_executable()
    {
        foreach (var scan in new[] { SampleReport(), DependencyScanReadResultV1.NotConfigured, DependencyScanReadResultV1.Invalid })
        {
            var result = await Tool(new FakeQuery(), scan).ReadAsync(new AiToolInputV1("{}"), Context(Company));

            using var document = JsonDocument.Parse(result.ContentJson!);
            var steps = document.RootElement.GetProperty("emergencySteps");
            Assert.Equal(SecurityTechWatchAiTool.EmergencyStepsStatus, steps.GetProperty("status").GetString());
            Assert.Equal("AI-D18", steps.GetProperty("decisionRef").GetString());
            Assert.False(steps.GetProperty("executable").GetBoolean());
            Assert.Equal(0, steps.GetProperty("steps").GetArrayLength());
        }

        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRepositoryRoot(), EmergencySchemaPath)));
        var properties = schema.RootElement.GetProperty("properties");
        Assert.Equal(0, properties.GetProperty("steps").GetProperty("maxItems").GetInt32());
        Assert.False(properties.GetProperty("executable").GetProperty("const").GetBoolean());
        Assert.Equal(SecurityTechWatchAiTool.EmergencyStepsStatus, properties.GetProperty("status").GetProperty("const").GetString());
        // The element schema names exactly the record's fields.
        Assert.Equal(
            typeof(SecurityEmergencyStepV1).GetProperties().Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name))
                .Order(StringComparer.Ordinal),
            properties.GetProperty("steps").GetProperty("items").GetProperty("properties").EnumerateObject()
                .Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Without_a_configured_report_the_summary_says_not_configured_and_lists_no_finding()
    {
        var result = await Tool(new FakeQuery(), DependencyScanReadResultV1.NotConfigured)
            .ReadAsync(new AiToolInputV1("{}"), Context(Company));

        using var document = JsonDocument.Parse(result.ContentJson!);
        var dependencies = document.RootElement.GetProperty("dependencies");
        Assert.Equal("not-configured", dependencies.GetProperty("status").GetString());
        Assert.Equal(0, dependencies.GetProperty("findingCount").GetInt32());
        Assert.Equal(0, dependencies.GetProperty("findings").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, dependencies.GetProperty("generatedAtUtc").ValueKind);
    }

    [Theory]
    [InlineData("\"format\": \"goymen.security.dependency-scan-report.v1\"", "\"format\": \"goymen.security.dependency-scan-report.v2\"")]
    [InlineData("\"severity\": \"critical\"", "\"severity\": \"urgent\"")]
    [InlineData("\"ecosystem\": \"pip\"", "\"ecosystem\": \"maven\"")]
    [InlineData("\"sourceCommit\": \"0000000000000000000000000000000000000001\"", "\"sourceCommit\": \"HEAD\"")]
    [InlineData("\"generatedAtUtc\": \"2026-11-22T06:00:00Z\"", "\"generatedAtUtc\": \"2026-11-22T09:00:00+03:00\"")]
    [InlineData("\"title\": \"SENTETIK kritik bulgu\"", "\"title\": \"SENTETIK\\u0007bulgu\"")]
    [InlineData("\"title\": \"SENTETIK kritik bulgu\"", "\"title\": \"SENTETIK kritik bulgu\", \"exploit\": \"x\"")]
    [InlineData("\"scanners\": [", "\"scanners\": [], \"unused\": [")]
    public void A_report_that_breaks_the_file_contract_is_invalid_and_releases_no_finding(string original, string replacement)
    {
        var json = File.ReadAllText(Path.Combine(FindRepositoryRoot(), SamplePath));
        Assert.Contains(original, json, StringComparison.Ordinal);

        var result = DependencyScanReportV1.Parse(json.Replace(original, replacement, StringComparison.Ordinal));

        Assert.Equal(DependencyScanReadStatusV1.Invalid, result.Status);
        Assert.Null(result.Report);
    }

    [Fact]
    public void The_reader_closed_lists_match_the_schema_file()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRepositoryRoot(), SchemaPath)));
        var finding = schema.RootElement.GetProperty("properties").GetProperty("findings").GetProperty("items").GetProperty("properties");
        Assert.Equal(DependencyScanReportV1.Ecosystems,
            finding.GetProperty("ecosystem").GetProperty("enum").EnumerateArray().Select(value => value.GetString()!));
        Assert.Equal(DependencyScanReportV1.Severities,
            finding.GetProperty("severity").GetProperty("enum").EnumerateArray().Select(value => value.GetString()!));
        Assert.Equal(DependencyScanReportV1.Format,
            schema.RootElement.GetProperty("properties").GetProperty("format").GetProperty("const").GetString());
        Assert.Equal(DependencyScanReportV1.MaximumFindings,
            schema.RootElement.GetProperty("properties").GetProperty("findings").GetProperty("maxItems").GetInt32());
    }

    [Fact]
    public async Task An_unauthorized_caller_is_denied_before_the_report_is_read()
    {
        var query = new FakeQuery { Status = SecurityEventQueryStatusV1.Denied };
        var source = new CountingSource(SampleReport());

        var result = await new SecurityTechWatchAiTool(source, new SecurityEventDigestReader(query), new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context(Company));

        Assert.Equal(AiToolOutcomeV1.Denied, result.Outcome);
        Assert.Null(result.ContentJson);
        Assert.Equal(0, source.Reads);
    }

    [Fact]
    public async Task A_security_event_of_another_company_releases_nothing()
    {
        var query = new FakeQuery();
        query.Add(Row(Company, SecurityEventTypesV1.PermissionChanged, ActorOne, 0, SecurityEventSeveritiesV1.High));
        query.Add(Row(OtherCompany, SecurityEventTypesV1.PermissionChanged, ActorTwo, 1, SecurityEventSeveritiesV1.High));

        var result = await Tool(query, SampleReport()).ReadAsync(new AiToolInputV1("{}"), Context(Company));

        Assert.Equal(AiToolOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.ContentJson);
    }

    [Fact]
    public async Task Only_high_severity_events_are_listed_and_their_actors_are_masked()
    {
        var query = new FakeQuery();
        query.Add(Row(Company, SecurityEventTypesV1.PermissionChanged, ActorOne, 0, SecurityEventSeveritiesV1.High));
        query.Add(Row(Company, SecurityEventTypesV1.AuthorizationDenied, ActorTwo, 1, SecurityEventSeveritiesV1.High, occurrences: 5));
        query.Add(Row(Company, SecurityEventTypesV1.AuthorizationDenied, ActorTwo, 2, SecurityEventSeveritiesV1.Warning));

        var result = await Tool(query, SampleReport()).ReadAsync(new AiToolInputV1("{\"limit\":10}"), Context(Company));

        var json = result.ContentJson!;
        Assert.DoesNotContain(ActorOne, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ActorTwo, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("corr-sentetik", json, StringComparison.Ordinal);
        Assert.DoesNotContain("hash-sentetik", json, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);
        var events = document.RootElement.GetProperty("securityEvents");
        Assert.Equal(6, events.GetProperty("highSeverityOccurrenceCount").GetInt32());
        Assert.Equal(2, events.GetProperty("highSeverityRowCount").GetInt32());
        Assert.Equal(new[] { "Kişi 1", "Kişi 2" },
            events.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("actor").GetString()).ToArray());
        Assert.All(events.GetProperty("items").EnumerateArray(),
            item => Assert.Equal(SecurityEventSeveritiesV1.High, item.GetProperty("severity").GetString()));
    }

    [Theory]
    [InlineData("{\"companyId\":\"aaaaaaaa-0000-4000-8000-0000000c10e1\"}")]
    [InlineData("{\"limit\":0}")]
    [InlineData("{\"limit\":51}")]
    [InlineData("{\"step\":\"block-logins\"}")]
    [InlineData("[]")]
    public async Task Malformed_or_unscoped_arguments_are_invalid_and_nothing_is_read(string arguments)
    {
        var query = new FakeQuery();
        var source = new CountingSource(SampleReport());

        var result = await new SecurityTechWatchAiTool(source, new SecurityEventDigestReader(query), new FixedTime(Now))
            .ReadAsync(new AiToolInputV1(arguments), Context(Company));

        Assert.Equal(AiToolOutcomeV1.Invalid, result.Outcome);
        Assert.Empty(query.RequestedTypes);
        Assert.Equal(0, source.Reads);
    }

    [Fact]
    public void The_configured_source_is_not_configured_by_default_missing_without_a_file_and_invalid_when_oversized()
    {
        var root = Path.Combine(Path.GetTempPath(), $"goymen-c10-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            Assert.Equal(DependencyScanReadStatusV1.NotConfigured, Source(root, null).Read().Status);
            Assert.Equal(DependencyScanReadStatusV1.Missing, Source(root, "scan.json").Read().Status);

            File.Copy(Path.Combine(FindRepositoryRoot(), SamplePath), Path.Combine(root, "scan.json"));
            var ok = Source(root, "scan.json").Read();
            Assert.Equal(DependencyScanReadStatusV1.Ok, ok.Status);
            Assert.Equal(3, ok.Report!.Findings.Count);

            File.WriteAllText(Path.Combine(root, "big.json"), new string(' ', DependencyScanReportV1.MaximumBytes + 1));
            Assert.Equal(DependencyScanReadStatusV1.Invalid, Source(root, "big.json").Read().Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_host_registers_the_tech_watch_tool_but_admits_nothing_without_an_allowlist()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Enabled"] = "false",
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddScoped<ISecurityEventQueryServiceV1, FakeQuery>();
        services.AddScoped<SecurityEventDigestReader>();
        services.AddAiRuntime(configuration);
        services.AddAiSecurityWatchTools();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var tools = scope.ServiceProvider.GetServices<IAiOwnerToolV1>().ToArray();
        var techWatch = Assert.Single(tools, tool => tool.Descriptor.ToolId == SecurityTechWatchAiTool.ToolId);
        Assert.Equal(AiToolRiskClassV1.Read, techWatch.Descriptor.Risk);
        Assert.Equal(new[] { ErpPermissions.PlatformSecurityEventsRead }, techWatch.Descriptor.RequiredPermissions);
        Assert.Equal(DependencyScanReadStatusV1.NotConfigured,
            scope.ServiceProvider.GetRequiredService<IDependencyScanReportSourceV1>().Read().Status);
        Assert.Empty(scope.ServiceProvider.GetRequiredService<AiToolCatalog>().Descriptors);
    }

    [Fact]
    public void The_shipped_configuration_keeps_the_ai_runtime_off_with_an_empty_allowlist_and_no_report_path()
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "src/backend/GoymenERP.Api/appsettings.json")));
        var ai = settings.RootElement.GetProperty("Ai");
        Assert.False(ai.GetProperty("Enabled").GetBoolean());
        Assert.Equal(0, ai.GetProperty("AllowedTools").GetArrayLength());
        Assert.False(ai.TryGetProperty("SecurityWatch", out _));
    }

    private static SecurityTechWatchAiTool Tool(FakeQuery query, DependencyScanReadResultV1 scan) =>
        new(new FixedDependencyScanReportSource(scan), new SecurityEventDigestReader(query), new FixedTime(Now));

    private static DependencyScanReadResultV1 SampleReport()
    {
        var result = DependencyScanReportV1.Parse(File.ReadAllText(Path.Combine(FindRepositoryRoot(), SamplePath)));
        Assert.Equal(DependencyScanReadStatusV1.Ok, result.Status);
        return result;
    }

    private static ConfiguredDependencyScanReportSource Source(string contentRoot, string? configuredPath) => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ConfiguredDependencyScanReportSource.PathKey] = configuredPath,
        }).Build(),
        new TestHostEnvironment { ContentRootPath = contentRoot });

    private static AiServerContextV1 Context(Guid company) => new(company, ActorOne, "v1.session-sentetik",
        $"company:{company:D}", "roles:sentetik", "corr-ai-c10-sentetik", null, null, null);

    private static SecurityEventItemV1 Row(Guid company, string eventType, string actorId, int minute, string severity,
        int occurrences = 1) => new(
        Guid.NewGuid(), 1, Now.AddMinutes(-60 + minute), company, null, SecurityEventActorTypesV1.User, actorId, eventType,
        SecurityEventPhasesV1.Single, SecurityEventOutcomesV1.Denied, SecurityEventReasonCodesV1.PermissionMissing, severity,
        "purchasing.requests.approve", null, null, $"corr-sentetik-{minute}", null, SourceChannels.Web, null, null, null, null,
        Now.AddYears(2), occurrences, null, null, $"hash-sentetik-{minute}");

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SamplePath)))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("GöymenERP repository kökü bulunamadı.");
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CountingSource(DependencyScanReadResultV1 result) : IDependencyScanReportSourceV1
    {
        public int Reads { get; private set; }

        public DependencyScanReadResultV1 Read()
        {
            Reads++;
            return result;
        }
    }

    /// <summary>A SENTETIK owner read: newest first, one page per type, the status it is told to answer.</summary>
    private sealed class FakeQuery : ISecurityEventQueryServiceV1
    {
        private readonly List<SecurityEventItemV1> rows = [];

        public SecurityEventQueryStatusV1 Status { get; init; } = SecurityEventQueryStatusV1.Ok;

        public List<string> RequestedTypes { get; } = [];

        public void Add(SecurityEventItemV1 row) => rows.Add(row);

        public Task<SecurityEventQueryResultV1> GetEventsAsync(int pageSize, string? cursor, string? eventType,
            string? correlationId, CancellationToken cancellationToken = default)
        {
            RequestedTypes.Add(eventType!);
            if (Status != SecurityEventQueryStatusV1.Ok)
            {
                return Task.FromResult(new SecurityEventQueryResultV1(Status, null));
            }

            var matching = rows.Where(row => row.EventType == eventType)
                .OrderByDescending(row => row.OccurredAtUtc).ThenByDescending(row => row.Id).ToArray();
            return Task.FromResult(SecurityEventQueryResultV1.Ok(
                new SecurityEventPageV1([.. matching.Take(pageSize)], pageSize, matching.Length > pageSize ? "next" : null)));
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "GoymenERP.Api.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
