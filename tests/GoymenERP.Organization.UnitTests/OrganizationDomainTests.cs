using GoymenERP.Modules.Organization.Domain.Locations;

namespace GoymenERP.Organization.UnitTests;

public sealed class OrganizationDomainTests
{
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
