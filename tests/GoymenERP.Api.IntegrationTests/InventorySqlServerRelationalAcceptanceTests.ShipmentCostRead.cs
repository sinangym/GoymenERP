using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Real Inventory receipt/average/stamp, allocation, definite-shipment command/outbox and reader SQL.
/// Receipt prices and demand references are explicitly synthetic fixture input, not Accounting authority.
/// Deliberate legacy/corruption injections below are isolated test SQL only. Runtime is NOT_RUN.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Shipment_cost_read_uses_the_immutable_actual_exit_stamp_after_later_receipts_change_the_average()
    {
        await using var run = await Run.StartAsync();
        var shipment = await SeedShipmentCostReadAsync(run);
        await using var reads = run.Context(CompanyA);
        var reader = ShipmentCostReader(reads, CompanyA);
        var before = await reader.GetAsync(CompanyA, shipment.Request.ShipmentId, shipment.Request.MovementId);
        var resolved = await reader.GetByShipmentAsync(CompanyA, shipment.Request.ShipmentId);
        Assert.Equal(InventoryShipmentCostReadOutcomeV1.Available, before.Outcome);
        Assert.Equal((before.Outcome, before.CompanyId, before.ShipmentId, before.MovementId, before.SourceVersion),
            (resolved.Outcome, resolved.CompanyId, resolved.ShipmentId, resolved.MovementId, resolved.SourceVersion));
        var line = Assert.Single(before.Lines);
        Assert.Equal(line, Assert.Single(resolved.Lines));
        Assert.Equal((shipment.Request.LoadingInstructionId, shipment.Request.LoadingInstructionLineId, shipment.Request.AllocationId,
            shipment.Request.LabelId, Product, "product-v1", 10m),
            (line.LoadingInstructionId, line.LoadingInstructionLineId, line.AllocationId, line.LabelId,
                line.ProductId, line.ProductVersion, line.QuantityKg));
        Assert.Equal((InventoryMovementCostStampStatusV1.Known, SettingsCurrencyIdentitiesV1.TurkishLira, 15m, 150m),
            (line.Tl.Status, line.Tl.CurrencyId, line.Tl.UnitCostPerKg, line.Tl.Amount));
        Assert.True(line.Tl.UnitCostRatio!.Value().TryToDecimal(out var exactUnit));
        Assert.Equal(15m, exactUnit);
        var movementsBefore = await reads.Movements.CountAsync();
        var eventsBefore = await reads.OutboxMessages.CountAsync();
        await CostedLabelAsync(run, shipment.LocationId, 100m, 50m, 10m); // Real later source, not direct average tampering.
        var actualAverage = await reads.AverageCostLedger.AsNoTracking().Where(row => row.LocationId == shipment.LocationId)
            .Select(row => row.AverageCostPerKg).SingleAsync();
        Assert.NotEqual(15m, actualAverage);
        var after = await reader.GetAsync(CompanyA, shipment.Request.ShipmentId, shipment.Request.MovementId);
        var resolvedAfter = await reader.GetByShipmentAsync(CompanyA, shipment.Request.ShipmentId);
        Assert.Equal(before.SourceVersion, after.SourceVersion);
        Assert.Equal(before.SourceVersion, resolvedAfter.SourceVersion);
        Assert.Equal(line, Assert.Single(after.Lines));
        Assert.Equal(line, Assert.Single(resolvedAfter.Lines));
        Assert.Equal(movementsBefore + 1, await reads.Movements.CountAsync());
        Assert.Equal(eventsBefore, await reads.OutboxMessages.CountAsync()); // Reading creates no proposal/event/terminal.
        Assert.Empty(reads.ChangeTracker.Entries());
    }

    [SqlServerFact]
    public async Task A_missing_legacy_TL_stamp_stays_unavailable_even_when_the_real_USD_stamp_and_current_TL_average_exist()
    {
        await using var run = await Run.StartAsync();
        var shipment = await SeedShipmentCostReadAsync(run);
        await using var reads = run.Context(CompanyA);
        Assert.True(await reads.MovementCostStamps.AnyAsync(row => row.MovementId == shipment.Request.MovementId
            && row.CurrencyId == ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD")
            && row.Status == (int)InventoryMovementCostStampStatusV1.Known));
        // Explicit legacy-data fault; no runtime writer is allowed to delete an immutable stamp.
        await run.ExecuteAsync("DELETE FROM inventory.MovementCostStamps WHERE MovementId = @m AND CurrencyId = @c",
            ("@m", shipment.Request.MovementId), ("@c", SettingsCurrencyIdentitiesV1.TurkishLira));
        var answer = await ShipmentCostReader(reads, CompanyA).GetAsync(CompanyA,
            shipment.Request.ShipmentId, shipment.Request.MovementId);
        Assert.Equal(InventoryShipmentCostReadOutcomeV1.Available, answer.Outcome); // Physical provenance is still factual.
        var tl = Assert.Single(answer.Lines).Tl;
        Assert.Equal(InventoryMovementCostStampStatusV1.Unavailable, tl.Status);
        Assert.Equal("movement_cost_stamp_not_recorded", tl.Reason);
        Assert.Null(tl.Amount); Assert.Null(tl.UnitCostPerKg); Assert.Null(tl.UnitCostRatio);
        Assert.Equal(SettingsCurrencyIdentitiesV1.TurkishLira, tl.CurrencyId);
        var resolved = await ShipmentCostReader(reads, CompanyA).GetByShipmentAsync(CompanyA, shipment.Request.ShipmentId);
        Assert.Equal(answer.SourceVersion, resolved.SourceVersion);
        Assert.Equal(tl, Assert.Single(resolved.Lines).Tl);
    }

    [SqlServerFact]
    public async Task Shipment_cost_read_does_not_cross_company_or_movement_shipment_identity_boundaries()
    {
        await using var run = await Run.StartAsync();
        var shipment = await SeedShipmentCostReadAsync(run);
        await using var reads = run.Context(CompanyA);
        var reader = ShipmentCostReader(reads, CompanyA);
        var otherShipment = await reader.GetAsync(CompanyA, Guid.NewGuid(), shipment.Request.MovementId);
        var otherMovement = await reader.GetAsync(CompanyA, shipment.Request.ShipmentId, Guid.NewGuid());
        Assert.Equal(InventoryShipmentCostReadOutcomeV1.NotFound, otherShipment.Outcome);
        Assert.Equal(InventoryShipmentCostReadOutcomeV1.NotFound, otherMovement.Outcome);
        Assert.Empty(otherShipment.Lines); Assert.Empty(otherMovement.Lines);
        await using var other = run.Context(CompanyB);
        var foreign = await ShipmentCostReader(other, CompanyB).GetAsync(CompanyB,
            shipment.Request.ShipmentId, shipment.Request.MovementId);
        Assert.Equal(InventoryShipmentCostReadOutcomeV1.NotFound, foreign.Outcome);
        Assert.Empty(foreign.Lines);
        var foreignResolved = await ShipmentCostReader(other, CompanyB).GetByShipmentAsync(CompanyB, shipment.Request.ShipmentId);
        var unknownResolved = await ShipmentCostReader(other, CompanyB).GetByShipmentAsync(CompanyB, Guid.NewGuid());
        Assert.Equal(InventoryShipmentCostReadOutcomeV1.NotFound, foreignResolved.Outcome);
        Assert.Equal((unknownResolved.Outcome, unknownResolved.MovementId, unknownResolved.SourceVersion, unknownResolved.Reason),
            (foreignResolved.Outcome, foreignResolved.MovementId, foreignResolved.SourceVersion, foreignResolved.Reason));
        Assert.Empty(foreignResolved.Lines);
    }

    [SqlServerFact]
    public async Task Unknown_or_mismatched_recorded_shipment_outbox_provenance_is_unreadable_instead_of_empty_success()
    {
        await using var run = await Run.StartAsync();
        var shipment = await SeedShipmentCostReadAsync(run);
        await using var reads = run.Context(CompanyA);
        var reader = ShipmentCostReader(reads, CompanyA);
        var eventId = shipment.Result.OutboxMessageId;
        await run.ExecuteAsync("UPDATE inventory.OutboxMessages SET PayloadJson = N'{}' WHERE Id = @id", ("@id", eventId));
        var missingShape = await reader.GetAsync(CompanyA, shipment.Request.ShipmentId, shipment.Request.MovementId);
        Assert.Equal(InventoryShipmentCostReadOutcomeV1.Unreadable, missingShape.Outcome);
        Assert.Empty(missingShape.Lines);
        Assert.Equal(InventoryShipmentCostReadOutcomeV1.Unreadable,
            (await reader.GetByShipmentAsync(CompanyA, shipment.Request.ShipmentId)).Outcome);
        await run.ExecuteAsync("UPDATE inventory.OutboxMessages SET PayloadJson = N'[]' WHERE Id = @id", ("@id", eventId));
        var wrongJsonType = await reader.GetAsync(CompanyA, shipment.Request.ShipmentId, shipment.Request.MovementId);
        Assert.Equal(InventoryShipmentCostReadOutcomeV1.Unreadable, wrongJsonType.Outcome);
        Assert.Empty(wrongJsonType.Lines);
        var original = shipment.Envelope with { LoadingInstructionLineId = Guid.NewGuid() };
        await run.ExecuteAsync("UPDATE inventory.OutboxMessages SET PayloadJson = @body WHERE Id = @id",
            ("@body", JsonSerializer.Serialize(original, new JsonSerializerOptions(JsonSerializerDefaults.Web))), ("@id", eventId));
        var mismatch = await reader.GetAsync(CompanyA, shipment.Request.ShipmentId, shipment.Request.MovementId);
        Assert.Equal(InventoryShipmentCostReadOutcomeV1.Unreadable, mismatch.Outcome);
        Assert.Empty(mismatch.Lines);
        Assert.Equal("shipment_cost_provenance_unreadable", mismatch.Reason);
        await run.ExecuteAsync("UPDATE inventory.OutboxMessages SET PayloadJson = @body, SchemaVersion = 2 WHERE Id = @id",
            ("@body", JsonSerializer.Serialize(shipment.Envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web))), ("@id", eventId));
        var unknownVersion = await reader.GetAsync(CompanyA, shipment.Request.ShipmentId, shipment.Request.MovementId);
        Assert.Equal(InventoryShipmentCostReadOutcomeV1.Unreadable, unknownVersion.Outcome);
        Assert.Empty(unknownVersion.Lines);
        Assert.Equal(InventoryShipmentCostReadOutcomeV1.Unreadable,
            (await reader.GetByShipmentAsync(CompanyA, shipment.Request.ShipmentId)).Outcome);
    }

    [SqlServerFact]
    public async Task Shipment_only_cost_resolution_refuses_multiple_original_commands_or_multiple_physical_source_movements()
    {
        await using var run = await Run.StartAsync();
        var shipment = await SeedShipmentCostReadAsync(run);
        // Deliberate database corruption: two otherwise real command results claim the same shipment under different keys.
        await run.ExecuteAsync("""
            INSERT INTO inventory.Commands(CompanyId,CommandName,IdempotencyKeyHash,ActorId,PayloadHash,TargetId,ResultJson,CorrelationId,RecordedAtUtc)
            SELECT CompanyId,CommandName,@key,ActorId,PayloadHash,TargetId,ResultJson,CorrelationId,RecordedAtUtc
            FROM inventory.Commands WHERE CompanyId=@company AND TargetId=@shipment AND CommandName=@operation
            """, ("@key", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")), ("@company", CompanyA),
            ("@shipment", shipment.Request.ShipmentId), ("@operation", InventoryDefiniteShipmentOutboxV1.SourceOperation));
        await using var reads = run.Context(CompanyA);
        var duplicateCommand = await ShipmentCostReader(reads, CompanyA).GetByShipmentAsync(CompanyA, shipment.Request.ShipmentId);
        Assert.Equal(InventoryShipmentCostReadOutcomeV1.Unreadable, duplicateCommand.Outcome);
        Assert.Equal(Guid.Empty, duplicateCommand.MovementId); Assert.Empty(duplicateCommand.Lines);

        var first = await SeedShipmentCostReadAsync(run);
        var second = await SeedShipmentCostReadAsync(run);
        // A different real physical line is incorrectly linked to the first source. Choosing first/last is forbidden.
        await run.ExecuteAsync("UPDATE inventory.Movements SET SourceId=@shipment WHERE CompanyId=@company AND Id=@movement",
            ("@shipment", first.Request.ShipmentId), ("@company", CompanyA), ("@movement", second.Request.MovementId));
        var duplicateMovement = await ShipmentCostReader(reads, CompanyA).GetByShipmentAsync(CompanyA, first.Request.ShipmentId);
        Assert.Equal(InventoryShipmentCostReadOutcomeV1.Unreadable, duplicateMovement.Outcome);
        Assert.Equal(Guid.Empty, duplicateMovement.MovementId); Assert.Empty(duplicateMovement.Lines);
    }

    [SqlServerFact]
    public async Task Shipment_only_cost_resolution_refuses_missing_or_tampered_immutable_command_linkage()
    {
        await using var run = await Run.StartAsync();
        var missing = await SeedShipmentCostReadAsync(run);
        await run.ExecuteAsync("DELETE FROM inventory.Commands WHERE CompanyId=@company AND TargetId=@shipment AND CommandName=@operation",
            ("@company", CompanyA), ("@shipment", missing.Request.ShipmentId), ("@operation", InventoryDefiniteShipmentOutboxV1.SourceOperation));
        await using var reads = run.Context(CompanyA);
        var missingCommand = await ShipmentCostReader(reads, CompanyA).GetByShipmentAsync(CompanyA, missing.Request.ShipmentId);
        Assert.Equal(InventoryShipmentCostReadOutcomeV1.Unreadable, missingCommand.Outcome);
        Assert.Equal(Guid.Empty, missingCommand.MovementId); Assert.Empty(missingCommand.Lines);

        var tampered = await SeedShipmentCostReadAsync(run);
        await run.ExecuteAsync("UPDATE inventory.Commands SET ResultJson=JSON_MODIFY(ResultJson,'$.movementId',@wrong) WHERE CompanyId=@company AND TargetId=@shipment AND CommandName=@operation",
            ("@wrong", Guid.NewGuid().ToString("D")), ("@company", CompanyA), ("@shipment", tampered.Request.ShipmentId),
            ("@operation", InventoryDefiniteShipmentOutboxV1.SourceOperation));
        var changedCommand = await ShipmentCostReader(reads, CompanyA).GetByShipmentAsync(CompanyA, tampered.Request.ShipmentId);
        Assert.Equal(InventoryShipmentCostReadOutcomeV1.Unreadable, changedCommand.Outcome);
        Assert.Equal(Guid.Empty, changedCommand.MovementId); Assert.Empty(changedCommand.Lines);
    }

    [SqlServerFact]
    public async Task Shipment_only_cost_resolution_discards_the_actual_SQL_result_if_company_changes_during_resolution()
    {
        await using var run = await Run.StartAsync();
        var shipment = await SeedShipmentCostReadAsync(run);
        var company = new ShipmentCostMutableCompany(); company.Set(CompanyA);
        var fault = new ShipmentCostCompanySwitch(() => company.Set(CompanyB));
        await using var reads = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString).AddInterceptors(fault).Options, company);
        var answer = await new InventoryShipmentCostReadPort(reads, company).GetByShipmentAsync(CompanyA, shipment.Request.ShipmentId);
        Assert.True(fault.Fired);
        Assert.Equal(InventoryShipmentCostReadOutcomeV1.Unreadable, answer.Outcome);
        Assert.Equal(Guid.Empty, answer.MovementId); Assert.Empty(answer.Lines);
    }

    [SqlServerFact]
    public async Task Shipment_cost_read_discards_the_real_SQL_result_when_the_company_boundary_changes_during_the_read()
    {
        await using var run = await Run.StartAsync();
        var shipment = await SeedShipmentCostReadAsync(run);
        // Explicit synthetic authority-context fault AFTER the real SQL command returns its reader.
        var company = new ShipmentCostMutableCompany(); company.Set(CompanyA);
        var fault = new ShipmentCostCompanySwitch(() => company.Set(CompanyB));
        await using var reads = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString).AddInterceptors(fault).Options, company);
        var answer = await new InventoryShipmentCostReadPort(reads, company).GetAsync(CompanyA,
            shipment.Request.ShipmentId, shipment.Request.MovementId);
        Assert.True(fault.Fired);
        Assert.Equal(InventoryShipmentCostReadOutcomeV1.Unreadable, answer.Outcome);
        Assert.Empty(answer.Lines);
    }

    private sealed class ShipmentCostMutableCompany : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; }
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class ShipmentCostCompanySwitch(Action change) : DbCommandInterceptor
    {
        public bool Fired { get; private set; }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!Fired && command.CommandText.Contains("[inventory].[Movements]", StringComparison.Ordinal))
            { Fired = true; change(); }
            return ValueTask.FromResult(result);
        }
    }

    private static InventoryShipmentCostReadPort ShipmentCostReader(InventoryDbContext reads, Guid companyId)
    {
        var company = new CompanyContext(); company.Set(companyId);
        return new(reads, company);
    }

    private sealed record ShipmentCostFixture(InventoryDefiniteShipmentRequestV1 Request,
        InventoryDefiniteShipmentResultV1 Result, InventoryDefiniteShipmentRecordedV1 Envelope, Guid LocationId);

    private static async Task<ShipmentCostFixture> SeedShipmentCostReadAsync(Run run)
    {
        var location = await run.LocationOfAsync(CompanyA);
        var label = await CostedLabelAsync(run, location, 100m, 15m, 2m);
        var allocationId = Guid.NewGuid();
        await using (var owner = run.Store(CompanyA))
        {
            var version = await owner.DbContext.Labels.Where(row => row.Id == label.Id).Select(row => row.Version).SingleAsync();
            await owner.Store.CreateAsync(new(allocationId, Guid.NewGuid(), Guid.NewGuid(), Product, "product-v1", label.Id,
                version.ToString(CultureInfo.InvariantCulture), "10", "Sentetik onaylı satış talebi referansı.", null), Metadata(run));
        }
        long labelVersion;
        await using (var context = run.Context(CompanyA))
            labelVersion = await context.Labels.AsNoTracking().Where(row => row.Id == label.Id).Select(row => row.Version).SingleAsync();
        var request = new InventoryDefiniteShipmentRequestV1(CompanyA, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), allocationId,
            "1", label.Id, labelVersion.ToString(CultureInfo.InvariantCulture), Guid.NewGuid(), "10", At.AddHours(2));
        InventoryDefiniteShipmentResultV1 result;
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using var participant = await InventoryDefiniteShipmentParticipant.EnlistAsync(connection, transaction,
                CompanyA, Settings(run, CompanyA));
            result = await participant.StageAsync(request, Metadata(run));
            await transaction.CommitAsync();
        }
        await using var reads = run.Context(CompanyA);
        var body = await reads.OutboxMessages.Where(row => row.Id == result.OutboxMessageId).Select(row => row.PayloadJson).SingleAsync();
        var envelope = JsonSerializer.Deserialize<InventoryDefiniteShipmentRecordedV1>(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return new(request, result, envelope, location);
    }
}
