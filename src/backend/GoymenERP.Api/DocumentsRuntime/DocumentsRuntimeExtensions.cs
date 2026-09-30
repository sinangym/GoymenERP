using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.DocumentsRuntime;

/// <summary>
/// ERP-000G / AT-065 host composition: the vault module with the audit interceptor (as every module), the owner link
/// ports the host adapts (the Cari party first), and the development object store and EICAR-only scanner ONLY in
/// Development/Testing. Everywhere else the module registers the server-directory store and the Windows Defender
/// scanner only when BOTH are configured (Documents:ObjectStore:RootPath, Documents:Scanner:Defender:ExecutablePath);
/// unconfigured, uploads and downloads answer 503; a partial or invalid configuration fails startup (DEC-DOC-009).
/// </summary>
public static class DocumentsRuntimeExtensions
{
    public static IServiceCollection AddDocumentsRuntime(this IServiceCollection services, IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        services.AddScoped<IDocumentOwnerLinkPortV1, CounterpartyDocumentOwnerLinkPort>();
        return services.AddDocumentsModule(
            configuration,
            (provider, options) => options.AddInterceptors(provider.GetRequiredService<AuditSaveChangesInterceptor>()),
            allowDevelopmentStorageAndScanner: environment.IsDevelopment() || environment.IsEnvironment("Testing"));
    }
}
