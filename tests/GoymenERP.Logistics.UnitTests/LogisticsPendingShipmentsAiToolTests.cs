using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Logistics.Application.Ai;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// Y-A1 <c>logistics.pending-shipments.v1</c>: the 20 SENTETIK pending-shipment scenarios of
/// <c>contracts/ai/eval/eval-set-v1.json</c> run against the tool over a fake Logistics owner list service. The counts
/// are exact and come only from the owner's pages; a permission refusal, another company's row or an owner failure
/// releases nothing.
/// </summary>
public sealed class LogisticsPendingShipmentsAiToolTests
{
    private const string EvalSetPath = "contracts/ai/eval/eval-set-v1.json";
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000a1");
    private static readonly Guid OtherCompany = Guid.Parse("bbbbbbbb-0000-4000-8000-0000000000b2");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> ScenarioIds()
    {
        var data = new TheoryData<string>();
        foreach (var item in LoadScenarios())
        {
            data.Add(item.GetProperty("id").GetString()!);
        }

        return data;
    }

    [Fact]
    public void The_eval_set_carries_exactly_twenty_pending_shipment_scenarios()
    {
        Assert.Equal(20, LoadScenarios().Count);
    }

    [Theory]
    [MemberData(nameof(ScenarioIds))]
    public async Task Every_pending_shipment_scenario_gives_the_expected_outcome_and_exact_counts(string scenarioId)
    {
        var scenario = LoadScenarios().Single(item => item.GetProperty("id").GetString() == scenarioId);
        var call = scenario.GetProperty("expected_tool_call");
        Assert.Equal(LogisticsPendingShipmentsAiTool.ToolId, call.GetProperty("toolId").GetString());
        var fixture = scenario.GetProperty("fixture");
        var service = new FakeInstructionService(fixture.GetProperty("permission").GetString()!);
        foreach (var group in fixture.GetProperty("instructions").EnumerateArray())
        {
            service.Add(group.GetProperty("status").GetString()!, group.GetProperty("count").GetInt32(),
                group.GetProperty("company").GetString() == "ayni" ? Company : OtherCompany);
        }

        var tool = new LogisticsPendingShipmentsAiTool(service, new FixedTime(Now));
        var result = await tool.ReadAsync(new AiToolInputV1(call.GetProperty("arguments").GetRawText()), Context());

        var expected = scenario.GetProperty("expected_tool_result");
        var outcome = expected.GetProperty("outcome").GetString();
        Assert.Equal(outcome switch
        {
            "succeeded" => AiToolOutcomeV1.Succeeded,
            "denied" => AiToolOutcomeV1.Denied,
            "invalid" => AiToolOutcomeV1.Invalid,
            _ => AiToolOutcomeV1.Unavailable,
        }, result.Outcome);
        if (outcome != "succeeded")
        {
            Assert.Null(result.ContentJson);
            Assert.Null(result.Source);
            return;
        }

        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        foreach (var name in new[] { "pendingCount", "draftCount", "issuedCount" })
        {
            var wanted = expected.GetProperty(name);
            if (wanted.ValueKind == JsonValueKind.Null)
            {
                Assert.Equal(JsonValueKind.Null, root.GetProperty(name).ValueKind);
            }
            else
            {
                Assert.Equal(wanted.GetInt32(), root.GetProperty(name).GetInt32());
            }
        }

        Assert.Equal(expected.GetProperty("listedCount").GetInt32(), root.GetProperty("listedCount").GetInt32());
        Assert.Equal(expected.GetProperty("listedCount").GetInt32(), root.GetProperty("items").GetArrayLength());
        Assert.Equal(expected.GetProperty("complete").GetBoolean(), root.GetProperty("complete").GetBoolean());
        Assert.Equal(LogisticsPendingShipmentsAiTool.Definition, root.GetProperty("definition").GetString());
        Assert.NotNull(result.Source);
        Assert.Equal(Company, result.Source!.TargetId);
        Assert.Equal(AiDataClassV1.Internal, result.Source.DataClass);
    }

    [Fact]
    public void The_descriptor_is_a_logistics_read_tool_with_the_instruction_read_permission_and_a_closed_field_list()
    {
        var descriptor = new LogisticsPendingShipmentsAiTool(new FakeInstructionService("verildi"), new FixedTime(Now)).Descriptor;

        Assert.Equal("logistics.pending-shipments.v1", descriptor.ToolId);
        Assert.Equal("1", descriptor.Version);
        Assert.Equal("Logistics", descriptor.OwnerModule);
        Assert.Equal(AiToolRiskClassV1.Read, descriptor.Risk);
        Assert.Equal(AiDataClassV1.Internal, descriptor.DataClass);
        Assert.Null(descriptor.OwnerCommandName);
        Assert.Equal(new[] { "logistics.loading-instructions.read" }, descriptor.RequiredPermissions);
        Assert.Equal(new[] { "limit", "status" }, descriptor.FieldAllowlist);
    }

    [Fact]
    public async Task The_released_items_carry_no_free_text_and_the_same_input_gives_the_same_bytes()
    {
        var service = new FakeInstructionService("verildi");
        service.Add("Draft", 3, Company);
        service.Add("Issued", 2, Company);
        var tool = new LogisticsPendingShipmentsAiTool(service, new FixedTime(Now));

        var first = await tool.ReadAsync(new AiToolInputV1("{}"), Context());
        var second = await tool.ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(first.ContentJson, second.ContentJson);
        Assert.Equal(first.Source!.SourceVersion, second.Source!.SourceVersion);
        Assert.DoesNotContain("cancelReason", first.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cancelledBy", first.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.All(service.Queries, query => Assert.Equal(LogisticsPendingShipmentsAiTool.PageSize, query.PageSize));
    }

    private static AiServerContextV1 Context() => new(Company, "11111111-1111-4111-8111-111111111111", "v1.session-sentetik",
        $"company:{Company:D}", "roles:sentetik", "corr-sentetik", null, null, null);

    private static IReadOnlyList<JsonElement> LoadScenarios()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRepositoryRoot(), EvalSetPath)));
        return [.. document.RootElement.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("category").GetString() == "islem_sorgusu")
            .Select(item => item.Clone())];
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, EvalSetPath)))
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

    /// <summary>The owner's list contract only: pages of 100 by exact status, a cursor while more remain.</summary>
    private sealed class FakeInstructionService(string permission) : ILogisticsInstructionService
    {
        private readonly List<LoadingInstructionSummaryView> rows = [];

        public List<LoadingInstructionListQuery> Queries { get; } = [];

        public void Add(string status, int count, Guid company)
        {
            for (var index = 0; index < count; index++)
            {
                var number = rows.Count + 1;
                rows.Add(new LoadingInstructionSummaryView(
                    new Guid($"00000000-0000-4000-8000-{number:D12}"), company, status, $"rv-{number}",
                    Guid.Parse("cccccccc-0000-4000-8000-0000000000c3"), Guid.Parse("dddddddd-0000-4000-8000-0000000000d4"),
                    new DateOnly(2026, 11, 22), Now.AddMinutes(number), Now.AddMinutes(number), 1,
                    null, null, null, "NotApplicable"));
            }
        }

        public Task<LoadingInstructionListResponse> GetInstructionListAsync(LoadingInstructionListQuery query,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            if (permission == "yok")
            {
                throw new LogisticsInstructionServiceException(403, "forbidden");
            }

            if (permission == "servis_hatasi")
            {
                throw new InvalidOperationException("SENTETIK owner failure");
            }

            var matching = rows.Where(row => query.Status is null || row.Status == query.Status).ToArray();
            var start = query.Cursor is null ? 0 : int.Parse(query.Cursor, System.Globalization.CultureInfo.InvariantCulture);
            var page = matching.Skip(start).Take(query.PageSize).ToArray();
            var next = start + page.Length < matching.Length
                ? (start + page.Length).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : null;
            return Task.FromResult(new LoadingInstructionListResponse(page, next));
        }

        public Task<LoadingInstructionResponse> CreateInstructionAsync(CreateLoadingInstructionRequest request, LogisticsCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LoadingInstructionResponse> IssueInstructionAsync(Guid instructionId, IssueLoadingInstructionRequest request,
            LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LoadingInstructionResponse> RecordLoadAsync(Guid instructionId, Guid lineId, RecordLoadRequest request,
            LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LoadingInstructionResponse> RecordWeighingAsync(Guid instructionId, RecordWeighingRequest request, LogisticsCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LoadingInstructionResponse> CreateDispatchNoteAsync(Guid instructionId, CreateDispatchNoteRequest request, LogisticsCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LoadingInstructionResponse> CancelInstructionAsync(Guid instructionId, CancelInstructionRequest request,
            LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LoadingInstructionResponse> RecordUnloadWeighingAsync(Guid instructionId, RecordUnloadWeighingRequest request,
            LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LoadingInstructionResponse> ApproveDispatchNoteAsync(Guid instructionId, Guid dispatchNoteId, ApproveDispatchNoteRequest request,
            LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LoadingInstructionResponse> CancelDispatchNoteAsync(Guid instructionId, Guid dispatchNoteId, CancelDispatchNoteRequest request,
            LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LoadingInstructionResponse> SetEDispatchNumberAsync(Guid instructionId, Guid dispatchNoteId, SetEDispatchNumberRequest request,
            LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SalesOrderDispatchNotesView> GetSalesOrderDispatchNotesAsync(Guid salesOrderId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LoadingInstructionDetailView> GetInstructionDetailAsync(Guid instructionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LogisticsApprovedOrderLinePage> GetApprovedOrderLinesAsync(LogisticsApprovedOrderLinesQuery query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
