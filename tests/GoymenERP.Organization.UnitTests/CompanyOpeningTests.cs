using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Organization.Domain.Companies;
using GoymenERP.Modules.Organization.Domain.CompanySetup;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Organization.UnitTests;

public sealed class CompanyOpeningTests
{
    [Fact]
    public void A_company_opening_always_starts_pending_and_does_not_enable_official_issuing()
    {
        var actor = Guid.NewGuid();
        var company = Company.OpenPending(Guid.NewGuid(), "SENTETIK", "SENTETIK şirket", "9000000033", actor, DateTimeOffset.UnixEpoch);
        Assert.Equal(("SetupPending", actor, 1L, false, false),
            (company.LifecycleState, company.OpenedByUserId!.Value, company.Version, company.InvoiceCapable, company.IsPlaceholder));
        Assert.True(ErpPermissions.IsCritical(ErpPermissions.OrganizationCompaniesOpen));
    }
    [Fact]
    public async Task Direct_company_creation_and_opening_journal_appends_without_the_authorized_owner_transaction_are_rejected()
    {
        var companyId = Guid.NewGuid(); var context = new CompanyContext(); context.Set(companyId);
        await using var database = new OrganizationDbContext(new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options, context, new OrganizationBootstrapWriteScope());
        database.Companies.Add(Company.OpenPending(companyId, "SENTETIK", "SENTETIK şirket", "9000000033", Guid.NewGuid(), DateTimeOffset.UnixEpoch));
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();
        database.CompanyOpeningExecutions.Add(new(new string('A', 64), companyId, Guid.NewGuid(), new string('B', 64), 409,
            "{}", "SENTETIK-correlation", DateTimeOffset.UnixEpoch));
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
    }
    [Fact]
    public void The_global_opening_key_cannot_be_partitioned_by_actor_or_target_and_its_Down_refuses_history()
    {
        using var database = new OrganizationDbContext(new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused").Options, new CompanyContext(), new OrganizationBootstrapWriteScope());
        var entity = database.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(CompanyOpeningExecution))!;
        Assert.Equal(("organization", "CompanyOpeningExecutions"), (entity.GetSchema(), entity.GetTableName()));
        Assert.Equal([nameof(CompanyOpeningExecution.IdempotencyKeyHash)], entity.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Empty(entity.GetDeclaredQueryFilters());
        Assert.Equal(OrganizationDbContext.CompanyOpeningExecutionCheck, Assert.Single(entity.GetCheckConstraints()).Sql);
        Assert.Equal("Latin1_General_100_BIN2", entity.FindProperty(nameof(CompanyOpeningExecution.IdempotencyKeyHash))!.GetCollation());
        Assert.False(entity.FindProperty(nameof(CompanyOpeningExecution.CompanyId))!.IsNullable);
        var migration = new AddAuthorizedCompanyOpening();
        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        Assert.Single(migration.UpOperations.OfType<CreateTableOperation>(), operation => operation.Name == "CompanyOpeningExecutions");
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51948", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[organization].[CompanyOpeningExecutions]", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }
}
