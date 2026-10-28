using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;

namespace GoymenERP.Counterparty.UnitTests;

/// <summary>SAT-D57: the company's customer (price) group and the account's assignment, in the domain.</summary>
public sealed class CustomerGroupTests
{
    private static readonly Guid Company = Guid.Parse("8e1d5a2c-41f3-4c35-9a5b-6c8a0d4f2b11");

    [Fact]
    public void A_new_group_is_active_at_version_one_with_a_trimmed_normalized_name()
    {
        var group = new CustomerGroup(Guid.NewGuid(), Company, "  Toptancı  ");

        Assert.Equal("Toptancı", group.Name);
        Assert.True(group.IsActive);
        Assert.Equal(1, group.Version);
        Assert.Equal(Company, group.CompanyId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Bayi\u0007")]
    public void An_empty_or_control_character_name_is_refused(string? name) =>
        Assert.ThrowsAny<ArgumentException>(() => new CustomerGroup(Guid.NewGuid(), Company, name!));

    [Fact]
    public void A_name_longer_than_the_maximum_is_refused()
    {
        Assert.Equal("x", CustomerGroup.NormalizeName(new string('x', 1)));
        _ = CustomerGroup.NormalizeName(new string('x', CustomerGroup.NameMaximumLength));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CustomerGroup.NormalizeName(new string('x', CustomerGroup.NameMaximumLength + 1)));
    }

    [Fact]
    public void Empty_ids_are_refused()
    {
        Assert.Throws<ArgumentException>(() => new CustomerGroup(Guid.Empty, Company, "Perakendeci"));
        Assert.Throws<ArgumentException>(() => new CustomerGroup(Guid.NewGuid(), Guid.Empty, "Perakendeci"));
    }

    [Fact]
    public void Rename_and_active_state_move_the_version_only_on_a_real_change()
    {
        var group = new CustomerGroup(Guid.NewGuid(), Company, "İmalatçı");

        Assert.False(group.Rename(" İmalatçı "));
        Assert.Equal(1, group.Version);
        Assert.True(group.Rename("Bayi"));
        Assert.Equal(2, group.Version);
        Assert.False(group.SetActive(true));
        Assert.True(group.SetActive(false));
        Assert.False(group.IsActive);
        Assert.Equal(3, group.Version);
        Assert.True(group.SetActive(true));
        Assert.Equal(4, group.Version);
    }

    [Fact]
    public void An_account_starts_without_a_group_and_assign_or_clear_moves_its_version_once()
    {
        var account = new CompanyPartyAccount(Guid.NewGuid(), Company, Guid.NewGuid(), PartyRole.Customer);
        var groupId = Guid.NewGuid();

        Assert.Null(account.CustomerGroupId);
        Assert.True(account.AssignCustomerGroup(groupId));
        Assert.Equal(groupId, account.CustomerGroupId);
        Assert.Equal(2, account.Version);
        Assert.False(account.AssignCustomerGroup(groupId));
        Assert.Equal(2, account.Version);
        Assert.True(account.AssignCustomerGroup(null));
        Assert.Null(account.CustomerGroupId);
        Assert.Equal(3, account.Version);
        Assert.Throws<ArgumentException>(() => account.AssignCustomerGroup(Guid.Empty));
    }

    [Fact]
    public void The_owner_permission_names_are_the_registered_ones()
    {
        Assert.Equal(ErpPermissions.CounterpartyCustomerGroupsManage, CounterpartyCustomerGroupPermissionsV1.Manage);
        Assert.Equal(ErpPermissions.CounterpartyAccountsCustomerGroupAssign, CounterpartyCustomerGroupPermissionsV1.Assign);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(CounterpartyCustomerGroupPermissionsV1.Manage));
        Assert.True(ErpPermissions.RequiresCompanyWideScope(CounterpartyCustomerGroupPermissionsV1.Assign));
    }
}
