using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Transfers;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// STK-S7 (KLN-D22) actual SQL: an accepted transfer is not cancelled; a NEW transfer from the original destination back
/// to the original source is planned with a mandatory reason and a link to the original transfer and its accepted lines.
/// The reverse transfer moves stock only through the ordinary dispatch and acceptance. A transfer that has not been
/// accepted, a line with an undecided receipt excess and a line already being brought back are refused. The link
/// migration's Down passes while no link exists and refuses (THROW 51722) once one does. SENTETIK data only.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    private const string TransferReverseReason = "SENTETIK yanlis depoya sevk";

    [SqlServerFact]
    public async Task Transfer_reverse_plans_a_linked_return_transfer_that_moves_the_label_back_only_through_dispatch_and_acceptance()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-REVERSE", 10m);
        var sourceLocation = await run.LocationOfAsync(CompanyA);
        var destinationLocation = await TransferLocationAsync(run, CompanyA, offset: 1);
        var original = await AcceptedTransferAsync(run, source, sourceLocation, destinationLocation, "10");
        var originalLineId = Assert.Single(original.Lines).LineId;
        var reverseLineId = Guid.NewGuid();
        var request = new ReverseInventoryTransferV1(Guid.NewGuid(), At.AddDays(1), TransferReverseReason,
            [new(reverseLineId, originalLineId, (await TransferLabelVersionAsync(run, source.LabelId)).ToString(), "10")]);
        var metadata = Metadata(run) with
        {
            IdempotencyKey = $"transfer-reverse-{Guid.NewGuid():N}",
            CorrelationId = $"transfer-reverse-{Guid.NewGuid():N}",
        };

        await using (var scope = run.Store(CompanyA))
        {
            var reversed = await scope.Store.ReverseAsync(original.TransferId, request, metadata, Authorized);
            Assert.Equal((false, "1", nameof(InventoryTransferStatus.Planned)),
                (reversed.IsReplay, reversed.Transfer.Version, reversed.Transfer.Status));
            Assert.Equal((request.ReverseTransferId, destinationLocation, sourceLocation),
                (reversed.Transfer.TransferId, reversed.Transfer.SourceLocationId,
                    reversed.Transfer.DestinationLocationId));
            Assert.Equal((reverseLineId, source.LabelId), (Assert.Single(reversed.Transfer.Lines).LineId,
                reversed.Transfer.Lines[0].LabelId));
            Assert.Equal((request.ReverseTransferId, original.TransferId, TransferReverseReason, metadata.ActorId),
                (reversed.Reversal.ReverseTransferId, reversed.Reversal.OriginalTransferId, reversed.Reversal.Reason,
                    reversed.Reversal.RecordedBy));
            Assert.Equal(new InventoryTransferReversalLineViewV1(reverseLineId, originalLineId),
                Assert.Single(reversed.Reversal.Lines));

            var keyReplay = Assert.IsType<InventoryTransferReverseResultV1>(await scope.Store.ReplayReverseAsync(
                original.TransferId, request, metadata with { CorrelationId = $"retry-{Guid.NewGuid():N}" },
                Authorized));
            Assert.Equal((true, metadata.CorrelationId), (keyReplay.IsReplay, keyReplay.OriginalCorrelationId));
            var identityReplay = await scope.Store.ReverseAsync(original.TransferId, request,
                Metadata(run) with { CorrelationId = $"retry-{Guid.NewGuid():N}" }, Authorized);
            Assert.Equal((true, metadata.CorrelationId), (identityReplay.IsReplay, identityReplay.OriginalCorrelationId));
            Assert.Equal((reversed.Reversal.ReverseTransferId, reversed.Reversal.OriginalTransferId,
                    reversed.Reversal.Reason, reversed.Reversal.RecordedBy),
                (identityReplay.Reversal.ReverseTransferId, identityReplay.Reversal.OriginalTransferId,
                    identityReplay.Reversal.Reason, identityReplay.Reversal.RecordedBy));
            Assert.Equal(reversed.Reversal.Lines, identityReplay.Reversal.Lines);
        }

        // Planning moved nothing: the label is still at the original destination.
        Assert.Equal((10m, destinationLocation), (await TransferLabelKgAsync(run, source.LabelId),
            await LabelLocationAsync(run, source.LabelId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.Transfer' AND EntityId = @id "
            + "AND EventType = N'inventory.transfer.reverse-planned.v1'",
            ("@id", request.ReverseTransferId.ToString("D"))));

        await using (var scope = run.Store(CompanyA))
        {
            var facts = await scope.Store.ReadReversalFactsAsync(original.TransferId);
            Assert.NotNull(facts);
            Assert.Null(facts.ReversalOf);
            var reversedBy = Assert.Single(facts.ReversedBy);
            Assert.Equal((request.ReverseTransferId, nameof(InventoryTransferStatus.Planned)),
                (reversedBy.Reversal.ReverseTransferId, reversedBy.ReverseTransferStatus));
            var reverseFacts = await scope.Store.ReadReversalFactsAsync(request.ReverseTransferId);
            Assert.NotNull(reverseFacts);
            Assert.Equal(original.TransferId, reverseFacts.ReversalOf?.OriginalTransferId);
            Assert.Empty(reverseFacts.ReversedBy);
        }

        var dispatch = TransferDispatch();
        await using (var scope = run.Store(CompanyA))
        {
            var dispatched = await scope.Store.DispatchAsync(request.ReverseTransferId, dispatch,
                EffectiveOutgoing(request.ReverseTransferId, dispatch, reverseLineId),
                InventoryTransferSourceScaleSetting.Off, Metadata(run), Authorized);
            Assert.Equal(("2", nameof(InventoryTransferStatus.InTransit)),
                (dispatched.Transfer.Version, dispatched.Transfer.Status));
        }

        Assert.Equal(0m, await TransferLabelKgAsync(run, source.LabelId));

        await using (var scope = run.Store(CompanyA))
        {
            var accepted = await scope.Store.AcceptLineAsync(request.ReverseTransferId, reverseLineId,
                new AcceptInventoryTransferLineV1("2", At.AddDays(1).AddHours(2),
                    new("inventory.transfer.incoming-note", Guid.NewGuid(), reverseLineId)), Metadata(run), Authorized);
            Assert.Equal(("3", nameof(InventoryTransferStatus.Accepted)),
                (accepted.Transfer.Version, accepted.Transfer.Status));
            var originalAfter = await scope.Transfers.ReadAsync(original.TransferId);
            Assert.NotNull(originalAfter);
            Assert.Equal(nameof(InventoryTransferStatus.Accepted), originalAfter.Status);
        }

        // The label is back at the original source; the original transfer and its acceptance were not touched.
        Assert.Equal((10m, sourceLocation), (await TransferLabelKgAsync(run, source.LabelId),
            await LabelLocationAsync(run, source.LabelId)));
        Assert.Equal((1, 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferAcceptances WHERE TransferId = @id",
                ("@id", original.TransferId)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferAcceptances WHERE TransferId = @id",
                ("@id", request.ReverseTransferId))));
        Assert.Equal((1, 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferReversals WHERE OriginalTransferId = @id",
                ("@id", original.TransferId)),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM inventory.TransferReversalLines WHERE OriginalTransferId = @id AND OriginalLineId = @line",
                ("@id", original.TransferId), ("@line", originalLineId))));
    }

    [SqlServerFact]
    public async Task Transfer_reverse_refuses_an_unaccepted_original_a_wrong_line_a_missing_reason_and_a_line_already_brought_back()
    {
        await using var run = await Run.StartAsync();
        var accepted = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-REVERSE-ACCEPTED", 6m);
        var planned = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-REVERSE-PLANNED", 4m);
        var sourceLocation = await run.LocationOfAsync(CompanyA);
        var destinationLocation = await TransferLocationAsync(run, CompanyA, offset: 1);
        var original = await AcceptedTransferAsync(run, accepted, sourceLocation, destinationLocation, "6");
        var originalLineId = Assert.Single(original.Lines).LineId;
        var plannedOnly = TransferPlan(planned, sourceLocation, destinationLocation, "4");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.PlanAsync(plannedOnly, Metadata(run), Authorized);
        }

        var labelVersion = (await TransferLabelVersionAsync(run, accepted.LabelId)).ToString();
        ReverseInventoryTransferV1 Request(Guid originalLine, string reason = TransferReverseReason) =>
            new(Guid.NewGuid(), At.AddDays(1), reason, [new(Guid.NewGuid(), originalLine, labelVersion, "6")]);

        await using (var scope = run.Store(CompanyA))
        {
            await TransferReverseRefusedAsync(scope.Store.ReverseAsync(plannedOnly.TransferId,
                    Request(plannedOnly.Lines[0].LineId), Metadata(run), Authorized),
                409, InventoryTransferReverseCodesV1.OriginalNotAccepted);
            await TransferReverseRefusedAsync(scope.Store.ReverseAsync(original.TransferId,
                    Request(Guid.NewGuid()), Metadata(run), Authorized),
                400, InventoryTransferCodesV1.ValidationFailed);
            await TransferReverseRefusedAsync(scope.Store.ReverseAsync(original.TransferId,
                    Request(originalLineId, "   "), Metadata(run), Authorized),
                422, InventoryTransferReverseCodesV1.ReasonRequired);
            await TransferReverseRefusedAsync(scope.Store.ReverseAsync(Guid.NewGuid(),
                    Request(originalLineId), Metadata(run), Authorized),
                404, InventoryTransferCodesV1.TransferNotFound);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferReversals"));

        // A reverse plan that was never dispatched is inert and does not block a second plan for the same line.
        var first = Request(originalLineId);
        var second = Request(originalLineId);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.ReverseAsync(original.TransferId, first, Metadata(run), Authorized);
            await scope.Store.ReverseAsync(original.TransferId, second, Metadata(run), Authorized);
        }

        var dispatch = TransferDispatch();
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.DispatchAsync(first.ReverseTransferId, dispatch,
                EffectiveOutgoing(first.ReverseTransferId, dispatch, first.Lines[0].LineId),
                InventoryTransferSourceScaleSetting.Off, Metadata(run), Authorized);
        }

        // Once one reverse transfer is under way, the same accepted line cannot be planned back again.
        await using (var scope = run.Store(CompanyA))
        {
            await TransferReverseRefusedAsync(scope.Store.ReverseAsync(original.TransferId,
                    Request(originalLineId), Metadata(run), Authorized),
                409, InventoryTransferReverseCodesV1.LineAlreadyReversed);

            var reusedIdentity = first with { Reason = "SENTETIK baska gerekce" };
            await TransferReverseRefusedAsync(scope.Store.ReverseAsync(original.TransferId, reusedIdentity,
                    Metadata(run), Authorized),
                409, InventoryTransferCodesV1.IdempotencyConflict);
            await TransferReverseRefusedAsync(scope.Store.ReverseAsync(plannedOnly.TransferId,
                    first with { Lines = [first.Lines[0] with { OriginalLineId = plannedOnly.Lines[0].LineId }] },
                    Metadata(run), Authorized),
                409, InventoryTransferCodesV1.IdempotencyConflict);
        }

        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.TransferReversals WHERE OriginalTransferId = @id", ("@id", original.TransferId)));
        Assert.Equal(0m, await TransferLabelKgAsync(run, accepted.LabelId));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.TransferAcceptances WHERE TransferId = @id", ("@id", original.TransferId)));
    }

    [SqlServerFact]
    public async Task Transfer_reverse_refuses_a_line_whose_receipt_excess_still_waits_for_its_decision()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-REVERSE-OVERAGE", 10m);
        var sourceLocation = await run.LocationOfAsync(CompanyA);
        var destinationLocation = await TransferLocationAsync(run, CompanyA, offset: 1);
        var plan = TransferPlan(source, sourceLocation, destinationLocation, "10");
        var lineId = plan.Lines[0].LineId;
        var dispatch = TransferDispatch();
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.PlanAsync(plan, Metadata(run), Authorized);
            await scope.Store.DispatchAsync(plan.TransferId, dispatch, EffectiveOutgoing(plan, dispatch),
                InventoryTransferSourceScaleSetting.Off, Metadata(run), Authorized);
            await scope.Store.ReceiveLineAsync(plan.TransferId, lineId,
                new ReceiveInventoryTransferLineV1("2", At.AddHours(2),
                    new("inventory.transfer.incoming-note", Guid.NewGuid(), lineId), "12", "SENTETIK tarti fazlasi"),
                Metadata(run), Authorized);
        }

        await using (var scope = run.Store(CompanyA))
        {
            await TransferReverseRefusedAsync(scope.Store.ReverseAsync(plan.TransferId,
                    new ReverseInventoryTransferV1(Guid.NewGuid(), At.AddDays(1), TransferReverseReason,
                        [new(Guid.NewGuid(), lineId, (await TransferLabelVersionAsync(run, source.LabelId)).ToString(),
                            "10")]),
                    Metadata(run), Authorized),
                409, InventoryTransferReverseCodesV1.OveragePending);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferReversals"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'inventory.transfer.reverse-planned.v1'"));
    }

    [SqlServerFact]
    public async Task Transfer_reversal_migrations_Down_passes_with_only_an_accepted_transfer_and_refuses_with_51722_while_a_link_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddInventoryTransferReversals().DownOperations[0]).Sql;
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-REVERSE-DOWN", 3m);
        var sourceLocation = await run.LocationOfAsync(CompanyA);
        var destinationLocation = await TransferLocationAsync(run, CompanyA, offset: 1);
        var original = await AcceptedTransferAsync(run, source, sourceLocation, destinationLocation, "3");

        await run.ExecuteAsync(guard);

        var request = new ReverseInventoryTransferV1(Guid.NewGuid(), At.AddDays(1), TransferReverseReason,
            [new(Guid.NewGuid(), Assert.Single(original.Lines).LineId,
                (await TransferLabelVersionAsync(run, source.LabelId)).ToString(), "3")]);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.ReverseAsync(original.TransferId, request, Metadata(run), Authorized);
        }

        var rejected = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51722, rejected.Number);
        Assert.Equal((1, 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferReversals WHERE TransferId = @id",
                ("@id", request.ReverseTransferId)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferReversalLines WHERE TransferId = @id",
                ("@id", request.ReverseTransferId))));
    }

    /// <summary>Plans, dispatches and accepts one whole-label transfer and returns its plan.</summary>
    private static async Task<PlanInventoryTransferV1> AcceptedTransferAsync(
        Run run,
        PhysicalSource source,
        Guid sourceLocation,
        Guid destinationLocation,
        string plannedKg)
    {
        var plan = TransferPlan(source, sourceLocation, destinationLocation, plannedKg);
        var lineId = plan.Lines[0].LineId;
        var dispatch = TransferDispatch();
        await using var scope = run.Store(CompanyA);
        await scope.Store.PlanAsync(plan, Metadata(run), Authorized);
        await scope.Store.DispatchAsync(plan.TransferId, dispatch, EffectiveOutgoing(plan, dispatch),
            InventoryTransferSourceScaleSetting.Off, Metadata(run), Authorized);
        var accepted = await scope.Store.AcceptLineAsync(plan.TransferId, lineId,
            new AcceptInventoryTransferLineV1("2", At.AddHours(2),
                new("inventory.transfer.incoming-note", Guid.NewGuid(), lineId)), Metadata(run), Authorized);
        Assert.Equal(nameof(InventoryTransferStatus.Accepted), accepted.Transfer.Status);
        return plan;
    }

    private static async Task TransferReverseRefusedAsync(Task task, int status, string code)
    {
        var exception = await Assert.ThrowsAsync<InventoryTransferException>(() => task);
        Assert.Equal((status, code), (exception.Status, exception.Code));
    }
}
