using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Opening;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// INV-OPEN-1 part 2 against a REAL SQL Server: the opening-count command over the real store, the real
/// Inventory schema and Audit's real interceptor. Authority, Organization's location state and Product's stock
/// eligibility are this test's own doubles (the host adapters are the central writer's): what is proven here is
/// what the command does with each answer — nothing is written unless all of them allow it.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    private static readonly Guid OtherProduct = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a64");
    private static readonly DateOnly CountedOn = new(2026, 9, 19);

    /// <summary>
    /// ADR-011: the first request records the count; the same key again replays it with the FIRST correlation
    /// and no second effect; another payload or another actor under the key is a conflict; the same count under
    /// a new key answers the count's first recording. Book = count: each piece's balance is its counted kilograms.
    /// </summary>
    [SqlServerFact]
    public async Task The_opening_command_records_once_and_replays_with_its_first_correlation()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var request = OpeningRequest(location, ("SYN-CMD-1", Product, "1250.5"), ("SYN-CMD-2", Product, "980"));
        var authority = new Authority();
        var locations = new Locations((location, InventoryLocationStatus.Active));

        OpeningCountResultV1 first;
        await using (var a = Command(run, CompanyA, authority, locations, new Products()))
        {
            first = await a.Service.RecordAsync(request, Metadata(run, "key-1", "corr-first"), default);
        }

        Assert.False(first.IsReplay);
        Assert.Equal(("corr-first", request.CountId, 2), (first.OriginalCorrelationId, first.Count.CountId, first.Count.Lines.Count));
        Assert.Equal([1250.5m, 980m],
            first.Count.Lines.Select(line => decimal.Parse(line.QuantityKg, System.Globalization.CultureInfo.InvariantCulture)));

        await using (var a = Command(run, CompanyA, authority, locations, new Products()))
        {
            var replay = await a.Service.RecordAsync(request, Metadata(run, "key-1", "corr-again"), default);
            Assert.True(replay.IsReplay);
            Assert.Equal("corr-first", replay.OriginalCorrelationId);
            Assert.Equal(first.Count.Lines.Select(line => line.LineId), replay.Count.Lines.Select(line => line.LineId));

            var changed = request with { SheetReference = "SYN-SHEET-CHANGED" };
            Assert.Equal((409, InventoryOpeningCodesV1.IdempotencyConflict), Refusal(await Assert.ThrowsAsync<InventoryOpeningServiceException>(
                () => a.Service.RecordAsync(changed, Metadata(run, "key-1", "corr-changed"), default))));

            // The same count under a new key: nothing new; the key answers the count's first recording.
            var sameCount = await a.Service.RecordAsync(request, Metadata(run, "key-2", "corr-new-key"), default);
            Assert.True(sameCount.IsReplay);
            Assert.Equal("corr-first", sameCount.OriginalCorrelationId);

            Assert.Equal(request.CountId, (await a.Service.ReadAsync(request.CountId, default)).CountId);
            Assert.Equal((404, InventoryOpeningCodesV1.NotFound), Refusal(await Assert.ThrowsAsync<InventoryOpeningServiceException>(
                () => a.Service.ReadAsync(Guid.NewGuid(), default))));
        }

        // Another actor under the same key discloses nothing of the first answer.
        var other = Guid.NewGuid();
        await using (var b = Command(run, CompanyA, authority, locations, new Products(), actor: other))
        {
            Assert.Equal((409, InventoryOpeningCodesV1.IdempotencyConflict), Refusal(await Assert.ThrowsAsync<InventoryOpeningServiceException>(
                () => b.Service.RecordAsync(request, Metadata(run, "key-1", "corr-other") with { ActorId = other.ToString("D") }, default))));
        }

        Assert.Equal((1, 2, 2, 2), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OpeningCounts"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OpeningCountLines"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE SourceKind = N'inventory.opening-count'"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands")));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Labels WHERE ProductVersion = N'1'"));
        Assert.Equal(2230.5m, await run.ScalarAsync<decimal>(
            "SELECT SUM(b.QuantityKg) FROM inventory.Balances b JOIN inventory.OpeningCountLines l ON l.CompanyId = b.CompanyId AND l.LabelId = b.LabelId"));
        // The lot of each piece, two labels, two receipts and the count: all under the first correlation only.
        Assert.Equal(7, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = N'corr-first'"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId IN (N'corr-again', N'corr-new-key', N'corr-changed', N'corr-other')"));
        // The raw key is never stored.
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE IdempotencyKeyHash IN (N'key-1', N'key-2')"));
    }

    /// <summary>
    /// Decision A: no grant (or a grant revoked while the command reads its owners), a location that is not
    /// active, or any counted product Product does not allow to be stocked refuses the whole count before any
    /// write, with per-line reasons for the products. A location the company does not have is a terminal answer
    /// of its key (ADR-011 §7), replayed.
    /// </summary>
    [SqlServerFact]
    public async Task The_opening_command_writes_nothing_without_authority_an_active_location_and_eligible_products()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var inactive = Guid.NewGuid();
        var external = Guid.NewGuid();
        var locationOfB = await run.LocationOfAsync(CompanyB);
        var locations = new Locations((location, InventoryLocationStatus.Active), (inactive, InventoryLocationStatus.Inactive),
            (external, InventoryLocationStatus.External));
        var request = OpeningRequest(location, ("SYN-DENY-1", Product, "10"), ("SYN-DENY-2", OtherProduct, "20"));

        async Task<InventoryOpeningServiceException> RefusedAsync(OpeningCountRequestV1 candidate, Authority authority,
            Products products, string key)
        {
            await using var a = Command(run, CompanyA, authority, locations, products);
            return await Assert.ThrowsAsync<InventoryOpeningServiceException>(
                () => a.Service.RecordAsync(candidate, Metadata(run, key, $"corr-{key}"), default));
        }

        Assert.Equal((403, InventoryOpeningCodesV1.Forbidden),
            Refusal(await RefusedAsync(request, new Authority(grantedCalls: 0), new Products(), "no-grant")));
        // Revoked after the location was read: the third fresh measurement (start, key, location) says no.
        var revoked = new Authority(grantedCalls: 2);
        Assert.Equal((403, InventoryOpeningCodesV1.Forbidden),
            Refusal(await RefusedAsync(request, revoked, new Products(), "revoked")));
        Assert.Equal(1, locations.Reads[location]);

        Assert.Equal((422, InventoryOpeningCodesV1.LocationInactive),
            Refusal(await RefusedAsync(request with { LocationId = inactive }, new Authority(), new Products(), "inactive")));
        Assert.Equal((422, InventoryOpeningCodesV1.LocationExternal),
            Refusal(await RefusedAsync(request with { LocationId = external }, new Authority(), new Products(), "external")));

        var ineligible = await RefusedAsync(request, new Authority(), new Products(OtherProduct), "ineligible");
        Assert.Equal((422, InventoryOpeningCodesV1.ProductIneligible), Refusal(ineligible));
        Assert.Equal([new InventoryFieldIssueV1("lines[1].productId", "product_not_stockable")], ineligible.Issues);
        Assert.Equal((503, InventoryOpeningCodesV1.ProductEligibilityUnavailable),
            Refusal(await RefusedAsync(request, new Authority(), new Products(unavailable: true), "product-down")));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands"));

        // Another company's location is not this company's: refused, and the key keeps that answer.
        var foreign = request with { LocationId = locationOfB };
        var notFound = await RefusedAsync(foreign, new Authority(), new Products(), "foreign");
        Assert.Equal((422, InventoryOpeningCodesV1.LocationNotFound, false), (notFound.Status, notFound.Code, notFound.IsReplay));
        var replayed = await RefusedAsync(foreign, new Authority(), new Products(), "foreign");
        Assert.Equal((422, InventoryOpeningCodesV1.LocationNotFound, true, "corr-foreign"),
            (replayed.Status, replayed.Code, replayed.IsReplay, replayed.OriginalCorrelationId));

        Assert.Equal((0, 0, 0, 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OpeningCounts"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Labels WHERE Code LIKE N'SYN-DENY-%'"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE SourceKind = N'inventory.opening-count'"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands")));
    }

    [SqlServerFact]
    public async Task Customer_owned_opening_validates_the_real_account_context_and_persists_one_exact_position()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var partyId = Guid.Parse("7a000000-0000-4000-8000-000000000001");
        var accountId = Guid.Parse("7a000000-0000-4000-8000-000000000002");
        var request = OpeningRequest(location, ("SYN-CUSTOMER-OWNED", Product, "42")) with
        {
            PositionContext = new OpeningCountPositionContextV1(
                InventoryOpeningPartyKindV1.Counterparty, partyId, accountId,
                InventoryOpeningPartyKindV1.Company, CompanyA),
        };
        var locations = new Locations((location, InventoryLocationStatus.Active));

        var requestedLine = request.Lines[0];
        var lot = new MaterialLot(requestedLine.LotId, CompanyA, requestedLine.ProductId);
        var directCount = new OpeningCount(request.CountId, CompanyA, location, CountedOn, request.SheetReference,
            new StockOwner(StockPartyKind.Counterparty, partyId), new StockCustodian(StockPartyKind.Company, CompanyA),
            [new OpeningCountLine(requestedLine.LineId, lot,
                new MaterialLabel(requestedLine.LabelId, CompanyA, requestedLine.LabelCode, requestedLine.ProductId,
                    requestedLine.LotId, requestedLine.ProductVersion), Kilograms.Positive(42m))]);
        await using (var direct = run.Store(CompanyA))
        {
            var missingProof = await Assert.ThrowsAsync<InventoryStoreException>(
                () => direct.Opening.RecordAsync(directCount, Metadata(run)));
            Assert.Equal(InventoryOpeningCodesV1.OwnerAccountUnavailable, missingProof.Code);
        }

        await using (var command = Command(run, CompanyA, new Authority(), locations, new Products(),
                         ownerAccounts: new OwnerAccounts()))
        {
            var result = await command.Service.RecordAsync(request, Metadata(run, "customer-owned", "corr-customer"), default);
            Assert.False(result.IsReplay);
        }

        Assert.Equal((2, partyId, 1, CompanyA), (
            await run.ScalarAsync<int>("SELECT OwnerKind FROM inventory.MovementLines WHERE MovementId = @id",
                ("@id", request.Lines[0].LineId)),
            await run.ScalarAsync<Guid>("SELECT OwnerId FROM inventory.MovementLines WHERE MovementId = @id",
                ("@id", request.Lines[0].LineId)),
            await run.ScalarAsync<int>("SELECT CustodianKind FROM inventory.MovementLines WHERE MovementId = @id",
                ("@id", request.Lines[0].LineId)),
            await run.ScalarAsync<Guid>("SELECT CustodianId FROM inventory.MovementLines WHERE MovementId = @id",
                ("@id", request.Lines[0].LineId))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Balances WHERE LabelId = @label AND OwnerKind = 2 AND OwnerId = @party AND CustodianKind = 1 AND CustodianId = @company",
            ("@label", request.Lines[0].LabelId), ("@party", partyId), ("@company", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = N'corr-customer' AND AfterJson LIKE @account",
            ("@account", $"%{accountId:D}%")));

        await using var read = run.Store(CompanyA);
        var stored = Assert.IsType<OpeningCount>(await read.Opening.ReadCountAsync(request.CountId));
        Assert.Equal((StockPartyKind.Counterparty, partyId), (stored.Owner.Kind, stored.Owner.Id));
        Assert.Equal((StockPartyKind.Company, CompanyA), (stored.Custodian.Kind, stored.Custodian.Id));
    }

    [SqlServerFact]
    public async Task Customer_owned_opening_fails_closed_for_ineligible_unavailable_and_revoked_account_reads()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var request = OpeningRequest(location, ("SYN-CUSTOMER-REFUSED", Product, "5")) with
        {
            PositionContext = new OpeningCountPositionContextV1(
                InventoryOpeningPartyKindV1.Counterparty, Guid.NewGuid(), Guid.NewGuid(),
                InventoryOpeningPartyKindV1.Company, CompanyA),
        };
        var locations = new Locations((location, InventoryLocationStatus.Active));

        async Task<InventoryOpeningServiceException> RefusedAsync(Authority authority, OwnerAccounts accounts, string key)
        {
            await using var command = Command(run, CompanyA, authority, locations, new Products(), ownerAccounts: accounts);
            return await Assert.ThrowsAsync<InventoryOpeningServiceException>(
                () => command.Service.RecordAsync(request with { CountId = Guid.NewGuid() }, Metadata(run, key, $"corr-{key}"), default));
        }

        Assert.Equal((422, InventoryOpeningCodesV1.OwnerAccountIneligible),
            Refusal(await RefusedAsync(new Authority(), new OwnerAccounts(active: false), "inactive-owner")));
        Assert.Equal((422, InventoryOpeningCodesV1.OwnerAccountIneligible),
            Refusal(await RefusedAsync(new Authority(), new OwnerAccounts(customer: false), "non-customer")));
        Assert.Equal((503, InventoryOpeningCodesV1.OwnerAccountUnavailable),
            Refusal(await RefusedAsync(new Authority(),
                new OwnerAccounts(InventoryOpeningOwnerAccountOutcomeV1.Unavailable), "owner-down")));
        Assert.Equal((403, InventoryOpeningCodesV1.Forbidden),
            Refusal(await RefusedAsync(new Authority(grantedCalls: 2), new OwnerAccounts(), "owner-revoked")));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OpeningCounts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Labels WHERE Code = N'SYN-CUSTOMER-REFUSED'"));
    }

    /// <summary>
    /// Opening is never an adjustment: a counted piece whose label already carries stock refuses the count; the
    /// refusal is the key's answer for good (§7) and replays with its first correlation; no stock moves.
    /// </summary>
    [SqlServerFact]
    public async Task A_stocked_label_refuses_the_count_terminally_and_the_refusal_replays()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var stocked = NewLabel(CompanyA, "SYN-CMD-STOCKED");
        await using (var a = run.Store(CompanyA))
        {
            await a.Labels.SaveLabelAsync(stocked, Metadata(run));
            await a.Movements.RecordMovementAsync(
                Movement(CompanyA, StockMovementKind.Receipt, In(Position(CompanyA, location, stocked, BlockState.Free), 10m)), Metadata(run));
        }

        var fresh = OpeningRequest(location, ("SYN-CMD-FRESH", Product, "5"));
        var request = fresh with
        {
            Lines = [.. fresh.Lines, new OpeningCountLineRequestV1(Guid.NewGuid(), stocked.Id, stocked.Code, LotA, null, null,
                Product, "1", "5")],
        };
        var locations = new Locations((location, InventoryLocationStatus.Active));
        foreach (var (correlation, isReplay) in new[] { ("corr-stocked", false), ("corr-stocked-again", true) })
        {
            await using var a = Command(run, CompanyA, new Authority(), locations, new Products());
            var error = await Assert.ThrowsAsync<InventoryOpeningServiceException>(
                () => a.Service.RecordAsync(request, Metadata(run, "key-stocked", correlation), default));
            Assert.Equal((409, InventoryRuleCodes.LabelAlreadyInStock, isReplay, "corr-stocked"),
                (error.Status, error.Code, error.IsReplay, error.OriginalCorrelationId));
        }

        Assert.Equal((0, 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OpeningCounts"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands")));
        Assert.Equal(10m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @id", ("@id", stocked.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Labels WHERE Code = N'SYN-CMD-FRESH'"));
    }

    /// <summary>
    /// ADR-011 §8: two requests with one key and one count both pass the key lookup and meet at the save. One
    /// commits; the other stores nothing and answers the winner's record — a replay with the winner's
    /// correlation, never a 409 or 422.
    /// </summary>
    [SqlServerFact]
    public async Task Two_requests_with_one_key_racing_to_the_save_leave_one_count_and_both_answer_it()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var request = OpeningRequest(location, ("SYN-CMD-RACE-1", Product, "7"), ("SYN-CMD-RACE-2", Product, "8"));
        var locations = new Locations((location, InventoryLocationStatus.Active));
        var barrier = new Barrier(2);
        async Task<object> SendAsync(string correlation)
        {
            await using var writer = Command(run, CompanyA, new Authority(), locations, new Products(), interceptors: new BarrierInterceptor(barrier));
            try
            {
                return await writer.Service.RecordAsync(request, Metadata(run, "key-race", correlation), default);
            }
            catch (InventoryOpeningServiceException exception)
            {
                return exception.Code;
            }
        }

        var results = await Task.WhenAll(Task.Run(() => SendAsync("corr-race-1")), Task.Run(() => SendAsync("corr-race-2")));

        var answers = results.Select(result => Assert.IsType<OpeningCountResultV1>(result)).ToArray();
        var winner = Assert.Single(answers, answer => !answer.IsReplay);
        var loser = Assert.Single(answers, answer => answer.IsReplay);
        Assert.Equal(winner.OriginalCorrelationId, loser.OriginalCorrelationId);
        Assert.Equal(winner.Count.Lines.Select(line => line.LabelId), loser.Count.Lines.Select(line => line.LabelId));
        Assert.Equal((1, 1, 2), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OpeningCounts"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE SourceKind = N'inventory.opening-count'")));
    }

    /// <summary>
    /// SİPARİŞ #337 P3 (#333 mapping): a count id company B holds collides for company A at the primary key,
    /// invisible to A. A gets <c>inventory_opening_conflict</c> with no retry advice, nothing of A is stored and
    /// nothing of B is disclosed. (B has no approved kg profile in this database, so B's count is a raw row.)
    /// </summary>
    [SqlServerFact]
    public async Task A_count_id_another_company_holds_is_an_opening_conflict_that_stores_nothing()
    {
        await using var run = await Run.StartAsync();
        var locationA = await run.LocationOfAsync(CompanyA);
        var locationB = await run.LocationOfAsync(CompanyB);
        var id = Guid.NewGuid();
        await using (var raw = run.Context(CompanyB))
        {
            raw.OpeningCounts.Add(new OpeningCountRow
            {
                Id = id, CompanyId = CompanyB, LocationId = locationB, CountedOn = CountedOn, SheetReference = "SYN-SHEET-OF-B",
                ContentHash = new string('B', 64), RecordedAtUtc = At, RecordedBy = "synthetic-actor", CorrelationId = run.Correlation,
            });
            await raw.SaveChangesAsync();
        }

        var lot = new MaterialLot(Guid.NewGuid(), CompanyA, Product);
        var countOfA = new OpeningCount(id, CompanyA, locationA, CountedOn, "SYN-SHEET-OF-A",
            [new(Guid.NewGuid(), lot, new MaterialLabel(Guid.NewGuid(), CompanyA, "SYN-SHARED-A", Product, lot.Id, "1"), Kilograms.Positive(3m))]);
        await using (var a = run.Store(CompanyA))
        {
            var error = await Assert.ThrowsAsync<InventoryStoreException>(() => a.Opening.RecordAsync(countOfA, Metadata(run)));
            Assert.Equal(InventoryStoreCodes.OpeningConflict, error.Code);
            Assert.Contains("aynı cevabı verir", error.Message, StringComparison.Ordinal);
            Assert.Null(await a.Opening.ReadCountAsync(id));
        }

        Assert.Equal((1, 0, 0, 0), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OpeningCounts WHERE Id = @id AND CompanyId = @b",
                ("@id", id), ("@b", CompanyB)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Labels WHERE Code = N'SYN-SHARED-A'"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Lots WHERE Id = @lot", ("@lot", lot.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE SourceKind = N'inventory.opening-count'")));
    }

    /// <summary>
    /// Forward fix (SİPARİŞ #337 P3, as #329/#334): a rollback never drops a recorded command answer nor an
    /// opening count and the stock it posted. Each Down refuses with 51046 while its rows exist and the
    /// migration history keeps the migration.
    /// </summary>
    [SqlServerFact]
    public async Task A_rollback_refuses_while_commands_or_opening_counts_exist()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var lot = new MaterialLot(Guid.NewGuid(), CompanyA, Product);
        await using (var a = run.Store(CompanyA))
        {
            await a.Opening.RecordAsync(new OpeningCount(Guid.NewGuid(), CompanyA, location, CountedOn, "SYN-SHEET-ROLLBACK",
                [new(Guid.NewGuid(), lot, new MaterialLabel(Guid.NewGuid(), CompanyA, "SYN-ROLLBACK", Product, lot.Id, "1"), Kilograms.Positive(4m))]),
                Metadata(run));
        }

        await using (var raw = run.Context(CompanyA))
        {
            raw.Commands.Add(new InventoryCommandRow
            {
                CompanyId = CompanyA, CommandName = InventoryOpeningCommandService.RecordCommand, IdempotencyKeyHash = new string('A', 64),
                ActorId = run.Actor.ToString("D"), PayloadHash = new string('0', 64), ResultJson = "{}", CorrelationId = run.Correlation,
                RecordedAtUtc = At,
            });
            await raw.SaveChangesAsync();
        }

        await RemoveSyntheticProductVersionsForHistoricalMigrationAsync(
            run, "SYN-ROLLBACK", expectedLabels: 1);

        await using (var context = run.Context(CompanyA))
        {
            var error = await Assert.ThrowsAsync<SqlException>(
                () => context.GetService<IMigrator>().MigrateAsync("20260919120000_AddOpeningCounts"));
            Assert.Equal(51046, error.Number);
            Assert.Equal("20260919140000_AddInventoryCommands", (await context.Database.GetAppliedMigrationsAsync()).Last());
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands"));
        Assert.Equal(1, await run.ScalarAsync<int>("DELETE FROM inventory.Commands; SELECT @@ROWCOUNT;"));

        await using (var context = run.Context(CompanyA))
        {
            var error = await Assert.ThrowsAsync<SqlException>(
                () => context.GetService<IMigrator>().MigrateAsync("20260918190000_AddMaterialLots"));
            Assert.Equal(51046, error.Number);
            Assert.Contains("Opening counts", error.Message, StringComparison.Ordinal);
            Assert.Contains("20260919120000_AddOpeningCounts", await context.Database.GetAppliedMigrationsAsync());
        }

        Assert.Equal((1, 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OpeningCounts"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE SourceKind = N'inventory.opening-count'")));
    }

    private static (int, string) Refusal(InventoryOpeningServiceException exception) => (exception.Status, exception.Code);

    private static InventoryCommandMetadata Metadata(Run run, string key, string correlation) =>
        Metadata(run) with { IdempotencyKey = key, CorrelationId = correlation, CausationId = null };

    /// <summary>A count of new pieces, each on its own new lot, of the given product at card version "1".</summary>
    private static OpeningCountRequestV1 OpeningRequest(Guid location, params (string Code, Guid Product, string Kilograms)[] pieces) =>
        new(Guid.NewGuid(), location, CountedOn, "SYN-SHEET-CMD",
            [.. pieces.Select(piece => new OpeningCountLineRequestV1(Guid.NewGuid(), Guid.NewGuid(), piece.Code, Guid.NewGuid(),
                null, null, piece.Product, "1", piece.Kilograms))]);

    private static CommandScope Command(Run run, Guid company, Authority authority, Locations locations, Products products,
        Guid? actor = null, OwnerAccounts? ownerAccounts = null, params IInterceptor[] interceptors)
    {
        var store = run.Store(company, interceptors);
        var companyContext = new CompanyContext();
        companyContext.Set(company);
        var service = new InventoryOpeningCommandService(store.DbContext, store.Store, locations, products, companyContext,
            new FixedCurrentUser(actor ?? run.Actor, company), authority, TimeProvider.System,
            ownerAccounts ?? new OwnerAccounts());
        return new CommandScope(store, service);
    }

    private sealed class CommandScope(StoreScope store, InventoryOpeningCommandService service) : IAsyncDisposable
    {
        public InventoryOpeningCommandService Service => service;

        public ValueTask DisposeAsync() => store.DisposeAsync();
    }

    /// <summary>Fresh authority as the test says: granted for the first <c>grantedCalls</c> measurements, then revoked.</summary>
    private sealed class Authority(int grantedCalls = int.MaxValue) : IErpAuthorityRevalidationService
    {
        private int calls;

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(InventoryPermissionManifestV1.OpeningStockRecord, permission);
            return Task.FromResult(Interlocked.Increment(ref calls) <= grantedCalls
                ? new PermissionScope(true, true, new HashSet<string>(StringComparer.Ordinal) { "SYNTHETIC-OPENING" },
                    new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
        }
    }

    /// <summary>Organization's location state as the test says; any other location is not the company's.</summary>
    private sealed class Locations(params (Guid Id, InventoryLocationStatus Status)[] known) : IInventoryLocationStatusPort
    {
        public Dictionary<Guid, int> Reads { get; } = [];

        public Task<InventoryLocationStatus> ReadAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken)
        {
            lock (Reads)
            {
                Reads[locationId] = Reads.GetValueOrDefault(locationId) + 1;
            }

            return Task.FromResult(known.Where(entry => entry.Id == locationId).Select(entry => entry.Status)
                .DefaultIfEmpty(InventoryLocationStatus.NotFound).First());
        }
    }

    /// <summary>Product's stock answer as the test says: the named products are not stockable; or Product is down.</summary>
    private sealed class Products(params Guid[] refused) : IInventoryProductStockPort
    {
        private readonly bool unavailable;

        public Products(bool unavailable) : this()
        {
            this.unavailable = unavailable;
        }

        public Task<InventoryProductStockAnswer> EvaluateAsync(Guid productId, string productVersion, DateOnly effectiveOn,
            CancellationToken cancellationToken)
        {
            if (unavailable)
            {
                throw new InvalidOperationException("synthetic: Product is unavailable");
            }

            Assert.Equal(("1", CountedOn), (productVersion, effectiveOn));
            return Task.FromResult(refused.Contains(productId)
                ? new InventoryProductStockAnswer(false, [new InventoryFieldIssueV1("productId", "product_not_stockable")])
                : new InventoryProductStockAnswer(true, []));
        }
    }

    private sealed class OwnerAccounts(
        InventoryOpeningOwnerAccountOutcomeV1 outcome = InventoryOpeningOwnerAccountOutcomeV1.Found,
        bool active = true,
        bool customer = true) : IInventoryOpeningOwnerAccountPortV1
    {
        public Task<InventoryOpeningOwnerAccountAnswerV1> GetAuthorizedAsync(Guid partyId, Guid companyPartyAccountId,
            CancellationToken cancellationToken) => Task.FromResult(new InventoryOpeningOwnerAccountAnswerV1(outcome,
                outcome == InventoryOpeningOwnerAccountOutcomeV1.Found
                    ? new InventoryOpeningOwnerAccountV1(partyId, companyPartyAccountId, CompanyA, active, active, customer, "3", "4")
                    : null));
    }
}
