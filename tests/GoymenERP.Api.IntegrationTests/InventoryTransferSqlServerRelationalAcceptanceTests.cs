using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Movements;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>INV-R20 actual SQL: two-leg whole-label transfer, line-level partial acceptance and exact replay.</summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Transfer_dispatches_whole_labels_into_transit_then_accepts_a_subset_and_reopens_the_same_record()
    {
        await using var run = await Run.StartAsync();
        var first = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-1", 10m);
        var second = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-2", 5m);
        var sourceLocation = await run.LocationOfAsync(CompanyA);
        var destinationLocation = await TransferLocationAsync(run, CompanyA, offset: 1);
        var transferId = Guid.NewGuid();
        var firstLine = Guid.NewGuid();
        var secondLine = Guid.NewGuid();
        var plan = new PlanInventoryTransferV1(transferId, sourceLocation, "Company", CompanyA,
            destinationLocation, "Company", CompanyA, At,
            [
                new(firstLine, first.LabelId, first.LabelVersion.ToString(), "12"),
                new(secondLine, second.LabelId, second.LabelVersion.ToString(), "5"),
            ]);
        var planMetadata = Metadata(run) with
        {
            IdempotencyKey = $"transfer-plan-{Guid.NewGuid():N}",
            CorrelationId = $"transfer-plan-{Guid.NewGuid():N}",
        };

        await using (var scope = run.Store(CompanyA))
        {
            var planned = await scope.Store.PlanAsync(plan, planMetadata, Authorized);
            Assert.Equal((false, "1", "Planned"),
                (planned.IsReplay, planned.Transfer.Version, planned.Transfer.Status));
            var replay = await scope.Store.PlanAsync(plan,
                planMetadata with { IdempotencyKey = Guid.NewGuid().ToString("N") }, Authorized);
            Assert.True(replay.IsReplay);
            Assert.Equal(planMetadata.CorrelationId, replay.OriginalCorrelationId);
        }

        var dispatch = new DispatchInventoryTransferV1("1", At.AddHours(1),
            new("inventory.transfer.outgoing-note", Guid.NewGuid(), null));
        var dispatchMetadata = Metadata(run) with
        {
            IdempotencyKey = $"transfer-dispatch-{Guid.NewGuid():N}",
            CorrelationId = $"transfer-dispatch-{Guid.NewGuid():N}",
        };
        await using (var scope = run.Store(CompanyA))
        {
            var dispatched = await scope.Store.DispatchAsync(transferId, dispatch, dispatchMetadata, Authorized);
            Assert.Equal(("2", "InTransit"), (dispatched.Transfer.Version, dispatched.Transfer.Status));
            Assert.Equal(("10", "-2"),
                (dispatched.Transfer.Lines[0].ActualShippedKg, dispatched.Transfer.Lines[0].DifferenceKg));
            Assert.Equal(("5", "0"),
                (dispatched.Transfer.Lines[1].ActualShippedKg, dispatched.Transfer.Lines[1].DifferenceKg));
        }

        Assert.Equal(0m, await TransferLabelKgAsync(run, first.LabelId));
        Assert.Equal(0m, await TransferLabelKgAsync(run, second.LabelId));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Kind = 8 AND SourceKind = N'inventory.transfer' AND SourceId = @id",
            ("@id", transferId)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferDispatchLines"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferBuckets"));

        var firstAccept = new AcceptInventoryTransferLineV1("2", At.AddHours(2),
            new("inventory.transfer.incoming-note", Guid.NewGuid(), firstLine));
        var firstAcceptMetadata = Metadata(run) with
        {
            IdempotencyKey = $"transfer-accept-{Guid.NewGuid():N}",
            CorrelationId = $"transfer-accept-{Guid.NewGuid():N}",
        };
        await using (var scope = run.Store(CompanyA))
        {
            var accepted = await scope.Store.AcceptLineAsync(transferId, firstLine, firstAccept, firstAcceptMetadata,
                Authorized);
            Assert.Equal(("3", "PartiallyAccepted"), (accepted.Transfer.Version, accepted.Transfer.Status));
            Assert.Equal("Accepted", accepted.Transfer.Lines.Single(line => line.LineId == firstLine).Status);
            Assert.Equal("InTransit", accepted.Transfer.Lines.Single(line => line.LineId == secondLine).Status);

            var replay = await scope.Store.AcceptLineAsync(transferId, firstLine, firstAccept,
                firstAcceptMetadata with { CorrelationId = $"retry-{Guid.NewGuid():N}" }, Authorized);
            Assert.True(replay.IsReplay);
            Assert.Equal(firstAcceptMetadata.CorrelationId, replay.OriginalCorrelationId);
        }

        Assert.Equal(10m, await TransferLabelKgAsync(run, first.LabelId));
        Assert.Equal(0m, await TransferLabelKgAsync(run, second.LabelId));
        Assert.Equal(destinationLocation, await LabelLocationAsync(run, first.LabelId));

        var secondAccept = new AcceptInventoryTransferLineV1("3", At.AddHours(3),
            new("inventory.transfer.incoming-note", Guid.NewGuid(), secondLine));
        await using (var scope = run.Store(CompanyA))
        {
            var accepted = await scope.Store.AcceptLineAsync(transferId, secondLine, secondAccept, Metadata(run),
                Authorized);
            Assert.Equal(("4", "Accepted"), (accepted.Transfer.Version, accepted.Transfer.Status));
            var reopened = await scope.Transfers.ReadAsync(transferId);
            Assert.NotNull(reopened);
            Assert.Equal(("4", "Accepted", 2), (reopened.Version, reopened.Status,
                reopened.Lines.Count(line => line.Status == "Accepted")));
        }

        Assert.Equal(5m, await TransferLabelKgAsync(run, second.LabelId));
        Assert.Equal(destinationLocation, await LabelLocationAsync(run, second.LabelId));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Kind = 9 AND SourceKind = N'inventory.transfer' AND SourceId = @id",
            ("@id", transferId)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferAcceptances"));
        Assert.Equal(4, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.Transfer' AND EntityId = @id",
            ("@id", transferId.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Transfer_refuses_stale_foreign_allocated_or_conflicting_commands_without_a_stock_effect()
    {
        await using var run = await Run.StartAsync();
        var own = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-GUARD", 10m);
        var foreign = await SeedFreePhysicalSourceAsync(run, CompanyB, "SYN-TRANSFER-FOREIGN", 10m);
        var sourceLocation = await run.LocationOfAsync(CompanyA);
        var destinationLocation = await TransferLocationAsync(run, CompanyA, offset: 1);
        var transferId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var plan = new PlanInventoryTransferV1(transferId, sourceLocation, "Company", CompanyA,
            destinationLocation, "Company", CompanyA, At,
            [new(lineId, own.LabelId, own.LabelVersion.ToString(), "10")]);

        var baselineEffects = (
            Transfers: await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Transfers"),
            Lines: await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferLines"),
            Commands: await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands"),
            Audits: await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.Transfer'"));
        var invalidPlans = new[]
        {
            plan with { Lines = [plan.Lines[0], plan.Lines[0] with { LineId = Guid.NewGuid() }] },
            plan with { Lines = [plan.Lines[0], plan.Lines[0] with { LabelId = Guid.NewGuid() }] },
            plan with
            {
                DestinationLocationId = sourceLocation,
                DestinationCustodianKind = "Company",
                DestinationCustodianId = CompanyA,
            },
        };
        await using (var scope = run.Store(CompanyA))
        {
            foreach (var invalid in invalidPlans)
            {
                var refused = await Assert.ThrowsAsync<InventoryTransferException>(
                    () => scope.Store.PlanAsync(invalid, Metadata(run), Authorized));
                Assert.Equal((400, InventoryTransferCodesV1.ValidationFailed), (refused.Status, refused.Code));
            }
        }

        var hiddenLabel = plan with
        {
            Lines = [plan.Lines[0] with { LabelId = foreign.LabelId, ExpectedLabelVersion = foreign.LabelVersion.ToString() }],
        };
        var revokedFenceCalls = 0;
        Task RevokeAfterReferenceRead(CancellationToken cancellationToken)
        {
            revokedFenceCalls++;
            return Task.FromException(new InventoryTransferException(403, InventoryTransferCodesV1.Forbidden));
        }

        await using (var scope = run.Store(CompanyA))
        {
            var revoked = await Assert.ThrowsAsync<InventoryTransferException>(
                () => scope.Store.PlanAsync(hiddenLabel, Metadata(run), RevokeAfterReferenceRead));
            Assert.Equal((403, InventoryTransferCodesV1.Forbidden, 1),
                (revoked.Status, revoked.Code, revokedFenceCalls));

            var hidden = await Assert.ThrowsAsync<InventoryTransferException>(
                () => scope.Store.PlanAsync(hiddenLabel, Metadata(run), Authorized));
            Assert.Equal((404, InventoryTransferCodesV1.LabelNotFound), (hidden.Status, hidden.Code));

            var badSource = await Assert.ThrowsAsync<InventoryTransferException>(() => scope.Store.PlanAsync(
                plan with { SourceLocationId = Guid.NewGuid() }, Metadata(run), Authorized));
            Assert.Equal((409, InventoryTransferCodesV1.SourcePositionConflict),
                (badSource.Status, badSource.Code));
        }

        Assert.Equal(baselineEffects, (
            Transfers: await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Transfers"),
            Lines: await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferLines"),
            Commands: await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands"),
            Audits: await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.Transfer'")));

        var foreignLocation = await run.LocationOfAsync(CompanyB);
        var foreignTransferId = Guid.NewGuid();
        await using (var foreignScope = run.Store(CompanyB))
        {
            await foreignScope.Store.PlanAsync(new PlanInventoryTransferV1(
                foreignTransferId,
                foreignLocation,
                "Company",
                CompanyB,
                foreignLocation,
                "Company",
                Guid.NewGuid(),
                At,
                [new(Guid.NewGuid(), foreign.LabelId, foreign.LabelVersion.ToString(), "10")]), Metadata(run), Authorized);
        }

        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.PlanAsync(plan, Metadata(run), Authorized);
            var conflict = await Assert.ThrowsAsync<InventoryTransferException>(() => scope.Store.PlanAsync(
                plan with { DestinationCustodianId = Guid.NewGuid() }, Metadata(run), Authorized));
            Assert.Equal((409, InventoryTransferCodesV1.IdempotencyConflict), (conflict.Status, conflict.Code));
            Assert.Null(await scope.Transfers.ReadAsync(foreignTransferId));
        }

        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(AllocationRequest(own, "1"), Metadata(run));
            var allocated = await Assert.ThrowsAsync<InventoryTransferException>(() => scope.Store.DispatchAsync(
                transferId, new("1", At.AddHours(1), new("inventory.transfer.outgoing-note", Guid.NewGuid(), null)),
                Metadata(run), Authorized));
            Assert.Equal((409, InventoryTransferCodesV1.ActiveAllocationConflict), (allocated.Status, allocated.Code));
        }

        Assert.Equal(10m, await TransferLabelKgAsync(run, own.LabelId));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferDispatches"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE Kind IN (8, 9)"));

        var staleSource = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-STALE", 10m);
        var staleId = Guid.NewGuid();
        var stale = new PlanInventoryTransferV1(staleId, sourceLocation, "Company", CompanyA,
            destinationLocation, "Company", CompanyA, At,
            [new(Guid.NewGuid(), staleSource.LabelId, (staleSource.LabelVersion + 1).ToString(), "10")]);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.PlanAsync(stale, Metadata(run), Authorized);
            var refused = await Assert.ThrowsAsync<InventoryTransferException>(() => scope.Store.DispatchAsync(
                staleId, new("1", At.AddHours(1), new("inventory.transfer.outgoing-note", Guid.NewGuid(), null)),
                Metadata(run), Authorized));
            Assert.Equal((409, InventoryTransferCodesV1.LabelVersionConflict), (refused.Status, refused.Code));
        }

        Assert.Equal(10m, await TransferLabelKgAsync(run, staleSource.LabelId));

        var sameKeySource = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-SAME-KEY", 4m);
        var sameKeyPlan = TransferPlan(sameKeySource, sourceLocation, destinationLocation, "4");
        var sameKeyMetadata = Metadata(run) with
        {
            IdempotencyKey = $"transfer-same-key-{Guid.NewGuid():N}",
            CorrelationId = $"transfer-same-key-{Guid.NewGuid():N}",
        };
        var sameKeyBarrier = new Barrier(2);
        var sameKeyResults = await Task.WhenAll(
            Task.Run(() => RacePlanAsync(run, sameKeyPlan, sameKeyMetadata, sameKeyBarrier)),
            Task.Run(() => RacePlanAsync(run, sameKeyPlan, sameKeyMetadata, sameKeyBarrier)));
        Assert.Single(sameKeyResults, result => result is InventoryTransferResultV1 { IsReplay: false });
        Assert.Single(sameKeyResults, result => result is InventoryTransferResultV1 { IsReplay: true });
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Transfers WHERE Id = @id", ("@id", sameKeyPlan.TransferId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE TargetId = @id", ("@id", sameKeyPlan.TransferId)));

        var differentKeySource = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-DIFFERENT-KEY", 4m);
        var differentKeyPlan = TransferPlan(differentKeySource, sourceLocation, destinationLocation, "4");
        var differentKeyBarrier = new Barrier(2);
        var differentKeyResults = await Task.WhenAll(
            Task.Run(() => RacePlanAsync(run, differentKeyPlan,
                Metadata(run) with { IdempotencyKey = $"transfer-key-a-{Guid.NewGuid():N}" }, differentKeyBarrier)),
            Task.Run(() => RacePlanAsync(run, differentKeyPlan,
                Metadata(run) with { IdempotencyKey = $"transfer-key-b-{Guid.NewGuid():N}" }, differentKeyBarrier)));
        Assert.Single(differentKeyResults, result => result is InventoryTransferResultV1 { IsReplay: false });
        Assert.Single(differentKeyResults, result => result is InventoryTransferException
            { Status: 409, Code: InventoryTransferCodesV1.ConcurrentChange });
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Transfers WHERE Id = @id", ("@id", differentKeyPlan.TransferId)));

        var stockRaceSource = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-STOCK-RACE", 8m);
        var stockRacePlan = TransferPlan(stockRaceSource, sourceLocation, destinationLocation, "8");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.PlanAsync(stockRacePlan, Metadata(run), Authorized);
        }

        var dispatchRequest = new DispatchInventoryTransferV1("1", At.AddHours(1),
            new("inventory.transfer.outgoing-note", Guid.NewGuid(), null));
        var dispatchMetadata = Metadata(run) with
        {
            IdempotencyKey = $"transfer-dispatch-race-{Guid.NewGuid():N}",
            CorrelationId = $"transfer-dispatch-race-{Guid.NewGuid():N}",
        };
        var dispatchBarrier = new Barrier(2);
        var dispatchResults = await Task.WhenAll(
            Task.Run(() => RaceDispatchAsync(run, stockRacePlan.TransferId, dispatchRequest, dispatchMetadata,
                dispatchBarrier)),
            Task.Run(() => RaceDispatchAsync(run, stockRacePlan.TransferId, dispatchRequest, dispatchMetadata,
                dispatchBarrier)));
        Assert.Single(dispatchResults, result => result.Outcome is InventoryTransferResultV1 { IsReplay: false });
        Assert.Single(dispatchResults, result => result.Outcome is InventoryTransferResultV1 { IsReplay: true });
        Assert.Equal(new[] { 4, 5 }, dispatchResults.Select(result => result.FenceCalls).Order().ToArray());
        Assert.Equal((1, 1, 0m, stockRaceSource.LabelVersion + 1), (
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM inventory.TransferDispatches WHERE TransferId = @id",
                ("@id", stockRacePlan.TransferId)),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM inventory.Movements WHERE Kind = 8 AND SourceKind = N'inventory.transfer' AND SourceId = @id",
                ("@id", stockRacePlan.TransferId)),
            await TransferLabelKgAsync(run, stockRaceSource.LabelId),
            await TransferLabelVersionAsync(run, stockRaceSource.LabelId)));

        var stockRaceLineId = Assert.Single(stockRacePlan.Lines).LineId;
        var acceptRequest = new AcceptInventoryTransferLineV1("2", At.AddHours(2),
            new("inventory.transfer.incoming-note", Guid.NewGuid(), stockRaceLineId));
        var acceptBarrier = new Barrier(2);
        var acceptResults = await Task.WhenAll(
            Task.Run(() => RaceAcceptAsync(run, stockRacePlan.TransferId, stockRaceLineId, acceptRequest,
                Metadata(run) with { IdempotencyKey = $"transfer-accept-a-{Guid.NewGuid():N}" }, acceptBarrier)),
            Task.Run(() => RaceAcceptAsync(run, stockRacePlan.TransferId, stockRaceLineId, acceptRequest,
                Metadata(run) with { IdempotencyKey = $"transfer-accept-b-{Guid.NewGuid():N}" }, acceptBarrier)));
        Assert.Single(acceptResults, result => result.Outcome is InventoryTransferResultV1 { IsReplay: false });
        Assert.Single(acceptResults, result => result.Outcome is InventoryTransferException
            { Status: 409, Code: InventoryTransferCodesV1.ConcurrentChange });
        Assert.Equal(new[] { 4, 5 }, acceptResults.Select(result => result.FenceCalls).Order().ToArray());
        Assert.Equal((1, 1, 8m, destinationLocation, stockRaceSource.LabelVersion + 2), (
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM inventory.TransferAcceptances WHERE TransferId = @id",
                ("@id", stockRacePlan.TransferId)),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM inventory.Movements WHERE Kind = 9 AND SourceKind = N'inventory.transfer' AND SourceId = @id",
                ("@id", stockRacePlan.TransferId)),
            await TransferLabelKgAsync(run, stockRaceSource.LabelId),
            await LabelLocationAsync(run, stockRaceSource.LabelId),
            await TransferLabelVersionAsync(run, stockRaceSource.LabelId)));
        Assert.Equal((3, 3, 3L, "Accepted"), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands WHERE TargetId = @id",
                ("@id", stockRacePlan.TransferId)),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.Transfer' AND EntityId = @id",
                ("@id", stockRacePlan.TransferId.ToString("D"))),
            await run.ScalarAsync<long>("SELECT Version FROM inventory.Transfers WHERE Id = @id",
                ("@id", stockRacePlan.TransferId)),
            await run.ScalarAsync<string>(
                "SELECT CASE Status WHEN 4 THEN N'Accepted' ELSE N'Unexpected' END FROM inventory.Transfers WHERE Id = @id",
                ("@id", stockRacePlan.TransferId))));

        var acceptInterleaveSource = await SeedFreePhysicalSourceAsync(run, CompanyA,
            "SYN-TRANSFER-ACCEPT-INTERLEAVE", 9m);
        var acceptInterleavePlan = TransferPlan(acceptInterleaveSource, sourceLocation, destinationLocation, "9");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.PlanAsync(acceptInterleavePlan, Metadata(run), Authorized);
            await scope.Store.DispatchAsync(acceptInterleavePlan.TransferId,
                new("1", At.AddHours(1), new("inventory.transfer.outgoing-note", Guid.NewGuid(), null)),
                Metadata(run), Authorized);
        }

        var acceptInterleaveLineId = Assert.Single(acceptInterleavePlan.Lines).LineId;
        var losingAccept = new AcceptInventoryTransferLineV1("2", At.AddHours(2),
            new("inventory.transfer.incoming-note", Guid.NewGuid(), acceptInterleaveLineId));
        var acceptFenceCalls = 0;
        async Task MutateLabelBetweenValidationAndStaging(CancellationToken cancellationToken)
        {
            acceptFenceCalls++;
            if (acceptFenceCalls != 3) return;
            var label = new GoymenERP.Modules.Inventory.Domain.Labels.MaterialLabel(
                acceptInterleaveSource.LabelId, CompanyA, "SYN-TRANSFER-ACCEPT-INTERLEAVE", Product, LotA);
            var mutation = Movement(CompanyA, StockMovementKind.Receipt,
                new StockSourceReference("synthetic.transfer-accept-interleave", Guid.NewGuid()),
                In(Position(CompanyA, destinationLocation, label,
                    GoymenERP.Modules.Inventory.Domain.Stock.BlockState.Free), 1m));
            await using var mutator = run.Store(CompanyA);
            Assert.Equal(InventoryWriteOutcome.Recorded,
                await mutator.Store.RecordMovementAsync(mutation, Metadata(run), cancellationToken));
        }

        await using (var scope = run.Store(CompanyA))
        {
            var raced = await Assert.ThrowsAsync<InventoryTransferException>(() => scope.Store.AcceptLineAsync(
                acceptInterleavePlan.TransferId, acceptInterleaveLineId, losingAccept, Metadata(run),
                MutateLabelBetweenValidationAndStaging));
            Assert.Equal((409, InventoryTransferCodesV1.ConcurrentChange), (raced.Status, raced.Code));
        }
        Assert.Equal((0, 0, 1m, destinationLocation, acceptInterleaveSource.LabelVersion + 2), (
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM inventory.TransferAcceptances WHERE TransferId = @id",
                ("@id", acceptInterleavePlan.TransferId)),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM inventory.Movements WHERE Kind = 9 AND SourceKind = N'inventory.transfer' AND SourceId = @id",
                ("@id", acceptInterleavePlan.TransferId)),
            await TransferLabelKgAsync(run, acceptInterleaveSource.LabelId),
            await LabelLocationAsync(run, acceptInterleaveSource.LabelId),
            await TransferLabelVersionAsync(run, acceptInterleaveSource.LabelId)));
        Assert.Equal((2, 2, 2L, "InTransit"), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands WHERE TargetId = @id",
                ("@id", acceptInterleavePlan.TransferId)),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.Transfer' AND EntityId = @id",
                ("@id", acceptInterleavePlan.TransferId.ToString("D"))),
            await run.ScalarAsync<long>("SELECT Version FROM inventory.Transfers WHERE Id = @id",
                ("@id", acceptInterleavePlan.TransferId)),
            await run.ScalarAsync<string>(
                "SELECT CASE Status WHEN 2 THEN N'InTransit' ELSE N'Unexpected' END FROM inventory.Transfers WHERE Id = @id",
                ("@id", acceptInterleavePlan.TransferId))));

        var allocationRace = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-ALLOC-RACE", 6m);
        var allocationRacePlan = TransferPlan(allocationRace, sourceLocation, destinationLocation, "6");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.PlanAsync(allocationRacePlan, Metadata(run), Authorized);
        }

        var fenceCount = 0;
        async Task AllocateAtDispatchFence(CancellationToken cancellationToken)
        {
            fenceCount++;
            if (fenceCount != 3) return;
            await using var allocator = run.Store(CompanyA);
            await allocator.Store.CreateAsync(AllocationRequest(allocationRace, "1"), Metadata(run), cancellationToken);
        }

        await using (var scope = run.Store(CompanyA))
        {
            var raced = await Assert.ThrowsAsync<InventoryTransferException>(() => scope.Store.DispatchAsync(
                allocationRacePlan.TransferId,
                new("1", At.AddHours(1), new("inventory.transfer.outgoing-note", Guid.NewGuid(), null)),
                Metadata(run), AllocateAtDispatchFence));
            Assert.Equal((409, InventoryTransferCodesV1.ConcurrentChange), (raced.Status, raced.Code));
        }
        Assert.Equal(6m, await TransferLabelKgAsync(run, allocationRace.LabelId));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.TransferDispatches WHERE TransferId = @id",
            ("@id", allocationRacePlan.TransferId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE SourceKind = N'inventory.transfer' AND SourceId = @id",
            ("@id", allocationRacePlan.TransferId)));

        var fencedSource = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-FENCE-ROLLBACK", 7m);
        var fencedPlan = TransferPlan(fencedSource, sourceLocation, destinationLocation, "7");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.PlanAsync(fencedPlan, Metadata(run), Authorized);
        }

        var rollbackFenceCount = 0;
        Task RejectFinalFence(CancellationToken cancellationToken)
        {
            rollbackFenceCount++;
            return rollbackFenceCount == 4
                ? Task.FromException(new InventoryTransferException(403, InventoryTransferCodesV1.Forbidden))
                : Task.CompletedTask;
        }

        await using (var scope = run.Store(CompanyA))
        {
            var forbidden = await Assert.ThrowsAsync<InventoryTransferException>(() => scope.Store.DispatchAsync(
                fencedPlan.TransferId,
                new("1", At.AddHours(1), new("inventory.transfer.outgoing-note", Guid.NewGuid(), null)),
                Metadata(run), RejectFinalFence));
            Assert.Equal((403, InventoryTransferCodesV1.Forbidden), (forbidden.Status, forbidden.Code));
        }
        Assert.Equal(7m, await TransferLabelKgAsync(run, fencedSource.LabelId));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.TransferDispatches WHERE TransferId = @id", ("@id", fencedPlan.TransferId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE SourceKind = N'inventory.transfer' AND SourceId = @id",
            ("@id", fencedPlan.TransferId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.Transfer' AND EntityId = @id",
            ("@id", fencedPlan.TransferId.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Transfer_migration_rolls_back_when_empty_and_blocks_after_a_plan()
    {
        const string previousMigration = "20260928161227_AddInventoryStockScraps";
        const string currentMigration = "20260930182024_AddInventoryTransfers";
        await using var run = await Run.StartAsync();
        await using (var context = run.Context(CompanyA))
        {
            await context.GetService<IMigrator>().MigrateAsync(previousMigration);
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'inventory') AND name LIKE N'Transfer%'"));
            await context.GetService<IMigrator>().MigrateAsync(currentMigration);
        }

        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-DOWN", 10m);
        var sourceLocation = await run.LocationOfAsync(CompanyA);
        var destinationLocation = await TransferLocationAsync(run, CompanyA, offset: 1);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.PlanAsync(new PlanInventoryTransferV1(
                Guid.NewGuid(), sourceLocation, "Company", CompanyA,
                destinationLocation, "Company", CompanyA, At,
                [new(Guid.NewGuid(), source.LabelId, source.LabelVersion.ToString(), "10")]), Metadata(run), Authorized);
        }

        await using (var context = run.Context(CompanyA))
        {
            var rejected = await Assert.ThrowsAsync<SqlException>(
                () => context.GetService<IMigrator>().MigrateAsync(previousMigration));
            Assert.Equal(51053, rejected.Number);
            Assert.Equal(currentMigration, (await context.Database.GetAppliedMigrationsAsync()).Last());
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Transfers"));
    }

    private static Task Authorized(CancellationToken cancellationToken) => Task.CompletedTask;

    private static PlanInventoryTransferV1 TransferPlan(
        PhysicalSource source,
        Guid sourceLocation,
        Guid destinationLocation,
        string plannedKg) => new(
        Guid.NewGuid(), sourceLocation, "Company", CompanyA, destinationLocation, "Company", CompanyA, At,
        [new(Guid.NewGuid(), source.LabelId, source.LabelVersion.ToString(), plannedKg)]);

    private static async Task<object> RacePlanAsync(
        Run run,
        PlanInventoryTransferV1 plan,
        InventoryCommandMetadata metadata,
        Barrier barrier)
    {
        await using var scope = run.Store(CompanyA, new BarrierInterceptor(barrier));
        try
        {
            return await scope.Store.PlanAsync(plan, metadata, Authorized);
        }
        catch (InventoryTransferException exception)
        {
            return exception;
        }
    }

    private static async Task<TransferRaceResult> RaceDispatchAsync(
        Run run,
        Guid transferId,
        DispatchInventoryTransferV1 request,
        InventoryCommandMetadata metadata,
        Barrier barrier)
    {
        var fenceCalls = 0;
        Task Fence(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref fenceCalls);
            return Task.CompletedTask;
        }

        await using var scope = run.Store(CompanyA, new BarrierInterceptor(barrier));
        try
        {
            return new(await scope.Store.DispatchAsync(transferId, request, metadata, Fence), fenceCalls);
        }
        catch (InventoryTransferException exception)
        {
            return new(exception, fenceCalls);
        }
    }

    private static async Task<TransferRaceResult> RaceAcceptAsync(
        Run run,
        Guid transferId,
        Guid lineId,
        AcceptInventoryTransferLineV1 request,
        InventoryCommandMetadata metadata,
        Barrier barrier)
    {
        var fenceCalls = 0;
        Task Fence(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref fenceCalls);
            return Task.CompletedTask;
        }

        await using var scope = run.Store(CompanyA, new BarrierInterceptor(barrier));
        try
        {
            return new(await scope.Store.AcceptLineAsync(transferId, lineId, request, metadata, Fence), fenceCalls);
        }
        catch (InventoryTransferException exception)
        {
            return new(exception, fenceCalls);
        }
    }

    private static async Task<long> TransferLabelVersionAsync(Run run, Guid labelId) =>
        await run.ScalarAsync<long>("SELECT Version FROM inventory.Labels WHERE Id = @id", ("@id", labelId));

    private sealed record TransferRaceResult(object Outcome, int FenceCalls);

    private static async Task<decimal> TransferLabelKgAsync(Run run, Guid labelId) => await run.ScalarAsync<decimal>(
        "SELECT COALESCE(SUM(QuantityKg), 0) FROM inventory.Balances WHERE LabelId = @id", ("@id", labelId));

    private static async Task<Guid> LabelLocationAsync(Run run, Guid labelId) => await run.ScalarAsync<Guid>(
        "SELECT TOP (1) LocationId FROM inventory.Balances WHERE LabelId = @id AND QuantityKg > 0", ("@id", labelId));

    private static async Task<Guid> TransferLocationAsync(Run run, Guid companyId, int offset) =>
        await run.ScalarAsync<Guid>(
            "SELECT Id FROM organization.Locations WHERE CompanyId = @company ORDER BY Id OFFSET @offset ROWS FETCH NEXT 1 ROWS ONLY",
            ("@company", companyId), ("@offset", offset));
}
