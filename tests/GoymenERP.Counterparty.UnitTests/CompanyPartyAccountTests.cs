using GoymenERP.Modules.Counterparty.Domain.Accounts;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CompanyPartyAccountTests
{
    [Fact]
    public void One_company_account_can_hold_multiple_commercial_roles()
    {
        var account = new CompanyPartyAccount(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            PartyRole.Customer | PartyRole.Supplier | PartyRole.Service);

        Assert.True(account.HasRole(PartyRole.Customer));
        Assert.True(account.HasRole(PartyRole.Supplier));
        Assert.True(account.HasRole(PartyRole.Service));
        Assert.False(account.HasRole(PartyRole.Tolling));
    }

    [Fact]
    public void Account_requires_a_company_and_party_identity()
    {
        Assert.Throws<ArgumentException>(() => new CompanyPartyAccount(
            Guid.NewGuid(),
            Guid.Empty,
            Guid.NewGuid(),
            PartyRole.Customer));

        Assert.Throws<ArgumentException>(() => new CompanyPartyAccount(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.Empty,
            PartyRole.Customer));
    }

    [Fact]
    public void Account_requires_at_least_one_known_role()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompanyPartyAccount(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            PartyRole.None));

        Assert.Throws<ArgumentOutOfRangeException>(() => new CompanyPartyAccount(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            (PartyRole)16));
    }

    [Fact]
    public void External_integration_code_is_company_account_specific_and_normalized()
    {
        var account = new CompanyPartyAccount(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            PartyRole.Supplier,
            "  MIKRO-320-001  ");

        Assert.Equal("MIKRO-320-001", account.ExternalIntegrationCode);
    }
}
