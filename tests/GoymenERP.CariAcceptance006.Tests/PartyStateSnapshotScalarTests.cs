using System.Security.Cryptography;
using System.Text;
using GoymenERP.CariAcceptance006;

namespace GoymenERP.CariAcceptance006.Tests;

public sealed class PartyStateSnapshotScalarTests
{
    [Fact]
    public void EmptySqlDbNullHasTheCanonicalEmptyArrayFingerprint()
    {
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("[]")));
        Assert.Equal(expected, DatabaseEvidence.HashPartyState(DBNull.Value, 0));
        Assert.Equal(expected, DatabaseEvidence.HashPartyState("[]", 0));
    }

    [Fact]
    public void PopulatedSnapshotRetainsItsExactRawJsonFingerprint()
    {
        const string json = "[ {\"Id\":\"one\",\"LegalName\":\"\\u011eoymen\",\"Optional\":null} ]";
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json))),
            DatabaseEvidence.HashPartyState(json, 1));
    }

    [Fact]
    public void MissingOrUnexpectedScalarCannotMasqueradeAsAnEmptySnapshot()
    {
        Assert.Throws<InvalidOperationException>(() => DatabaseEvidence.HashPartyState(null, 0));
        Assert.Throws<InvalidOperationException>(() => DatabaseEvidence.HashPartyState(0, 0));
        Assert.Throws<InvalidOperationException>(() => DatabaseEvidence.HashPartyState(DBNull.Value, 1));
        Assert.Throws<InvalidOperationException>(() => DatabaseEvidence.HashPartyState(DBNull.Value, -1));
    }

    [Theory]
    [InlineData("[]", 1)]
    [InlineData("[{}]", 0)]
    [InlineData("[{}]", 2)]
    [InlineData("[]", -1)]
    [InlineData("{}", 0)]
    [InlineData("null", 0)]
    [InlineData("42", 0)]
    public void ArrayTypeAndIndependentPartyCountMustAgree(string scalar, int count)
    {
        Assert.Throws<InvalidOperationException>(() => DatabaseEvidence.HashPartyState(scalar, count));
    }

    [Fact]
    public void MalformedJsonIsNotAcceptedAsFingerprintEvidence()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => DatabaseEvidence.HashPartyState("[", 0));
    }
}
