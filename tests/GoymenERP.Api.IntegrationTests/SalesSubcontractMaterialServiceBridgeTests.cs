using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class SalesSubcontractMaterialServiceBridgeTests
{
    [Fact]
    public async Task Typed_subcontract_weighing_retains_physical_material_and_maps_only_the_actual_sales_service_identity_for_invoicing()
    {
        var company = Guid.NewGuid(); var instruction = Guid.NewGuid(); var weighing = Guid.NewGuid(); var material = Guid.NewGuid(); var service = Guid.NewGuid();
        var source = Source(company, instruction, weighing, material, service, true);
        var mapped = await new SalesSubcontractDeliveryWeighingHost(new Owner(source)).ReadAsync(company, instruction, weighing);
        Assert.Equal(SalesSubcontractDeliveryWeighingStatusV1.Available, mapped.Status);
        Assert.Equal(service, Assert.Single(mapped.Value!.Lines).ProductId);
        Assert.Equal(material, Assert.Single(source.Value!.Lines).ProductId);
        Assert.Equal("79.125", mapped.Value.NetKg); Assert.Equal(2, mapped.Value.WeighingRevision);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(true, "00000000-0000-0000-0000-000000000000")]
    [InlineData(false, "22222222-2222-4222-8222-222222222222")]
    public async Task Missing_or_contradictory_typed_service_proof_never_falls_back_to_the_material(bool typed, string? serviceText)
    {
        var company = Guid.NewGuid(); var instruction = Guid.NewGuid(); var weighing = Guid.NewGuid();
        var source = Source(company, instruction, weighing, Guid.NewGuid(), serviceText is null ? null : Guid.Parse(serviceText), typed);
        var answer = await new SalesSubcontractDeliveryWeighingHost(new Owner(source)).ReadAsync(company, instruction, weighing);
        Assert.Equal(SalesSubcontractDeliveryWeighingStatusV1.Unavailable, answer.Status); Assert.Null(answer.Value);
    }

    [Fact]
    public async Task Legacy_sale_weight_contract_keeps_its_original_product_without_a_new_service_claim()
    {
        var company = Guid.NewGuid(); var instruction = Guid.NewGuid(); var weighing = Guid.NewGuid(); var product = Guid.NewGuid();
        var source = Source(company, instruction, weighing, product, null, false);
        Assert.Equal(product, Assert.Single((await new SalesSubcontractDeliveryWeighingHost(new Owner(source)).ReadAsync(company, instruction, weighing)).Value!.Lines).ProductId);
    }

    private static LogisticsSubcontractDeliveryNetWeightReadV1 Source(Guid company, Guid instruction, Guid weighing, Guid material, Guid? service, bool typed) =>
        LogisticsSubcontractDeliveryNetWeightReadV1.Available(company, instruction, weighing, new("79.125", "kg", LogisticsNetWeightMeasurementKindV1.ManualWeighbridge,
            "TareFirst", "80", "-0.875", 2, 8, DateTimeOffset.UtcNow,
            [new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, material, Guid.NewGuid(), "80", "80", "80", "0", service)], [], typed));
    private sealed class Owner(LogisticsSubcontractDeliveryNetWeightReadV1 answer) : ILogisticsSubcontractDeliveryNetWeightReadPortV1
    {
        public Task<LogisticsSubcontractDeliveryNetWeightReadV1> GetAsync(Guid company, Guid instruction, Guid weighing, CancellationToken token = default) => Task.FromResult(answer);
    }
}
