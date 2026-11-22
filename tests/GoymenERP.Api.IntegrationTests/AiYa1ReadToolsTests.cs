using System.Text.Json;
using GoymenERP.Api.Ai;
using GoymenERP.Api.Ai.Knowledge;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.Counterparty.Infrastructure.Ai;
using GoymenERP.Modules.Logistics.Application.Ai;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Ai;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Y-A1 READ tools over the AI-3 runtime, with no model, no network and no database: <c>decisions.lookup.v1</c> over the
/// committed decision index and <c>logistics.pending-shipments.v1</c> over a SENTETIK Logistics shipment-state read. The ask path
/// runs in tool-plan mode (the deterministic fake provider plans exactly the hinted call). Registration is default-deny:
/// without an <c>Ai:AllowedTools</c> entry the catalog admits nothing.
/// </summary>
public sealed class AiYa1ReadToolsTests
{
    private const string IndexPath = "contracts/ai/decision-index-v1.json";
    private const string CatalogPath = "contracts/ai/tool-catalog-v1.json";
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000e1");
    private static readonly Guid Actor = Guid.Parse("11111111-1111-4111-8111-1111111111e1");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);
    private static readonly Lazy<AiDecisionIndex> RepositoryIndex = new(() =>
        AiDecisionIndex.TryLoad(Path.Combine(FindRepositoryRoot(), IndexPath))
        ?? throw new InvalidOperationException("Decision index not loadable."));

    [Fact]
    public async Task A_lookup_by_identity_returns_the_record_with_its_source_line_status_and_effective_date()
    {
        var result = await DecisionTool().ReadAsync(new AiToolInputV1("{\"decisionId\":\"AI-D24\"}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        var record = Assert.Single(content.RootElement.GetProperty("records").EnumerateArray());
        Assert.Equal("AI-D24", record.GetProperty("id").GetString());
        Assert.Equal("kilitli", record.GetProperty("status").GetString());
        Assert.Equal("2026-11-22", record.GetProperty("effectiveDate").GetString());
        Assert.StartsWith("docs/decisions/SINAN_KARARLARI_2026-11-22", record.GetProperty("source").GetProperty("path").GetString());
        Assert.True(record.GetProperty("source").GetProperty("line").GetInt32() > 0);
        Assert.Equal(RepositoryIndex.Value.SourceSetSha256, content.RootElement.GetProperty("indexVersion").GetString());
        Assert.Equal(RepositoryIndex.Value.SourceSetSha256, result.Source!.SourceVersion);
        Assert.Equal(DecisionLookupAiTool.ToolId, result.Source.ToolId);
        Assert.Equal(AiDataClassV1.Internal, result.Source.DataClass);
    }

    [Fact]
    public async Task A_keyword_lookup_is_case_folded_limited_and_says_when_it_was_cut()
    {
        var result = await DecisionTool().ReadAsync(new AiToolInputV1("{\"keyword\":\"İSKONTO\",\"limit\":2}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        Assert.Equal(2, content.RootElement.GetProperty("records").GetArrayLength());
        Assert.True(content.RootElement.GetProperty("matchCount").GetInt32() > 2);
        Assert.True(content.RootElement.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task A_superseded_decision_names_the_decision_that_replaced_it()
    {
        var result = await DecisionTool().ReadAsync(new AiToolInputV1("{\"decisionId\":\"SAT-D40\"}"), Context());

        using var content = JsonDocument.Parse(result.ContentJson!);
        var record = Assert.Single(content.RootElement.GetProperty("records").EnumerateArray());
        Assert.Equal("gecersiz", record.GetProperty("status").GetString());
        Assert.Contains(record.GetProperty("supersededBy").EnumerateArray(),
            link => link.GetProperty("by").GetString() == "SAT-D44" && link.GetProperty("confidence").GetString() == "acik");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"decisionId\":\"AI-D24\",\"keyword\":\"model\"}")]
    [InlineData("{\"keyword\":\"x\"}")]
    [InlineData("{\"decisionId\":\"ai-d24\"}")]
    [InlineData("{\"decisionId\":\"AI-D24\",\"limit\":11}")]
    [InlineData("{\"decisionId\":\"AI-D24\",\"companyId\":\"aaaaaaaa-0000-4000-8000-0000000000e1\"}")]
    [InlineData("[]")]
    public async Task Malformed_or_unscoped_lookup_arguments_are_invalid_and_release_nothing(string arguments)
    {
        var result = await DecisionTool().ReadAsync(new AiToolInputV1(arguments), Context());

        Assert.Equal(AiToolOutcomeV1.Invalid, result.Outcome);
        Assert.Null(result.ContentJson);
        Assert.Null(result.Source);
    }

    [Fact]
    public async Task An_unknown_identity_is_not_found_and_a_missing_index_is_unavailable()
    {
        Assert.Equal(AiToolOutcomeV1.NotFound,
            (await DecisionTool().ReadAsync(new AiToolInputV1("{\"decisionId\":\"ZZZ-D999999\"}"), Context())).Outcome);
        var withoutIndex = new DecisionLookupAiTool(new FixedAiDecisionIndexSource(null), new FixedTime(Now));
        Assert.Equal(AiToolOutcomeV1.Unavailable,
            (await withoutIndex.ReadAsync(new AiToolInputV1("{\"decisionId\":\"AI-D24\"}"), Context())).Outcome);
        Assert.Null(AiDecisionIndex.TryLoad(Path.Combine(FindRepositoryRoot(), "contracts/ai/missing-index.json")));
    }

    [Fact]
    public void Personal_data_candidates_are_masked_and_no_registry_owner_name_remains_in_them()
    {
        var index = RepositoryIndex.Value;
        var candidates = index.Records.Where(record => record.DataClassCandidate == AiDecisionIndex.PersonalDataCandidate).ToArray();

        Assert.NotEmpty(index.PersonalNameTerms);
        Assert.NotEmpty(candidates);
        Assert.All(candidates, record =>
        {
            Assert.True(record.Masked);
            foreach (var term in index.PersonalNameTerms)
            {
                Assert.DoesNotMatch($"(?<![\\p{{L}}\\p{{N}}]){System.Text.RegularExpressions.Regex.Escape(term)}(?![\\p{{L}}])",
                    record.Title + "\n" + record.Text);
            }
        });
        Assert.Contains(candidates, record => record.Text.Contains(AiDecisionIndex.PersonMask, StringComparison.Ordinal));
    }

    [Fact]
    public void The_tool_catalog_contract_matches_every_registered_read_tool_descriptor()
    {
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRepositoryRoot(), CatalogPath)));
        // Y-A1 and WP-A3a: every catalogued tool has exactly one C# descriptor and the reverse (descriptors need no owner).
        var descriptors = new[]
        {
            DecisionTool().Descriptor,
            new LogisticsPendingShipmentsAiTool(new ListOnlyInstructionService(), new FixedTime(Now)).Descriptor,
            new LogisticsOrderDispatchNotesAiTool(null!, new FixedTime(Now)).Descriptor,
            new LogisticsInstructionShipmentStatusAiTool(null!, new FixedTime(Now)).Descriptor,
            new SalesPendingApprovalsAiTool(null!, new FixedTime(Now)).Descriptor,
            new SalesQuoteStatusAiTool(null!, new FixedTime(Now)).Descriptor,
            new SalesOrderStatusAiTool(null!, new FixedTime(Now)).Descriptor,
            new CounterpartyPartyLookupAiTool(null!, new FixedTime(Now)).Descriptor,
            new CounterpartyPartySummaryAiTool(null!, new FixedTime(Now)).Descriptor,
            new CounterpartyRoleCountsAiTool(null!, new FixedTime(Now)).Descriptor,
        };
        Assert.Equal(descriptors.Length, descriptors.Select(descriptor => descriptor.ToolId).Distinct(StringComparer.Ordinal).Count());
        var entries = catalog.RootElement.GetProperty("tools").EnumerateArray().ToArray();
        Assert.Equal(descriptors.Length, entries.Length);
        foreach (var descriptor in descriptors)
        {
            var entry = Assert.Single(entries, e => e.GetProperty("toolId").GetString() == descriptor.ToolId);
            Assert.Equal(descriptor.Version, entry.GetProperty("version").GetString());
            Assert.Equal(descriptor.OwnerModule, entry.GetProperty("ownerModule").GetString());
            Assert.Equal(descriptor.Risk.ToString(), entry.GetProperty("risk").GetString());
            Assert.Equal(descriptor.DataClass.ToString(), entry.GetProperty("dataClass").GetString());
            Assert.Equal(descriptor.RequiredPermissions,
                entry.GetProperty("requiredPermissions").EnumerateArray().Select(value => value.GetString()!).Order(StringComparer.Ordinal));
            Assert.Equal(descriptor.FieldAllowlist,
                entry.GetProperty("fieldAllowlist").EnumerateArray().Select(value => value.GetString()!).Order(StringComparer.Ordinal));
            Assert.Null(descriptor.OwnerCommandName);
        }
    }

    [Fact]
    public async Task Tool_plan_mode_answers_a_decision_lookup_with_its_source_and_records_started_and_completed()
    {
        var harness = new Harness();

        var result = await harness.Service().AskAsync(Ask(DecisionLookupAiTool.ToolId, "{\"decisionId\":\"AI-D34\"}"));

        Assert.Equal(AiAskStatusV1.Answered, result.Status);
        var toolResult = Assert.Single(result.Response!.ToolResults);
        Assert.Equal(AiToolResultOutcomesV1.Succeeded, toolResult.Outcome);
        Assert.Contains("\"AI-D34\"", toolResult.ContentJson!, StringComparison.Ordinal);
        Assert.Equal(DecisionLookupAiTool.TargetKind, toolResult.Source!.TargetKind);
        Assert.Equal(
            new[] { SecurityEventPhasesV1.Started, SecurityEventPhasesV1.Completed },
            harness.Writer.Events.Where(e => e.EventType == SecurityEventTypesV1.AiToolCalled).Select(e => e.Phase).ToArray());
        Assert.All(harness.Writer.Events, e => Assert.Empty(SecurityEventV1Validator.Validate(e)));
        Assert.All(harness.Writer.Events, e => Assert.Equal(DecisionLookupAiTool.ToolId, e.AiToolId));
    }

    [Fact]
    public async Task Without_the_decision_permission_the_lookup_is_refused_as_ai_authz_denied()
    {
        var harness = new Harness();
        harness.Authority.Revoke(ErpPermissions.AiDecisionsRead);

        var result = await harness.Service().AskAsync(Ask(DecisionLookupAiTool.ToolId, "{\"decisionId\":\"AI-D34\"}"));

        Assert.Equal(AiAskStatusV1.Refused, result.Status);
        var denied = Assert.Single(harness.Writer.Events);
        Assert.Equal(SecurityEventTypesV1.AiAuthorizationDenied, denied.EventType);
        Assert.Equal(ErpPermissions.AiDecisionsRead, denied.PermissionCode);
    }

    [Fact]
    public async Task Tool_plan_mode_counts_pending_shipments_through_the_logistics_owner_list()
    {
        var harness = new Harness();
        harness.Instructions.Add("Draft", 2);
        harness.Instructions.Add("Issued", 3, LogisticsShipmentStatesV1.PartiallyShipped);
        harness.Instructions.Add("Issued", 6, LogisticsShipmentStatesV1.FullyShipped);
        harness.Instructions.Add("Cancelled", 4);

        var result = await harness.Service().AskAsync(Ask(LogisticsPendingShipmentsAiTool.ToolId, "{}"));

        Assert.Equal(AiAskStatusV1.Answered, result.Status);
        var toolResult = Assert.Single(result.Response!.ToolResults);
        using var content = JsonDocument.Parse(toolResult.ContentJson!);
        Assert.Equal(5, content.RootElement.GetProperty("pendingCount").GetInt32());
        Assert.Equal(6, content.RootElement.GetProperty("fullyShippedExcludedCount").GetInt32());
        Assert.True(content.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal(Company, toolResult.Source!.TargetId);
    }

    [Fact]
    public async Task A_company_argument_to_the_shipment_tool_is_refused_before_the_owner_list_is_read()
    {
        var harness = new Harness();

        var result = await harness.Service().AskAsync(
            Ask(LogisticsPendingShipmentsAiTool.ToolId, $"{{\"companyId\":\"{Company:D}\"}}"));

        Assert.Equal(AiAskStatusV1.Invalid, result.Status);
        Assert.Empty(harness.Instructions.Queries);
    }

    [Fact]
    public void The_runtime_registers_the_decision_tool_but_admits_nothing_without_an_allowlist_and_has_no_index_path()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Enabled"] = "false",
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment("Development"));
        services.AddAiRuntime(configuration);
        using var provider = services.BuildServiceProvider();

        var tools = provider.GetServices<IAiOwnerToolV1>().ToArray();
        Assert.Contains(tools, tool => tool.Descriptor.ToolId == DecisionLookupAiTool.ToolId);
        Assert.Null(provider.GetRequiredService<IAiDecisionIndexSource>().Current);
        using var scope = provider.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<AiToolCatalog>().Descriptors);
    }

    [Fact]
    public async Task Tool_plan_mode_counts_sales_pending_approvals_through_the_sales_owner_read()
    {
        var owner = new PendingApprovalOwner(3);
        var harness = new Harness(new SalesPendingApprovalsAiTool(owner, new FixedTime(Now)));

        var result = await harness.Service().AskAsync(Ask(SalesPendingApprovalsAiTool.ToolId, "{\"limit\":2}"));

        Assert.Equal(AiAskStatusV1.Answered, result.Status);
        var toolResult = Assert.Single(result.Response!.ToolResults);
        using var content = JsonDocument.Parse(toolResult.ContentJson!);
        Assert.Equal(3, content.RootElement.GetProperty("pendingCount").GetInt32());
        Assert.Equal(2, content.RootElement.GetProperty("listedCount").GetInt32());
        Assert.True(content.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal(Company, toolResult.Source!.TargetId);
        Assert.All(owner.Contexts, context => Assert.Equal((Company, Actor.ToString("D")), (context.CompanyId, context.ActorId)));
    }

    [Fact]
    public async Task Without_the_order_approve_permission_the_sales_approval_tool_is_refused_before_the_owner_is_read()
    {
        var owner = new PendingApprovalOwner(1);
        var harness = new Harness(new SalesPendingApprovalsAiTool(owner, new FixedTime(Now)));
        harness.Authority.Revoke(SalesPermissionManifestV1.OrdersApprove);

        var result = await harness.Service().AskAsync(Ask(SalesPendingApprovalsAiTool.ToolId, "{}"));

        Assert.Equal(AiAskStatusV1.Refused, result.Status);
        var denied = Assert.Single(harness.Writer.Events);
        Assert.Equal(SecurityEventTypesV1.AiAuthorizationDenied, denied.EventType);
        Assert.Equal(SalesPermissionManifestV1.OrdersApprove, denied.PermissionCode);
        Assert.Empty(owner.Contexts);
    }

    [Fact]
    public async Task A_confidential_cari_lookup_is_refused_while_the_data_class_limit_is_internal_and_the_owner_is_not_read()
    {
        var owner = new CounterpartyOwner();
        var harness = new Harness(new CounterpartyPartyLookupAiTool(owner, new FixedTime(Now)));

        var result = await harness.Service().AskAsync(Ask(CounterpartyPartyLookupAiTool.ToolId, "{\"query\":\"SENTETIK\"}"));

        Assert.Equal(AiAskStatusV1.Refused, result.Status);
        Assert.Contains(harness.Writer.Events, e => e.EventType == SecurityEventTypesV1.AiRequestRefused
                                                    && e.ReasonCode == SecurityEventReasonCodesV1.AiDataClassNotAllowed);
        Assert.Equal(0, owner.Calls);
    }

    [Fact]
    public void Every_wpa3a_read_tool_is_a_read_descriptor_and_an_empty_allowlist_admits_none_of_them()
    {
        IAiOwnerToolV1[] tools =
        [
            DecisionTool(),
            new LogisticsPendingShipmentsAiTool(new ListOnlyInstructionService(), new FixedTime(Now)),
            new LogisticsOrderDispatchNotesAiTool(null!, new FixedTime(Now)),
            new LogisticsInstructionShipmentStatusAiTool(null!, new FixedTime(Now)),
            new SalesPendingApprovalsAiTool(new PendingApprovalOwner(0), new FixedTime(Now)),
            new SalesQuoteStatusAiTool(null!, new FixedTime(Now)),
            new SalesOrderStatusAiTool(null!, new FixedTime(Now)),
            new CounterpartyPartyLookupAiTool(new CounterpartyOwner(), new FixedTime(Now)),
            new CounterpartyPartySummaryAiTool(new CounterpartyOwner(), new FixedTime(Now)),
            new CounterpartyRoleCountsAiTool(new CounterpartyOwner(), new FixedTime(Now)),
        ];
        var empty = new AiRuntimeOptions
        {
            Enabled = false,
            Provider = AiProviderKindsV1.Fake,
            PolicyRef = "TEST-AI-POLICY",
            PolicyVersion = 1,
            MaxQuestionLength = 200,
            MaxArgumentBytes = 256,
            MaxToolCallsPerRequest = 1,
            MaxDataClass = AiDataClassV1.Internal,
            AllowedTools = [],
        };

        Assert.All(tools, tool =>
        {
            Assert.IsAssignableFrom<IAiOwnerReadToolV1>(tool);
            Assert.Equal(AiToolRiskClassV1.Read, tool.Descriptor.Risk);
            Assert.Null(tool.Descriptor.OwnerCommandName);
        });
        Assert.Empty(new AiToolCatalog(tools, Options.Create(empty)).Descriptors);
    }

    /// <summary>A SENTETIK Sales pending-approval read: one page of this company's revisions, the contexts it was asked with.</summary>
    private sealed class PendingApprovalOwner(int count) : GoymenERP.Modules.Sales.Application.Contracts.ISalesOrderDraftPendingApprovalReadServiceV1
    {
        public List<GoymenERP.Modules.Sales.Application.Contracts.SalesOrderDraftPendingApprovalReadContextV1> Contexts { get; } = [];

        public Task<GoymenERP.Modules.Sales.Application.Contracts.SalesOrderDraftPendingApprovalReadResultV1<GoymenERP.Modules.Sales.Application.Contracts.SalesOrderDraftPendingApprovalPageV1>> ListAsync(
            GoymenERP.Modules.Sales.Application.Contracts.SalesOrderDraftPendingApprovalReadContextV1 context, string? cursor,
            CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            var items = Enumerable.Range(1, count).Select(number => new GoymenERP.Modules.Sales.Application.Contracts.SalesOrderDraftPendingApprovalItemV1(
                new Guid($"00000000-0000-4000-8000-{number:D12}"), Company, $"SENTETIK-SO-{number}", $"v{number}", "0", "actor-sentetik",
                Now.AddMinutes(number), false)).ToArray();
            return Task.FromResult(new GoymenERP.Modules.Sales.Application.Contracts.SalesOrderDraftPendingApprovalReadResultV1<GoymenERP.Modules.Sales.Application.Contracts.SalesOrderDraftPendingApprovalPageV1>(
                GoymenERP.Modules.Sales.Application.Contracts.SalesOrderDraftPendingApprovalReadOutcomeV1.Found,
                new GoymenERP.Modules.Sales.Application.Contracts.SalesOrderDraftPendingApprovalPageV1(Company, items, null)));
        }

        public Task<GoymenERP.Modules.Sales.Application.Contracts.SalesOrderDraftPendingApprovalReadResultV1<GoymenERP.Modules.Sales.Application.Contracts.SalesOrderDraftPendingApprovalDetailV1>> GetAsync(
            GoymenERP.Modules.Sales.Application.Contracts.SalesOrderDraftPendingApprovalReadContextV1 context, Guid draftId,
            CancellationToken cancellationToken) => throw new NotSupportedException("The tool reads the list only.");
    }

    /// <summary>A Cari owner read that only counts calls: the data-class refusal must stop before it.</summary>
    private sealed class CounterpartyOwner : GoymenERP.Modules.Counterparty.Application.Contracts.ICounterpartyReadService
    {
        public int Calls { get; private set; }

        public Task<GoymenERP.Modules.Counterparty.Application.Contracts.CounterpartyPageExecution> GetAuthorizedPageAsync(
            GoymenERP.Modules.Counterparty.Application.Contracts.CounterpartyReadRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(GoymenERP.Modules.Counterparty.Application.Contracts.CounterpartyPageExecution.ServiceUnavailable());
        }

        public Task<GoymenERP.Modules.Counterparty.Application.Contracts.CounterpartyDetailView?> GetAuthorizedPartyAsync(Guid partyId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<GoymenERP.Modules.Counterparty.Application.Contracts.CounterpartyDetailView?>(null);
        }
    }

    private static DecisionLookupAiTool DecisionTool() =>
        new(new FixedAiDecisionIndexSource(RepositoryIndex.Value), new FixedTime(Now));

    private static AiServerContextV1 Context() => new(Company, Actor.ToString("D"), "v1.session-sentetik",
        $"company:{Company:D}", "roles:sentetik", "corr-ai-ya1-sentetik", null, null, null);

    private static AiAskRequestV1 Ask(string toolId, string arguments)
    {
        using var document = JsonDocument.Parse(arguments);
        return new AiAskRequestV1("SENTETIK soru", toolId, "1", document.RootElement.Clone());
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, IndexPath)))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("GöymenERP repository kökü bulunamadı.");
    }

    private sealed class Harness
    {
        private readonly AiRuntimeOptions options;
        private readonly IAiOwnerToolV1[] extraTools;

        /// <param name="extraTools">WP-A3a tools allowlisted next to the two Y-A1 tools for one test.</param>
        public Harness(params IAiOwnerToolV1[] extraTools)
        {
            this.extraTools = extraTools;
            options = new AiRuntimeOptions
            {
                Enabled = true,
                Provider = AiProviderKindsV1.Fake,
                PolicyRef = "TEST-AI-POLICY",
                PolicyVersion = 1,
                MaxQuestionLength = 200,
                MaxArgumentBytes = 256,
                MaxToolCallsPerRequest = 1,
                MaxDataClass = AiDataClassV1.Internal,
                AllowedTools =
                [
                    new AiAllowedToolOptions { ToolId = DecisionLookupAiTool.ToolId, Version = DecisionLookupAiTool.ToolVersion },
                    new AiAllowedToolOptions { ToolId = LogisticsPendingShipmentsAiTool.ToolId, Version = LogisticsPendingShipmentsAiTool.ToolVersion },
                    .. extraTools.Select(tool => new AiAllowedToolOptions { ToolId = tool.Descriptor.ToolId, Version = tool.Descriptor.Version }),
                ],
            };
            CompanyScope.Set(Company);
            Correlation.Set("corr-ai-ya1-sentetik", null, SourceChannels.Web, null, null);
        }

        public ListOnlyInstructionService Instructions { get; } = new();
        public RecordingWriter Writer { get; } = new();
        public FakeAuthority Authority { get; } = new(ErpPermissions.AiPanelUse, ErpPermissions.AiDecisionsRead,
            LogisticsPermissionManifestV1.InstructionsRead, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.OrdersApprove, ErpPermissions.CounterpartyPartiesRead);
        public CompanyContext CompanyScope { get; } = new();
        public CorrelationContext Correlation { get; } = new();

        public AiAskService Service()
        {
            IAiOwnerToolV1[] tools =
            [
                DecisionTool(),
                new LogisticsPendingShipmentsAiTool(Instructions, new FixedTime(Now)),
                .. extraTools,
            ];
            return new AiAskService(
                Options.Create(options),
                new TestHostEnvironment("Development"),
                new FakeAiModelProvider(),
                new AiToolCatalog(tools, Options.Create(options)),
                new TestUser(Actor, Company),
                CompanyScope,
                Authority,
                new FixedSessionResolver(),
                Correlation,
                Writer,
                new FixedTime(Now));
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>A SENTETIK Logistics owner shipment-state read: this company's rows only, pages by exact status.</summary>
    private sealed class ListOnlyInstructionService : ILogisticsPendingShipmentReadV1
    {
        private readonly List<LogisticsPendingShipmentRowV1> rows = [];

        public List<LogisticsPendingShipmentPageQueryV1> Queries { get; } = [];

        public void Add(string status, int count, string shipmentState = LogisticsShipmentStatesV1.NotShipped)
        {
            for (var index = 0; index < count; index++)
            {
                var number = rows.Count + 1;
                rows.Add(new LogisticsPendingShipmentRowV1(
                    new Guid($"00000000-0000-4000-8000-{number:D12}"), Company, status, $"rv-{number}",
                    Guid.Parse("cccccccc-0000-4000-8000-0000000000e3"), new DateOnly(2026, 11, 22), Now.AddMinutes(number), 1,
                    0, shipmentState));
            }
        }

        public Task<LogisticsPendingShipmentPageV1> GetPendingShipmentPageAsync(LogisticsPendingShipmentPageQueryV1 query,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            var matching = rows.Where(row => row.Status == query.Status).ToArray();
            var start = query.Cursor is null ? 0 : int.Parse(query.Cursor, System.Globalization.CultureInfo.InvariantCulture);
            var page = matching.Skip(start).Take(query.PageSize).ToArray();
            var next = start + page.Length < matching.Length
                ? (start + page.Length).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : null;
            return Task.FromResult(new LogisticsPendingShipmentPageV1(page, next));
        }
    }

    private sealed class FakeAuthority(params string[] granted) : IErpAuthorityRevalidationService, IErpAuthorizationService
    {
        private readonly HashSet<string> grants = new(granted, StringComparer.Ordinal);

        public void Revoke(string permission) => grants.Remove(permission);

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ScopeOf(permission, companyId));

        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ScopeOf(permission, companyId));

        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ScopeOf(permission, companyId).IsGranted);

        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The AI runtime measures scopes, not snapshots.");

        private PermissionScope ScopeOf(string permission, Guid companyId) =>
            companyId == Company && grants.Contains(permission)
                ? new PermissionScope(true, true, new HashSet<string>(StringComparer.Ordinal) { "ROLE-SENTETIK" }, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied;
    }

    private sealed class RecordingWriter : ISecurityEventWriterV1
    {
        public List<SecurityEventV1> Events { get; } = [];

        public Task<SecurityEventWriteResultV1> WriteAsync(SecurityEventV1 securityEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(securityEvent);
            return Task.FromResult(SecurityEventWriteResultV1.Written(
                SecurityEventWriteFailureModesV1.Resolve(securityEvent.EventType, securityEvent.Phase)));
        }
    }

    private sealed class TestUser(Guid userId, params Guid[] companies) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = companies.ToHashSet();
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class FixedSessionResolver : IActorSessionBindingResolver
    {
        public Task<string?> ResolveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("v1.session-sentetik");
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "GoymenERP.Api.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
