using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// URT-S16 (URT-D43, BS-5, BS-14) through the composed host on a REAL SQL Server: every stockable output opens its
/// Inventory label with the fixed classification of its output class through the V2 output declaration; a usable
/// remnant opens a SecondQuality label by default or the Standard label the operator chose, and carries the separate
/// UsableRemnant production status. A remnant classification the mapping does not allow writes nothing.
/// </summary>
public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Manufacturing_output_labels_take_the_fixed_class_mapping_and_a_usable_remnant_its_production_status()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var seed = await PrepareIssuedOrderAsync(run, "SYN-MFG-OUTPUT-LABEL-CLASS", 100m);
        var authority = new ManufacturingHostAuthority(GrantedManufacturingScope());
        var admission = new ManufacturingInventoryOutputAdmission(
            new ManufacturingOutputProducts(), new ManufacturingOutputLocations(seed.LocationId));
        await using var scope = ReturnOutputHostScope.Create(run, authority, admission);

        var refused = OutputCommand(seed, seed.OrderVersion, seed.ParentLabelVersion, 10m) with
        {
            OutputClass = OutputClass.UsableRemnant,
            RemnantClassification = InventoryLabelClassificationV1.Scrap,
        };
        var refusedMetadata = ManufacturingHostMetadata(run, "output-label-remnant-scrap");
        await Assert.ThrowsAsync<ArgumentException>(() => scope.Host.DeclareOutputAsync(refused, refusedMetadata));
        await AssertNoOutputEffectAsync(run, seed, refused, refusedMetadata.CorrelationId);

        (OutputClass OutputClass, InventoryLabelClassificationV1? Chosen, InventoryLabelClassificationV1 Classification,
            InventoryLabelProductionStatusV1? ProductionStatus)[] cases =
        [
            (OutputClass.GoodProduct, null, InventoryLabelClassificationV1.Standard, null),
            (OutputClass.SecondQuality, null, InventoryLabelClassificationV1.SecondQuality, null),
            (OutputClass.UsableRemnant, null, InventoryLabelClassificationV1.SecondQuality,
                InventoryLabelProductionStatusV1.UsableRemnant),
            (OutputClass.UsableRemnant, InventoryLabelClassificationV1.Standard, InventoryLabelClassificationV1.Standard,
                InventoryLabelProductionStatusV1.UsableRemnant),
        ];
        var orderVersion = seed.OrderVersion;
        var parentLabelVersion = seed.ParentLabelVersion;
        var minute = 2;
        ManufacturingInventoryOutputCommand? last = null;
        foreach (var (outputClass, chosen, classification, productionStatus) in cases)
        {
            var command = OutputCommand(seed, orderVersion, parentLabelVersion, 10m) with
            {
                OutputClass = outputClass,
                RemnantClassification = chosen,
                OccurredAtUtc = seed.OccurredAtUtc.AddMinutes(minute++),
            };
            var result = await scope.Host.DeclareOutputAsync(command,
                ManufacturingHostMetadata(run, $"output-label-{outputClass}"));

            Assert.False(result.IsReplay);
            Assert.Equal(1, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM inventory.Labels WHERE Id = @id AND Classification = @classification AND "
                + (productionStatus is null ? "ProductionStatus IS NULL" : "ProductionStatus = @status"),
                ("@id", command.LabelId), ("@classification", (int)classification),
                ("@status", (int)(productionStatus ?? InventoryLabelProductionStatusV1.UsableRemnant))));
            Assert.Equal(InventoryManufacturingRealizationContractV2.OutputCommandName,
                await run.ScalarAsync<string>(
                    "SELECT CommandName FROM inventory.Commands WHERE TargetId = @id", ("@id", command.OutputId)));
            Assert.Equal(1, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM manufacturing.Facts WHERE Id = @id", ("@id", command.OutputId)));
            orderVersion = result.OrderVersion;
            parentLabelVersion = result.ParentLabelVersion;
            last = command;
        }

        // The operator's remnant choice is part of the replayed V2 declaration: the same command replays once.
        Assert.NotNull(last);
        var replayMetadata = ManufacturingHostMetadata(run, "output-label-remnant-replay");
        var replay = await scope.Host.DeclareOutputAsync(last, replayMetadata);
        Assert.True(replay.IsReplay);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE TargetId = @id", ("@id", last.OutputId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @correlation",
            ("@correlation", replayMetadata.CorrelationId)));
    }
}
