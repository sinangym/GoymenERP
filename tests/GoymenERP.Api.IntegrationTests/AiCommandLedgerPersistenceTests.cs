using GoymenERP.Api.Ai.Commands;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.AiKnowledge.Domain;
using GoymenERP.Modules.AiKnowledge.Infrastructure;
using GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;
using GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Model and application guards only. InMemory is not SQL atomicity, rowversion or constraint evidence.</summary>
public sealed class AiCommandLedgerPersistenceTests
{
    [Fact]
    public void The_command_tables_are_company_filtered_in_the_ai_schema_and_Down_refuses_data_loss()
    {
        using var db = new AiKnowledgeDbContext(new DbContextOptionsBuilder<AiKnowledgeDbContext>()
            .UseSqlServer("Server=unused;Database=unused").Options, new CompanyContext());
        var model = db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
        var record = model.FindEntityType(typeof(AiCommandRecordRow))!;
        Assert.Equal("ai", record.GetSchema());
        Assert.Equal(new[] { "CompanyId", "PreviewId" }, record.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.True(record.FindProperty("Version")!.IsConcurrencyToken);
        Assert.True(record.FindProperty("RowVersion")!.IsConcurrencyToken);
        Assert.NotEmpty(record.GetDeclaredQueryFilters());
        Assert.Equal(AiKnowledgeDbContext.CommandRecordCheck, Assert.Single(record.GetCheckConstraints()).Sql);
        var events = model.FindEntityType(typeof(AiCommandEventRow))!;
        Assert.NotEmpty(events.GetDeclaredQueryFilters());
        Assert.Null(events.FindProperty("RecordJson"));
        Assert.Equal(AiKnowledgeDbContext.CommandEventCheck, Assert.Single(events.GetCheckConstraints()).Sql);
        var migration = new AddAiCommandLedger();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Contains("THROW 52200", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }

    [Fact]
    public async Task The_owner_filters_companies_and_rejects_a_changed_binding_or_stale_version_without_an_event()
    {
        var company = new CompanyContext(); company.Set(SqlAiCommandLedgerTests.Company);
        await using var db = Memory(company);
        var store = new AiCommandPersistenceV1(db, company);
        var ledger = new SqlAiCommandLedger(store);
        var record = SqlAiCommandLedgerTests.Record();
        Assert.True(await ledger.TryAddAsync(record, [SqlAiCommandLedgerTests.Event(record)]));
        var current = await store.GetAsync(record.CompanyId, record.PreviewId);
        Assert.NotNull(current);
        Assert.False(await store.TryReplaceAsync(current with { Version = 2, BindingSha256 = new string('b', 64) }, 1,
            StoredEvent(record)));
        Assert.False(await store.TryReplaceAsync(current with { Version = 3 }, 2, StoredEvent(record)));
        Assert.Equal(1, await db.CommandEvents.CountAsync());
        var approved = record with { Version = 2, State = AiCommandStateV1.Approved };
        Assert.True(await ledger.TryReplaceAsync(approved, 1, SqlAiCommandLedgerTests.Event(record)));
        Assert.Equal(2, (await ledger.GetAsync(record.CompanyId, record.PreviewId))!.Version);
        var other = Guid.NewGuid();
        Assert.Null(await store.GetAsync(other, record.PreviewId));
        var otherCompany = new CompanyContext(); otherCompany.Set(other);
        await using var otherDb = new AiKnowledgeDbContext(db.GetService<IDbContextOptions>() is DbContextOptions<AiKnowledgeDbContext> options
            ? options : throw new InvalidOperationException(), otherCompany);
        Assert.Empty(await otherDb.CommandRecords.ToArrayAsync());
        Assert.Empty(await otherDb.CommandEvents.ToArrayAsync());
        Assert.Null(await new AiCommandPersistenceV1(otherDb, otherCompany).GetAsync(record.CompanyId, record.PreviewId));
    }

    [Fact]
    public async Task Events_cannot_be_rewritten_and_a_command_binding_is_immutable_in_the_context_guard()
    {
        var company = new CompanyContext(); company.Set(SqlAiCommandLedgerTests.Company);
        await using var db = Memory(company);
        var ledger = new SqlAiCommandLedger(new AiCommandPersistenceV1(db, company));
        var record = SqlAiCommandLedgerTests.Record();
        Assert.True(await ledger.TryAddAsync(record, [SqlAiCommandLedgerTests.Event(record)]));
        var item = await db.CommandEvents.SingleAsync();
        item.ReasonCode = "changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.Entry(item).State = EntityState.Detached;
        var row = await db.CommandRecords.SingleAsync();
        row.Version = 2; row.BindingSha256 = new string('b', 64);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    private static AiCommandStoredEventV1 StoredEvent(AiCommandRecordV1 record) =>
        new(Guid.NewGuid(), record.CompanyId, record.ActorId, 3, record.PreviewId, null, null, null, null, null, null,
            "SENTETIK-cas", null, SqlAiCommandLedgerTests.At);

    private static AiKnowledgeDbContext Memory(ICompanyContext company) => new(
        new DbContextOptionsBuilder<AiKnowledgeDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options, company);
}
