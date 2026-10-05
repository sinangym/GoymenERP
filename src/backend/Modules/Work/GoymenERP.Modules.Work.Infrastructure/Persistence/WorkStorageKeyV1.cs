using System.Buffers.Binary;
using System.Security.Cryptography;

namespace GoymenERP.Modules.Work.Infrastructure.Persistence;

internal interface IWorkStorageKeyDigestV1
{
    byte[] Compute(ReadOnlySpan<byte> value);
}

internal sealed class WorkSha256StorageKeyDigestV1 : IWorkStorageKeyDigestV1
{
    public static WorkSha256StorageKeyDigestV1 Instance { get; } = new();

    private WorkSha256StorageKeyDigestV1()
    {
    }

    public byte[] Compute(ReadOnlySpan<byte> value) => SHA256.HashData(value);
}

internal sealed record WorkStorageKeyV1(byte[] Utf16Be, byte[] Digest)
{
    public static WorkStorageKeyV1 Create(
        string value,
        IWorkStorageKeyDigestV1? digest = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
        {
            throw new ArgumentException("Work storage key cannot be empty.", nameof(value));
        }

        var bytes = GC.AllocateUninitializedArray<byte>(checked(value.Length * 2));
        for (var index = 0; index < value.Length; index++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(
                bytes.AsSpan(index * 2, 2),
                value[index]);
        }

        return new WorkStorageKeyV1(
            bytes,
            (digest ?? WorkSha256StorageKeyDigestV1.Instance).Compute(bytes));
    }

    public string DecodeAndVerify()
    {
        if (Utf16Be.Length == 0 || Utf16Be.Length % 2 != 0 || Digest.Length != 32)
        {
            throw new WorkStorageCorruptionException("Work storage key bytes are malformed.");
        }

        var actual = SHA256.HashData(Utf16Be);
        if (!CryptographicOperations.FixedTimeEquals(actual, Digest))
        {
            throw new WorkStorageCorruptionException("Work storage key digest does not match its bytes.");
        }

        var characters = GC.AllocateUninitializedArray<char>(Utf16Be.Length / 2);
        for (var index = 0; index < characters.Length; index++)
        {
            characters[index] = (char)BinaryPrimitives.ReadUInt16BigEndian(
                Utf16Be.AsSpan(index * 2, 2));
        }

        return new string(characters);
    }

    public void RequireSameRaw(WorkStorageKeyV1 other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (!Digest.AsSpan().SequenceEqual(other.Digest))
        {
            throw new WorkStorageCorruptionException("Compared Work storage digests differ.");
        }

        if (!Utf16Be.AsSpan().SequenceEqual(other.Utf16Be)
            || !string.Equals(DecodeAndVerify(), other.DecodeAndVerify(), StringComparison.Ordinal))
        {
            throw new WorkStorageKeyCollisionException();
        }
    }
}

public sealed class WorkStorageKeyCollisionException()
    : InvalidOperationException("work_storage_key_collision");

public sealed class WorkStorageCorruptionException(string message)
    : InvalidOperationException(message);
