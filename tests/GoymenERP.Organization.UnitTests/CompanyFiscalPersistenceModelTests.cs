using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Domain.Companies;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Organization.UnitTests;

public sealed class CompanyFiscalPersistenceModelTests
{
    [Fact]
    public void CompanyModel_StoresFiscalFlagsWithoutChangingDirectoryContract()
    {
        using var dbContext = CreateDbContext();
        var company = dbContext.GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(Company));
        Assert.NotNull(company);

        var vkn = company!.FindProperty(nameof(Company.Vkn));
        Assert.NotNull(vkn);
        Assert.True(vkn!.IsNullable);
        Assert.Equal(10, vkn.GetMaxLength());

        var invoiceCapable = company.FindProperty(nameof(Company.InvoiceCapable));
        Assert.NotNull(invoiceCapable);
        Assert.False(invoiceCapable!.IsNullable);
        Assert.Equal(false, invoiceCapable.GetDefaultValue());

        var constraints = company.GetCheckConstraints()
            .ToDictionary(item => item.Name!, item => item.Sql);
        Assert.Equal(
            "[Vkn] IS NULL OR (LEN([Vkn]) = 10 AND [Vkn] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%')",
            constraints["CK_Companies_Vkn_Format"]);
        Assert.Equal(
            "[InvoiceCapable] = 0 OR ([IsPlaceholder] = 0 AND [Vkn] IS NOT NULL)",
            constraints["CK_Companies_Placeholder_InvoiceDisabled"]);

        Assert.DoesNotContain(
            company.GetIndexes(),
            index => index.Properties.Any(property => property.Name == nameof(Company.Vkn)));
    }

    private static OrganizationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseInMemoryDatabase($"organization-company-fiscal-{Guid.NewGuid():N}")
            .Options;
        return new OrganizationDbContext(
            options,
            new CompanyContext(),
            new OrganizationBootstrapWriteScope());
    }
}
