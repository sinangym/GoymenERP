using System.Security.Cryptography;
using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CounterpartyCursorCodecTests
{
    private static readonly DateTimeOffset Now = new(
        2026,
        8,
        31,
        12,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void ProductionWithoutSecretBackedKey_IsUnavailable()
    {
        using var codec = CreateCodec(allowEphemeralKey: false);

        Assert.False(codec.IsAvailable);
        Assert.False(codec.TryUnprotect("payload.signature", out _));
    }

    [Fact]
    public void TestingProcessKey_AuthenticatesAndRejectsTamper()
    {
        using var codec = CreateCodec(allowEphemeralKey: true);
        var state = ValidState();

        var cursor = codec.Protect(state);

        Assert.True(codec.IsAvailable);
        Assert.True(codec.TryUnprotect(cursor, out var decoded));
        Assert.Equal(state, decoded);
        var tampered = cursor[..^1] + (cursor[^1] == 'A' ? 'B' : 'A');
        Assert.False(codec.TryUnprotect(tampered, out _));
    }

    [Fact]
    public void ProductionConfiguredKey_IsAvailable_AndExpiredCursorFailsClosed()
    {
        var time = new MutableTimeProvider(Now);
        var key = Convert.ToBase64String(
            Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        using var codec = CreateCodec(
            allowEphemeralKey: false,
            signingKey: key,
            timeProvider: time);
        var cursor = codec.Protect(ValidState());

        time.UtcNow = Now.AddMinutes(31);

        Assert.True(codec.IsAvailable);
        Assert.False(codec.TryUnprotect(cursor, out _));
    }

    [Fact]
    public void ConfiguredKeyShorterThan256Bits_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CreateCodec(
                allowEphemeralKey: false,
                signingKey: Convert.ToBase64String(new byte[31])));
    }

    [Fact]
    public void AuthenticatedUnknownOrMalformedStates_FailClosedWithoutThrowing()
    {
        var key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        using var codec = CreateCodec(
            allowEphemeralKey: false,
            signingKey: Convert.ToBase64String(key));
        var valid = ValidState();
        var malformedStates = new[]
        {
            valid with { Version = 2 },
            valid with { BindingHash = null! },
            valid with
            {
                Sort = CounterpartySort.UpdatedAt,
                PrimaryText = null,
                PrimaryUtcTicks = long.MaxValue,
            },
        };

        foreach (var malformed in malformedStates)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(
                malformed,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var cursor = Sign(payload, key);

            Assert.False(codec.TryUnprotect(cursor, out _));
        }
    }

    private static CounterpartyCursorState ValidState()
    {
        return new CounterpartyCursorState(
            1,
            new string('a', 64),
            CounterpartySort.Code,
            "CAR-000001",
            null,
            null,
            Guid.Parse("51000000-0000-0000-0000-000000000001"),
            Now,
            Now.AddMinutes(30));
    }

    private static string Sign(byte[] payload, byte[] key)
    {
        return $"{Base64Url(payload)}.{Base64Url(HMACSHA256.HashData(key, payload))}";
    }

    private static string Base64Url(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static CounterpartyCursorCodec CreateCodec(
        bool allowEphemeralKey,
        string? signingKey = null,
        TimeProvider? timeProvider = null)
    {
        return new CounterpartyCursorCodec(
            timeProvider ?? new MutableTimeProvider(Now),
            new CounterpartyCursorRuntimeOptions(allowEphemeralKey, signingKey));
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

}
