using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// B-03 (API-22/27, DEC-PRDCT-003/007, URT-D8/URT-D14): a Manufacturing output records one kind 3 genealogy to the
/// order's net-issued input labels, and the V2 output creates a classified label or refuses before any write.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Manufacturing_output_genealogy_links_only_net_issued_inputs_and_replays_the_same_edges()
    {
        await using var run = await Run.StartAsync();
        var kept = await SeedManufacturingParentAsync(run, "SYN-MFG-GEN-KEPT", 100m);
        var returnedInput = await SeedManufacturingParentAsync(run, "SYN-MFG-GEN-RETURNED", 50m);
        var orderId = Guid.NewGuid();

        var keptIssue = await CommitIssueAsync(run, IssueRequest(orderId, kept.Label.Id, "2", "30"),
            ManufacturingMetadata(run, "gen-issue-kept"));
        var keptReturn = await CommitReturnAsync(run,
            ReturnRequest(orderId, kept.Label.Id, keptIssue.ParentLabelVersion, "7.5"),
            ManufacturingMetadata(run, "gen-return-kept"));
        var returnedIssue = await CommitIssueAsync(run, IssueRequest(orderId, returnedInput.Label.Id, "2", "20"),
            ManufacturingMetadata(run, "gen-issue-returned"));
        await CommitReturnAsync(run,
            ReturnRequest(orderId, returnedInput.Label.Id, returnedIssue.ParentLabelVersion, "20"),
            ManufacturingMetadata(run, "gen-return-returned"));

        var request = OutputRequestV2(orderId, kept.Label.Id, keptReturn.ParentLabelVersion, kept.LocationId,
            "18.25", InventoryManufacturingOutputClassificationV2.GoodProduct);
        var metadata = ManufacturingMetadata(run, "gen-output");
        var output = await CommitOutputV2Async(run, CompanyA, request, metadata);

        Assert.False(output.IsReplay);
        Assert.Equal((InventoryManufacturingOutputClassificationV2.GoodProduct, InventoryLabelClassificationV1.Standard),
            (output.OutputClass, output.Classification));
        var input = Assert.Single(output.GenealogyInputs);
        Assert.Equal((kept.Label.Id, keptReturn.ParentLabelVersion, "22.5"),
            (input.LabelId, input.LabelVersion, input.QuantityKg));
        Assert.NotEqual(request.OutputId, output.GenealogyTransformationId);

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.LabelTransformations WHERE CompanyId = @company AND Id = @id "
            + "AND Kind = 3 AND Reason = @reason",
            ("@company", CompanyA), ("@id", output.GenealogyTransformationId),
            ("@reason", InventoryStore.ManufacturingGenealogyReason)));
        Assert.Equal(22.5m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.LabelTransformationInputs "
            + "WHERE TransformationId = @id AND LabelId = @label AND Sequence = 1",
            ("@id", output.GenealogyTransformationId), ("@label", kept.Label.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.LabelTransformationInputs WHERE LabelId = @label",
            ("@label", returnedInput.Label.Id)));
        Assert.Equal(18.25m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.LabelTransformationOutputs "
            + "WHERE TransformationId = @id AND LabelId = @label AND Sequence = 1",
            ("@id", output.GenealogyTransformationId), ("@label", request.LabelId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Labels WHERE Id = @label AND Classification = 1",
            ("@label", request.LabelId)));

        await using (var scope = run.Store(CompanyA))
        {
            var genealogy = (await scope.Store.ReadAsync(request.LabelId))!;
            var edge = Assert.Single(genealogy.Edges);
            Assert.Equal(("Manufacturing", kept.Label.Id, request.LabelId, "22.5", "18.25"),
                (edge.Kind, edge.SourceLabelId, edge.OutputLabelId, edge.SourceQuantityKg, edge.OutputQuantityKg));
        }

        // A later return against the same order no longer changes the recorded edges: replay never re-derives them.
        await CommitReturnAsync(run,
            ReturnRequest(orderId, kept.Label.Id, keptReturn.ParentLabelVersion, "2.5"),
            ManufacturingMetadata(run, "gen-return-after-output"));
        var replay = await CommitOutputV2Async(run, CompanyA, request,
            metadata with { CorrelationId = "manufacturing-gen-output-retry" });
        Assert.True(replay.IsReplay);
        Assert.Equal(metadata.CorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(output.GenealogyTransformationId, replay.GenealogyTransformationId);
        Assert.Equal(output.GenealogyInputs, replay.GenealogyInputs);
        Assert.Equal(output.Classification, replay.Classification);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.LabelTransformations WHERE Kind = 3"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.LabelTransformationInputs"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.LabelTransformationOutputs"));
        Assert.Equal(22.5m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.LabelTransformationInputs WHERE LabelId = @label",
            ("@label", kept.Label.Id)));
    }

    [SqlServerFact]
    public async Task Manufacturing_v1_output_also_records_genealogy_and_refuses_when_every_input_was_returned()
    {
        await using var run = await Run.StartAsync();
        var first = await SeedManufacturingParentAsync(run, "SYN-MFG-GEN-V1-A", 40m);
        var second = await SeedManufacturingParentAsync(run, "SYN-MFG-GEN-V1-B", 40m);
        var orderId = Guid.NewGuid();
        var firstIssue = await CommitIssueAsync(run, IssueRequest(orderId, first.Label.Id, "2", "10"),
            ManufacturingMetadata(run, "gen-v1-issue-a"));
        await CommitIssueAsync(run, IssueRequest(orderId, second.Label.Id, "2", "15"),
            ManufacturingMetadata(run, "gen-v1-issue-b"));

        var request = OutputRequest(orderId, first.Label.Id, firstIssue.ParentLabelVersion, first.LocationId, "20");
        var output = await CommitOutputAsync(run, request, ManufacturingMetadata(run, "gen-v1-output"));
        Assert.False(output.IsReplay);
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.LabelTransformationInputs i "
            + "JOIN inventory.LabelTransformationOutputs o ON o.TransformationId = i.TransformationId "
            + "JOIN inventory.LabelTransformations t ON t.Id = i.TransformationId "
            + "WHERE o.LabelId = @label AND t.Kind = 3",
            ("@label", request.LabelId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Labels WHERE Id = @label AND Classification IS NOT NULL",
            ("@label", request.LabelId)));

        var emptyOrder = Guid.NewGuid();
        var emptyIssue = await CommitIssueAsync(run, IssueRequest(emptyOrder, second.Label.Id, "3", "5"),
            ManufacturingMetadata(run, "gen-v1-empty-issue"));
        var emptyReturn = await CommitReturnAsync(run,
            ReturnRequest(emptyOrder, second.Label.Id, emptyIssue.ParentLabelVersion, "5"),
            ManufacturingMetadata(run, "gen-v1-empty-return"));
        var orphan = OutputRequest(emptyOrder, second.Label.Id, emptyReturn.ParentLabelVersion,
            second.LocationId, "1");
        await AssertOutputRefusalAsync(run, orphan, ManufacturingMetadata(run, "gen-v1-empty-output"), 409,
            InventoryManufacturingRealizationCodesV1.ParentInputNotIssued);
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Labels WHERE Id = @label", ("@label", orphan.LabelId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Id = @movement", ("@movement", orphan.MovementId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.LabelTransformations"));
    }

    [SqlServerFact]
    public async Task Manufacturing_v2_scrap_output_creates_a_scrap_classified_label()
    {
        await using var run = await Run.StartAsync();
        var parent = await SeedManufacturingParentAsync(run, "SYN-MFG-GEN-SCRAP", 30m);
        var orderId = Guid.NewGuid();
        var issued = await CommitIssueAsync(run, IssueRequest(orderId, parent.Label.Id, "2", "12"),
            ManufacturingMetadata(run, "gen-scrap-issue"));
        var request = OutputRequestV2(orderId, parent.Label.Id, issued.ParentLabelVersion, parent.LocationId,
            "1.5", InventoryManufacturingOutputClassificationV2.Scrap);

        var output = await CommitOutputV2Async(run, CompanyA, request, ManufacturingMetadata(run, "gen-scrap-output"));

        Assert.Equal((InventoryManufacturingOutputClassificationV2.Scrap, InventoryLabelClassificationV1.Scrap),
            (output.OutputClass, output.Classification));
        Assert.Equal((int)LabelClassification.Scrap, await run.ScalarAsync<int>(
            "SELECT Classification FROM inventory.Labels WHERE CompanyId = @company AND Id = @label",
            ("@company", CompanyA), ("@label", request.LabelId)));
        Assert.Equal(1.5m, await run.ScalarAsync<decimal>(
            "SELECT SUM(QuantityKg) FROM inventory.Balances WHERE LabelId = @label", ("@label", request.LabelId)));
        Assert.Equal((parent.Label.Id, "12"),
            (Assert.Single(output.GenealogyInputs).LabelId, output.GenealogyInputs[0].QuantityKg));
    }

    [SqlServerFact]
    public async Task Manufacturing_v2_usable_remnant_and_process_loss_are_refused_with_nothing_written()
    {
        await using var run = await Run.StartAsync();
        var parent = await SeedManufacturingParentAsync(run, "SYN-MFG-GEN-REMNANT", 30m);
        var orderId = Guid.NewGuid();
        var issued = await CommitIssueAsync(run, IssueRequest(orderId, parent.Label.Id, "2", "12"),
            ManufacturingMetadata(run, "gen-remnant-issue"));

        foreach (var outputClass in new[] { "UsableRemnant", "ProcessLoss" })
        {
            var request = OutputRequestV2(orderId, parent.Label.Id, issued.ParentLabelVersion, parent.LocationId,
                "2", outputClass);
            await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using (var participant = await InventoryManufacturingRealizationParticipant.EnlistAsync(
                             connection, transaction, CompanyA, ManufacturingSettingsFor(run, CompanyA)))
            {
                var error = await Assert.ThrowsAsync<InventoryManufacturingRealizationException>(() =>
                    participant.StageOutputAsync(request, ManufacturingMetadata(run, "gen-remnant-output")));
                Assert.Equal((400, InventoryManufacturingRealizationCodesV2.OutputClassNotStockable),
                    (error.Status, error.Code));
            }

            // Committing the host transaction proves the refusal staged nothing.
            await transaction.CommitAsync();
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM inventory.Labels WHERE Id = @label", ("@label", request.LabelId)));
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM inventory.Lots WHERE Id = @lot", ("@lot", request.LotId)));
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM inventory.Movements WHERE Id = @movement", ("@movement", request.MovementId)));
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM inventory.Commands WHERE TargetId = @output", ("@output", request.OutputId)));
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.LabelTransformations"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.LabelTransformationInputs"));
    }

    [SqlServerFact]
    public async Task Manufacturing_output_genealogy_never_links_another_companys_input_on_the_same_order()
    {
        await using var run = await Run.StartAsync();
        var parentA = await SeedManufacturingParentAsync(run, "SYN-MFG-GEN-ISO-A", 30m);
        // GSP/CompanyB's Settings kilogram profile is in effect only from 2026-10-26 (SAT-D18), so the
        // foreign company's parent stock and issue use a date inside its profile window.
        var parentB = await SeedManufacturingParentForAsync(run, CompanyB, "SYN-MFG-GEN-ISO-B", 30m,
            CompanyBManufacturingAt);
        var orderId = Guid.NewGuid();

        var issuedA = await CommitIssueAsync(run, IssueRequest(orderId, parentA.Label.Id, "2", "10"),
            ManufacturingMetadata(run, "gen-iso-issue-a"));
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using (var participant = await InventoryManufacturingRealizationParticipant.EnlistAsync(
                             connection, transaction, CompanyB, ManufacturingSettingsFor(run, CompanyB)))
            {
                await participant.StageInputIssueAsync(
                    IssueRequest(orderId, parentB.Id, "2", "10") with
                    {
                        CompanyId = CompanyB, OccurredAtUtc = CompanyBManufacturingAt.AddMinutes(1),
                    },
                    ManufacturingMetadata(run, "gen-iso-issue-b"));
            }

            await transaction.CommitAsync();
        }

        var request = OutputRequestV2(orderId, parentA.Label.Id, issuedA.ParentLabelVersion, parentA.LocationId,
            "8", InventoryManufacturingOutputClassificationV2.SecondQuality);
        var output = await CommitOutputV2Async(run, CompanyA, request, ManufacturingMetadata(run, "gen-iso-output"));

        Assert.Equal(InventoryLabelClassificationV1.SecondQuality, output.Classification);
        Assert.Equal(parentA.Label.Id, Assert.Single(output.GenealogyInputs).LabelId);
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.LabelTransformationInputs WHERE LabelId = @label",
            ("@label", parentB.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.LabelTransformations WHERE CompanyId = @company",
            ("@company", CompanyB)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.LabelTransformationInputs WHERE CompanyId = @company "
            + "AND TransformationId = @id",
            ("@company", CompanyA), ("@id", output.GenealogyTransformationId)));
    }

    [SqlServerFact]
    public async Task The_manufacturing_genealogy_kind_migration_Down_passes_with_only_split_or_merge_and_refuses_while_a_kind_3_row_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(
            new GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations.WidenInventoryLabelTransformationKindToManufacturing().DownOperations[0]).Sql;

        await run.ExecuteAsync(guard);
        await run.ExecuteAsync(
            """
            INSERT INTO inventory.LabelTransformations
                (Id, CompanyId, Kind, OccurredAtUtc, Reason, PayloadHash, RecordedAtUtc, RecordedBy, CorrelationId)
            VALUES (NEWID(), @company, 1, SYSUTCDATETIME(), N'synthetic split', REPLICATE(N'a', 64),
                SYSUTCDATETIME(), N'synthetic', N'synthetic-split');
            """, ("@company", CompanyA));
        await run.ExecuteAsync(guard);
        var outOfRange = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            """
            INSERT INTO inventory.LabelTransformations
                (Id, CompanyId, Kind, OccurredAtUtc, Reason, PayloadHash, RecordedAtUtc, RecordedBy, CorrelationId)
            VALUES (NEWID(), @company, 4, SYSUTCDATETIME(), N'synthetic', REPLICATE(N'a', 64),
                SYSUTCDATETIME(), N'synthetic', N'synthetic-kind-4');
            """, ("@company", CompanyA)));
        Assert.Equal(547, outOfRange.Number);

        var parent = await SeedManufacturingParentAsync(run, "SYN-MFG-GEN-DOWN", 20m);
        var orderId = Guid.NewGuid();
        var issued = await CommitIssueAsync(run, IssueRequest(orderId, parent.Label.Id, "2", "10"),
            ManufacturingMetadata(run, "gen-down-issue"));
        await CommitOutputAsync(run,
            OutputRequest(orderId, parent.Label.Id, issued.ParentLabelVersion, parent.LocationId, "9"),
            ManufacturingMetadata(run, "gen-down-output"));

        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51262, refused.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.LabelTransformations WHERE Kind = 3"));
    }

    private static InventoryManufacturingOutputRequestV2 OutputRequestV2(
        Guid orderId, Guid parentLabelId, string labelVersion, Guid locationId, string quantity, string outputClass) =>
        new(CompanyA, orderId, Guid.NewGuid(), "4", parentLabelId, labelVersion, outputClass,
            Product, "product-v1", Guid.NewGuid(), Guid.NewGuid(), $"SYN-MFG-OUT-{Guid.NewGuid():N}",
            "SYN-HEAT", "SYN-COIL", [], StockPartyKind.Company, CompanyA,
            StockPartyKind.Company, CompanyA, locationId, BlockState.Free, Guid.NewGuid(), quantity,
            ManufacturingAt.AddMinutes(3));

    private static async Task<InventoryManufacturingOutputResultV2> CommitOutputV2Async(
        Run run, Guid company, InventoryManufacturingOutputRequestV2 request, InventoryCommandMetadata metadata)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var participant = await InventoryManufacturingRealizationParticipant.EnlistAsync(
            connection, transaction, company, ManufacturingSettingsFor(run, company));
        IInventoryManufacturingRealizationParticipantV2 v2 = participant;
        var result = await v2.StageOutputAsync(request, metadata);
        await transaction.CommitAsync();
        return result;
    }

    private static ISettingsPrerequisiteProvider ManufacturingSettingsFor(Run run, Guid company)
    {
        var companyContext = new CompanyContext();
        companyContext.Set(company);
        return new ApprovedSettingsPrerequisiteProvider(companyContext, new FixedCurrentUser(run.Actor, company));
    }

    private static readonly DateTimeOffset CompanyBManufacturingAt =
        new(2026, 10, 27, 16, 0, 0, TimeSpan.Zero);

    private static async Task<MaterialLabel> SeedManufacturingParentForAsync(
        Run run, Guid company, string code, decimal quantity, DateTimeOffset at)
    {
        var location = await run.LocationOfAsync(company);
        var label = NewLabel(company, code);
        await using var store = run.Store(company);
        await store.Labels.SaveLabelAsync(label,
            Metadata(run) with { CorrelationId = $"manufacturing-parent-label-{Guid.NewGuid():N}" });
        var receipt = new StockMovement(Guid.NewGuid(), company, StockMovementKind.Receipt, at,
            new StockSourceReference("synthetic.manufacturing-parent", Guid.NewGuid()),
            [new StockMovementLine(Position(company, location, label, BlockState.Free),
                StockDirection.In, Kilograms.Positive(quantity))]);
        await store.Movements.RecordMovementAsync(receipt,
            Metadata(run) with { CorrelationId = $"manufacturing-parent-stock-{Guid.NewGuid():N}" });
        return label;
    }
}
