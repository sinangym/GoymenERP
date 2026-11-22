using GoymenERP.Api.Ai;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Inventory.Application.Ai;
using GoymenERP.Modules.Manufacturing.Application.Ai;
using GoymenERP.Modules.Purchasing.Application.Ai;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP-A3b: the nine Inventory / Manufacturing / Purchasing READ tools are registered by their owner modules in the real
/// host composition, every dependency resolves, and the default configuration (<c>Ai:AllowedTools</c> empty) admits none
/// of them. The catalog contract itself is checked against these descriptors in <see cref="AiYa1ReadToolsTests"/>.
/// </summary>
public sealed class AiWpa3bReadToolsTests
{
    private static readonly string[] ToolIds =
    [
        InventoryStockPositionsAiTool.ToolId,
        InventoryNegativeStockAttemptsAiTool.ToolId,
        InventoryPendingStockCountsAiTool.ToolId,
        ManufacturingOrderStatusAiTool.ToolId,
        ManufacturingPlanStatusAiTool.ToolId,
        ManufacturingWorkCenterQueueAiTool.ToolId,
        PurchasingOrderStatusAiTool.ToolId,
        PurchasingPendingReceiptsAiTool.ToolId,
        PurchasingInvoiceVariancesAiTool.ToolId,
    ];

    /// <summary>The descriptors only; the owner reads are never called to build a descriptor.</summary>
    internal static IReadOnlyList<AiToolDescriptorV1> Descriptors() =>
    [
        new InventoryStockPositionsAiTool(null!, null!, TimeProvider.System).Descriptor,
        new InventoryNegativeStockAttemptsAiTool(null!, null!, TimeProvider.System).Descriptor,
        new InventoryPendingStockCountsAiTool(null!, null!, TimeProvider.System).Descriptor,
        new ManufacturingOrderStatusAiTool(null!, null!, TimeProvider.System).Descriptor,
        new ManufacturingPlanStatusAiTool(null!, null!, TimeProvider.System).Descriptor,
        new ManufacturingWorkCenterQueueAiTool(null!, null!, TimeProvider.System).Descriptor,
        new PurchasingOrderStatusAiTool(null!, null!, TimeProvider.System).Descriptor,
        new PurchasingPendingReceiptsAiTool(null!, null!, TimeProvider.System).Descriptor,
        new PurchasingInvoiceVariancesAiTool(null!, null!, TimeProvider.System).Descriptor,
    ];

    [Fact]
    public void The_host_registers_and_resolves_the_nine_owner_read_tools_and_the_default_allowlist_admits_none()
    {
        using var factory = new GoymenErpApiFactory();
        using var scope = factory.Services.CreateScope();

        var registered = scope.ServiceProvider.GetServices<IAiOwnerToolV1>().Select(tool => tool.Descriptor.ToolId).ToArray();
        foreach (var toolId in ToolIds)
        {
            Assert.Single(registered, id => id == toolId);
        }

        var catalog = scope.ServiceProvider.GetRequiredService<AiToolCatalog>();
        Assert.DoesNotContain(catalog.Descriptors, descriptor => ToolIds.Contains(descriptor.ToolId));
    }

    [Fact]
    public void Every_tool_is_a_read_tool_without_an_owner_command_and_only_the_invoice_variance_tool_is_confidential()
    {
        var descriptors = Descriptors();

        Assert.Equal(ToolIds.Order(StringComparer.Ordinal), descriptors.Select(d => d.ToolId).Order(StringComparer.Ordinal));
        Assert.All(descriptors, descriptor =>
        {
            Assert.Equal(AiToolRiskClassV1.Read, descriptor.Risk);
            Assert.Null(descriptor.OwnerCommandName);
            Assert.NotEmpty(descriptor.RequiredPermissions);
            Assert.Contains("limit", descriptor.FieldAllowlist);
            Assert.DoesNotContain("companyId", descriptor.FieldAllowlist);
        });
        Assert.Equal(new[] { PurchasingInvoiceVariancesAiTool.ToolId },
            descriptors.Where(d => d.DataClass == AiDataClassV1.Confidential).Select(d => d.ToolId).ToArray());
        Assert.All(descriptors.Where(d => d.ToolId != PurchasingInvoiceVariancesAiTool.ToolId),
            descriptor => Assert.Equal(AiDataClassV1.Internal, descriptor.DataClass));
    }

    [Fact]
    public void The_catalog_admits_a_tool_only_by_its_exact_allowlisted_id_and_version()
    {
        var tools = new IAiOwnerToolV1[]
        {
            new InventoryStockPositionsAiTool(null!, null!, TimeProvider.System),
            new PurchasingInvoiceVariancesAiTool(null!, null!, TimeProvider.System),
        };
        var none = new AiToolCatalog(tools, Options.Create(new AiRuntimeOptions { AllowedTools = [] }));
        var one = new AiToolCatalog(tools, Options.Create(new AiRuntimeOptions
        {
            Enabled = true,
            Provider = AiProviderKindsV1.Fake,
            PolicyRef = "TEST-AI-POLICY",
            PolicyVersion = 1,
            MaxQuestionLength = 200,
            MaxArgumentBytes = 256,
            MaxToolCallsPerRequest = 1,
            MaxDataClass = AiDataClassV1.Confidential,
            AllowedTools =
            [
                new AiAllowedToolOptions { ToolId = InventoryStockPositionsAiTool.ToolId, Version = "1" },
                new AiAllowedToolOptions { ToolId = PurchasingInvoiceVariancesAiTool.ToolId, Version = "2" },
            ],
        }));

        Assert.Empty(none.Descriptors);
        Assert.Equal(new[] { InventoryStockPositionsAiTool.ToolId }, one.Descriptors.Select(d => d.ToolId).ToArray());
    }
}
