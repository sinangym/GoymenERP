using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceCreditMovementPersistenceTests
{
    [Fact]
    public void Actual_credit_company_source_and_replacement_slots_are_unique_and_outbox_cursor_is_database_rowversion()
    {
        using var db = Context();
        var model = db.GetService<IDesignTimeModel>().Model;
        var movement = model.FindEntityType(typeof(FinanceCreditMovementRow))!;
        Assert.Equal("finance", movement.GetSchema()); Assert.Single(movement.GetDeclaredQueryFilters());
        Assert.True(movement.FindProperty("Version")!.IsConcurrencyToken);
        Assert.Contains(movement.GetIndexes(), index => index.IsUnique && index.Properties.Select(x => x.Name).SequenceEqual(["CompanyId", "CreditAccountId", "SourceIdentityHash"]));
        Assert.Contains(movement.GetIndexes(), index => index.IsUnique && index.Properties.Select(x => x.Name).SequenceEqual(["CompanyId", "ReplacesOpeningMovementId"]));
        Assert.Contains(movement.GetCheckConstraints(), check => check.Name == "CK_CreditMovements_Decision" && check.Sql.Contains("[DecidedBy] <> [ProposedBy]", StringComparison.Ordinal));
        Assert.Contains(movement.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(BankAccountRow)
            && key.Properties.Select(x => x.Name).SequenceEqual(["CompanyId", "BankAccountId"])
            && key.DeleteBehavior == DeleteBehavior.Restrict);
        var position = model.FindEntityType(typeof(FinanceOutboxRow))!.FindProperty("Position")!;
        Assert.True(position.IsConcurrencyToken); Assert.Equal(ValueGenerated.OnAddOrUpdate, position.ValueGenerated);
        var migration = new GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations.AddCreditActualMovements();
        Assert.Contains("THROW 51969", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql, StringComparison.Ordinal);
    }
    [Fact]
    public async Task Approved_actual_source_and_outbox_payload_cannot_be_overwritten_before_database_access()
    {
        using var db = Context();
        var movement = new FinanceCreditMovementRow { CompanyId = Company, Id = Guid.NewGuid(), CreditAccountId = Guid.NewGuid(), CurrencyId = Guid.NewGuid(),
            Status = 2, Version = 2, Kind = 1, SourceJson = "{}", SourceIdentityHash = new string('A', 64), MoneyProfileJson = "{}", ProposedBy = "SENTETIK maker", ProposedAtUtc = DateTimeOffset.UtcNow,
            DecidedBy = "SENTETIK checker", DecidedAtUtc = DateTimeOffset.UtcNow };
        db.Attach(movement); movement.SourceJson = "{\"overwrite\":true}"; movement.Version++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var outbox = new FinanceOutboxRow { CompanyId = Company, Id = Guid.NewGuid(), AggregateId = movement.Id, AggregateVersion = 2, EventType = "SENTETIK", SchemaVersion = 1,
            PayloadJson = "{}", OccurredAtUtc = DateTimeOffset.UtcNow, CorrelationId = "SENTETIK" };
        db.Attach(outbox); outbox.PayloadJson = "{\"overwrite\":true}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    private static readonly Guid Company = Guid.Parse("c0692000-0000-4000-8000-000000000001");
    private static FinanceDbContext Context()
    {
        var company = new CompanyContext(); company.Set(Company);
        return new(new DbContextOptionsBuilder<FinanceDbContext>().UseSqlServer("Server=unused;Database=unused;Trusted_Connection=True;TrustServerCertificate=True").Options, company);
    }
}
