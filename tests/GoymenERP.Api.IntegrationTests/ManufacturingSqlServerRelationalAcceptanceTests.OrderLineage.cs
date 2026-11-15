using System.Data.SqlTypes;
using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.Modules.Manufacturing.Domain.Lineage;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// URT-S22 (API-28, URT-D63, KLN-D44) on a REAL SQL Server: the order lineage read answers every output of the order
/// with all its input labels and the one calculator's shares, never another order's outputs, and one step upstream and
/// downstream only where another order produced or consumed one of its labels; an order without edges answers empty
/// lists, and the read stays under the caller's company (another company's id is refused, another company's context
/// sees nothing).
/// </summary>
public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task The_order_lineage_read_answers_every_output_with_all_its_inputs_and_shares_under_its_company_only()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var seed = await PrepareIssuedOrderAsync(run, "SYN-MFG-ORDER-LINEAGE", 100m);
        var other = await PrepareIssuedOrderAsync(run, "SYN-MFG-ORDER-LINEAGE-OTHER", 50m);
        var authority = new ManufacturingHostAuthority(GrantedManufacturingScope());
        ManufacturingInventoryOutputCommand first;
        ManufacturingInventoryOutputCommand second;
        await using (var scope = ReturnOutputHostScope.Create(run, authority, new ManufacturingInventoryOutputAdmission(
                         new ManufacturingOutputProducts(), new ManufacturingOutputLocations(seed.LocationId))))
        {
            first = OutputCommand(seed, seed.OrderVersion, seed.ParentLabelVersion, 60m);
            var firstResult = await scope.Host.DeclareOutputAsync(first,
                ManufacturingHostMetadata(run, "order-lineage-1"));
            second = OutputCommand(seed, firstResult.OrderVersion, firstResult.ParentLabelVersion, 30m) with
            {
                OccurredAtUtc = seed.OccurredAtUtc.AddMinutes(3),
            };
            await scope.Host.DeclareOutputAsync(second, ManufacturingHostMetadata(run, "order-lineage-2"));
        }

        ManufacturingInventoryOutputCommand foreignOutput;
        await using (var scope = ReturnOutputHostScope.Create(run, authority, new ManufacturingInventoryOutputAdmission(
                         new ManufacturingOutputProducts(), new ManufacturingOutputLocations(other.LocationId))))
        {
            foreignOutput = OutputCommand(other, other.OrderVersion, other.ParentLabelVersion, 40m);
            await scope.Host.DeclareOutputAsync(foreignOutput, ManufacturingHostMetadata(run, "order-lineage-other"));
        }

        await using (var reader = LineageReader(run, CompanyA, CompanyA))
        {
            var lineage = await reader.Port.ReadOrderAsync(CompanyA, seed.OrderId);

            Assert.Equal((CompanyA, seed.OrderId, false), (lineage.CompanyId, lineage.OrderId, lineage.Truncated));
            // One order: its outputs are ordered by output id in SQL Server's order, and the other order's output is not
            // among them.
            Assert.Equal(new[] { first.OutputId, second.OutputId }.OrderBy(id => new SqlGuid(id)).ToArray(),
                lineage.Outputs.Select(output => output.OutputId).ToArray());
            Assert.DoesNotContain(lineage.Outputs, output => output.OutputId == foreignOutput.OutputId);
            Assert.Equal(new[] { first.LabelId, second.LabelId }.ToHashSet(),
                lineage.Outputs.Select(output => output.OutputLabelId).ToHashSet());
            Assert.All(lineage.Outputs, output =>
            {
                Assert.Equal((CompanyA, seed.OrderId, nameof(ProductionLineageShareStatus.Calculated), (string?)null),
                    (output.CompanyId, output.OrderId, output.ShareStatus, output.ShareCode));
                var input = Assert.Single(output.Inputs);
                Assert.Equal((seed.ParentLabelId, (decimal?)1m), (input.InputLabelId, input.Share));
            });
            // No other order produced this order's input label or consumed its output labels.
            Assert.Empty(lineage.Upstream);
            Assert.Empty(lineage.Downstream);

            var foreign = await reader.Port.ReadOrderAsync(CompanyA, other.OrderId);
            var foreignLineage = Assert.Single(foreign.Outputs);
            Assert.Equal((foreignOutput.OutputId, foreignOutput.LabelId, other.ParentLabelId),
                (foreignLineage.OutputId, foreignLineage.OutputLabelId, Assert.Single(foreignLineage.Inputs).InputLabelId));

            var none = await reader.Port.ReadOrderAsync(CompanyA, Guid.NewGuid());
            Assert.Equal((false, 0, 0, 0),
                (none.Truncated, none.Outputs.Count, none.Upstream.Count, none.Downstream.Count));
            await Assert.ThrowsAsync<ArgumentException>(() => reader.Port.ReadOrderAsync(CompanyA, Guid.Empty));
            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.Port.ReadOrderAsync(CompanyB, seed.OrderId));
        }

        await using (var reader = LineageReader(run, CompanyB, CompanyB))
        {
            var hidden = await reader.Port.ReadOrderAsync(CompanyB, seed.OrderId);
            Assert.Equal((0, 0, 0), (hidden.Outputs.Count, hidden.Upstream.Count, hidden.Downstream.Count));
        }
    }
}
