using System.Text.RegularExpressions;
using System.Text.Json;
using GoymenERP.Api.ProductRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.PriceLists;
using GoymenERP.Modules.Product.Infrastructure.Fixtures;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using GoymenERP.Modules.Settings.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// AT-018 price lists on a REAL SQL Server: create → reopen → draft revision (a kept line changes in place, a dropped one
/// goes, a new one comes) → publication → the Sales reference read; ADR-011 replay and terminal refusal; one draft per
/// list and increasing published starts held by the database; company isolation; the audit trail; the rollback refusal
/// (51063). The Product card is the isolated synthetic s04 sales candidate (Active, sellable, kg). The price-list
/// authority is a granted test double: the three names are registered by the central step, not by this slice.
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed class ProductPriceListSqlServerRelationalAcceptanceTests
{
    private static readonly Guid ProductId = ProductSalesCandidateFixture.ProductId;
    private static readonly Guid Kg = ApprovedUnitProfileCatalog.GdcKilogramV1.UnitId;

    [SqlServerFact]
    public async Task A_list_is_created_reopened_revised_published_and_answers_the_order_line_reference()
    {
        await using var run = await Run.StartAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var listId = Guid.NewGuid();
        var keptLine = Guid.NewGuid();
        var editorActor = Guid.NewGuid();
        var publisherActor = Guid.NewGuid();
        var create = new CreatePriceListRequestV1(listId, Guid.NewGuid(), "SENTETIK yurt içi", "TRY", "Sales", "ExcludingVat",
            ProductPriceListWire.FormatDate(today), null, [new PriceListLineRequestV1(keptLine, ProductId, Kg, "42000.50", "40000")]);

        PriceListResultV1 created;
        await using (var scope = run.Request())
        {
            created = await scope.Service.CreateAsync(create, Metadata("create-1"));
            Assert.False(created.IsReplay);
            Assert.Equal(("1", 1, "Draft"), (created.PriceList.Version, created.PriceList.DraftVersionNo, created.PriceList.Versions[0].State));
            var replay = await scope.Service.CreateAsync(create, Metadata("create-1") with { CorrelationId = "retry" });
            Assert.True(replay.IsReplay);
            Assert.Equal(created.OriginalCorrelationId, replay.OriginalCorrelationId);
            var other = await Assert.ThrowsAsync<ProductPriceListException>(() => scope.Service.CreateAsync(create with { Name = "başka" },
                Metadata("create-1")));
            Assert.Equal((409, ProductPriceListCodesV1.IdempotencyConflict), (other.Status, other.Code));
        }

        // Reopen under another writer: the stored list is rebuilt and this actor joins the exact version's preparer history.
        await using (var scope = run.Request(actor: editorActor))
        {
            var reopened = await scope.Service.GetAsync(listId);
            var line = Assert.Single(reopened.Versions[0].Lines);
            // decimal(19,4) restored in its canonical value: trailing zeros dropped, the value unchanged.
            Assert.Equal((ProductSalesCandidateFixture.Name, "42000.5", "40000"), (line.ProductName, line.UnitPrice, line.FloorUnitPrice));
            Assert.Equal(listId, Assert.Single((await scope.Service.ListAsync(null, null)).Items).Id);

            var stale = await Assert.ThrowsAsync<ProductPriceListException>(() => scope.Service.ReviseDraftAsync(listId,
                new RevisePriceListDraftRequestV1("7", "SENTETIK yurt içi", ProductPriceListWire.FormatDate(today), null, []), Metadata("stale-1")));
            Assert.Equal((409, ProductPriceListCodesV1.VersionConflict), (stale.Status, stale.Code));
            // ADR-011 §7: the same key answers the recorded refusal again, even though version 7 is still wrong.
            var staleReplay = await Assert.ThrowsAsync<ProductPriceListException>(() => scope.Service.ReviseDraftAsync(listId,
                new RevisePriceListDraftRequestV1("7", "SENTETIK yurt içi", ProductPriceListWire.FormatDate(today), null, []), Metadata("stale-1")));
            Assert.Equal(ProductPriceListCodesV1.VersionConflict, staleReplay.Code);

            var revised = await scope.Service.ReviseDraftAsync(listId, new RevisePriceListDraftRequestV1("1", "SENTETIK yurt içi satış",
                ProductPriceListWire.FormatDate(today), null,
                [
                    new PriceListLineRequestV1(keptLine, ProductId, Kg, "41000", null, "0", "10"),
                    new PriceListLineRequestV1(Guid.NewGuid(), ProductId, Kg, "40000", null, "10", "20"),
                    new PriceListLineRequestV1(Guid.NewGuid(), ProductId, Kg, "39000", null, "25", null),
                ]),
                Metadata("revise-1"));
            Assert.Equal(("2", "41000"), (revised.PriceList.Version, revised.PriceList.Versions[0].Lines[0].UnitPrice));
        }

        await using (var creatorScope = run.Request())
        {
            var refused = await Assert.ThrowsAsync<ProductPriceListException>(() => creatorScope.Service.PublishAsync(listId,
                new PublishPriceListVersionRequestV1("2", 1), Metadata("publish-creator")));
            Assert.Equal((409, ProductPriceListCodesV1.StateConflict, PriceListRuleCodes.PublisherPreparedVersion),
                (refused.Status, refused.Code, Assert.Single(refused.Errors).Code));
            var replay = await Assert.ThrowsAsync<ProductPriceListException>(() => creatorScope.Service.PublishAsync(listId,
                new PublishPriceListVersionRequestV1("2", 1), Metadata("publish-creator") with { CorrelationId = "retry" }));
            Assert.Equal(PriceListRuleCodes.PublisherPreparedVersion, Assert.Single(replay.Errors).Code);
        }

        await using (var editorScope = run.Request(actor: editorActor))
        {
            var refused = await Assert.ThrowsAsync<ProductPriceListException>(() => editorScope.Service.PublishAsync(listId,
                new PublishPriceListVersionRequestV1("2", 1), Metadata("publish-editor")));
            Assert.Equal((409, ProductPriceListCodesV1.StateConflict, PriceListRuleCodes.PublisherPreparedVersion),
                (refused.Status, refused.Code, Assert.Single(refused.Errors).Code));
        }

        await using (var scope = run.Request(actor: publisherActor))
        {
            var published = await scope.Service.PublishAsync(listId, new PublishPriceListVersionRequestV1("2", 1), Metadata("publish-1"));
            Assert.Equal(("Published", "Active", (int?)1), (published.PriceList.Versions[0].State, published.PriceList.Versions[0].Status,
                published.PriceList.ActiveVersionNo));

            Assert.Empty(await scope.Reader.SearchAsync(ProductId, [Kg], "TRY", today));
            var reference = Assert.Single(await scope.QuantityReader.SearchAsync(ProductId, Kg, 9.999m, "TRY", today));
            Assert.Equal((listId, 1, 41000m, (decimal?)null, "ExcludingVat", "Sales"),
                (reference.PriceListId, reference.VersionNo, reference.UnitPrice, reference.FloorUnitPrice, reference.VatBasis, reference.Channel));
            Assert.Equal(40000m, Assert.Single(await scope.QuantityReader.SearchAsync(ProductId, Kg, 10m, "TRY", today)).UnitPrice);
            Assert.Empty(await scope.QuantityReader.SearchAsync(ProductId, Kg, 20m, "TRY", today));
            Assert.Equal(39000m, Assert.Single(await scope.QuantityReader.SearchAsync(ProductId, Kg, 25m, "TRY", today)).UnitPrice);
            Assert.Empty(await scope.QuantityReader.SearchAsync(ProductId, Kg, 10m, "USD", today));
            Assert.Empty(await scope.QuantityReader.SearchAsync(ProductId, Kg, 10m, "TRY", today.AddDays(-1)));
            Assert.NotNull(await scope.QuantityReader.GetAsync(listId, 1, ProductId, Kg, 10m, today));
            Assert.Null(await scope.QuantityReader.GetAsync(listId, 2, ProductId, Kg, 10m, today));
        }

        // The create key returns the exact successful response captured in its transaction, not today's revised/published list.
        await using (var scope = run.Request())
        {
            var replay = await scope.Service.CreateAsync(create, Metadata("create-1") with { CorrelationId = "much-later" });
            Assert.True(replay.IsReplay);
            Assert.Equal(JsonSerializer.Serialize(created.PriceList), JsonSerializer.Serialize(replay.PriceList));
        }

        // Another company sees neither the list nor its prices.
        await using (var scope = run.Request(TestCompanyIds.CompanyB))
        {
            Assert.Equal(404, (await Assert.ThrowsAsync<ProductPriceListException>(() => scope.Service.GetAsync(listId))).Status);
            Assert.Empty(await scope.Reader.SearchAsync(ProductId, [Kg], "TRY", today));
            Assert.Empty(await scope.QuantityReader.SearchAsync(ProductId, Kg, 10m, "TRY", today));
        }

        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM product.PriceListLines WHERE PriceListId = @id AND LowerQuantity >= 0", listId));

        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Product.PriceList' AND EntityId = @id", listId.ToString("D")));
        var storedPreparers = JsonSerializer.Deserialize<string[]>(await run.ScalarAsync<string>(
            "SELECT PreparerActorIdsJson FROM product.PriceListVersions WHERE PriceListId = @id AND VersionNo = 1", listId));
        Assert.Equal(new[] { run.CreatorActorId.ToString("D"), editorActor.ToString("D") }, storedPreparers);
        Assert.Equal(6, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceListAttempts"));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceListAttempts WHERE Outcome = 2"));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceListAttempts WHERE Outcome = 1 AND ISJSON(ResultJson) = 1"));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceListAttempts WHERE Outcome = 2 AND ResultJson IS NULL"));
    }

    [SqlServerFact]
    public async Task The_last_authority_fence_and_same_key_race_leave_one_exact_effect()
    {
        await using var run = await Run.StartAsync();
        var today = ProductPriceListWire.FormatDate(DateOnly.FromDateTime(DateTime.UtcNow));

        var revokedId = Guid.NewGuid();
        var revoked = new CreatePriceListRequestV1(revokedId, Guid.NewGuid(), "SENTETIK revoked", "TRY", "Sales", "ExcludingVat", today,
            null, [new PriceListLineRequestV1(Guid.NewGuid(), ProductId, Kg, "10", null)]);
        var finalBeforeCommit = new DenyAtBoundaryAuthority(denyAtCall: 5);
        await using (var scope = run.Request(authority: finalBeforeCommit))
        {
            var forbidden = await Assert.ThrowsAsync<ProductPriceListException>(() => scope.Service.CreateAsync(revoked, Metadata("revoked")));
            Assert.Equal(403, forbidden.Status);
        }

        Assert.True(finalBeforeCommit.ReachedDeniedBoundary);
        Assert.Equal(5, finalBeforeCommit.Calls);

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceLists WHERE Id = @id", revokedId));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceListAttempts WHERE PriceListId = @id", revokedId));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id", revokedId.ToString("D")));

        // A tenant-filtered miss and detailed Product-card failures are business answers. Late authority loss wins
        // over 404/product_invalid, and neither path leaves a list, attempt or audit effect.
        var missingReadBoundary = new DenyAtBoundaryAuthority(denyAtCall: 2);
        await using (var revokedMissing = run.Request(authority: missingReadBoundary))
        {
            var forbidden = await Assert.ThrowsAsync<ProductPriceListException>(() => revokedMissing.Service.GetAsync(Guid.NewGuid()));
            Assert.Equal((403, ProductPriceListCodesV1.Forbidden), (forbidden.Status, forbidden.Code));
        }
        Assert.True(missingReadBoundary.ReachedDeniedBoundary);
        Assert.Equal(2, missingReadBoundary.Calls);

        var missingMutationBoundary = new DenyAtBoundaryAuthority(denyAtCall: 4);
        await using (var revokedMutationMissing = run.Request(authority: missingMutationBoundary))
        {
            var forbidden = await Assert.ThrowsAsync<ProductPriceListException>(() => revokedMutationMissing.Service.ReviseDraftAsync(
                Guid.NewGuid(), new RevisePriceListDraftRequestV1("1", "SENTETIK missing revoked", today, null, []),
                Metadata("missing-mutation-revoked")));
            Assert.Equal((403, ProductPriceListCodesV1.Forbidden), (forbidden.Status, forbidden.Code));
        }
        Assert.True(missingMutationBoundary.ReachedDeniedBoundary);
        Assert.Equal(4, missingMutationBoundary.Calls);

        var invalidProductId = Guid.NewGuid();
        var invalidProduct = revoked with
        {
            PriceListId = invalidProductId,
            VersionId = Guid.NewGuid(),
            Name = "SENTETIK invalid product revoked",
            Lines = [new PriceListLineRequestV1(Guid.NewGuid(), Guid.NewGuid(), Kg, "10", null)],
        };
        var productFactsBoundary = new DenyAtBoundaryAuthority(denyAtCall: 3);
        await using (var revokedProductFacts = run.Request(authority: productFactsBoundary))
        {
            var forbidden = await Assert.ThrowsAsync<ProductPriceListException>(() => revokedProductFacts.Service.CreateAsync(
                invalidProduct, Metadata("invalid-product-revoked")));
            Assert.Equal((403, ProductPriceListCodesV1.Forbidden), (forbidden.Status, forbidden.Code));
        }
        Assert.True(productFactsBoundary.ReachedDeniedBoundary);
        Assert.Equal(3, productFactsBoundary.Calls);

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceLists WHERE Id = @id", invalidProductId));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceListAttempts WHERE PriceListId = @id", invalidProductId));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id", invalidProductId.ToString("D")));

        var revokedNameAnswerId = Guid.NewGuid();
        var freshNameBoundary = new DenyAtBoundaryAuthority(denyAtCall: 4);
        await using (var revokedNameAnswer = run.Request(authority: freshNameBoundary))
        {
            var forbidden = await Assert.ThrowsAsync<ProductPriceListException>(() => revokedNameAnswer.Service.CreateAsync(
                revoked with
                {
                    PriceListId = revokedNameAnswerId,
                    VersionId = Guid.NewGuid(),
                    Name = "SENTETIK fresh name revoked",
                }, Metadata("fresh-name-revoked")));
            Assert.Equal((403, ProductPriceListCodesV1.Forbidden), (forbidden.Status, forbidden.Code));
        }
        Assert.True(freshNameBoundary.ReachedDeniedBoundary);
        Assert.Equal(4, freshNameBoundary.Calls);

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceLists WHERE Id = @id", revokedNameAnswerId));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceListAttempts WHERE PriceListId = @id", revokedNameAnswerId));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id", revokedNameAnswerId.ToString("D")));

        var driftedProductId = Guid.NewGuid();
        var driftAuthority = new PauseAuthority(pauseAtCall: 3);
        await using (var drifted = run.Request(authority: driftAuthority, driftableCompanyContext: true))
        {
            var pending = drifted.Service.CreateAsync(revoked with
            {
                PriceListId = driftedProductId,
                VersionId = Guid.NewGuid(),
                Name = "SENTETIK product context drift",
            }, Metadata("product-context-drift"));
            await driftAuthority.Entered;
            drifted.CompanyContext.Set(TestCompanyIds.CompanyB);
            driftAuthority.Release();
            var forbidden = await Assert.ThrowsAsync<ProductPriceListException>(() => pending);
            Assert.Equal((403, ProductPriceListCodesV1.Forbidden), (forbidden.Status, forbidden.Code));
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceLists WHERE Id = @id", driftedProductId));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceListAttempts WHERE PriceListId = @id", driftedProductId));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id", driftedProductId.ToString("D")));

        var terminalId = Guid.NewGuid();
        await using (var scope = run.Request())
        {
            await scope.Service.CreateAsync(revoked with { PriceListId = terminalId, VersionId = Guid.NewGuid(), Name = "SENTETIK terminal" },
                Metadata("terminal-create"));
        }
        var recordRefusalBoundary = new DenyAtBoundaryAuthority(denyAtCall: 5);
        await using (var scope = run.Request(authority: recordRefusalBoundary))
        {
            var forbidden = await Assert.ThrowsAsync<ProductPriceListException>(() => scope.Service.ReviseDraftAsync(terminalId,
                new RevisePriceListDraftRequestV1("99", "SENTETIK terminal", today, null, []), Metadata("terminal-refusal")));
            Assert.Equal(403, forbidden.Status);
        }
        Assert.True(recordRefusalBoundary.ReachedDeniedBoundary);
        Assert.Equal(5, recordRefusalBoundary.Calls);
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM product.PriceListAttempts WHERE PriceListId = @id AND CommandName = N'product.price-list.revise-draft.v1'",
            terminalId));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM product.PriceLists WHERE Id = @id", terminalId));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id", terminalId.ToString("D")));

        // A duplicate name is company business data. Lose the write grant only after the replay lookup and the awaited
        // name lookup: ReviseDraft must answer 403 without disclosing duplicate_name or recording any effect.
        var duplicateHolderId = Guid.NewGuid();
        var duplicateTargetId = Guid.NewGuid();
        await using (var scope = run.Request())
        {
            await scope.Service.CreateAsync(revoked with
            {
                PriceListId = duplicateHolderId,
                VersionId = Guid.NewGuid(),
                Name = "SENTETIK revise duplicate holder",
            }, Metadata("revise-holder-create"));
            await scope.Service.CreateAsync(revoked with
            {
                PriceListId = duplicateTargetId,
                VersionId = Guid.NewGuid(),
                Name = "SENTETIK revise duplicate target",
            }, Metadata("revise-target-create"));
        }

        var duplicateRevision = new RevisePriceListDraftRequestV1(
            "1", "SENTETIK revise duplicate holder", today, null, []);
        var duplicateRevisionNameBoundary = new DenyAtBoundaryAuthority(denyAtCall: 3);
        await using (var revokedDuplicate = run.Request(authority: duplicateRevisionNameBoundary))
        {
            var forbidden = await Assert.ThrowsAsync<ProductPriceListException>(() => revokedDuplicate.Service.ReviseDraftAsync(
                duplicateTargetId, duplicateRevision, Metadata("revise-duplicate-revoked")));
            Assert.Equal((403, ProductPriceListCodesV1.Forbidden), (forbidden.Status, forbidden.Code));
        }
        Assert.True(duplicateRevisionNameBoundary.ReachedDeniedBoundary);
        Assert.Equal(3, duplicateRevisionNameBoundary.Calls);

        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM product.PriceLists WHERE Id = @id", duplicateTargetId));
        Assert.Equal("SENTETIK revise duplicate target", await run.ScalarAsync<string>(
            "SELECT Name FROM product.PriceLists WHERE Id = @id", duplicateTargetId));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM product.PriceListAttempts WHERE PriceListId = @id AND CommandName = N'product.price-list.revise-draft.v1'",
            duplicateTargetId));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id", duplicateTargetId.ToString("D")));

        await using (var authorizedDuplicate = run.Request())
        {
            var duplicate = await Assert.ThrowsAsync<ProductPriceListException>(() => authorizedDuplicate.Service.ReviseDraftAsync(
                duplicateTargetId, duplicateRevision, Metadata("revise-duplicate-authorized")));
            Assert.Equal((409, ProductPriceListCodesV1.DuplicateName), (duplicate.Status, duplicate.Code));
        }

        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM product.PriceLists WHERE Id = @id", duplicateTargetId));
        Assert.Equal("SENTETIK revise duplicate target", await run.ScalarAsync<string>(
            "SELECT Name FROM product.PriceLists WHERE Id = @id", duplicateTargetId));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM product.PriceListAttempts WHERE PriceListId = @id AND CommandName = N'product.price-list.revise-draft.v1'",
            duplicateTargetId));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id", duplicateTargetId.ToString("D")));

        var raceId = Guid.NewGuid();
        var same = new CreatePriceListRequestV1(raceId, Guid.NewGuid(), "SENTETIK race", "TRY", "Sales", "ExcludingVat", today,
            null, [new PriceListLineRequestV1(Guid.NewGuid(), ProductId, Kg, "11", null)]);
        var barrier = new CommitBarrier(2);
        await using var first = run.Request(authority: new BarrierAuthority(barrier));
        await using var second = run.Request(authority: new BarrierAuthority(barrier));
        var answers = await Task.WhenAll(
            first.Service.CreateAsync(same, Metadata("same-race")),
            second.Service.CreateAsync(same, Metadata("same-race") with { CorrelationId = "corr-loser" }));
        Assert.Equal(1, answers.Count(answer => answer.IsReplay));
        Assert.Equal(1, answers.Count(answer => !answer.IsReplay));
        Assert.Equal(JsonSerializer.Serialize(answers[0].PriceList), JsonSerializer.Serialize(answers[1].PriceList));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceLists WHERE Id = @id", raceId));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceListAttempts WHERE PriceListId = @id", raceId));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id", raceId.ToString("D")));

        // The loser can miss the initial attempt lookup, then observe the committed name before it reaches CreateAsync.
        // That exact attempt must still replay; the name check must not turn it into a false duplicate-name refusal.
        var earlyRaceId = Guid.NewGuid();
        var earlySame = same with
        {
            PriceListId = earlyRaceId,
            VersionId = Guid.NewGuid(),
            Name = "SENTETIK early race",
        };
        var pausedAuthority = new PauseAuthority(pauseAtCall: 2);
        await using var earlyLoser = run.Request(authority: pausedAuthority);
        var earlyLoserTask = earlyLoser.Service.CreateAsync(earlySame, Metadata("early-race") with { CorrelationId = "corr-early-loser" });
        await pausedAuthority.Entered;
        PriceListResultV1 earlyWinner;
        try
        {
            await using var earlyWinnerScope = run.Request();
            earlyWinner = await earlyWinnerScope.Service.CreateAsync(earlySame, Metadata("early-race"));
        }
        finally
        {
            pausedAuthority.Release();
        }

        var earlyReplay = await earlyLoserTask;
        Assert.False(earlyWinner.IsReplay);
        Assert.True(earlyReplay.IsReplay);
        Assert.Equal(earlyWinner.OriginalCorrelationId, earlyReplay.OriginalCorrelationId);
        Assert.Equal(JsonSerializer.Serialize(earlyWinner.PriceList), JsonSerializer.Serialize(earlyReplay.PriceList));

        var replayConflictBoundary = new DenyAtBoundaryAuthority(denyAtCall: 2);
        await using (var revokedConflict = run.Request(authority: replayConflictBoundary))
        {
            var forbidden = await Assert.ThrowsAsync<ProductPriceListException>(() => revokedConflict.Service.CreateAsync(
                earlySame with { Name = "SENTETIK hidden changed payload" }, Metadata("early-race")));
            Assert.Equal((403, ProductPriceListCodesV1.Forbidden), (forbidden.Status, forbidden.Code));
        }
        Assert.True(replayConflictBoundary.ReachedDeniedBoundary);
        Assert.Equal(2, replayConflictBoundary.Calls);

        await using (var changedPayload = run.Request())
        {
            var payloadConflict = await Assert.ThrowsAsync<ProductPriceListException>(() => changedPayload.Service.CreateAsync(
                earlySame with { Name = "SENTETIK early changed" }, Metadata("early-race")));
            Assert.Equal((409, ProductPriceListCodesV1.IdempotencyConflict), (payloadConflict.Status, payloadConflict.Code));
        }

        await using (var changedActor = run.Request(actor: Guid.NewGuid()))
        {
            var actorConflict = await Assert.ThrowsAsync<ProductPriceListException>(() =>
                changedActor.Service.CreateAsync(earlySame, Metadata("early-race")));
            Assert.Equal((409, ProductPriceListCodesV1.IdempotencyConflict), (actorConflict.Status, actorConflict.Code));
        }

        var revokedDuplicateId = Guid.NewGuid();
        var createDuplicateNameBoundary = new DenyAtBoundaryAuthority(denyAtCall: 4);
        await using (var revokedDuplicate = run.Request(authority: createDuplicateNameBoundary))
        {
            var forbidden = await Assert.ThrowsAsync<ProductPriceListException>(() => revokedDuplicate.Service.CreateAsync(
                earlySame with { PriceListId = revokedDuplicateId, VersionId = Guid.NewGuid() }, Metadata("early-revoked-duplicate")));
            Assert.Equal(403, forbidden.Status);
        }
        Assert.True(createDuplicateNameBoundary.ReachedDeniedBoundary);
        Assert.Equal(4, createDuplicateNameBoundary.Calls);

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceLists WHERE Id = @id", revokedDuplicateId));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceListAttempts WHERE PriceListId = @id", revokedDuplicateId));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id", revokedDuplicateId.ToString("D")));

        await using (var otherNameHolder = run.Request())
        {
            var duplicate = await Assert.ThrowsAsync<ProductPriceListException>(() => otherNameHolder.Service.CreateAsync(
                earlySame with { PriceListId = Guid.NewGuid(), VersionId = Guid.NewGuid() }, Metadata("early-duplicate")));
            Assert.Equal((409, ProductPriceListCodesV1.DuplicateName), (duplicate.Status, duplicate.Code));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceLists WHERE Id = @id", earlyRaceId));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceListAttempts WHERE PriceListId = @id", earlyRaceId));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id", earlyRaceId.ToString("D")));

        var changedId = Guid.NewGuid();
        var changedBarrier = new CommitBarrier(2);
        var changedBase = same with { PriceListId = changedId, VersionId = Guid.NewGuid(), Name = "SENTETIK changed-a" };
        await using var changedFirst = run.Request(authority: new BarrierAuthority(changedBarrier));
        await using var changedSecond = run.Request(authority: new BarrierAuthority(changedBarrier));
        var changedTasks = new[]
        {
            Capture(changedFirst.Service.CreateAsync(changedBase, Metadata("changed-race"))),
            Capture(changedSecond.Service.CreateAsync(changedBase with { Name = "SENTETIK changed-b" },
                Metadata("changed-race") with { CorrelationId = "corr-changed" })),
        };
        var changedAnswers = await Task.WhenAll(changedTasks);
        Assert.Single(changedAnswers, answer => answer.Result is not null);
        var conflict = Assert.Single(changedAnswers, answer => answer.Error is not null).Error!;
        Assert.Equal((409, ProductPriceListCodesV1.IdempotencyConflict), (conflict.Status, conflict.Code));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceLists WHERE Id = @id", changedId));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceListAttempts WHERE PriceListId = @id", changedId));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id", changedId.ToString("D")));
    }

    [SqlServerFact]
    public async Task Versions_supersede_by_date_and_the_database_holds_one_draft_and_increasing_published_starts()
    {
        await using var run = await Run.StartAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var listId = Guid.NewGuid();
        var firstPublisher = Guid.NewGuid();
        var secondPublisher = Guid.NewGuid();
        await using (var scope = run.Request())
        {
            await scope.Service.CreateAsync(new CreatePriceListRequestV1(listId, Guid.NewGuid(), "SENTETIK SÜRÜMLÜ", "TRY", "Sales", "ExcludingVat",
                ProductPriceListWire.FormatDate(today), null, [new PriceListLineRequestV1(Guid.NewGuid(), ProductId, Kg, "100", null)]),
                Metadata("v-create"));
        }

        await using (var scope = run.Request(actor: firstPublisher))
        {
            await scope.Service.PublishAsync(listId, new PublishPriceListVersionRequestV1("1", 1), Metadata("v-publish-1"));
        }

        await using (var scope = run.Request())
        {
            var started = await scope.Service.StartVersionAsync(listId,
                new StartPriceListVersionRequestV1("2", Guid.NewGuid(), ProductPriceListWire.FormatDate(today.AddDays(10)), null), Metadata("v-start"));
            Assert.Equal("100", started.PriceList.Versions[1].Lines[0].UnitPrice);
            var line = started.PriceList.Versions[1].Lines[0];
            await scope.Service.ReviseDraftAsync(listId, new RevisePriceListDraftRequestV1("3", "SENTETIK SÜRÜMLÜ",
                ProductPriceListWire.FormatDate(today.AddDays(10)), null, [new PriceListLineRequestV1(line.LineId, ProductId, Kg, "110", "105")]),
                Metadata("v-revise"));
            var twice = await Assert.ThrowsAsync<ProductPriceListException>(() => scope.Service.StartVersionAsync(listId,
                new StartPriceListVersionRequestV1("4", Guid.NewGuid(), ProductPriceListWire.FormatDate(today.AddDays(20)), null), Metadata("v-start-2")));
            Assert.Equal((409, ProductPriceListCodesV1.StateConflict), (twice.Status, twice.Code));
        }

        await using (var scope = run.Request(actor: secondPublisher))
        {
            await scope.Service.PublishAsync(listId, new PublishPriceListVersionRequestV1("4", 2), Metadata("v-publish-2"));

            Assert.Equal(100m, Assert.Single(await scope.Reader.SearchAsync(ProductId, [Kg], "TRY", today.AddDays(9))).UnitPrice);
            var later = Assert.Single(await scope.Reader.SearchAsync(ProductId, [Kg], "TRY", today.AddDays(10)));
            Assert.Equal((2, 110m, (decimal?)105m), (later.VersionNo, later.UnitPrice, later.FloorUnitPrice));

            var duplicate = await Assert.ThrowsAsync<ProductPriceListException>(() => scope.Service.CreateAsync(new CreatePriceListRequestV1(
                Guid.NewGuid(), Guid.NewGuid(), "SENTETIK sürümlü", "TRY", "Sales", "ExcludingVat", ProductPriceListWire.FormatDate(today), null, []),
                Metadata("v-dup")));
            Assert.Equal((409, ProductPriceListCodesV1.DuplicateName), (duplicate.Status, duplicate.Code));
        }

        // The database refuses a second draft and a repeated published start even if the aggregate were bypassed.
        await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO product.PriceListVersions (CompanyId, PriceListId, VersionNo, VersionId, ValidFrom, State, CreatedAtUtc, CreatedBy, "
            + "PreparerActorIdsJson, PublishedAtUtc, PublishedBy) SELECT CompanyId, PriceListId, 3, NEWID(), ValidFrom, 2, "
            + "SYSDATETIMEOFFSET(), N'sql', N'[\"sql\"]', SYSDATETIMEOFFSET(), N'publisher' FROM product.PriceListVersions "
            + "WHERE PriceListId = @id AND VersionNo = 1", listId));
        await run.ExecuteAsync(
            "INSERT INTO product.PriceListVersions (CompanyId, PriceListId, VersionNo, VersionId, ValidFrom, State, CreatedAtUtc, CreatedBy, "
            + "PreparerActorIdsJson) SELECT CompanyId, PriceListId, 3, NEWID(), DATEADD(day, 30, ValidFrom), 1, "
            + "SYSDATETIMEOFFSET(), N'sql', N'[\"sql\"]' FROM product.PriceListVersions "
            + "WHERE PriceListId = @id AND VersionNo = 1", listId);
        await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO product.PriceListVersions (CompanyId, PriceListId, VersionNo, VersionId, ValidFrom, State, CreatedAtUtc, CreatedBy, "
            + "PreparerActorIdsJson) SELECT CompanyId, PriceListId, 4, NEWID(), DATEADD(day, 40, ValidFrom), 1, "
            + "SYSDATETIMEOFFSET(), N'sql', N'[\"sql\"]' FROM product.PriceListVersions "
            + "WHERE PriceListId = @id AND VersionNo = 1", listId));
    }

    [SqlServerFact]
    public async Task An_inactive_card_is_refused_and_the_rollback_refuses_while_price_lists_exist()
    {
        await using var run = await Run.StartAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (var scope = run.Request())
        {
            await run.ExecuteAsync("UPDATE product.Products SET Lifecycle = 2 WHERE Id = @id", ProductId);
            var refused = await Assert.ThrowsAsync<ProductPriceListException>(() => scope.Service.CreateAsync(new CreatePriceListRequestV1(
                Guid.NewGuid(), Guid.NewGuid(), "SENTETIK pasif", "TRY", "Sales", "ExcludingVat", ProductPriceListWire.FormatDate(today), null,
                [new PriceListLineRequestV1(Guid.NewGuid(), ProductId, Kg, "1", null)]), Metadata("inactive")));
            Assert.Equal((422, "lines[0].productId", ProductPriceListCodesV1.ProductNotActive),
                (refused.Status, refused.Errors[0].Field, refused.Errors[0].Code));
            await run.ExecuteAsync("UPDATE product.Products SET Lifecycle = 1 WHERE Id = @id", ProductId);
            await scope.Service.CreateAsync(new CreatePriceListRequestV1(Guid.NewGuid(), Guid.NewGuid(), "SENTETIK geri alma", "TRY", "Sales",
                "ExcludingVat", ProductPriceListWire.FormatDate(today), null, []), Metadata("rollback-create"));
        }

        await using (var scope = run.Request())
        {
            var migrator = scope.Context.GetService<IMigrator>();
            var refusal = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync("0"));
            Assert.Equal(51063, refusal.Number);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceLists"));
    }

    private static ProductPriceListCommandMetadata Metadata(string key) =>
        new(string.Empty, ProductPriceListPermissionsV1.Write, [], $"corr-{key}", null, key, "Web");

    private sealed class User(Guid id) : ICurrentUser
    {
        public Guid Id { get; } = id;

        public bool IsAuthenticated => true;

        public string? UserId => Id.ToString("D");

        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { TestCompanyIds.CompanyA, TestCompanyIds.CompanyB };

        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    /// <summary>A granted, company-wide test authority: the central grant path is proven where the names are registered.</summary>
    private sealed class Granted : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(true, true, new HashSet<string> { "SENTETIK-PRICE" }, new HashSet<Guid>(), new HashSet<Guid>(),
                new HashSet<Guid>()));
    }

    private sealed class DenyAtBoundaryAuthority(int denyAtCall) : IErpAuthorityRevalidationService
    {
        private int calls;
        private int reachedDeniedBoundary;

        public int Calls => Volatile.Read(ref calls);

        public bool ReachedDeniedBoundary => Volatile.Read(ref reachedDeniedBoundary) == 1;

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref calls);
            if (call == denyAtCall)
            {
                Volatile.Write(ref reachedDeniedBoundary, 1);
                return Task.FromResult(PermissionScope.Denied);
            }

            return Task.FromResult(GrantedScope());
        }
    }

    private sealed class CommitBarrier(int participants)
    {
        private int arrivals;
        private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ArriveAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref arrivals) == participants) ready.TrySetResult(true);
            await ready.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class BarrierAuthority(CommitBarrier barrier) : IErpAuthorityRevalidationService
    {
        private int calls;

        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref calls) == 3) await barrier.ArriveAsync(cancellationToken);
            return GrantedScope();
        }
    }

    private sealed class PauseAuthority(int pauseAtCall) : IErpAuthorityRevalidationService
    {
        private int calls;
        private readonly TaskCompletionSource<bool> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => entered.Task;

        public void Release() => released.TrySetResult(true);

        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref calls) == pauseAtCall)
            {
                entered.TrySetResult(true);
                await released.Task.WaitAsync(cancellationToken);
            }

            return GrantedScope();
        }
    }

    private static PermissionScope GrantedScope() => new(true, true, new HashSet<string> { "SENTETIK-PRICE" }, new HashSet<Guid>(),
        new HashSet<Guid>(), new HashSet<Guid>());

    private static async Task<(PriceListResultV1? Result, ProductPriceListException? Error)> Capture(Task<PriceListResultV1> task)
    {
        try
        {
            return (await task, null);
        }
        catch (ProductPriceListException exception)
        {
            return (null, exception);
        }
    }

    private sealed class RequestScope(AsyncServiceScope scope, ProductPriceListService service, IProductPriceReferenceReaderV1 reader,
        IProductQuantityPriceReferenceReaderV1 quantityReader, ProductPriceListDbContext context, ICompanyContext companyContext) : IAsyncDisposable
    {
        public ProductPriceListService Service => service;

        public IProductPriceReferenceReaderV1 Reader => reader;

        public IProductQuantityPriceReferenceReaderV1 QuantityReader => quantityReader;

        public ProductPriceListDbContext Context => context;

        public ICompanyContext CompanyContext => companyContext;

        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }

    /// <summary>
    /// Test-only live request identity. Production <see cref="CompanyContext"/> deliberately refuses a second Set;
    /// this probe models a context provider whose value changes while an owner read is awaited, so the service's
    /// captured-scope fence is exercised without weakening the production context implementation.
    /// </summary>
    private sealed class DriftableCompanyContext(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => true;

        public Guid CompanyId { get; private set; } = companyId;

        public void Set(Guid value) => CompanyId = value;
    }

    private sealed class Run(SqlServerRelationalApiFactory factory, User user) : IAsyncDisposable
    {
        public Guid CreatorActorId => user.Id;

        public static async Task<Run> StartAsync()
        {
            var actor = Guid.NewGuid();
            var user = new User(actor);
            var baseConnection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var databaseName = "goymen_erp_ci_" + Guid.NewGuid().ToString("N");
            var connection = new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = databaseName }.ConnectionString;
            var factory = new SqlServerRelationalApiFactory(baseConnection, services =>
            {
                services.RemoveAll<ICurrentUser>();
                services.AddSingleton<ICurrentUser>(user);
                services.RemoveAll<ProductRuntimeOptions>();
                services.AddSingleton(new ProductRuntimeOptions(connection, new ProductPilotIssuerBinding(actor, Guid.NewGuid()), new byte[32]));
                services.AddProductTechnicalCardFixtureStore();
                services.AddProductSalesCandidateFixtureStore();
                services.AddProductPriceLists(new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?> { ["DB_CONNECTION_STRING"] = connection }).Build(),
                    (provider, options) => options.AddInterceptors(provider.GetRequiredService<AuditSaveChangesInterceptor>()));
            }, databaseName: databaseName);
            var run = new Run(factory, user);
            try
            {
                using var client = factory.CreateClient();
                using var health = await client.GetAsync("/health/live");
                health.EnsureSuccessStatusCode();
                await using (var setup = factory.Services.CreateAsyncScope())
                {
                    setup.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
                    await setup.ServiceProvider.GetRequiredService<IProductTechnicalCardFixtureStore>().EnsureAsync(default);
                }
                await using (var setup = factory.Services.CreateAsyncScope())
                {
                    setup.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
                    await setup.ServiceProvider.GetRequiredService<IProductSalesCandidateFixtureStore>().EnsureAsync(default);
                }
                await factory.Services.InitializeProductPriceListsAsync();
                return run;
            }
            catch
            {
                await run.DisposeAsync();
                throw;
            }
        }

        public RequestScope Request(Guid? company = null, Guid? actor = null, IErpAuthorityRevalidationService? authority = null,
            bool driftableCompanyContext = false)
        {
            var scope = factory.Services.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            var companyContext = provider.GetRequiredService<ICompanyContext>();
            companyContext.Set(company ?? TestCompanyIds.CompanyA);
            var context = provider.GetRequiredService<ProductPriceListDbContext>();
            var store = new ProductPriceListStore(context, companyContext,
                provider.GetRequiredService<GoymenERP.Modules.Audit.Application.Contracts.IAuditEventCollector>(), TimeProvider.System);
            var requestUser = actor is null ? user : new User(actor.Value);
            ICompanyContext serviceCompanyContext = driftableCompanyContext
                ? new DriftableCompanyContext(companyContext.CompanyId)
                : companyContext;
            var service = new ProductPriceListService(serviceCompanyContext, requestUser, authority ?? new Granted(), store,
                TimeProvider.System, null, _ => true);
            var reader = new ProductPriceReferenceReader(store, provider.GetRequiredService<ICompanyContext>());
            return new RequestScope(scope, service, reader, reader, context, serviceCompanyContext);
        }

        public async Task<T> ScalarAsync<T>(string sql, object? id = null)
        {
            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            if (id is not null)
            {
                command.Parameters.AddWithValue("@id", id);
            }

            return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        }

        public async Task ExecuteAsync(string sql, Guid id)
        {
            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("@id", id);
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await factory.DisposeAsync();
            var name = factory.DatabaseName;
            if (!Regex.IsMatch(name, "\\Agoymen_erp_ci_[0-9a-f]{32}\\z", RegexOptions.CultureInvariant))
            {
                throw new InvalidOperationException("Refusing cleanup outside this test database.");
            }

            var builder = new SqlConnectionStringBuilder(factory.DatabaseConnectionString) { InitialCatalog = "master" };
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END;";
            await command.ExecuteNonQueryAsync();
        }
    }
}
