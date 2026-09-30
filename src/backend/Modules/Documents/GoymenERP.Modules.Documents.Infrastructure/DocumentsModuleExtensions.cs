using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Documents.Application;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Infrastructure.Persistence;
using GoymenERP.Modules.Documents.Infrastructure.Scanning;
using GoymenERP.Modules.Documents.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Documents.Infrastructure;

/// <summary>
/// The vault's module registration (ERP-000G core). The host calls it (the central writer's step) with the audit
/// interceptor through <paramref name="configureDbContext"/>, as for every module, and registers the owner-declared
/// link ports (<see cref="IDocumentOwnerLinkPortV1"/>; the Cari party first). Storage and scanning are fail-closed:
/// <list type="bullet">
/// <item><b>Object store</b>: the local file-system store only where the host allows development storage
/// (<paramref name="allowDevelopmentStorageAndScanner"/>, Development/Testing), under
/// <c>Documents:ObjectStore:RootPath</c> or a temp-directory default. Elsewhere no store is registered and uploads and
/// downloads answer 503 until the S3-compatible adapter (provider/bucket/credentials undecided) is registered.</item>
/// <item><b>Scanner</b>: only the development EICAR-signature stand-in, and only where development storage is allowed
/// (<c>Documents:Scanner:Mode</c> = <c>DevelopmentSignature</c>, the default there; <c>None</c> turns it off). Without a
/// scanner every version is stored NotScanned and is never downloadable (DEC-DOC-009 fail-closed).</item>
/// <item><b>Size bound</b>: <c>Documents:MaximumUploadBytes</c>, else ASSUMPTION-DOC-1 (20 MiB; OPEN-015).</item>
/// </list>
/// </summary>
public static class DocumentsModuleExtensions
{
    public const string ObjectStoreRootPathKey = "Documents:ObjectStore:RootPath";
    public const string ScannerModeKey = "Documents:Scanner:Mode";
    public const string MaximumUploadBytesKey = "Documents:MaximumUploadBytes";
    public const string DevelopmentScannerMode = "DevelopmentSignature";
    public const string NoScannerMode = "None";

    public static IServiceCollection AddDocumentsModule(this IServiceCollection services, IConfiguration configuration,
        Action<IServiceProvider, DbContextOptionsBuilder>? configureDbContext = null, bool allowDevelopmentStorageAndScanner = false)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var provider = configuration["DB_PROVIDER"] ?? "SqlServer";
        if (!string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Desteklenmeyen veritabanı sağlayıcısı: {provider}");
        }

        var connectionString = configuration["DB_CONNECTION_STRING"] ?? configuration.GetConnectionString("GoymenErp");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("SQL Server bağlantı dizesi DB_CONNECTION_STRING secret/env değeriyle tanımlanmalıdır.");
        }

        services.AddDbContext<DocumentsDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", DocumentsDbContext.Schema));
            configureDbContext?.Invoke(serviceProvider, options);
        });
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(ReadBindings(configuration));
        services.AddScoped<DocumentsStore>();
        services.AddScoped<IDocumentStore>(serviceProvider => serviceProvider.GetRequiredService<DocumentsStore>());

        var scannerMode = configuration[ScannerModeKey];
        if (!allowDevelopmentStorageAndScanner)
        {
            if (!string.IsNullOrWhiteSpace(configuration[ObjectStoreRootPathKey])
                || (!string.IsNullOrWhiteSpace(scannerMode) && !string.Equals(scannerMode, NoScannerMode, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "Geliştirme belge deposu ve tarayıcısı yalnız Development/Testing ortamında etkinleştirilebilir.");
            }
        }
        else
        {
            var root = configuration[ObjectStoreRootPathKey];
            var store = new FileSystemDocumentObjectStore(string.IsNullOrWhiteSpace(root)
                ? Path.Combine(Path.GetTempPath(), "goymen-erp-documents")
                : root);
            services.TryAddSingleton<IDocumentObjectStoreV1>(store);
            if (string.IsNullOrWhiteSpace(scannerMode) || string.Equals(scannerMode, DevelopmentScannerMode, StringComparison.Ordinal))
            {
                services.TryAddSingleton<IDocumentContentScannerV1, DevelopmentSignatureScanner>();
            }
            else if (!string.Equals(scannerMode, NoScannerMode, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Bilinmeyen belge tarayıcısı kipi: {scannerMode}");
            }
        }

        services.AddScoped<IDocumentService>(CreateService);
        return services;
    }

    // A named factory so that no registration statement of this module names the company context (architecture guard).
    private static DocumentService CreateService(IServiceProvider serviceProvider) => new DocumentService(
        serviceProvider.GetRequiredService<ICompanyContext>(),
        serviceProvider.GetRequiredService<ICurrentUser>(),
        serviceProvider.GetRequiredService<IErpAuthorityRevalidationService>(),
        serviceProvider.GetRequiredService<ICorrelationContext>(),
        serviceProvider.GetServices<IDocumentOwnerLinkPortV1>(),
        serviceProvider.GetRequiredService<IDocumentStore>(),
        serviceProvider.GetService<IDocumentObjectStoreV1>(),
        serviceProvider.GetService<IDocumentContentScannerV1>(),
        serviceProvider.GetRequiredService<DocumentsServiceBindings>(),
        serviceProvider.GetRequiredService<TimeProvider>());

    private static DocumentsServiceBindings ReadBindings(IConfiguration configuration)
    {
        var text = configuration[MaximumUploadBytesKey];
        if (string.IsNullOrWhiteSpace(text))
        {
            return DocumentsServiceBindings.Default;
        }

        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var bytes) && bytes > 0
            ? new DocumentsServiceBindings(bytes)
            : throw new InvalidOperationException($"{MaximumUploadBytesKey} pozitif bir bayt sayısı olmalıdır.");
    }

    /// <summary>Applies the vault's migrations. Its foreign keys need Organization's tables first.</summary>
    public static async Task InitializeDocumentsModuleAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
        if (dbContext.Database.IsRelational())
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
        else
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }
    }
}
