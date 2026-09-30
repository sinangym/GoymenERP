using System.Text.RegularExpressions;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;

namespace GoymenERP.Modules.Documents.Infrastructure.Storage;

/// <summary>
/// The development/test implementation of <see cref="IDocumentObjectStoreV1"/>: immutable, content-addressed files
/// under one root directory, no cloud credentials. A key is accepted only in its exact
/// <c>companies/{32 hex}/sha256/{64 hex}</c> shape and resolves strictly inside the root. A put writes a temporary file
/// in the target directory and moves it into place WITHOUT overwrite; an existing object is kept when it holds the same
/// bytes (its hash re-computed) and reported as unavailable when it does not. Nothing is ever deleted. Registered only
/// where the host allows the development storage (Development/Testing); production uses
/// <see cref="ServerDirectoryDocumentObjectStore"/> (validated root, write-through, read-only objects, hash-checked reads).
/// </summary>
public sealed partial class FileSystemDocumentObjectStore : IDocumentObjectStoreV1
{
    private readonly string root;

    public FileSystemDocumentObjectStore(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        root = Path.GetFullPath(rootPath);
    }

    public string RootPath => root;

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
            if (File.Exists(path))
            {
                await RequireSameAsync(path, sha256, cancellationToken);
                return;
            }

            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await stream.WriteAsync(content, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                try
                {
                    File.Move(temporary, path, overwrite: false);
                }
                catch (IOException) when (File.Exists(path))
                {
                    // A concurrent put of the same key won; it must hold the same bytes.
                    await RequireSameAsync(path, sha256, cancellationToken);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
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
        try
        {
            return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new DocumentObjectStoreUnavailableException("Belge nesne deposu okunamadı.", exception);
        }
    }

    private static async Task RequireSameAsync(string path, string sha256, CancellationToken cancellationToken)
    {
        var existing = await File.ReadAllBytesAsync(path, cancellationToken);
        if (!string.Equals(DocumentContentHash.Compute(existing), sha256, StringComparison.Ordinal))
        {
            throw new DocumentObjectStoreUnavailableException("Nesne anahtarı başka bir içerik taşıyor; hiçbir şey değiştirilmedi.");
        }
    }

    private string Resolve(string key)
    {
        if (key is null || !KeyPattern().IsMatch(key))
        {
            throw new ArgumentException("Nesne anahtarı companies/{şirket}/sha256/{özet} biçiminde olmalıdır.", nameof(key));
        }

        var path = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Nesne anahtarı deponun dışına çıkamaz.", nameof(key));
        }

        return path;
    }

    [GeneratedRegex("^companies/[0-9a-f]{32}/sha256/[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
