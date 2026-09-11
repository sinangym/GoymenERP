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
    public void Account_without_trade_classification_is_active_but_has_no_legacy_flags()
    {
        var noneAccount = new CompanyPartyAccount(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            PartyRole.None);
        Assert.Equal(PartyRole.None, noneAccount.Roles);
        Assert.True(noneAccount.IsActive);
        Assert.Equal(1L, noneAccount.Version);
        Assert.False(noneAccount.HasRole(PartyRole.Customer));
        Assert.False(noneAccount.HasRole(PartyRole.Supplier));
        Assert.False(noneAccount.HasRole(PartyRole.None));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(16)]
    [InlineData(17)]
    public void Account_rejects_unknown_trade_classification_bits(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompanyPartyAccount(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            (PartyRole)value));
    }

    [Fact]
    public void Clearing_trade_classification_preserves_inactive_state_and_advances_version()
    {
        var account = new CompanyPartyAccount(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PartyRole.Supplier);
        account.Deactivate();
        var previousVersion = account.Version;

        account.SetRoles(PartyRole.None);

        Assert.Equal(PartyRole.None, account.Roles);
        Assert.False(account.IsActive);
        Assert.Equal(previousVersion + 1, account.Version);
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

    [Fact]
    public void No_op_reactivate_advances_version_for_optimistic_concurrency()
    {
        var account = new CompanyPartyAccount(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            PartyRole.Customer);

        account.Reactivate();

        Assert.True(account.IsActive);
        Assert.Equal(2L, account.Version);
    }

    [Fact]
    public void No_op_deactivate_advances_version_for_optimistic_concurrency()
    {
        var account = new CompanyPartyAccount(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            PartyRole.Customer);
        account.Deactivate();
        Assert.Equal(2L, account.Version);

        account.Deactivate();

        Assert.False(account.IsActive);
        Assert.Equal(3L, account.Version);
    }
}
