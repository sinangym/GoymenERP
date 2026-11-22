using System.Reflection;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Logistics.Application.Ai;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// WP-A3a Logistics READ tools <c>logistics.order-dispatch-notes.v1</c> and <c>logistics.instruction-shipment-status.v1</c> over a
/// SENTETIK owner instruction service: the owner's exact quantity texts released as they are, no actor or reason released, a
/// permission refusal as Denied, an unknown record as NotFound, another company's / another record's answer and an unknown
/// status releasing nothing, the owner's "more exist" withholding the counts, and an unscoped argument refused first.
/// </summary>
public sealed class LogisticsReadAiToolsTests
{
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000a1");
    private static readonly Guid OtherCompany = Guid.Parse("bbbbbbbb-0000-4000-8000-0000000000b2");
    private static readonly Guid Order = Guid.Parse("50000000-0000-4000-8000-0000000000a1");
    private static readonly Guid Instruction = Guid.Parse("60000000-0000-4000-8000-0000000000a1");
    private static readonly Guid Unit = Guid.Parse("55555555-5555-4555-8555-5555555555a5");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_two_descriptors_are_logistics_read_tools_under_the_instruction_read_permission()
    {
        var notes = new LogisticsOrderDispatchNotesAiTool(InstructionProxy.Create().Service, new FixedTime(Now)).Descriptor;
        var status = new LogisticsInstructionShipmentStatusAiTool(InstructionProxy.Create().Service, new FixedTime(Now)).Descriptor;

        Assert.All(new[] { notes, status }, descriptor =>
        {
            Assert.Equal("Logistics", descriptor.OwnerModule);
            Assert.Equal(AiToolRiskClassV1.Read, descriptor.Risk);
            Assert.Equal(AiDataClassV1.Internal, descriptor.DataClass);
            Assert.Null(descriptor.OwnerCommandName);
            Assert.Equal(new[] { "logistics.loading-instructions.read" }, descriptor.RequiredPermissions);
        });
        Assert.Equal(new[] { "limit", "salesOrderId" }, notes.FieldAllowlist);
        Assert.Equal(new[] { "instructionId" }, status.FieldAllowlist);
    }

    [Fact]
    public void The_logistics_module_registers_the_three_read_tools_as_scoped_owner_tools()
    {
        var services = new ServiceCollection().AddLogisticsModule(new Settings("Server=unused.invalid;Database=unused;"));

        var tools = services.Where(descriptor => descriptor.ServiceType == typeof(IAiOwnerToolV1)).ToArray();
        Assert.Equal(
            new[] { typeof(LogisticsInstructionShipmentStatusAiTool), typeof(LogisticsOrderDispatchNotesAiTool), typeof(LogisticsPendingShipmentsAiTool) },
            tools.Select(descriptor => descriptor.ImplementationType!).OrderBy(type => type.Name, StringComparer.Ordinal).ToArray());
        Assert.All(tools, descriptor => Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime));
    }

    [Fact]
    public async Task The_order_dispatch_notes_are_counted_by_status_with_exact_quantities_and_no_person()
    {
        var proxy = InstructionProxy.Create();
        proxy.Notes = new SalesOrderDispatchNotesView(Order,
        [
            Note(1, "Approved", "IRS-000001", "EIR-1"),
            Note(2, "Approved", "IRS-000002", null),
            Note(3, "Draft", null, null),
            Note(4, "Cancelled", "IRS-000003", null),
        ], false);

        var result = await new LogisticsOrderDispatchNotesAiTool(proxy.Service, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1($"{{\"salesOrderId\":\"{Order:D}\",\"limit\":3}}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.True(root.GetProperty("complete").GetBoolean());
        Assert.Equal(2, root.GetProperty("approvedCount").GetInt32());
        Assert.Equal(1, root.GetProperty("draftCount").GetInt32());
        Assert.Equal(1, root.GetProperty("cancelledCount").GetInt32());
        Assert.Equal(1, root.GetProperty("approvedWithoutEDispatchNumberCount").GetInt32());
        Assert.Equal(3, root.GetProperty("listedCount").GetInt32());
        Assert.Equal("12.500", root.GetProperty("items")[0].GetProperty("lines")[0].GetProperty("quantity").GetString());
        Assert.Equal(Order, Assert.Single(proxy.OrderIds));
        Assert.Equal(Order, result.Source!.TargetId);
    }

    [Fact]
    public async Task When_the_owner_says_more_dispatch_notes_exist_the_counts_are_withheld()
    {
        var proxy = InstructionProxy.Create();
        proxy.Notes = new SalesOrderDispatchNotesView(Order, [Note(1, "Approved", "IRS-000001", null)], true);

        var result = await new LogisticsOrderDispatchNotesAiTool(proxy.Service, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1($"{{\"salesOrderId\":\"{Order:D}\"}}"), Context());

        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.False(root.GetProperty("complete").GetBoolean());
        foreach (var name in new[] { "draftCount", "approvedCount", "cancelledCount", "approvedWithoutEDispatchNumberCount" })
        {
            Assert.Equal(JsonValueKind.Null, root.GetProperty(name).ValueKind);
        }

        Assert.Equal(1, root.GetProperty("listedCount").GetInt32());
    }

    [Fact]
    public async Task Another_orders_answer_an_unknown_note_status_or_a_repeated_note_releases_nothing()
    {
        foreach (var view in new[]
                 {
                     new SalesOrderDispatchNotesView(Guid.Parse("50000000-0000-4000-8000-0000000000ff"), [Note(1, "Approved", "IRS-1", null)], false),
                     new SalesOrderDispatchNotesView(Order, [Note(1, "Posted", "IRS-1", null)], false),
                     new SalesOrderDispatchNotesView(Order, [Note(1, "Draft", null, null), Note(1, "Draft", null, null)], false),
                 })
        {
            var proxy = InstructionProxy.Create();
            proxy.Notes = view;

            var result = await new LogisticsOrderDispatchNotesAiTool(proxy.Service, new FixedTime(Now))
                .ReadAsync(new AiToolInputV1($"{{\"salesOrderId\":\"{Order:D}\"}}"), Context());

            Assert.Equal(AiToolOutcomeV1.Unavailable, result.Outcome);
            Assert.Null(result.ContentJson);
        }
    }

    [Theory]
    [InlineData(403, AiToolOutcomeV1.Denied)]
    [InlineData(401, AiToolOutcomeV1.Denied)]
    [InlineData(404, AiToolOutcomeV1.NotFound)]
    [InlineData(503, AiToolOutcomeV1.Unavailable)]
    public async Task An_owner_refusal_maps_to_a_closed_outcome_and_releases_nothing(int status, AiToolOutcomeV1 expected)
    {
        var proxy = InstructionProxy.Create();
        proxy.Failure = new LogisticsInstructionServiceException(status, "sentetik");

        var notes = await new LogisticsOrderDispatchNotesAiTool(proxy.Service, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1($"{{\"salesOrderId\":\"{Order:D}\"}}"), Context());
        var detail = await new LogisticsInstructionShipmentStatusAiTool(proxy.Service, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1($"{{\"instructionId\":\"{Instruction:D}\"}}"), Context());

        Assert.Equal(expected, notes.Outcome);
        Assert.Equal(expected, detail.Outcome);
        Assert.Null(notes.ContentJson);
        Assert.Null(detail.ContentJson);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"salesOrderId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"salesOrderId\":\"not-a-guid\"}")]
    [InlineData("{\"salesOrderId\":\"{50000000-0000-4000-8000-0000000000a1}\"}")]
    [InlineData("{\"salesOrderId\":\"50000000-0000-4000-8000-0000000000a1\",\"companyId\":\"aaaaaaaa-0000-4000-8000-0000000000a1\"}")]
    [InlineData("{\"salesOrderId\":\"50000000-0000-4000-8000-0000000000a1\",\"limit\":51}")]
    public async Task A_missing_malformed_or_unscoped_order_argument_is_invalid_before_the_owner_is_read(string arguments)
    {
        var proxy = InstructionProxy.Create();

        var result = await new LogisticsOrderDispatchNotesAiTool(proxy.Service, new FixedTime(Now)).ReadAsync(new AiToolInputV1(arguments), Context());

        Assert.Equal(AiToolOutcomeV1.Invalid, result.Outcome);
        Assert.Empty(proxy.OrderIds);
    }

    [Fact]
    public async Task The_instruction_status_releases_the_owner_line_figures_as_exact_text_without_reason_or_person()
    {
        var proxy = InstructionProxy.Create();
        proxy.Detail = Detail(Company, "Issued", [Line(2, "10", "10", "4", "0", "0", "6", "4"), Line(1, "5", "0", "0", "0", "5", "0", "0")]);

        var result = await new LogisticsInstructionShipmentStatusAiTool(proxy.Service, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1($"{{\"instructionId\":\"{Instruction:D}\"}}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.Equal("Issued", root.GetProperty("status").GetString());
        Assert.Equal(2, root.GetProperty("lineCount").GetInt32());
        Assert.True(root.GetProperty("linesComplete").GetBoolean());
        Assert.Equal(1, root.GetProperty("lines")[0].GetProperty("lineNo").GetInt32());
        var second = root.GetProperty("lines")[1];
        Assert.Equal("10", second.GetProperty("instructed").GetString());
        Assert.Equal("4", second.GetProperty("shipped").GetString());
        Assert.Equal("6", second.GetProperty("loadedNotShipped").GetString());
        Assert.Equal(LogisticsPendingShipmentsAiTool.ToolId, root.GetProperty("pendingShipmentDefinitionTool").GetString());
        Assert.Equal("IRS-000009", Assert.Single(root.GetProperty("dispatchNotes").EnumerateArray()).GetProperty("number").GetString());
        Assert.DoesNotContain("SENTETIK-NEDEN", result.ContentJson!, StringComparison.Ordinal);
        Assert.DoesNotContain("actor-sentetik", result.ContentJson!, StringComparison.Ordinal);
        Assert.Equal(Instruction, result.Source!.TargetId);
    }

    [Fact]
    public async Task Another_companys_instruction_or_an_unknown_status_releases_nothing()
    {
        foreach (var detail in new[] { Detail(OtherCompany, "Issued", [Line(1, "1", "0", "0", "0", "1", "0", "0")]),
                     Detail(Company, "Shipped", [Line(1, "1", "0", "0", "0", "1", "0", "0")]) })
        {
            var proxy = InstructionProxy.Create();
            proxy.Detail = detail;

            var result = await new LogisticsInstructionShipmentStatusAiTool(proxy.Service, new FixedTime(Now))
                .ReadAsync(new AiToolInputV1($"{{\"instructionId\":\"{Instruction:D}\"}}"), Context());

            Assert.Equal(AiToolOutcomeV1.Unavailable, result.Outcome);
            Assert.Null(result.ContentJson);
        }
    }

    [Fact]
    public async Task More_lines_than_the_line_limit_are_cut_and_marked_incomplete_with_the_exact_line_count()
    {
        var proxy = InstructionProxy.Create();
        proxy.Detail = Detail(Company, "Draft",
            [.. Enumerable.Range(1, LogisticsInstructionShipmentStatusAiTool.LineLimit + 1).Select(number => Line(number, "1", "0", "0", "0", "1", "0", "0"))]);

        var result = await new LogisticsInstructionShipmentStatusAiTool(proxy.Service, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1($"{{\"instructionId\":\"{Instruction:D}\"}}"), Context());

        using var content = JsonDocument.Parse(result.ContentJson!);
        Assert.False(content.RootElement.GetProperty("linesComplete").GetBoolean());
        Assert.Equal(LogisticsInstructionShipmentStatusAiTool.LineLimit + 1, content.RootElement.GetProperty("lineCount").GetInt32());
        Assert.Equal(LogisticsInstructionShipmentStatusAiTool.LineLimit, content.RootElement.GetProperty("lines").GetArrayLength());
    }

    private static AiServerContextV1 Context() => new(Company, "11111111-1111-4111-8111-111111111111", "v1.session-sentetik",
        $"company:{Company:D}", "roles:sentetik", "corr-sentetik", null, null, null);

    private static Guid Id(int number) => new($"00000000-0000-4000-8000-{number:D12}");

    private static SalesOrderDispatchNoteView Note(int number, string status, string? systemNumber, string? eDispatch) => new(
        Id(number), Instruction, status, systemNumber, eDispatch, Now.AddMinutes(number),
        [new SalesOrderDispatchNoteLineView(1, Id(100 + number), Id(200 + number), Unit, "12.500")]);

    private static LoadingInstructionLineView Line(int number, string instructed, string loaded, string shipped, string delivered,
        string remainingToLoad, string loadedNotShipped, string shippedNotDelivered) => new(
        Id(300 + number), number, Order, Id(400 + number), 0, Id(500 + number), Unit, instructed, loaded, shipped, delivered,
        remainingToLoad, loadedNotShipped, shippedNotDelivered, false);

    private static LoadingInstructionDetailView Detail(Guid company, string status, LoadingInstructionLineView[] lines) => new(
        new LoadingInstructionSummaryView(Instruction, company, status, "rv-7", Id(900), Id(901), new DateOnly(2026, 11, 22), Now, Now,
            lines.Length, "actor-sentetik", null, "SENTETIK-NEDEN", "None"),
        lines,
        [],
        null,
        [new DispatchNoteView(Id(950), "Approved", "IRS-000009", "actor-sentetik", Now, "actor-sentetik", Now, null, null, null, null, [])],
        null);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Settings(string connectionString) : IConfiguration
    {
        public string? this[string key]
        {
            get => key == "DB_CONNECTION_STRING" ? connectionString : null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotSupportedException();

        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }

    /// <summary>The owner's dispatch-note and detail reads only; any other member is not supported.</summary>
    public class InstructionProxy : DispatchProxy
    {
        public ILogisticsInstructionService Service => (ILogisticsInstructionService)(object)this;
        public SalesOrderDispatchNotesView? Notes { get; set; }
        public LoadingInstructionDetailView? Detail { get; set; }
        public Exception? Failure { get; set; }
        public List<Guid> OrderIds { get; } = [];

        public static InstructionProxy Create() =>
            (InstructionProxy)(object)DispatchProxy.Create<ILogisticsInstructionService, InstructionProxy>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(ILogisticsInstructionService.GetSalesOrderDispatchNotesAsync):
                    OrderIds.Add((Guid)args![0]!);
                    return Failure is null ? Task.FromResult(Notes!) : Task.FromException<SalesOrderDispatchNotesView>(Failure);
                case nameof(ILogisticsInstructionService.GetInstructionDetailAsync):
                    return Failure is null ? Task.FromResult(Detail!) : Task.FromException<LoadingInstructionDetailView>(Failure);
                default:
                    throw new NotSupportedException(targetMethod?.Name);
            }
        }
    }
}
