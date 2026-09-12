using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CounterpartyContactInformationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void OmissionNullAndWhitespace_AreDistinctAndCollectionsAreNotReplacementLists()
    {
        var address = Guid.NewGuid();
        var contact = Guid.NewGuid();
        var channel = Guid.NewGuid();
        var first = Apply(CounterpartyContactInformationEditor.Empty(), $$"""
            {"phone":"+90 0328 000 00 00","email":" a@example.test ",
             "addresses":[{"id":"{{address}}","operation":"upsert","purpose":"delivery",
                "countryCode":"de","line1":" Route 01 "}],
             "contacts":[{"id":"{{contact}}","operation":"upsert","displayName":"Person","purpose":"Operations",
                "channels":[{"id":"{{channel}}","operation":"upsert","kind":"phone","value":"00123","extension":"001"}]}]}
            """);
        var omitted = Apply(first, """{"website":"https://example.test/"}""");
        Assert.Equal(first.Phone, omitted.Phone);
        Assert.Equal(first.Addresses, omitted.Addresses);
        Assert.Equal(first.Contacts, omitted.Contacts);
        var cleared = Apply(omitted, """{"phone":null,"email":"  "}""");
        Assert.Null(cleared.Phone);
        Assert.Null(cleared.Email);
        Assert.Equal("DE", Assert.Single(cleared.Addresses).CountryCode);
        Assert.Equal("00123", Assert.Single(Assert.Single(cleared.Contacts).Channels).Value);
        var inactive = Apply(cleared, $$"""{"addresses":[{"id":"{{address}}","operation":"deactivate"}]}""");
        Assert.False(Assert.Single(inactive.Addresses).IsActive);
        Assert.Equal("2", Assert.Single(inactive.Addresses).Version);
        var editedInactive = Apply(inactive, $$"""{"addresses":[{"id":"{{address}}","operation":"upsert","line1":"New"}]}""");
        Assert.False(Assert.Single(editedInactive.Addresses).IsActive);
        Assert.Equal(inactive.Contacts, editedInactive.Contacts);
        Assert.Equal(CounterpartyContactInformationEditor.Serialize(editedInactive),
            CounterpartyContactInformationEditor.Serialize(
                CounterpartyContactInformationEditor.Load(CounterpartyContactInformationEditor.Serialize(editedInactive))));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("01")]
    [InlineData("-1")]
    [InlineData("1.0")]
    [InlineData("1e1")]
    [InlineData(" 1")]
    [InlineData("9223372036854775808")]
    [InlineData(null)]
    public void Version_RejectsNonCanonicalOrOutOfRangeText(string? value) =>
        Assert.Throws<ArgumentException>(() => CounterpartyContactInformationEditor.ParseVersion(value));

    [Fact]
    public void Version_PreservesFullInt64WithoutJavaScriptRounding()
    {
        Assert.Equal(9007199254740993L, CounterpartyContactInformationEditor.ParseVersion("9007199254740993"));
        Assert.Equal(long.MaxValue, CounterpartyContactInformationEditor.ParseVersion("9223372036854775807"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpdateCounterpartyContactInformation>(
            """{"expectedVersion":1,"changes":{}}""", JsonOptions));
    }

    [Theory]
    [InlineData("""{"email":"not-an-email"}""")]
    [InlineData("""{"phone":10}""")]
    [InlineData("""{"website":"javascript:alert(1)"}""")]
    [InlineData("""{"website":"https://user:password@example.test/"}""")]
    [InlineData("""{"addresses":null}""")]
    [InlineData("""{"contacts":null}""")]
    [InlineData("""{"phoneExtension":"12"}""")]
    [InlineData("""{"addresses":[{"id":"00000000-0000-0000-0000-000000000001","operation":"upsert","purpose":"registered","countryCode":"TR","line1":"A"}]}""")]
    public void InvalidChanges_AreRejectedWithoutChangingTheInput(string json)
    {
        var current = CounterpartyContactInformationEditor.Empty();
        Assert.Throws<ArgumentException>(() => Apply(current, json));
        Assert.Null(current.Phone);
        Assert.Empty(current.Addresses);
    }

    [Fact]
    public void DuplicateChildOperation_AndImplicitDeletionAreRejected()
    {
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => Apply(CounterpartyContactInformationEditor.Empty(), $$"""
            {"addresses":[{"id":"{{id}}","operation":"deactivate"},{"id":"{{id}}","operation":"deactivate"}]}
            """));
        Assert.Throws<ArgumentException>(() => Apply(CounterpartyContactInformationEditor.Empty(),
            $$"""{"addresses":[{"id":"{{id}}","operation":"delete"}]}"""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpdateCounterpartyContactInformation>(
            """{"expectedVersion":"1","changes":{"legalName":"forbidden"}}""", JsonOptions));
    }

    [Fact]
    public void CanonicalNoOp_DoesNotAdvanceChildVersions()
    {
        var id = Guid.NewGuid();
        var contact = Guid.NewGuid();
        var channel = Guid.NewGuid();
        var payload = $$"""
            {"addresses":[{"id":"{{id}}","operation":"upsert","purpose":"delivery","countryCode":"TR","line1":"Address"}],
             "contacts":[{"id":"{{contact}}","operation":"upsert","displayName":"Name","purpose":"Operations",
             "channels":[{"id":"{{channel}}","operation":"upsert","kind":"phone","value":"001"}]}]}
            """;
        var first = Apply(CounterpartyContactInformationEditor.Empty(), payload);
        var repeated = Apply(first, payload);
        Assert.Equal(CounterpartyContactInformationEditor.Serialize(first),
            CounterpartyContactInformationEditor.Serialize(repeated));
        var inactivePayload = $$"""{"addresses":[{"id":"{{id}}","operation":"deactivate"}]}""";
        var inactive = Apply(first, inactivePayload);
        Assert.Equal(CounterpartyContactInformationEditor.Serialize(inactive),
            CounterpartyContactInformationEditor.Serialize(Apply(inactive, inactivePayload)));
    }

    [Fact]
    public void PersistedCollectionsCanGrowAcrossBoundedRequests()
    {
        var current = CounterpartyContactInformationEditor.Empty();
        for (var batch = 0; batch < 2; batch++)
        {
            var changes = new CounterpartyContactInformationChanges
            {
                Addresses = Enumerable.Range(0, 60).Select(_ => new CounterpartyAddressChange
                {
                    Id = Guid.NewGuid(), Operation = "upsert",
                    Purpose = JsonSerializer.SerializeToElement("operational"),
                    CountryCode = JsonSerializer.SerializeToElement("TR"),
                    Line1 = JsonSerializer.SerializeToElement("Address"),
                }).ToArray(),
            };
            current = CounterpartyContactInformationEditor.Apply(current, changes);
        }

        Assert.Equal(120, CounterpartyContactInformationEditor.Load(
            CounterpartyContactInformationEditor.Serialize(current)).Addresses.Count);
    }

    private static CounterpartyContactInformation Apply(CounterpartyContactInformation current, string json) =>
        CounterpartyContactInformationEditor.Apply(current,
            JsonSerializer.Deserialize<CounterpartyContactInformationChanges>(json, JsonOptions)!);

    [Theory]
    [InlineData("\r001")]
    [InlineData("001\n")]
    [InlineData("\r\n")]
    [InlineData("\t")]
    [InlineData("  \n  ")]
    public void RawControlCharactersCannotBeTrimmedIntoAValueOrClear(string value)
    {
        var current = CounterpartyContactInformationEditor.Empty() with { Phone = "retained" };
        Assert.Throws<ArgumentException>(() => Apply(current, JsonSerializer.Serialize(new { phone = value })));
        Assert.Equal("retained", current.Phone);
    }
}
