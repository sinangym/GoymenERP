using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Documents.Infrastructure;

public static partial class DocumentsModuleExtensions
{
    private static void AddDocumentBranding(IServiceCollection services)
    {
        services.AddScoped<DocumentBrandingService>(CreateBrandingService);
        services.AddScoped<ICompanyDocumentBrandingServiceV1>(provider => provider.GetRequiredService<DocumentBrandingService>());
        services.AddScoped<IDocumentPublishedTemplateReadPortV1>(provider => provider.GetRequiredService<DocumentBrandingService>());
        services.AddScoped<IDocumentOwnerLinkPortV1, CompanyBrandingDocumentOwnerLinkPort>();
    }

    private static DocumentBrandingService CreateBrandingService(IServiceProvider provider) => new(
        provider.GetRequiredService<DbContextOptions<DocumentsDbContext>>(), provider.GetRequiredService<ICompanyContext>(),
        provider.GetRequiredService<ICurrentUser>(), provider.GetRequiredService<IErpAuthorityRevalidationService>(),
        provider.GetRequiredService<ICorrelationContext>(), provider.GetRequiredService<IAuditEventCollector>(),
        provider.GetRequiredService<IAuditTransactionWriterV1>(), provider.GetRequiredService<IRefusedAttemptRecorderV1>(),
        provider.GetRequiredService<DocumentsServiceBindings>(), provider.GetRequiredService<TimeProvider>(),
        provider.GetService<IDocumentObjectStoreV1>(), provider.GetService<IDocumentBrandingAssetRenderValidatorV1>());
}

/// <summary>Upload still needs documents.files.upload; a branding permission grants no general vault capability.</summary>
internal sealed class CompanyBrandingDocumentOwnerLinkPort(ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority) : IDocumentOwnerLinkPortV1
{
    public string Kind => DocumentBrandingContractV1.CompanyOwnerKind;
    public async Task<DocumentOwnerAccessV1> CheckReadableAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || !company.IsResolved || company.CompanyId == Guid.Empty || company.CompanyId != ownerId
            || !user.CanAccessCompany(ownerId)) return DocumentOwnerAccessV1.NotFound;
        var actor = user.UserId;
        var read = await authority.RevalidatePermissionScopeAsync(DocumentBrandingPermissionsV1.Manage, ownerId, cancellationToken);
        if (!user.IsAuthenticated || user.UserId != actor || !company.IsResolved || company.CompanyId != ownerId
            || !user.CanAccessCompany(ownerId))
            return DocumentOwnerAccessV1.Denied;
        return read.IsGranted && read.IsCompanyWide ? DocumentOwnerAccessV1.Readable : DocumentOwnerAccessV1.Denied;
    }
}
