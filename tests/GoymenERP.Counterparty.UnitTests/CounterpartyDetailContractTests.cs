using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CounterpartyDetailContractTests
{
    [Fact]
    public void Detail_contract_is_narrow_and_uses_lossless_version_strings()
    {
        AssertPropertyContract(
            typeof(CounterpartyDetailView),
            new Dictionary<string, Type>(StringComparer.Ordinal)
            {
                [nameof(CounterpartyDetailView.Id)] = typeof(Guid),
                [nameof(CounterpartyDetailView.Code)] = typeof(string),
                [nameof(CounterpartyDetailView.Kind)] = typeof(PartyKind),
                [nameof(CounterpartyDetailView.LegalName)] = typeof(string),
                [nameof(CounterpartyDetailView.TradeName)] = typeof(string),
                [nameof(CounterpartyDetailView.IsActive)] = typeof(bool),
                [nameof(CounterpartyDetailView.Version)] = typeof(string),
                [nameof(CounterpartyDetailView.CompanyAccounts)] =
                    typeof(IReadOnlyList<CounterpartyDetailAccountView>),
            });
        AssertPropertyContract(
            typeof(CounterpartyDetailAccountView),
            new Dictionary<string, Type>(StringComparer.Ordinal)
            {
                [nameof(CounterpartyDetailAccountView.Id)] = typeof(Guid),
                [nameof(CounterpartyDetailAccountView.CompanyId)] = typeof(Guid),
                [nameof(CounterpartyDetailAccountView.Roles)] = typeof(PartyRole),
                [nameof(CounterpartyDetailAccountView.IsActive)] = typeof(bool),
                [nameof(CounterpartyDetailAccountView.Version)] = typeof(string),
            });

        Assert.DoesNotContain(
            typeof(CounterpartyDetailView).GetProperties(),
            property => string.Equals(
                property.Name,
                "ExternalIntegrationCode",
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(CounterpartyDetailAccountView).GetProperties(),
            property => string.Equals(
                property.Name,
                "ExternalIntegrationCode",
                StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertPropertyContract(
        Type type,
        IReadOnlyDictionary<string, Type> expected)
    {
        var actual = type.GetProperties()
            .ToDictionary(
                property => property.Name,
                property => property.PropertyType,
                StringComparer.Ordinal);

        Assert.Equal(expected.Count, actual.Count);
        foreach (var (propertyName, propertyType) in expected)
        {
            Assert.True(actual.TryGetValue(propertyName, out var actualType));
            Assert.Equal(propertyType, actualType);
        }
    }
}
