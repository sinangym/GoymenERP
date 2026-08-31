using System.Security.Cryptography;
using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

internal sealed class CounterpartyCursorCodec : ICounterpartyCursorCodec, IDisposable
{
    private const int SignatureLength = 32;
    private const int MaximumCursorLength = 4096;
    private static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MaximumClockSkew = TimeSpan.FromMinutes(1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TimeProvider _timeProvider;
    private readonly byte[] _key;

    public CounterpartyCursorCodec(
        TimeProvider timeProvider,
        CounterpartyCursorRuntimeOptions runtimeOptions)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        ArgumentNullException.ThrowIfNull(runtimeOptions);
        var configuredKey = ReadConfiguredKey(runtimeOptions.SigningKey);
        if (configuredKey is not null)
        {
            _key = configuredKey;
            IsAvailable = true;
        }
        else if (runtimeOptions.AllowEphemeralKey)
        {
            _key = RandomNumberGenerator.GetBytes(32);
            IsAvailable = true;
        }
        else
        {
            _key = [];
            IsAvailable = false;
        }
    }

    public bool IsAvailable { get; }

    public string Protect(CounterpartyCursorState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!IsAvailable
            || !HasValidShape(state, _timeProvider.GetUtcNow(), allowExpired: false))
        {
            throw new InvalidOperationException("Cari cursor durumu geçersizdir.");
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        var signature = HMACSHA256.HashData(_key, payload);
        return $"{Base64UrlEncode(payload)}.{Base64UrlEncode(signature)}";
    }

    public bool TryUnprotect(string cursor, out CounterpartyCursorState state)
    {
        state = null!;
        if (!IsAvailable
            || string.IsNullOrWhiteSpace(cursor)
            || cursor.Length > MaximumCursorLength
            || !string.Equals(cursor, cursor.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        var separator = cursor.IndexOf('.');
        if (separator <= 0
            || separator != cursor.LastIndexOf('.')
            || separator == cursor.Length - 1
            || !TryBase64UrlDecode(cursor[..separator], out var payload)
            || !TryBase64UrlDecode(cursor[(separator + 1)..], out var signature)
            || signature.Length != SignatureLength)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(_key, payload);
        if (!CryptographicOperations.FixedTimeEquals(expected, signature))
        {
            return false;
        }

        try
        {
            var decoded = JsonSerializer.Deserialize<CounterpartyCursorState>(
                payload,
                JsonOptions);
            if (decoded is null
                || !HasValidShape(
                    decoded,
                    _timeProvider.GetUtcNow(),
                    allowExpired: false))
            {
                return false;
            }

            state = decoded;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_key);
    }

    private static bool HasValidShape(
        CounterpartyCursorState state,
        DateTimeOffset now,
        bool allowExpired)
    {
        if (state.Version != 1
            || string.IsNullOrEmpty(state.BindingHash)
            || state.BindingHash.Length != 64
            || state.BindingHash.Any(character =>
                !(character is >= '0' and <= '9' or >= 'a' and <= 'f'))
            || !Enum.IsDefined(state.Sort)
            || state.PartyId == Guid.Empty
            || state.ExpiresAtUtc <= state.IssuedAtUtc
            || state.ExpiresAtUtc - state.IssuedAtUtc > MaximumAge
            || state.IssuedAtUtc > now + MaximumClockSkew
            || !allowExpired && state.ExpiresAtUtc <= now)
        {
            return false;
        }

        return state.Sort switch
        {
            CounterpartySort.Code =>
                !string.IsNullOrEmpty(state.PrimaryText)
                && state.SecondaryText is null
                && state.PrimaryUtcTicks is null,
            CounterpartySort.LegalName =>
                !string.IsNullOrEmpty(state.PrimaryText)
                && !string.IsNullOrEmpty(state.SecondaryText)
                && state.PrimaryUtcTicks is null,
            CounterpartySort.UpdatedAt =>
                state.PrimaryText is null
                && state.SecondaryText is null
                && state.PrimaryUtcTicks is >= 0
                && state.PrimaryUtcTicks <= DateTimeOffset.MaxValue.UtcTicks,
            _ => false,
        };
    }

    private static byte[]? ReadConfiguredKey(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(configured);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "Counterparty:CursorSigningKey geçerli base64 olmalıdır.",
                exception);
        }

        if (key.Length < 32)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidOperationException(
                "Counterparty:CursorSigningKey en az 32 byte olmalıdır.");
        }

        return key;
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool TryBase64UrlDecode(string value, out byte[] bytes)
    {
        bytes = [];
        if (value.Length == 0
            || value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            return false;
        }

        var padding = value.Length % 4;
        if (padding == 1)
        {
            return false;
        }

        var canonical = value.Replace('-', '+').Replace('_', '/');
        canonical += padding switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty,
        };

        try
        {
            bytes = Convert.FromBase64String(canonical);
            return string.Equals(Base64UrlEncode(bytes), value, StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

internal sealed record CounterpartyCursorRuntimeOptions(
    bool AllowEphemeralKey,
    string? SigningKey);
