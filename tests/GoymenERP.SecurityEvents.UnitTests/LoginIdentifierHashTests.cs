using System.Security.Cryptography;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.BuildingBlocks.Security.SecretStore;

namespace GoymenERP.SecurityEvents.UnitTests;

/// <summary>
/// AI_KARAR_PAKETI_2026-11 E8: the login identifier hash key lives only in the secret store and nothing is hashed
/// until it is configured. Keys are generated at run time; identifiers are synthetic (SENTETIK).
/// </summary>
public sealed class LoginIdentifierHashTests
{
    private const string KeyId = "sentetik-e8-k1";
    private const string PolicyRef = "SENTETIK-E8-policy";
    private const string SecretName = "SENTETIK_E8_KEY";
    private const string Identifier = "sentetik-kullanici";

    private static ErpSecretV1 NewKey(int bytes) =>
        new(SecretName, Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)));

    [Fact]
    public void Nothing_is_hashed_while_the_key_id_the_policy_or_the_key_is_missing()
    {
        var key = NewKey(LoginIdentifierHasherV1.MinimumKeyBytes);

        Assert.Null(LoginIdentifierHasherV1.TryCreate(null, key, PolicyRef));
        Assert.Null(LoginIdentifierHasherV1.TryCreate(KeyId, key, null));
        Assert.Null(LoginIdentifierHasherV1.TryCreate(KeyId, key, " "));
        Assert.Null(LoginIdentifierHasherV1.TryCreate(KeyId, null, PolicyRef));
        Assert.Null(LoginIdentifierHasherV1.TryCreate("bad key id", key, PolicyRef));
    }

    [Fact]
    public void A_key_shorter_than_the_hash_size_or_not_base64_is_refused()
    {
        Assert.Null(LoginIdentifierHasherV1.TryCreate(KeyId, NewKey(LoginIdentifierHasherV1.MinimumKeyBytes - 1), PolicyRef));
        Assert.Null(LoginIdentifierHasherV1.TryCreate(KeyId, new ErpSecretV1(SecretName, "not base64 at all"), PolicyRef));
    }

    [Fact]
    public void The_hash_is_keyed_stable_for_one_key_and_carries_the_key_id_and_policy()
    {
        var key = NewKey(LoginIdentifierHasherV1.MinimumKeyBytes);
        using var first = LoginIdentifierHasherV1.TryCreate(KeyId, key, PolicyRef);
        using var again = LoginIdentifierHasherV1.TryCreate(KeyId, key, PolicyRef);
        using var rotated = LoginIdentifierHasherV1.TryCreate("sentetik-e8-k2", NewKey(LoginIdentifierHasherV1.MinimumKeyBytes), PolicyRef);
        Assert.NotNull(first);
        Assert.NotNull(again);
        Assert.NotNull(rotated);

        var one = first.Hash(Identifier);
        var two = again.Hash(Identifier);
        var other = rotated.Hash(Identifier);

        Assert.Equal(one, two);
        Assert.NotEqual(one.Value, other.Value);
        Assert.Equal(KeyId, one.KeyId);
        Assert.Equal(PolicyRef, one.PolicyRef);
        Assert.Equal(HMACSHA256.HashSizeInBytes * 2, one.Value.Length);
        Assert.DoesNotContain(Identifier, one.Value, StringComparison.Ordinal);
        Assert.NotEqual(one.Value, first.Hash("sentetik-diger").Value);
    }

    [Fact]
    public void The_key_never_appears_in_text_and_a_disposed_hasher_refuses()
    {
        var key = NewKey(LoginIdentifierHasherV1.MinimumKeyBytes);
        var hasher = LoginIdentifierHasherV1.TryCreate(KeyId, key, PolicyRef);
        Assert.NotNull(hasher);

        Assert.DoesNotContain(key.Reveal(), key.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(key.Reveal(), hasher.ToString(), StringComparison.Ordinal);

        hasher.Dispose();
        Assert.Throws<ObjectDisposedException>(() => hasher.Hash(Identifier));
    }

    [Theory]
    [InlineData("lower_case")]
    [InlineData("1STARTS_WITH_DIGIT")]
    [InlineData("HAS-DASH")]
    [InlineData("")]
    public void A_secret_reference_name_must_be_upper_case_letters_digits_and_underscore(string name)
    {
        Assert.False(ErpSecretNamesV1.IsValid(name));
        Assert.Throws<ArgumentException>(() => new ErpSecretV1(name, "value"));
        Assert.True(ErpSecretNamesV1.IsValid(SecretName));
    }
}
