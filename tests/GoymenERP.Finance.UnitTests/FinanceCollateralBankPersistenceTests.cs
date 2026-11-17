using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceCollateralBankPersistenceTests
{
    [Fact]
    public void Explicit_bank_proof_is_nullable_company_bound_immutable_history_and_has_no_backfill_or_default()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer("Server=invalid;Database=Synthetic;Integrated Security=true;TrustServerCertificate=true").Options, company);
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PartyCollateralRow))!;
        foreach (var name in new[] { "BankInstitutionId", "BankSourceVersion", "BankProofJson" })
        { var property = entity.FindProperty(name)!; Assert.True(property.IsNullable); Assert.Null(property.FindAnnotation("Relational:DefaultValue")); }
        var check = Assert.Single(entity.GetCheckConstraints(), x => x.Name == "CK_PartyCollaterals_BankProof").Sql;
        Assert.Contains("$.companyId", check); Assert.Contains("$.bankInstitutionId", check); Assert.Contains("$.sourceVersion", check);
        var migration = new AddCollateralBankProof();
        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        Assert.All(migration.UpOperations.OfType<AddColumnOperation>(), x => Assert.True(x.IsNullable));
        Assert.Equal(3, migration.UpOperations.OfType<AddColumnOperation>().Count());
        Assert.Equal(check, Assert.Single(migration.UpOperations.OfType<AddCheckConstraintOperation>()).Sql);
        Assert.Contains("THROW 51808", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Contains("20261117220054_AddCollateralBankProof", db.Database.GetMigrations());
    }
}
