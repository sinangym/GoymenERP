using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed class LogisticsTransferDeparturePersistenceModelTests
{
    [Fact]
    public void Departure_migration_is_generated_DDL_without_seed_and_refuses_down_before_any_immutable_history_drop()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        using var database = Context(company);
        Assert.False(database.Database.HasPendingModelChanges());
        Assert.EndsWith("_AddTransferDepartures", database.Database.GetMigrations().Last(), StringComparison.Ordinal);
        var migration = new AddTransferDepartures();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation or InsertDataOperation or UpdateDataOperation or DeleteDataOperation);
        Assert.Equal(new[] { "TransferDepartureAttempts", "TransferDepartureEvents", "TransferDepartureStates" },
            migration.UpOperations.OfType<CreateTableOperation>().Select(x => x.Name).Order(StringComparer.Ordinal));
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51991", guard.Sql, StringComparison.Ordinal); Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        foreach (var table in new[] { "TransferDepartureStates", "TransferDepartureEvents", "TransferDepartureAttempts" })
            Assert.Contains("[logistics].[" + table + "]", guard.Sql, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("AssociationRecorded", false)]
    [InlineData("ExitWeighing", false)]
    [InlineData("VehicleExit", true)]
    public void Departure_fact_update_refuses_the_wrong_event_kind_two_facts_or_wrong_time_before_database_access(string kind, bool wrongTime)
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        using var database = Context(company);
        var at = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var row = new TransferDepartureStateRow { CompanyId = company.CompanyId, TransferId = Guid.NewGuid(), DocumentId = Guid.NewGuid(),
            SourceLocationId = Guid.NewGuid(), Association = 1, Version = 1, CreatedBy = "SENTETIK", CreatedAtUtc = at };
        database.Attach(row);
        row.Version = 2; row.VehicleExitAtUtc = at.AddHours(1);
        if (kind == "ExitWeighing") row.ExitWeighingAtUtc = at.AddHours(1);
        database.Add(new TransferDepartureEventRow { CompanyId = company.CompanyId, TransferId = row.TransferId, Version = 2, Kind = kind,
            OccurredAtUtc = wrongTime ? at.AddHours(2) : at.AddHours(1), RecordedAtUtc = at.AddHours(2), RecordedBy = "SENTETIK",
            Reason = "SENTETIK", CorrelationId = "SENTETIK" });
        var exception = Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
        Assert.Contains(kind == "AssociationRecorded" ? "Association events" : "Each departure fact", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void New_departure_state_cannot_invent_a_prior_exit_or_omit_its_explicit_initial_association_event()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        using var database = Context(company);
        database.Add(new TransferDepartureStateRow { CompanyId = company.CompanyId, TransferId = Guid.NewGuid(), DocumentId = Guid.NewGuid(),
            SourceLocationId = Guid.NewGuid(), Association = 1, Version = 1, CreatedBy = "SENTETIK", CreatedAtUtc = DateTimeOffset.UtcNow,
            VehicleExitAtUtc = DateTimeOffset.UtcNow });
        Assert.Contains("explicit association event", Assert.Throws<InvalidOperationException>(() => database.SaveChanges()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Departure_state_event_attempt_and_weighing_columns_are_company_scoped_append_only_and_concurrency_protected()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        using var database = Context(company);
        var model = database.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(TransferDepartureStateRow), typeof(TransferDepartureEventRow), typeof(TransferDepartureAttemptRow) })
        {
            var entity = model.FindEntityType(type)!;
            Assert.Equal("logistics", entity.GetSchema());
            Assert.Contains(entity.FindPrimaryKey()!.Properties, x => x.Name == "CompanyId");
            Assert.NotNull(entity.GetDeclaredQueryFilters().SingleOrDefault());
        }
        var state = model.FindEntityType(typeof(TransferDepartureStateRow))!;
        Assert.True(state.FindProperty("Version")!.IsConcurrencyToken);
        Assert.True(state.FindProperty("RowVersion")!.IsConcurrencyToken);
        var item = model.FindEntityType(typeof(TransferDepartureEventRow))!;
        Assert.Equal((18, 3), (item.FindProperty("NetWeighingKg")!.GetPrecision(), item.FindProperty("NetWeighingKg")!.GetScale()));
        Assert.Equal(LogisticsDbContext.TransferDepartureEventsWeighingCheck,
            item.GetCheckConstraints().Single(x => x.Name == "CK_TransferDepartureEvents_Weighing").Sql);
    }

    private static LogisticsDbContext Context(CompanyContext company) => new(new DbContextOptionsBuilder<LogisticsDbContext>()
        .UseSqlServer("Server=unused;Database=SyntheticModelOnly;Integrated Security=True;TrustServerCertificate=True").Options, company);
}
