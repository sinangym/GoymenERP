using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Domain.Companies;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Organization.UnitTests;

/// <summary>KIM-D41/KIM-D48: the staging table's model matches the DbContext constants the migration was generated from.</summary>
public sealed class CompanyTaxIdCorrectionPersistenceModelTests
{
    [Fact]
    public void The_correction_model_carries_its_checks_the_pending_index_and_a_restrict_company_key()
    {
        using var dbContext = CreateDbContext();
        var entity = dbContext.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(CompanyTaxIdCorrection));
        Assert.NotNull(entity);
        Assert.Equal(("CompanyTaxIdCorrections", "organization"), (entity!.GetTableName(), entity.GetSchema()));

        Assert.False(entity.FindProperty(nameof(CompanyTaxIdCorrection.StagedVkn))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(CompanyTaxIdCorrection.CorrectedVkn))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(CompanyTaxIdCorrection.AppliedBy))!.IsNullable);
        Assert.False(entity.FindProperty(nameof(CompanyTaxIdCorrection.CompanyId))!.IsNullable);

        var checks = entity.GetCheckConstraints().ToDictionary(item => item.Name!, item => item.Sql);
        Assert.Equal(OrganizationDbContext.CompanyTaxIdCorrectionStatusCheck, checks["CK_CompanyTaxIdCorrections_Status"]);
        Assert.Equal(OrganizationDbContext.CompanyTaxIdCorrectionStagedVknCheck, checks["CK_CompanyTaxIdCorrections_StagedVkn"]);
        Assert.Equal(
            OrganizationDbContext.CompanyTaxIdCorrectionCorrectedVknCheck,
            checks["CK_CompanyTaxIdCorrections_CorrectedVkn"]);
        Assert.Equal(OrganizationDbContext.CompanyTaxIdCorrectionAppliedCheck, checks["CK_CompanyTaxIdCorrections_Applied"]);
        Assert.Equal(OrganizationDbContext.CompanyTaxIdCorrectionVersionCheck, checks["CK_CompanyTaxIdCorrections_Version"]);

        var pending = Assert.Single(
            entity.GetIndexes(),
            index => index.GetDatabaseName() == OrganizationDbContext.CompanyTaxIdCorrectionPendingIndexName);
        Assert.True(pending.IsUnique);
        Assert.Equal(OrganizationDbContext.CompanyTaxIdCorrectionPendingFilter, pending.GetFilter());
        Assert.Equal([nameof(CompanyTaxIdCorrection.CompanyId)], pending.Properties.Select(x => x.Name));

        var company = Assert.Single(entity.GetForeignKeys());
        Assert.Equal(typeof(Company), company.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, company.DeleteBehavior);
        Assert.Single(entity.GetDeclaredQueryFilters());
    }

    [Fact]
    public void The_migration_creates_the_same_checks_and_its_down_guard_comes_first()
    {
        var migration = new StageCompanyTaxIdCorrections();

        var create = Assert.IsType<CreateTableOperation>(migration.UpOperations[0]);
        var checks = create.CheckConstraints.ToDictionary(item => item.Name, item => item.Sql);
        Assert.Equal(OrganizationDbContext.CompanyTaxIdCorrectionStatusCheck, checks["CK_CompanyTaxIdCorrections_Status"]);
        Assert.Equal(OrganizationDbContext.CompanyTaxIdCorrectionAppliedCheck, checks["CK_CompanyTaxIdCorrections_Applied"]);
        Assert.Equal(
            OrganizationDbContext.CompanyTaxIdCorrectionCorrectedVknCheck,
            checks["CK_CompanyTaxIdCorrections_CorrectedVkn"]);
        Assert.Contains("THROW 51877", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql, StringComparison.Ordinal);
    }

    private static OrganizationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseInMemoryDatabase($"organization-tax-id-correction-model-{Guid.NewGuid():N}")
            .Options;
        return new OrganizationDbContext(options, new CompanyContext(), new OrganizationBootstrapWriteScope());
    }
}
