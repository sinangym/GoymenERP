using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.DocumentsRuntime;

/// <summary>
/// ERP-000G / AT-065 host composition: the vault module with the audit interceptor (as every module), the owner link
/// ports the host adapts (the Cari party first), and the development object store and EICAR-only scanner ONLY in
/// Development/Testing. Everywhere else no object store and no scanner are registered, so uploads and downloads answer
/// 503 until the S3-compatible adapter and a real scanner are decided and registered (fail-closed, DEC-DOC-009).
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
