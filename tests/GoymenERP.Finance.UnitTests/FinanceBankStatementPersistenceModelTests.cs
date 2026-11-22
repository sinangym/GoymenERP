using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceBankStatementPersistenceModelTests
{
    [Fact]
    public void Statement_tables_are_company_bound_checked_unique_per_account_line_and_the_down_refuses_while_rows_exist()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer("Server=invalid;Database=Synthetic;Integrated Security=true;TrustServerCertificate=true").Options, company);
        var model = db.GetService<IDesignTimeModel>().Model;
        var import = model.FindEntityType(typeof(FinanceBankStatementImportRow))!;
        var line = model.FindEntityType(typeof(FinanceBankStatementLineRow))!;
        var command = model.FindEntityType(typeof(FinanceBankStatementCommandRow))!;
        foreach (var entity in new[] { import, line, command })
        {
            Assert.Equal(["CompanyId"], entity.FindPrimaryKey()!.Properties.Take(1).Select(x => x.Name).ToArray());
            Assert.NotEmpty(entity.GetDeclaredQueryFilters());
            Assert.Equal("finance", entity.GetSchema());
        }

        Assert.Equal(FinanceDbContext.BankStatementImportIdentityCheck,
            Assert.Single(import.GetCheckConstraints(), x => x.Name == "CK_BankStatementImports_Identity").Sql);
        Assert.Equal(FinanceDbContext.BankStatementImportStateCheck,
            Assert.Single(import.GetCheckConstraints(), x => x.Name == "CK_BankStatementImports_State").Sql);
        Assert.Equal(FinanceDbContext.BankStatementLineCheck,
            Assert.Single(line.GetCheckConstraints(), x => x.Name == "CK_BankStatementLines_Content").Sql);
        Assert.True(import.FindProperty(nameof(FinanceBankStatementImportRow.Version))!.IsConcurrencyToken);
        var unique = Assert.Single(line.GetIndexes(), x => x.IsUnique);
        Assert.Equal(["CompanyId", "BankAccountId", "Fingerprint", "Occurrence"], unique.Properties.Select(x => x.Name).ToArray());
        Assert.Equal("varchar(max)", line.FindProperty(nameof(FinanceBankStatementLineRow.AmountText))!.GetColumnType());

        var migration = new AddBankStatementImports();
        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        Assert.Equal(3, migration.UpOperations.OfType<CreateTableOperation>().Count());
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 52250", guard);
        Assert.Contains("[finance].[BankStatementImports]", guard);
        Assert.Contains("[finance].[BankStatementLines]", guard);
        Assert.Contains("[finance].[BankStatementCommands]", guard);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal("20261122201621_AddBankStatementImports", db.Database.GetMigrations().Last());
    }
}
