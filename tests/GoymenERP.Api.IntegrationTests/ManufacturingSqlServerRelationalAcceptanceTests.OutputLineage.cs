using System.Data.SqlTypes;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Lineage;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// URT-S20 (URT-D62, URT-D63) through the composed output host on a REAL SQL Server: a declared output records one
/// Manufacturing lineage edge per input label Inventory staged for the output's genealogy, in the same transaction and
/// with the same consumed kg (Manufacturing never writes Inventory's label tables); a replay writes nothing more; the
/// read port answers the edges backward by output label and forward by input label with the one calculator's shares,
/// under the caller's company only; the store refuses an edge set it cannot read or that differs from the stored one;
/// and the migration's Down passes while no edge exists and refuses (51543) once one is stored.
/// </summary>
public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_declared_output_records_its_input_label_lineage_edges_once_with_the_inventory_genealogy_kg()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var seed = await PrepareIssuedOrderAsync(run, "SYN-MFG-LINEAGE", 100m);
        var authority = new ManufacturingHostAuthority(GrantedManufacturingScope());
        var admission = new ManufacturingInventoryOutputAdmission(
            new ManufacturingOutputProducts(), new ManufacturingOutputLocations(seed.LocationId));
        await using var scope = ReturnOutputHostScope.Create(run, authority, admission);
        var output = OutputCommand(seed, seed.OrderVersion, seed.ParentLabelVersion, 80m);
        var outputMetadata = ManufacturingHostMetadata(run, "lineage-output");

        var result = await scope.Host.DeclareOutputAsync(output, outputMetadata);

        Assert.False(result.IsReplay);
        // The issued input is the only net-issued label of the order: one edge, carrying the whole issued kg.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.OutputLineageEdges WHERE OutputId = @output",
            ("@output", output.OutputId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.OutputLineageEdges WHERE OutputId = @output AND OrderId = @order "
            + "AND OutputKind = 3 AND InputLabelId = @input AND OutputLabelId = @label AND ConsumedKg = 100 "
            + "AND CalculatorVersion = @version AND CompanyId = @company AND CorrelationId = @correlation",
            ("@output", output.OutputId), ("@order", seed.OrderId), ("@input", seed.ParentLabelId),
            ("@label", output.LabelId), ("@version", ProductionLineageShareCalculatorV1.ContractVersion),
            ("@company", CompanyA), ("@correlation", outputMetadata.CorrelationId)));

        // Every edge is the same input, transformation and kg Inventory recorded for the output label's genealogy.
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.OutputLineageEdges edge WHERE edge.OutputId = @output AND NOT EXISTS ("
            + "SELECT 1 FROM inventory.LabelTransformationInputs input "
            + "JOIN inventory.LabelTransformationOutputs result ON result.CompanyId = input.CompanyId "
            + "AND result.TransformationId = input.TransformationId "
            + "WHERE input.CompanyId = edge.CompanyId AND input.TransformationId = edge.GenealogyTransformationId "
            + "AND input.LabelId = edge.InputLabelId AND input.QuantityKg = edge.ConsumedKg "
            + "AND result.LabelId = edge.OutputLabelId)",
            ("@output", output.OutputId)));
        Assert.Equal(await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM inventory.LabelTransformationInputs input "
                + "JOIN inventory.LabelTransformationOutputs result ON result.CompanyId = input.CompanyId "
                + "AND result.TransformationId = input.TransformationId WHERE result.LabelId = @label",
                ("@label", output.LabelId)),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM manufacturing.OutputLineageEdges WHERE OutputLabelId = @label",
                ("@label", output.LabelId)));

        var replayMetadata = ManufacturingHostMetadata(run, "lineage-output-replay");
        var replay = await scope.Host.DeclareOutputAsync(output, replayMetadata);

        Assert.True(replay.IsReplay);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.OutputLineageEdges WHERE OutputId = @output",
            ("@output", output.OutputId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @correlation",
            ("@correlation", replayMetadata.CorrelationId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @correlation "
            + "AND EventType = N'manufacturing.output.lineage.recorded.v1' "
            + "AND EntityType = N'Manufacturing.ProductionOrder' AND EntityId = @order",
            ("@correlation", outputMetadata.CorrelationId), ("@order", seed.OrderId.ToString("D"))));
    }

    [SqlServerFact]
    public async Task The_lineage_read_port_answers_backward_and_forward_with_the_calculator_shares_under_its_company_only()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var seed = await PrepareIssuedOrderAsync(run, "SYN-MFG-LINEAGE-READ", 100m);
        var authority = new ManufacturingHostAuthority(GrantedManufacturingScope());
        var admission = new ManufacturingInventoryOutputAdmission(
            new ManufacturingOutputProducts(), new ManufacturingOutputLocations(seed.LocationId));
        await using var scope = ReturnOutputHostScope.Create(run, authority, admission);
        var first = OutputCommand(seed, seed.OrderVersion, seed.ParentLabelVersion, 60m);
        var firstResult = await scope.Host.DeclareOutputAsync(first, ManufacturingHostMetadata(run, "lineage-read-1"));
        var second = OutputCommand(seed, firstResult.OrderVersion, firstResult.ParentLabelVersion, 30m) with
        {
            OccurredAtUtc = seed.OccurredAtUtc.AddMinutes(3),
        };
        await scope.Host.DeclareOutputAsync(second, ManufacturingHostMetadata(run, "lineage-read-2"));

        await using (var reader = LineageReader(run, CompanyA, CompanyA))
        {
            var backward = await reader.Port.ReadBackwardAsync(CompanyA, first.LabelId);

            Assert.NotNull(backward);
            Assert.Equal((CompanyA, seed.OrderId, first.OutputId, first.LabelId),
                (backward.CompanyId, backward.OrderId, backward.OutputId, backward.OutputLabelId));
            Assert.Equal((ProductionLineageShareCalculatorV1.ContractVersion,
                    nameof(ProductionLineageShareStatus.Calculated), (string?)null, (decimal?)100m),
                (backward.CalculatorVersion, backward.ShareStatus, backward.ShareCode, backward.TotalConsumedKg));
            var input = Assert.Single(backward.Inputs);
            Assert.Equal((seed.ParentLabelId, 100m, (decimal?)1m), (input.InputLabelId, input.ConsumedKg, input.Share));
            Assert.Equal(await run.ScalarAsync<Guid>(
                    "SELECT TOP (1) TransformationId FROM inventory.LabelTransformationOutputs WHERE LabelId = @label",
                    ("@label", first.LabelId)),
                backward.GenealogyTransformationId);

            var forward = await reader.Port.ReadForwardAsync(CompanyA, seed.ParentLabelId);

            // Both outputs belong to one order, so the forward read is ordered by output id in SQL Server's order.
            Assert.Equal(new[] { first.OutputId, second.OutputId }.OrderBy(id => new SqlGuid(id)).ToArray(),
                forward.Select(lineage => lineage.OutputId).ToArray());
            Assert.Equal(new[] { first.LabelId, second.LabelId }.ToHashSet(),
                forward.Select(lineage => lineage.OutputLabelId).ToHashSet());
            Assert.All(forward, lineage => Assert.Equal(seed.ParentLabelId, Assert.Single(lineage.Inputs).InputLabelId));

            // An output label is never an input of its own lineage, and a label without edges has no lineage.
            Assert.Empty(await reader.Port.ReadForwardAsync(CompanyA, first.LabelId));
            Assert.Null(await reader.Port.ReadBackwardAsync(CompanyA, seed.ParentLabelId));
            await Assert.ThrowsAsync<ArgumentException>(() => reader.Port.ReadBackwardAsync(CompanyA, Guid.Empty));
        }

        // Deny-first: another company's id under this company's context is refused, and under the other company's own
        // context the tenant filter shows nothing of this company.
        await using (var reader = LineageReader(run, CompanyA, CompanyA))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.Port.ReadBackwardAsync(CompanyB, first.LabelId));
            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.Port.ReadForwardAsync(CompanyB, seed.ParentLabelId));
        }

        await using (var reader = LineageReader(run, CompanyB, CompanyB))
        {
            Assert.Null(await reader.Port.ReadBackwardAsync(CompanyB, first.LabelId));
            Assert.Empty(await reader.Port.ReadForwardAsync(CompanyB, seed.ParentLabelId));
        }
    }

    [SqlServerFact]
    public async Task The_lineage_store_refuses_an_unreadable_or_different_edge_set_and_replays_the_same_one()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var seed = await PrepareIssuedOrderAsync(run, "SYN-MFG-LINEAGE-STORE", 100m);
        var authority = new ManufacturingHostAuthority(GrantedManufacturingScope());
        var admission = new ManufacturingInventoryOutputAdmission(
            new ManufacturingOutputProducts(), new ManufacturingOutputLocations(seed.LocationId));
        await using var scope = ReturnOutputHostScope.Create(run, authority, admission);
        var output = OutputCommand(seed, seed.OrderVersion, seed.ParentLabelVersion, 80m);
        await scope.Host.DeclareOutputAsync(output, ManufacturingHostMetadata(run, "lineage-store-output"));
        var transformation = await run.ScalarAsync<Guid>(
            "SELECT GenealogyTransformationId FROM manufacturing.OutputLineageEdges WHERE OutputId = @output",
            ("@output", output.OutputId));
        ProductionLineageShareInput[] stored = [new(seed.ParentLabelId, new Kilograms(100m))];

        Assert.Equal(ManufacturingWriteOutcome.Replayed, await scope.Store.RecordOutputLineageAsync(
            seed.OrderId, output.OutputId, output.LabelId, transformation, stored, outputReplayed: true,
            ManufacturingHostMetadata(run, "lineage-store-replay")));

        var different = await Assert.ThrowsAsync<ManufacturingStoreException>(() => scope.Store.RecordOutputLineageAsync(
            seed.OrderId, output.OutputId, output.LabelId, transformation,
            [new ProductionLineageShareInput(seed.ParentLabelId, new Kilograms(99m))], outputReplayed: true,
            ManufacturingHostMetadata(run, "lineage-store-different")));
        Assert.Equal(ManufacturingStoreCodes.FactIdConflict, different.Code);

        var selfReference = await Assert.ThrowsAsync<ManufacturingStoreException>(() => scope.Store.RecordOutputLineageAsync(
            seed.OrderId, output.OutputId, output.LabelId, transformation,
            [new ProductionLineageShareInput(output.LabelId, new Kilograms(1m))], outputReplayed: false,
            ManufacturingHostMetadata(run, "lineage-store-self")));
        Assert.Equal(ManufacturingStoreCodes.LineageInvalid, selfReference.Code);

        var unreadable = await Assert.ThrowsAsync<ManufacturingStoreException>(() => scope.Store.RecordOutputLineageAsync(
            seed.OrderId, output.OutputId, output.LabelId, transformation,
            [new ProductionLineageShareInput(seed.ParentLabelId, Kilograms.Zero)], outputReplayed: false,
            ManufacturingHostMetadata(run, "lineage-store-zero")));
        Assert.Equal(ManufacturingStoreCodes.LineageInvalid, unreadable.Code);

        var undeclared = await Assert.ThrowsAsync<ManufacturingStoreException>(() => scope.Store.RecordOutputLineageAsync(
            seed.OrderId, Guid.NewGuid(), Guid.NewGuid(), transformation, stored, outputReplayed: false,
            ManufacturingHostMetadata(run, "lineage-store-undeclared")));
        Assert.Equal(ManufacturingStoreCodes.LineageInvalid, undeclared.Code);

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.OutputLineageEdges"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.output.lineage.recorded.v1'"));
    }

    [SqlServerFact]
    public async Task The_output_lineage_migrations_Down_passes_with_no_edge_and_refuses_while_an_edge_exists()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var guard = Assert.IsType<SqlOperation>(new AddManufacturingOutputLineageEdges().DownOperations[0]).Sql;
        await run.ScalarAsync<int>(guard + "; SELECT 1");

        var seed = await PrepareIssuedOrderAsync(run, "SYN-MFG-LINEAGE-DOWN", 100m);
        var authority = new ManufacturingHostAuthority(GrantedManufacturingScope());
        var admission = new ManufacturingInventoryOutputAdmission(
            new ManufacturingOutputProducts(), new ManufacturingOutputLocations(seed.LocationId));
        await using (var scope = ReturnOutputHostScope.Create(run, authority, admission))
        {
            await scope.Host.DeclareOutputAsync(OutputCommand(seed, seed.OrderVersion, seed.ParentLabelVersion, 80m),
                ManufacturingHostMetadata(run, "lineage-down-output"));
        }

        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + "; SELECT 1"));
        Assert.Equal(51543, refused.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.OutputLineageEdges"));
    }

    private static LineageReaderScope LineageReader(Run run, Guid contextCompany, Guid portCompany)
    {
        var company = new CompanyContext();
        company.Set(portCompany);
        var context = run.Context(contextCompany);
        return new LineageReaderScope(context, new ManufacturingLabelLineageReadPort(context, company));
    }

    private sealed class LineageReaderScope(ManufacturingDbContext context, IManufacturingLabelLineageReadPortV1 port)
        : IAsyncDisposable
    {
        public IManufacturingLabelLineageReadPortV1 Port => port;

        public ValueTask DisposeAsync() => context.DisposeAsync();
    }
}
