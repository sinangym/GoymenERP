using GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-017 STOK F017-c (§3.5) against a REAL SQL Server: the goods receipt conversion link table's checks and keys, and the
/// forward-fix Down guard of <see cref="AddInventoryFutureSupplyConversions"/> (THROW 51711). Rows are written with raw SQL so that
/// the schema itself, not the conversion session, is what refuses. Every identity is synthetic test data in this test's own database.
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    private const string InsertFutureSupplyConversion =
        """
        INSERT INTO inventory.FutureSupplyConversions
            (Id, CompanyId, AcceptanceId, FutureAllocationId, LabelId, Outcome, Quantity, PhysicalAllocationId, PhysicalQuantityKg,
             FailureReason, CreatedAtUtc)
        VALUES (@id, @company, @acceptance, @allocation, @label, @outcome, @quantity, NULL, @kg, @failure, @at);
        """;

    [SqlServerFact]
    public async Task The_future_supply_conversion_migrations_Down_passes_on_an_empty_link_table_and_refuses_while_a_link_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddInventoryFutureSupplyConversions().DownOperations[0]).Sql;
        var (label, allocation) = await SeedConversionTargetsAsync(run);

        // Empty link table: an untouched store rolls back freely.
        await run.ExecuteAsync(guard);

        var acceptance = Guid.NewGuid();
        await run.ExecuteAsync(InsertFutureSupplyConversion,
            ConversionRow(acceptance, allocation, label, outcome: 2, quantity: 40m, kg: null, failure: "label-blocked"));
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));

        Assert.Equal(51711, refused.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.FutureSupplyConversions WHERE AcceptanceId = @acceptance", ("@acceptance", acceptance)));
    }

    [SqlServerFact]
    public async Task The_future_supply_conversion_checks_refuse_a_converted_link_without_physical_allocation_and_a_repeated_acceptance_link()
    {
        await using var run = await Run.StartAsync();
        var (label, allocation) = await SeedConversionTargetsAsync(run);
        var acceptance = Guid.NewGuid();

        var converted = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(InsertFutureSupplyConversion,
            ConversionRow(acceptance, allocation, label, outcome: 1, quantity: 40m, kg: 40m, failure: null)));
        var failedWithKg = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(InsertFutureSupplyConversion,
            ConversionRow(acceptance, allocation, label, outcome: 2, quantity: 40m, kg: 40m, failure: "label-blocked")));
        var failedWithoutReason = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(InsertFutureSupplyConversion,
            ConversionRow(acceptance, allocation, label, outcome: 2, quantity: 40m, kg: null, failure: "")));
        var zero = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(InsertFutureSupplyConversion,
            ConversionRow(acceptance, allocation, label, outcome: 2, quantity: 0m, kg: null, failure: "label-blocked")));

        Assert.All(new[] { converted, failedWithKg, failedWithoutReason, zero }, error =>
        {
            Assert.Equal(547, error.Number);
            Assert.Contains("CK_FutureSupplyConversions_Outcome", error.Message, StringComparison.Ordinal);
        });

        await run.ExecuteAsync(InsertFutureSupplyConversion,
            ConversionRow(acceptance, allocation, label, outcome: 2, quantity: 40m, kg: null, failure: "label-blocked"));
        var repeated = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(InsertFutureSupplyConversion,
            ConversionRow(acceptance, allocation, label, outcome: 2, quantity: 40m, kg: null, failure: "label-blocked")));

        Assert.Contains(repeated.Number, new[] { 2601, 2627 });
        Assert.Contains("UX_FutureSupplyConversions_Acceptance", repeated.Message, StringComparison.Ordinal);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.FutureSupplyConversions WHERE AcceptanceId = @acceptance", ("@acceptance", acceptance)));
    }

    /// <summary>A company A label and one active future allocation of a synthetic purchase line: what a link row must name.</summary>
    private static async Task<(Guid Label, Guid Allocation)> SeedConversionTargetsAsync(Run run)
    {
        var label = RawLabel(CompanyA, LotA);
        await using (var raw = run.Context(CompanyA))
        {
            raw.Labels.Add(label);
            await raw.SaveChangesAsync();
        }

        var supply = FutureSupplyIds.New();
        var allocation = Guid.NewGuid();
        await run.ExecuteAsync(InsertFutureSupplyLine, supply.Line(CompanyA, snapshot: 100m, active: 40m));
        await run.ExecuteAsync(InsertFutureSupplyAllocation,
            supply.Allocation(CompanyA, allocation, allocated: 40m, remaining: 40m, status: 1));
        return (label.Id, allocation);
    }

    private static (string Name, object Value)[] ConversionRow(
        Guid acceptance, Guid allocation, Guid label, int outcome, decimal quantity, decimal? kg, string? failure) =>
    [
        ("@id", Guid.NewGuid()), ("@company", CompanyA), ("@acceptance", acceptance), ("@allocation", allocation), ("@label", label),
        ("@outcome", outcome), ("@quantity", quantity), ("@kg", (object?)kg ?? DBNull.Value),
        ("@failure", (object?)failure ?? DBNull.Value), ("@at", At),
    ];
}
