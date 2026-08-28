using GoymenERP.Modules.Counterparty.Application.Views;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;

namespace GoymenERP.Cari.UnitTests;

public sealed class PartyCoreViewComposerTests
{
    [Fact]
    public void Group_view_contains_only_authorized_company_accounts_and_one_party()
    {
        var partyId = Guid.NewGuid();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var companyC = Guid.NewGuid();
        var party = new Party(
            partyId,
            PartyCode.FromSequence(1),
            PartyKind.LegalEntity,
            "Ortak Müşteri A.Ş.");
        var accounts = new[]
        {
            new CompanyPartyAccount(Guid.NewGuid(), companyA, partyId, PartyRole.Customer),
            new CompanyPartyAccount(Guid.NewGuid(), companyB, partyId, PartyRole.Supplier),
            new CompanyPartyAccount(Guid.NewGuid(), companyC, partyId, PartyRole.Service),
        };

        var result = PartyCoreViewComposer.ComposeAuthorized(
            party,
            accounts,
            new HashSet<Guid> { companyA, companyB });

        Assert.NotNull(result);
        Assert.Equal(partyId, result!.Id);
        Assert.Equal("CAR-000001", result.CariCode);
        Assert.Equal(2, result.CompanyAccounts.Count);
        Assert.Contains(result.CompanyAccounts, item => item.CompanyId == companyA);
        Assert.Contains(result.CompanyAccounts, item => item.CompanyId == companyB);
        Assert.DoesNotContain(result.CompanyAccounts, item => item.CompanyId == companyC);
    }

    [Fact]
    public void Party_is_not_exposed_without_an_authorized_company_link()
    {
        var partyId = Guid.NewGuid();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var party = new Party(
            partyId,
            PartyCode.FromSequence(2),
            PartyKind.NaturalPerson,
            "Ayşe Örnek");
        var accounts = new[]
        {
            new CompanyPartyAccount(Guid.NewGuid(), companyA, partyId, PartyRole.Customer),
        };

        var result = PartyCoreViewComposer.ComposeAuthorized(
            party,
            accounts,
            new HashSet<Guid> { companyB });

        Assert.Null(result);
    }

    [Fact]
    public void Composer_never_invents_balance_or_financial_fields()
    {
        var names = typeof(GoymenERP.Modules.Counterparty.Application.Contracts.PartyCoreView)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("Balance", names);
        Assert.DoesNotContain("Risk", names);
        Assert.DoesNotContain("OpenItems", names);
    }
}
