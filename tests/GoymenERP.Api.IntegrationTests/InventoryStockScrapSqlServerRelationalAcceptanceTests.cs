using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Scraps;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// ST-02 scrap write-off over real SQL: a recorded scrap with its reason changes no stock; it leaves stock only after
/// another actor approves it (fail-closed maker≠checker while the scrap approval authority is undecided); the approval
/// posts exactly one Scrap movement (kind 7), which is the balance ST-01, the label detail and a new allocation see
/// next; a blocked piece is scrapped without a release; replays have no second effect; a scrap never breaks an active
/// allocation; history is never deleted and the migration refuses rollback while scraps exist.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Stock_scrap_is_pending_until_another_actor_approves_then_one_scrap_movement_and_ST01_and_allocation_see_the_rest()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-SCRAP-FREE", 100m);
        var recorder = Metadata(run) with { IdempotencyKey = $"scrap-{Guid.NewGuid():N}", CorrelationId = $"scrap-{Guid.NewGuid():N}" };
        var request = new RecordInventoryStockScrapV1(Guid.NewGuid(), source.LabelId, source.LabelVersion.ToString(), "12.5",
            At.AddHours(2), "Sentetik hurda: kenar ezilmesi.");

        await using (var scope = run.Store(CompanyA))
        {
            var recorded = await scope.Store.RecordStockScrapAsync(request, recorder, static _ => Task.CompletedTask);
            Assert.False(recorded.IsReplay);
            Assert.Equal((InventoryStockScrapStatusesV1.PendingApproval, "100", "12.5", "87.5", source.LabelVersion.ToString()),
                (recorded.Scrap.Status, recorded.Scrap.BookKg, recorded.Scrap.ScrapKg, recorded.Scrap.RemainingKg,
                    recorded.Scrap.LabelVersion));
        }

        // A recorded scrap is a request, not an effect: no movement, the same balance and label version.
        Assert.Equal(100m, await LabelKgAsync(run, source.LabelId));
        Assert.Equal(source.LabelVersion, await LabelVersionAsync(run, source.LabelId));
        Assert.Equal(0, await ScrapMovementsAsync(run));

        await using (var scope = run.Store(CompanyA))
        {
            var sameKey = await scope.Store.RecordStockScrapAsync(request, recorder with { CorrelationId = $"retry-{Guid.NewGuid():N}" }, static _ => Task.CompletedTask);
            Assert.True(sameKey.IsReplay);
            Assert.Equal(recorder.CorrelationId, sameKey.OriginalCorrelationId);
            var newKey = await scope.Store.RecordStockScrapAsync(request, recorder with { IdempotencyKey = $"other-{Guid.NewGuid():N}" }, static _ => Task.CompletedTask);
            Assert.True(newKey.IsReplay);
            await AssertScrapRefused(scope.Store.RecordStockScrapAsync(request with { ScrapKg = "12" },
                    recorder with { IdempotencyKey = $"other-content-{Guid.NewGuid():N}" }, static _ => Task.CompletedTask),
                409, InventoryStockScrapCodesV1.ScrapConflict);
            var reopened = (await scope.Store.ReadStockScrapsAsync(source.LabelId))!;
            var only = Assert.Single(reopened.Items);
            Assert.Equal((request.ScrapId, InventoryStockScrapStatusesV1.PendingApproval, request.Reason),
                (only.ScrapId, only.Status, only.Reason));
        }

        await using (var scope = run.Store(CompanyA))
        {
            await AssertScrapRefused(scope.Store.DecideStockScrapAsync(request.ScrapId,
                    new(InventoryStockScrapDecisionKindV1.Approve, "Kendi hurdam"), recorder with { IdempotencyKey = $"self-{Guid.NewGuid():N}" }, static _ => Task.CompletedTask),
                409, InventoryStockScrapCodesV1.SelfApproval);
        }

        Assert.Equal(100m, await LabelKgAsync(run, source.LabelId));
        var approver = Metadata(run) with
        {
            ActorId = Guid.NewGuid().ToString("D"),
            IdempotencyKey = $"approve-{Guid.NewGuid():N}",
            CorrelationId = $"approve-{Guid.NewGuid():N}",
        };
        var approval = new DecideInventoryStockScrapV1(InventoryStockScrapDecisionKindV1.Approve, "Kalite raporuyla doğrulandı.");
        Guid movementId;
        await using (var scope = run.Store(CompanyA))
        {
            var approved = await scope.Store.DecideStockScrapAsync(request.ScrapId, approval, approver, static _ => Task.CompletedTask);
            Assert.False(approved.IsReplay);
            Assert.Equal(InventoryStockScrapStatusesV1.Approved, approved.Scrap.Status);
            movementId = approved.Scrap.Decision!.ScrapMovementId!.Value;
        }

        Assert.Equal(87.5m, await LabelKgAsync(run, source.LabelId));
        Assert.Equal(source.LabelVersion + 1, await LabelVersionAsync(run, source.LabelId));
        Assert.Equal(1, await ScrapMovementsAsync(run));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements m JOIN inventory.MovementLines l ON l.MovementId = m.Id "
            + "WHERE m.Id = @id AND m.Kind = 7 AND m.SourceKind = N'inventory.stock-scrap' AND m.SourceId = @scrap "
            + "AND m.CostStatus = 1 AND l.Direction = 2 AND l.QuantityKg = 12.5 AND l.LabelId = @label",
            ("@id", movementId), ("@scrap", request.ScrapId), ("@label", source.LabelId)));

        await using (var scope = run.Store(CompanyA))
        {
            var sameKey = await scope.Store.DecideStockScrapAsync(request.ScrapId, approval,
                approver with { CorrelationId = $"retry-{Guid.NewGuid():N}" }, static _ => Task.CompletedTask);
            Assert.True(sameKey.IsReplay);
            Assert.Equal(approver.CorrelationId, sameKey.OriginalCorrelationId);
            var newKey = await scope.Store.DecideStockScrapAsync(request.ScrapId, approval,
                approver with { IdempotencyKey = $"approve-again-{Guid.NewGuid():N}" }, static _ => Task.CompletedTask);
            Assert.True(newKey.IsReplay);
            Assert.Equal(movementId, newKey.Scrap.Decision!.ScrapMovementId);
            await AssertScrapRefused(scope.Store.DecideStockScrapAsync(request.ScrapId,
                    new(InventoryStockScrapDecisionKindV1.Reject, "Vazgeçildi"), approver with { IdempotencyKey = $"reject-{Guid.NewGuid():N}" }, static _ => Task.CompletedTask),
                409, InventoryStockScrapCodesV1.AlreadyDecided);

            // The same approval content is replayed only to its decider (#512 review P3): the recorder meets the
            // maker≠checker refusal, a third actor the already-decided one — never a 200 of someone else's decision.
            await AssertScrapRefused(scope.Store.DecideStockScrapAsync(request.ScrapId, approval,
                    recorder with { IdempotencyKey = $"recorder-replay-{Guid.NewGuid():N}" }, static _ => Task.CompletedTask),
                409, InventoryStockScrapCodesV1.SelfApproval);
            await AssertScrapRefused(scope.Store.DecideStockScrapAsync(request.ScrapId, approval,
                    approver with { ActorId = Guid.NewGuid().ToString("D"), IdempotencyKey = $"third-replay-{Guid.NewGuid():N}" }, static _ => Task.CompletedTask),
                409, InventoryStockScrapCodesV1.AlreadyDecided);
        }

        // No double effect, and the history is intact and audited from the web channel.
        Assert.Equal(87.5m, await LabelKgAsync(run, source.LabelId));
        Assert.Equal(1, await ScrapMovementsAsync(run));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.StockScraps"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.StockScrapDecisions"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.StockScrap' AND SourceChannel = N'Web'"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.StockScrap' "
            + "AND EventType = N'inventory.stock-scrap.approved.v1' AND ActorId = @actor",
            ("@actor", approver.ActorId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.StockMovement' AND EntityId = @id",
            ("@id", movementId.ToString("D"))));

        // Reopen: the scrap shows its decision and ST-01, the label detail and allocation all read what remains.
        await using (var scope = run.Store(CompanyA))
        {
            var reopened = Assert.Single((await scope.Store.ReadStockScrapsAsync(source.LabelId))!.Items);
            Assert.Equal((InventoryStockScrapStatusesV1.Approved, "Kalite raporuyla doğrulandı.", (Guid?)movementId),
                (reopened.Status, reopened.Decision!.Reason, reopened.Decision.ScrapMovementId));

            var detail = await LabelDetails(scope.DbContext, CompanyA, scope.Store).GetAsync(source.LabelId);
            Assert.Equal("87.5", detail.Balances.PhysicalKg);
            Assert.Contains(detail.MovementTimeline.Entries, entry => entry.MovementId == movementId
                && entry.Kind == nameof(StockMovementKind.Scrap));
        }

        await using (var context = run.Context(CompanyA))
        {
            var position = Assert.Single((await StockPositions(context, CompanyA).SearchAsync(new("SYN-SCRAP-FREE", 25))).Items);
            Assert.Equal(("87.5", "87.5", (source.LabelVersion + 1).ToString()),
                (position.PhysicalKg, position.AvailableKg, position.LabelVersion));
        }

        var scrapped = source with { LabelVersion = source.LabelVersion + 1 };
        await using (var scope = run.Store(CompanyA))
        {
            var tooMuch = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
                scope.Store.CreateAsync(AllocationRequest(scrapped, "87.501"), Metadata(run)));
            Assert.Equal(409, tooMuch.Status);
        }

        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(AllocationRequest(scrapped, "87.5"), Metadata(run));
        }
    }

    [SqlServerFact]
    public async Task Stock_scrap_of_a_blocked_piece_leaves_without_a_release_and_a_whole_piece_scrap_empties_the_label()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-SCRAP-BLOCKED", 40m);
        var location = await run.LocationOfAsync(CompanyA);
        await using (var scope = run.Store(CompanyA))
        {
            var label = (await scope.Labels.FindLabelAsync(source.LabelId))!;
            var free = Position(CompanyA, location, label, BlockState.Free);
            await scope.Movements.RecordMovementAsync(Movement(CompanyA, StockMovementKind.Block,
                Out(free, 40m), In(free.WithBlock(BlockState.Blocked), 40m)), Metadata(run));
        }

        var version = await LabelVersionAsync(run, source.LabelId);
        var request = new RecordInventoryStockScrapV1(Guid.NewGuid(), source.LabelId, version.ToString(), "40",
            At.AddHours(2), "Sentetik hurda: kalite blokajındaki rulo kullanılamaz.");
        await using (var scope = run.Store(CompanyA))
        {
            var recorded = await scope.Store.RecordStockScrapAsync(request, Metadata(run), static _ => Task.CompletedTask);
            Assert.Equal((nameof(BlockState.Blocked), "0"), (recorded.Scrap.Block, recorded.Scrap.RemainingKg));
        }

        await using (var scope = run.Store(CompanyA))
        {
            var approved = await scope.Store.DecideStockScrapAsync(request.ScrapId,
                new(InventoryStockScrapDecisionKindV1.Approve, "Kalite onayı"),
                Metadata(run) with { ActorId = Guid.NewGuid().ToString("D"), IdempotencyKey = Guid.NewGuid().ToString("N") }, static _ => Task.CompletedTask);
            Assert.Equal(InventoryStockScrapStatusesV1.Approved, approved.Scrap.Status);
        }

        Assert.Equal(0m, await LabelKgAsync(run, source.LabelId));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.MovementLines l JOIN inventory.Movements m ON m.Id = l.MovementId "
            + "WHERE m.Kind = 7 AND l.Block = 2 AND l.Direction = 2 AND l.QuantityKg = 40"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Kind = 4"));

        // An empty label has no stock left to scrap.
        await using (var scope = run.Store(CompanyA))
        {
            await AssertScrapRefused(scope.Store.RecordStockScrapAsync(new RecordInventoryStockScrapV1(Guid.NewGuid(),
                    source.LabelId, (version + 1).ToString(), "1", At.AddHours(3), "Tekrar"), Metadata(run), static _ => Task.CompletedTask),
                409, InventoryStockScrapCodesV1.PositionConflict);
        }
    }

    [SqlServerFact]
    public async Task Stock_scrap_refuses_stale_foreign_not_own_over_book_and_over_allocated_scraps_without_an_effect()
    {
        await using var run = await Run.StartAsync();
        var own = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-SCRAP-OWN", 100m);
        var foreign = await SeedFreePhysicalSourceAsync(run, CompanyB, "SYN-SCRAP-FOREIGN", 100m);
        var approver = Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") };
        // Writing off a customer's goods, or our goods at a fason, is not decided: refused before anything is stored.
        var customerOwned = await SeedOtherPartyFreeSourceAsync(run, "SYN-SCRAP-CUSTOMER",
            new StockOwner(StockPartyKind.Counterparty, Guid.NewGuid()), new StockCustodian(StockPartyKind.Company, CompanyA));
        var fasonHeld = await SeedOtherPartyFreeSourceAsync(run, "SYN-SCRAP-FASON",
            new StockOwner(StockPartyKind.Company, CompanyA), new StockCustodian(StockPartyKind.Counterparty, Guid.NewGuid()));

        await using (var scope = run.Store(CompanyA))
        {
            await AssertScrapRefused(scope.Store.RecordStockScrapAsync(Scrap(own.LabelId, own.LabelVersion + 1, "10"), Metadata(run), static _ => Task.CompletedTask),
                409, InventoryStockScrapCodesV1.LabelVersionConflict);
            await AssertScrapRefused(scope.Store.RecordStockScrapAsync(Scrap(foreign.LabelId, foreign.LabelVersion, "10"), Metadata(run), static _ => Task.CompletedTask),
                404, InventoryStockScrapCodesV1.LabelNotFound);
            await AssertScrapRefused(scope.Store.RecordStockScrapAsync(Scrap(own.LabelId, own.LabelVersion, "10.0001"), Metadata(run), static _ => Task.CompletedTask),
                400, InventoryStockScrapCodesV1.ValidationFailed);
            await AssertScrapRefused(scope.Store.RecordStockScrapAsync(Scrap(own.LabelId, own.LabelVersion, "0"), Metadata(run), static _ => Task.CompletedTask),
                400, InventoryStockScrapCodesV1.ValidationFailed);
            await AssertScrapRefused(scope.Store.RecordStockScrapAsync(
                    Scrap(own.LabelId, own.LabelVersion, "10") with { Reason = "  " }, Metadata(run), static _ => Task.CompletedTask),
                400, InventoryStockScrapCodesV1.ValidationFailed);
            await AssertScrapRefused(scope.Store.RecordStockScrapAsync(Scrap(own.LabelId, own.LabelVersion, "100.001"), Metadata(run), static _ => Task.CompletedTask),
                409, InventoryStockScrapCodesV1.ExceedsBook);
            Assert.Null(await scope.Store.ReadStockScrapsAsync(foreign.LabelId));
            await AssertScrapRefused(scope.Store.RecordStockScrapAsync(
                    Scrap(customerOwned.LabelId, customerOwned.LabelVersion, "1"), Metadata(run), static _ => Task.CompletedTask),
                409, InventoryStockScrapCodesV1.NotOwnStock);
            await AssertScrapRefused(scope.Store.RecordStockScrapAsync(
                    Scrap(fasonHeld.LabelId, fasonHeld.LabelVersion, "1"), Metadata(run), static _ => Task.CompletedTask),
                409, InventoryStockScrapCodesV1.NotOwnStock);
            await AssertScrapRefused(scope.Store.DecideStockScrapAsync(Guid.NewGuid(),
                    new(InventoryStockScrapDecisionKindV1.Approve, "Yok"), approver, static _ => Task.CompletedTask),
                404, InventoryStockScrapCodesV1.ScrapNotFound);
        }

        // One key, other content.
        var key = Metadata(run) with { IdempotencyKey = $"scrap-key-{Guid.NewGuid():N}" };
        var first = Scrap(own.LabelId, own.LabelVersion, "5");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.RecordStockScrapAsync(first, key, static _ => Task.CompletedTask);
            await AssertScrapRefused(scope.Store.RecordStockScrapAsync(Scrap(own.LabelId, own.LabelVersion, "6"), key, static _ => Task.CompletedTask),
                409, InventoryStockScrapCodesV1.IdempotencyConflict);
        }

        // The company fence: another company cannot see or decide this company's scrap.
        await using (var scope = run.Store(CompanyB))
        {
            await AssertScrapRefused(scope.Store.DecideStockScrapAsync(first.ScrapId,
                    new(InventoryStockScrapDecisionKindV1.Approve, "Başka şirket"), approver with { IdempotencyKey = Guid.NewGuid().ToString("N") }, static _ => Task.CompletedTask),
                404, InventoryStockScrapCodesV1.ScrapNotFound);
            Assert.Null(await scope.Store.ReadStockScrapsAsync(own.LabelId));
        }

        // Stale: the label changed after the scrap was recorded. Rejecting still closes it without an effect.
        await TouchLabelAsync(run, own.LabelId);
        await using (var scope = run.Store(CompanyA))
        {
            await AssertScrapRefused(scope.Store.DecideStockScrapAsync(first.ScrapId,
                    new(InventoryStockScrapDecisionKindV1.Approve, "Onay"), approver with { IdempotencyKey = Guid.NewGuid().ToString("N") }, static _ => Task.CompletedTask),
                409, InventoryStockScrapCodesV1.Stale);
            var rejected = await scope.Store.DecideStockScrapAsync(first.ScrapId,
                new(InventoryStockScrapDecisionKindV1.Reject, "Kayıttan sonra hareket oldu; yeniden girilecek."),
                approver with { IdempotencyKey = Guid.NewGuid().ToString("N") }, static _ => Task.CompletedTask);
            Assert.Equal((InventoryStockScrapStatusesV1.Rejected, (Guid?)null),
                (rejected.Scrap.Status, rejected.Scrap.Decision!.ScrapMovementId));
        }

        // A scrap below the active allocations is refused at record time, and at approval if a reservation arrived
        // after the scrap was recorded; the reservation is never silently broken.
        var version = await LabelVersionAsync(run, own.LabelId);
        var pending = Scrap(own.LabelId, version, "60");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.RecordStockScrapAsync(pending, Metadata(run), static _ => Task.CompletedTask);
            await scope.Store.CreateAsync(AllocationRequest(own with { LabelVersion = version }, "50"), Metadata(run));
        }

        version = await LabelVersionAsync(run, own.LabelId);
        await using (var scope = run.Store(CompanyA))
        {
            await AssertScrapRefused(scope.Store.RecordStockScrapAsync(Scrap(own.LabelId, version, "50.001"), Metadata(run), static _ => Task.CompletedTask),
                409, InventoryStockScrapCodesV1.AllocationConflict);
            // The allocation bumped the label, so the earlier scrap is stale before its allocation check.
            await AssertScrapRefused(scope.Store.DecideStockScrapAsync(pending.ScrapId,
                    new(InventoryStockScrapDecisionKindV1.Approve, "Onay"), approver with { IdempotencyKey = Guid.NewGuid().ToString("N") }, static _ => Task.CompletedTask),
                409, InventoryStockScrapCodesV1.Stale);
        }

        // A scrap recorded above the allocation still fails if the stored allocations grew without a label change.
        var within = Scrap(own.LabelId, version, "50");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.RecordStockScrapAsync(within, Metadata(run), static _ => Task.CompletedTask);
        }

        await ExecuteAsync(run, "UPDATE inventory.Allocations SET RemainingKg = 60, AllocatedKg = 60 WHERE LabelId = @label",
            ("@label", own.LabelId));
        await using (var scope = run.Store(CompanyA))
        {
            await AssertScrapRefused(scope.Store.DecideStockScrapAsync(within.ScrapId,
                    new(InventoryStockScrapDecisionKindV1.Approve, "Onay"), approver with { IdempotencyKey = Guid.NewGuid().ToString("N") }, static _ => Task.CompletedTask),
                409, InventoryStockScrapCodesV1.AllocationConflict);
        }

        Assert.Equal(100m, await LabelKgAsync(run, own.LabelId));
        Assert.Equal(0, await ScrapMovementsAsync(run));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.StockScraps"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.StockScrapDecisions WHERE Decision = 2"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.StockScrapDecisions WHERE Decision = 1"));
    }

    [SqlServerFact]
    public async Task Stock_scrap_late_authority_denials_do_not_disclose_replays_or_commit_record_and_approval_effects()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-SCRAP-LATE-AUTH", 100m);
        var request = Scrap(source.LabelId, source.LabelVersion, "12.5");
        var recorder = Metadata(run) with
        {
            IdempotencyKey = $"scrap-late-{Guid.NewGuid():N}",
            CorrelationId = $"scrap-late-{Guid.NewGuid():N}",
        };
        var forbidden = static () => new InventoryStockScrapException(403, InventoryStockScrapCodesV1.Forbidden);

        var beforeRecord = await ScrapEffectsAsync(run, source.LabelId);
        await using (var scope = run.Store(CompanyA))
        {
            await AssertScrapRefused(scope.Store.RecordStockScrapAsync(request, recorder,
                    FailStockScrapFenceOnCall(7, forbidden)),
                403, InventoryStockScrapCodesV1.Forbidden);
        }
        Assert.Equal(beforeRecord, await ScrapEffectsAsync(run, source.LabelId));

        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.RecordStockScrapAsync(request, recorder, static _ => Task.CompletedTask);
        }
        var recorded = await ScrapEffectsAsync(run, source.LabelId);

        await using (var scope = run.Store(CompanyA))
        {
            await AssertScrapRefused(scope.Store.RecordStockScrapAsync(request,
                    recorder with { CorrelationId = $"retry-{Guid.NewGuid():N}" },
                    FailStockScrapFenceOnCall(1, forbidden)),
                403, InventoryStockScrapCodesV1.Forbidden);
        }
        Assert.Equal(recorded, await ScrapEffectsAsync(run, source.LabelId));

        await using (var scope = run.Store(CompanyA))
        {
            await AssertScrapRefused(scope.Store.RecordStockScrapAsync(request,
                    recorder with { IdempotencyKey = $"identity-{Guid.NewGuid():N}" },
                    FailStockScrapFenceOnCall(2, forbidden)),
                403, InventoryStockScrapCodesV1.Forbidden);
        }
        Assert.Equal(recorded, await ScrapEffectsAsync(run, source.LabelId));

        var approval = new DecideInventoryStockScrapV1(InventoryStockScrapDecisionKindV1.Approve, "Geç yetki testi.");
        var ownerRefusalScrap = Scrap(source.LabelId, source.LabelVersion, "2");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.RecordStockScrapAsync(ownerRefusalScrap,
                recorder with
                {
                    IdempotencyKey = $"owner-refusal-record-{Guid.NewGuid():N}",
                    CorrelationId = $"owner-refusal-record-{Guid.NewGuid():N}",
                },
                static _ => Task.CompletedTask);
        }
        await SeedStockScrapOwnerRefusalAsync(run, ownerRefusalScrap.ScrapId);
        var ownerRefusalBaseline = await ScrapEffectsAsync(run, source.LabelId);
        await using (var scope = run.Store(CompanyA))
        {
            await AssertScrapRefused(scope.Store.DecideStockScrapAsync(ownerRefusalScrap.ScrapId, approval,
                    Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") },
                    FailStockScrapFenceOnCall(6, forbidden)),
                403, InventoryStockScrapCodesV1.Forbidden);
        }
        Assert.Equal(ownerRefusalBaseline, await ScrapEffectsAsync(run, source.LabelId));

        var approver = Metadata(run) with
        {
            ActorId = Guid.NewGuid().ToString("D"),
            IdempotencyKey = $"approve-late-{Guid.NewGuid():N}",
            CorrelationId = $"approve-late-{Guid.NewGuid():N}",
        };
        var sameScopeScrap = Scrap(source.LabelId, source.LabelVersion, "1");
        var sameScopeRecorder = recorder with
        {
            IdempotencyKey = $"same-scope-{Guid.NewGuid():N}",
            CorrelationId = $"same-scope-{Guid.NewGuid():N}",
        };
        await using (var scope = run.Store(CompanyA))
        {
            await AssertScrapRefused(scope.Store.DecideStockScrapAsync(request.ScrapId, approval, approver,
                    FailStockScrapFenceOnCall(6, forbidden)),
                403, InventoryStockScrapCodesV1.Forbidden);
            await scope.Store.RecordStockScrapAsync(sameScopeScrap, sameScopeRecorder,
                static _ => Task.CompletedTask);
        }
        var afterSameScopeWrite = await ScrapEffectsAsync(run, source.LabelId);
        Assert.Equal(ownerRefusalBaseline with
        {
            Scraps = ownerRefusalBaseline.Scraps + 1,
            Commands = ownerRefusalBaseline.Commands + 1,
            AuditEvents = ownerRefusalBaseline.AuditEvents + 1,
        }, afterSameScopeWrite);

        await using (var scope = run.Store(CompanyA))
        {
            await AssertScrapRefused(scope.Store.DecideStockScrapAsync(request.ScrapId, approval, approver,
                    FailStockScrapFenceOnCall(7, forbidden)),
                403, InventoryStockScrapCodesV1.Forbidden);
        }
        Assert.Equal(afterSameScopeWrite, await ScrapEffectsAsync(run, source.LabelId));

        var cancelledRequest = Scrap(source.LabelId, source.LabelVersion, "10");
        using var cancellation = new CancellationTokenSource();
        await using (var scope = run.Store(CompanyA))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Store.RecordStockScrapAsync(
                cancelledRequest,
                recorder with { IdempotencyKey = $"cancel-{Guid.NewGuid():N}" },
                CancelStockScrapFenceOnCall(7, cancellation),
                cancellation.Token));
        }
        Assert.Equal(afterSameScopeWrite, await ScrapEffectsAsync(run, source.LabelId));
    }

    [SqlServerFact]
    public async Task Stock_scrap_rows_are_constrained_by_SQL()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-SCRAP-SQL", 10m);
        var location = await run.LocationOfAsync(CompanyA);
        foreach (var (book, scrap) in new[] { (10m, 0m), (10m, 10.001m), (0m, 1m) })
        {
            var exception = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(run,
                "INSERT INTO inventory.StockScraps (Id, CompanyId, LabelId, LabelVersion, OwnerKind, OwnerId, CustodianKind, "
                + "CustodianId, LocationId, ProductId, Block, BookKg, ScrapKg, ScrappedAtUtc, Reason, PayloadHash, RecordedAtUtc, "
                + "RecordedBy, CorrelationId) VALUES (NEWID(), @company, @label, 1, 1, @company, 1, @company, @location, "
                + "@product, 1, @book, @scrap, SYSDATETIMEOFFSET(), N'x', REPLICATE('0', 64), SYSDATETIMEOFFSET(), N'a', N'c')",
                ("@company", CompanyA), ("@label", source.LabelId), ("@location", location), ("@product", source.ProductId),
                ("@book", book), ("@scrap", scrap)));
            Assert.Equal(547, exception.Number);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.StockScraps"));
    }

    [SqlServerFact]
    public async Task Stock_scrap_migration_rolls_back_when_empty_and_blocks_after_a_scrap()
    {
        const string previousMigration = "20260928120614_AddInventoryAllocationChanges";
        const string currentMigration = "20260928161227_AddInventoryStockScraps";
        await using var run = await Run.StartAsync();
        await using (var context = run.Context(CompanyA))
        {
            await context.GetService<IMigrator>().MigrateAsync(previousMigration);
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'inventory') AND name LIKE N'StockScrap%'"));
            await context.GetService<IMigrator>().MigrateAsync(currentMigration);
            await context.Database.MigrateAsync();
        }

        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-SCRAP-DOWN", 10m);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.RecordStockScrapAsync(Scrap(source.LabelId, source.LabelVersion, "1"), Metadata(run), static _ => Task.CompletedTask);
        }

        await RemoveSyntheticProductVersionsForHistoricalMigrationAsync(
            run, "SYN-SCRAP-DOWN", expectedLabels: 1);

        await using (var context = run.Context(CompanyA))
        {
            var rejected = await Assert.ThrowsAsync<SqlException>(
                () => context.GetService<IMigrator>().MigrateAsync(previousMigration));
            Assert.Equal(51053, rejected.Number);
            Assert.Equal(currentMigration, (await context.Database.GetAppliedMigrationsAsync()).Last());
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.StockScraps"));
    }

    private static RecordInventoryStockScrapV1 Scrap(Guid labelId, long version, string scrapKg) =>
        new(Guid.NewGuid(), labelId, version.ToString(), scrapKg, At.AddHours(1), "Sentetik hurda.");

    private static async Task AssertScrapRefused(Task task, int status, string code)
    {
        var exception = await Assert.ThrowsAsync<InventoryStockScrapException>(() => task);
        Assert.Equal((status, code), (exception.Status, exception.Code));
    }

    private static async Task<int> ScrapMovementsAsync(Run run) =>
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE Kind = 7");

    private static async Task<StockScrapEffects> ScrapEffectsAsync(Run run, Guid labelId) => new(
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.StockScraps"),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.StockScrapDecisions"),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE Kind = 7"),
        await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE CommandName LIKE N'inventory.stock-scrap.%'"),
        await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType IN (N'Inventory.StockScrap', N'Inventory.StockMovement')"),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages"),
        await LabelVersionAsync(run, labelId),
        await LabelKgAsync(run, labelId));

    private static async Task SeedStockScrapOwnerRefusalAsync(Run run, Guid scrapId)
    {
        await using var context = run.Context(CompanyA);
        context.SourceGrains.Add(new SourceGrainRow
        {
            CompanyId = CompanyA,
            Kind = (int)StockMovementKind.Scrap,
            SourceKind = StockScrap.SourceKind,
            SourceId = scrapId,
            Grain = (int)StockSourceGrain.Whole,
        });
        context.Movements.Add(new MovementRow
        {
            Id = Guid.NewGuid(),
            CompanyId = CompanyA,
            Kind = (int)StockMovementKind.Scrap,
            OccurredAtUtc = At,
            SourceKind = StockScrap.SourceKind,
            SourceId = scrapId,
            SourceGrain = (int)StockSourceGrain.Whole,
            CostStatus = (int)CostStatus.NotCalculated,
            PayloadHash = new string('d', 64),
            RecordedAtUtc = At,
            RecordedBy = "synthetic-owner-refusal",
            CorrelationId = run.Correlation,
        });
        await context.SaveChangesAsync();
    }

    private static Func<CancellationToken, Task> FailStockScrapFenceOnCall(
        int expectedCall,
        Func<Exception> error)
    {
        var calls = 0;
        return cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Interlocked.Increment(ref calls) == expectedCall
                ? Task.FromException(error())
                : Task.CompletedTask;
        };
    }

    private static Func<CancellationToken, Task> CancelStockScrapFenceOnCall(
        int expectedCall,
        CancellationTokenSource cancellation)
    {
        var calls = 0;
        return token =>
        {
            if (Interlocked.Increment(ref calls) == expectedCall)
            {
                cancellation.Cancel();
            }

            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };
    }

    private sealed record StockScrapEffects(
        int Scraps,
        int Decisions,
        int ScrapMovements,
        int Commands,
        int AuditEvents,
        int OutboxMessages,
        long LabelVersion,
        decimal LabelKg);
}
