using GoymenERP.Api.LogisticsRuntime;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Logistics pins Inventory's definite-shipment 409 set as its durable terminal refusals without an application
/// reference to Inventory. Only the host sees both owners, so the drift check lives here: a new Inventory
/// shipment refusal (e.g. the Sales ownership gate's inventory_definite_shipment_owner_not_company) must become
/// a Logistics terminal attempt in the same change, never an unknown outcome retried forever.
/// </summary>
public sealed class LogisticsInventoryTerminalCodeDriftTests
{
    [Fact]
    public void Logistics_terminal_inventory_codes_are_exactly_the_inventory_definite_shipment_409_codes()
    {
        Assert.Equal(
            InventoryDefiniteShipmentCodesV1.Status409Codes.Order(StringComparer.Ordinal),
            LogisticsRealizationDependenciesV1.InventoryTerminalConflictCodes.Order(StringComparer.Ordinal));
    }
    [Fact]
    public void Only_a_proven_physical_guard_collision_uses_the_existing_nonterminal_shipment_path()
    {
        var cause = new InventoryStoreException(InventoryStoreCodes.ConcurrentChange, "SENTETIK guard collision");
        var physical = InventoryDefiniteShipmentException.Refused(409, InventoryDefiniteShipmentCodesV1.ConcurrentChange, cause);
        var mapped = Assert.IsType<LogisticsStoreException>(LogisticsRealizationTransactionRunner.MapShipmentRefusal(physical));
        Assert.Equal(LogisticsStoreCodes.ConcurrentChange, mapped.Code);
        Assert.Same(physical, mapped.InnerException);
        Assert.Same(cause, mapped.InnerException!.InnerException);
        LogisticsRealizationTransactionRunner.RequirePhysicalGuardRollback(true, mapped);
        var unsafeRollback = Assert.Throws<LogisticsInstructionServiceException>(() =>
            LogisticsRealizationTransactionRunner.RequirePhysicalGuardRollback(false, mapped));
        Assert.Equal((503, LogisticsRealizationCodesV1.Unavailable), (unsafeRollback.Status, unsafeRollback.Code));

        InventoryDefiniteShipmentException[] terminal =
        [
            InventoryDefiniteShipmentException.Refused(409, InventoryDefiniteShipmentCodesV1.ConcurrentChange),
            InventoryDefiniteShipmentException.Refused(409, InventoryDefiniteShipmentCodesV1.AllocationVersionConflict, cause),
            InventoryDefiniteShipmentException.Refused(409, InventoryDefiniteShipmentCodesV1.ConcurrentChange,
                new InventoryStoreException(InventoryStoreCodes.LabelNotRegistered, "SENTETIK another cause")),
            InventoryDefiniteShipmentException.Refused(409, InventoryDefiniteShipmentCodesV1.ConcurrentChange,
                new InvalidOperationException("SENTETIK unrelated failure", cause)),
            InventoryDefiniteShipmentException.Refused(503, InventoryDefiniteShipmentCodesV1.Unavailable, cause),
        ];
        Assert.All(terminal, error =>
        {
            var unchanged = Assert.IsType<LogisticsInstructionServiceException>(LogisticsRealizationTransactionRunner.MapShipmentRefusal(error));
            Assert.Equal((error.Status, error.Code), (unchanged.Status, unchanged.Code));
            Assert.Same(error, unchanged.InnerException);
            LogisticsRealizationTransactionRunner.RequirePhysicalGuardRollback(false, unchanged);
        });
    }

}
