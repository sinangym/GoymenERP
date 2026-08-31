using GoymenERP.Modules.Organization.Domain.Companies;
using GoymenERP.Modules.Organization.Domain.Locations;

namespace GoymenERP.Organization.UnitTests;

public sealed class OrganizationDomainTests
{
    [Fact]
    public void Company_NormalizesValidVknAndAllowsInvoiceProduction()
    {
        var company = new Company(
            Guid.NewGuid(),
            "GDC",
            "Göymen Demir Çelik",
            " 4100046586 ",
            isPlaceholder: false,
            invoiceCapable: true);

        Assert.Equal("4100046586", company.Vkn);
        Assert.True(company.InvoiceCapable);
        Assert.False(company.IsPlaceholder);
    }

    [Fact]
    public void Company_AllowsMissingVknOnlyWhenInvoiceProductionIsDisabled()
    {
        var company = new Company(
            Guid.NewGuid(),
            "DRAFT",
            "Taslak Şirket",
            null,
            isPlaceholder: true,
            invoiceCapable: false);

        Assert.Null(company.Vkn);
        Assert.False(company.InvoiceCapable);
    }

    [Theory]
    [InlineData("123456789")]
    [InlineData("12345678901")]
    [InlineData("12345A7890")]
    [InlineData("１２３４５６７８９０")]
    public void Company_RejectsNonCanonicalVkn(string vkn)
    {
        Assert.Throws<ArgumentException>(() => new Company(
            Guid.NewGuid(),
            "TEST",
            "Test Şirketi",
            vkn,
            isPlaceholder: false,
            invoiceCapable: false));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("4100046586", true)]
    public void Company_InvoiceProductionRequiresRealCompanyAndVkn(
        string? vkn,
        bool isPlaceholder)
    {
        Assert.Throws<ArgumentException>(() => new Company(
            Guid.NewGuid(),
            "TEST",
            "Test Şirketi",
            vkn,
            isPlaceholder,
            invoiceCapable: true));
    }

    [Fact]
    public void Location_RequiresCompanyId()
    {
        Assert.Throws<ArgumentException>(() => new Location(
            Guid.NewGuid(),
            Guid.Empty,
            Guid.NewGuid(),
            "LOC-01",
            "Lokasyon",
            false));
    }

    [Fact]
    public void Location_RequiresSiteId()
    {
        Assert.Throws<ArgumentException>(() => new Location(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.Empty,
            "LOC-01",
            "Lokasyon",
            false));
    }
}
