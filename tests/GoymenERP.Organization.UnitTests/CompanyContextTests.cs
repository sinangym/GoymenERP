using GoymenERP.BuildingBlocks.Multitenancy;

namespace GoymenERP.Organization.UnitTests;

public sealed class CompanyContextTests
{
    [Fact]
    public void Set_ResolvesCompanyForRequest()
    {
        var companyId = Guid.NewGuid();
        var context = new CompanyContext();

        context.Set(companyId);

        Assert.True(context.IsResolved);
        Assert.Equal(companyId, context.CompanyId);
    }

    [Fact]
    public void Set_RejectsCompanySwitchInsideSameRequest()
    {
        var context = new CompanyContext();
        context.Set(Guid.NewGuid());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            context.Set(Guid.NewGuid()));

        Assert.Equal("Bir istek içinde şirket bağlamı değiştirilemez.", exception.Message);
    }

    [Fact]
    public void CompanyId_RejectsUnresolvedContext()
    {
        var context = new CompanyContext();

        var exception = Assert.Throws<InvalidOperationException>(() => context.CompanyId);

        Assert.Equal("Şirket bağlamı henüz belirlenmedi.", exception.Message);
    }
}
