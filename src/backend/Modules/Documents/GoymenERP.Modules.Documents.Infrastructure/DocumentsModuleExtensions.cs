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
/// <item><b>Production</b> (the host does not allow development storage): the server-directory store
/// (<see cref="ServerDirectoryDocumentObjectStore"/>, <c>Documents:ObjectStore:RootPath</c> — the company's own disk/NAS,
/// no cloud) and the Windows Defender scanner (<see cref="WindowsDefenderDocumentScanner"/>,
/// <c>Documents:Scanner:Defender:ExecutablePath</c>) are registered only TOGETHER and only when both are configured;
/// neither configured → nothing is registered and uploads and downloads answer 503; one without the other, the
/// development scanner, an invalid root or MpCmdRun path, or a non-Windows host → startup fails.</item>
/// <item><b>Development/Testing</b> (<paramref name="allowDevelopmentStorageAndScanner"/>): the local file-system
/// stand-in store under <c>Documents:ObjectStore:RootPath</c> or a temp-directory default, and the EICAR-only scanner
/// (<c>Documents:Scanner:Mode</c> = <c>DevelopmentSignature</c>, the default there; <c>None</c> turns it off;
/// <c>WindowsDefender</c> uses the real adapter).</item>
/// <item><b>Size bound</b>: <c>Documents:MaximumUploadBytes</c>, else 20 MiB (user decision 2026-09-30).</item>
/// </list>
/// Nothing registered here deletes a document object (legal retention 10 years; archive/legal hold later).
/// </summary>
public static class DocumentsModuleExtensions
{
    public const string ObjectStoreRootPathKey = "Documents:ObjectStore:RootPath";
    public const string ScannerModeKey = "Documents:Scanner:Mode";
    public const string MaximumUploadBytesKey = "Documents:MaximumUploadBytes";
    public const string DevelopmentScannerMode = "DevelopmentSignature";
    public const string NoScannerMode = "None";
    public const string WindowsDefenderScannerMode = "WindowsDefender";
    public const string DefenderExecutablePathKey = "Documents:Scanner:Defender:ExecutablePath";
    public const string DefenderTimeoutSecondsKey = "Documents:Scanner:Defender:TimeoutSeconds";
    public const string DefenderQuarantinePathKey = "Documents:Scanner:Defender:QuarantinePath";
    public const string DefenderMaximumConcurrentScansKey = "Documents:Scanner:Defender:MaximumConcurrentScans";

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
            RegisterProductionStorageAndScanner(services, configuration, scannerMode);
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
            else if (string.Equals(scannerMode, WindowsDefenderScannerMode, StringComparison.Ordinal))
            {
                services.TryAddSingleton<IDocumentContentScannerV1>(CreateDefenderScanner(configuration));
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

    // Production: the company's own server directory and Windows Defender, both or neither (fail-closed either way).
    private static void RegisterProductionStorageAndScanner(IServiceCollection services, IConfiguration configuration, string? scannerMode)
    {
        var defenderMode = string.Equals(scannerMode, WindowsDefenderScannerMode, StringComparison.Ordinal);
        var noScannerMode = string.Equals(scannerMode, NoScannerMode, StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(scannerMode) && !defenderMode && !noScannerMode)
        {
            throw new InvalidOperationException(
                $"Üretimde belge tarayıcısı yalnız {WindowsDefenderScannerMode} olabilir; geliştirme tarayıcısı yalnız Development/Testing ortamındadır.");
        }

        var root = configuration[ObjectStoreRootPathKey];
        var rootConfigured = !string.IsNullOrWhiteSpace(root);
        var defenderConfigured = !string.IsNullOrWhiteSpace(configuration[DefenderExecutablePathKey]);
        if (!rootConfigured && !defenderConfigured && !defenderMode)
        {
            // Not configured: no store, no scanner — uploads and downloads answer 503.
            return;
        }

        if (!rootConfigured || !defenderConfigured || noScannerMode)
        {
            throw new InvalidOperationException(
                $"Üretimde belge deposu ({ObjectStoreRootPathKey}) ve Windows Defender taraması ({DefenderExecutablePathKey}) birlikte yapılandırılmalıdır.");
        }

        var scanner = CreateDefenderScanner(configuration);
        var store = ServerDirectoryDocumentObjectStore.Open(root!);
        services.TryAddSingleton<IDocumentObjectStoreV1>(store);
        services.TryAddSingleton<IDocumentContentScannerV1>(scanner);
    }

    private static WindowsDefenderDocumentScanner CreateDefenderScanner(IConfiguration configuration)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException("Windows Defender taraması yalnız Windows sunucuda çalışır.");
        }

        var executablePath = configuration[DefenderExecutablePathKey];
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException($"{DefenderExecutablePathKey} (MpCmdRun.exe tam yolu) tanımlanmalıdır.");
        }

        var quarantine = configuration[DefenderQuarantinePathKey];
        var timeoutSeconds = ReadPositive(configuration, DefenderTimeoutSecondsKey, (long)WindowsDefenderScannerOptions.DefaultTimeout.TotalSeconds);
        var concurrency = ReadPositive(configuration, DefenderMaximumConcurrentScansKey, WindowsDefenderScannerOptions.DefaultMaximumConcurrentScans);
        var options = new WindowsDefenderScannerOptions(
            executablePath,
            TimeSpan.FromSeconds(Math.Min(timeoutSeconds, (long)TimeSpan.FromDays(1).TotalSeconds)),
            string.IsNullOrWhiteSpace(quarantine) ? Path.Combine(Path.GetTempPath(), "goymen-erp-document-scan") : quarantine,
            (int)Math.Min(concurrency, int.MaxValue));
        return WindowsDefenderDocumentScanner.Create(options, new SystemDocumentScanProcessRunner());
    }

    private static long ReadPositive(IConfiguration configuration, string key, long fallback)
    {
        var text = configuration[key];
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : throw new InvalidOperationException($"{key} pozitif bir tam sayı olmalıdır.");
    }

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
