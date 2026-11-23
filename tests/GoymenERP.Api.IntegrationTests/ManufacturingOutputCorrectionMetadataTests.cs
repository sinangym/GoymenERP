using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ManufacturingOutputCorrectionMetadataTests
{
    [Fact]
    public void Output_correction_inventory_keys_are_bounded_stable_and_separate_each_owner_fact()
    {
        var company = Guid.NewGuid();
        var order = Guid.NewGuid();
        var reversal = Guid.NewGuid();
        var metadata = new ManufacturingCommandMetadata("SYNTHETIC-maker", ErpPermissions.ManufacturingFactsCorrect,
            ["SYNTHETIC-MANUFACTURING"], "SYNTHETIC-correlation", "SYNTHETIC-causation", "SYNTHETIC-caller-key", "Integration");
        var actual = ManufacturingInventoryReturnOutputHost.OutputCorrectionInventoryMetadata(metadata, company, order, reversal);
        Assert.InRange(actual.IdempotencyKey.Length, 1, 128);
        Assert.Equal(actual, ManufacturingInventoryReturnOutputHost.OutputCorrectionInventoryMetadata(metadata, company, order, reversal));
        // The internal fact key remains stable when an outer caller uses another command key or conversation.
        var repeated = ManufacturingInventoryReturnOutputHost.OutputCorrectionInventoryMetadata(
            metadata with { IdempotencyKey = "SYNTHETIC-other-key", CorrelationId = "SYNTHETIC-other-conversation" },
            company, order, reversal);
        Assert.Equal(actual.IdempotencyKey, repeated.IdempotencyKey);
        Assert.Equal(metadata.ActorId, actual.ActorId);
        Assert.Equal(metadata.PermissionCode, actual.PermissionCode);
        Assert.Equal(metadata.RoleCodes, actual.RoleCodes);
        Assert.Equal(metadata.CorrelationId, actual.CorrelationId);
        Assert.Equal(metadata.CausationId, actual.CausationId);
        Assert.Equal(metadata.SourceChannel, actual.SourceChannel);
        var distinctKeys = new[]
        {
            actual.IdempotencyKey,
            ManufacturingInventoryReturnOutputHost.OutputCorrectionInventoryMetadata(metadata, Guid.NewGuid(), order, reversal).IdempotencyKey,
            ManufacturingInventoryReturnOutputHost.OutputCorrectionInventoryMetadata(metadata, company, Guid.NewGuid(), reversal).IdempotencyKey,
            ManufacturingInventoryReturnOutputHost.OutputCorrectionInventoryMetadata(metadata, company, order, Guid.NewGuid()).IdempotencyKey,
        };
        Assert.Equal(4, distinctKeys.Distinct(StringComparer.Ordinal).Count());
        Assert.All(distinctKeys, key =>
        {
            Assert.InRange(key.Length, 1, 128);
            Assert.StartsWith("mfg-output-correction:", key, StringComparison.Ordinal);
            Assert.False(key.StartsWith("manufacturing-output:", StringComparison.Ordinal));
            Assert.False(key.StartsWith("manufacturing-return:", StringComparison.Ordinal));
        });
    }
}
