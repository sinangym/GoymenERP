using System.Security.Cryptography;
using System.Text.RegularExpressions;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;

namespace GoymenERP.Modules.Documents.Infrastructure.Storage;

/// <summary>
/// The PRODUCTION implementation of <see cref="IDocumentObjectStoreV1"/> (user decision 2026-09-30: files stay on the
/// company's own server — a local disk or NAS/UNC path that the existing backups include; no cloud). Objects are
/// immutable and content-addressed under one configured root:
/// <list type="bullet">
/// <item><b>Root</b>: must be a fully qualified, EXISTING directory the service account can write to
/// (<see cref="Open"/> probes it); a missing or unwritable root fails startup rather than silently creating a folder that
/// no backup covers.</item>
/// <item><b>Keys</b>: only <c>companies/{32 hex}/sha256/{64 hex}</c>, resolved strictly inside the root; no directory
/// below the root nor the object itself may be a reparse point (symlink/junction), so nothing escapes the backed-up
/// tree.</item>
/// <item><b>Write once</b>: the bytes are written to a temporary file in the target directory with write-through and
/// flushed to the device (<c>FlushFileBuffers</c>/<c>fsync</c>), re-hashed, and then renamed into place WITHOUT
/// overwrite; the object is then marked read-only. An existing object is never opened for writing: holding the same
/// bytes it is a no-op, holding other bytes it is reported unavailable. A directory entry fsync is not available through
/// .NET; the rename is atomic on NTFS/SMB.</item>
/// <item><b>Read</b>: the bytes are re-hashed against the key's SHA-256; a mismatch (bit rot, tampering) answers
/// unavailable, never the wrong bytes.</item>
/// <item><b>No deletion</b>: nothing here deletes or overwrites an object (legal retention 10 years; archive and legal
/// hold later). Only its own temporary file of an unfinished write is removed.</item>
/// </list>
/// </summary>
public sealed partial class ServerDirectoryDocumentObjectStore : IDocumentObjectStoreV1
{
    private const string TemporarySuffix = ".partial";
    private readonly string root;
    private readonly string rootPrefix;

    private ServerDirectoryDocumentObjectStore(string fullRoot)
    {
        root = fullRoot;
        rootPrefix = Path.TrimEndingDirectorySeparator(fullRoot) + Path.DirectorySeparatorChar;
    }

    public string RootPath => root;

    /// <summary>
    /// Validates the configured root (fully qualified, existing directory, writable by this process) and opens the store.
    /// Throws <see cref="InvalidOperationException"/> otherwise, so a misconfigured server fails at startup.
    /// </summary>
    public static ServerDirectoryDocumentObjectStore Open(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Path.IsPathFullyQualified(rootPath))
        {
            throw new InvalidOperationException("Belge deposu kök yolu tam nitelikli bir yol olmalıdır (ör. D:\\GoymenERP\\Belgeler veya \\\\nas\\paylasim\\Belgeler).");
        }

        var fullRoot = Path.GetFullPath(rootPath);
        if (!Directory.Exists(fullRoot))
        {
            throw new InvalidOperationException($"Belge deposu kök klasörü bulunamadı: {fullRoot}. Klasör yedeklenen alanda önceden oluşturulmalıdır.");
        }

        var probe = Path.Combine(fullRoot, $".goymen-erp-write-probe-{Guid.NewGuid():N}{TemporarySuffix}");
        try
        {
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.WriteThrough);
            stream.WriteByte(0);
            stream.Flush(flushToDisk: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Belge deposu kök klasörüne yazılamıyor: {fullRoot}. Hizmet hesabının yazma izni olmalıdır.", exception);
        }
        finally
        {
            TryDeleteTemporary(probe);
        }

        return new ServerDirectoryDocumentObjectStore(fullRoot);
    }

    public async Task PutAsync(string key, ReadOnlyMemory<byte> content, string sha256, CancellationToken cancellationToken = default)
    {
        var path = Resolve(key);
        if (!DocumentContentHash.IsWellFormed(sha256) || !key.EndsWith(sha256, StringComparison.Ordinal)
            || !string.Equals(DocumentContentHash.Compute(content.Span), sha256, StringComparison.Ordinal))
        {
            throw new ArgumentException("Nesne içeriği anahtarındaki özetle eşleşmiyor.", nameof(content));
        }

        try
        {
            RequireNoReparsePoint(path);
            if (File.Exists(path))
            {
                await RequireSameAsync(path, sha256, cancellationToken);
                return;
            }

            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            RequireNoReparsePoint(path);
            var temporary = Path.Combine(directory, $".{Guid.NewGuid():N}{TemporarySuffix}");
            try
            {
                var options = new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.WriteThrough | FileOptions.Asynchronous,
                    PreallocationSize = content.Length,
                };
                await using (var stream = new FileStream(temporary, options))
                {
                    await stream.WriteAsync(content, cancellationToken);
                    stream.Flush(flushToDisk: true);
                }

                // What reached the disk is what the key names, before it becomes visible under the key.
                await RequireSameAsync(temporary, sha256, cancellationToken);
                try
                {
                    File.Move(temporary, path, overwrite: false);
                }
                catch (IOException) when (File.Exists(path))
                {
                    // A concurrent put of the same key won; it must hold the same bytes.
                    await RequireSameAsync(path, sha256, cancellationToken);
                    return;
                }

                MarkReadOnly(path);
            }
            finally
            {
                TryDeleteTemporary(temporary);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new DocumentObjectStoreUnavailableException("Belge nesne deposu yazılamadı.", exception);
        }
    }

    public async Task<byte[]?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = Resolve(key);
        byte[] content;
        try
        {
            RequireNoReparsePoint(path);
            if (!File.Exists(path))
            {
                return null;
            }

            content = await File.ReadAllBytesAsync(path, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new DocumentObjectStoreUnavailableException("Belge nesne deposu okunamadı.", exception);
        }

        if (!string.Equals(DocumentContentHash.Compute(content), key[^64..], StringComparison.Ordinal))
        {
            throw new DocumentObjectStoreUnavailableException("Depodaki nesne anahtarındaki özetle eşleşmiyor (bütünlük hatası); içerik sunulmadı.");
        }

        return content;
    }

    private static async Task RequireSameAsync(string path, string sha256, CancellationToken cancellationToken)
    {
        string actual;
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
        }

        if (!string.Equals(actual, sha256, StringComparison.Ordinal))
        {
            throw new DocumentObjectStoreUnavailableException("Nesne anahtarı başka bir içerik taşıyor; hiçbir şey değiştirilmedi.");
        }
    }

    /// <summary>Every existing entry between the root (exclusive) and the object (inclusive) must be a plain entry.</summary>
    private void RequireNoReparsePoint(string path)
    {
        for (var current = path; current.Length > rootPrefix.Length; current = Path.GetDirectoryName(current)!)
        {
            if ((File.Exists(current) || Directory.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new DocumentObjectStoreUnavailableException("Belge deposunda bağlantı (symlink/junction) kabul edilmez; hiçbir şey okunmadı veya yazılmadı.");
            }
        }
    }

    private static void MarkReadOnly(string path)
    {
        try
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Defence in depth only: the object is already durable and is never opened for writing.
        }
    }

    private static void TryDeleteTemporary(string temporary)
    {
        try
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An unfinished temporary file is invisible to reads (its name is outside the key shape).
        }
    }

    private string Resolve(string key)
    {
        if (key is null || !KeyPattern().IsMatch(key))
        {
            throw new ArgumentException("Nesne anahtarı companies/{şirket}/sha256/{özet} biçiminde olmalıdır.", nameof(key));
        }

        var path = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(rootPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("Nesne anahtarı deponun dışına çıkamaz.", nameof(key));
        }

        return path;
    }

    [GeneratedRegex("^companies/[0-9a-f]{32}/sha256/[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
