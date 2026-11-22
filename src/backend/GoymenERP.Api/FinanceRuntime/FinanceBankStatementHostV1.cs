using GoymenERP.Api.DocumentsRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.FinanceRuntime;

/// <summary>
/// WP12 host composition: the statement import service (audited with the import permission and the roles that granted it),
/// the vault read adapter and, in <see cref="DocumentsRuntimeExtensions"/>, the owner-link port. Registration creates no
/// record and no grant.
/// </summary>
public static class FinanceBankStatementHostV1
{
    public static IServiceCollection AddFinanceBankStatementImportsV1(this IServiceCollection services)
    {
        services.AddScoped<IFinanceBankStatementDocumentReadV1, DocumentsFinanceBankStatementDocumentReadV1>();
        // FNS-D114: the cari card carries no IBAN list on this tree, so party IBAN matching stays off until the Counterparty
        // owner registers its own IFinancePartyIbanReadPortV1 (TryAdd keeps that registration when it exists).
        services.TryAddScoped<IFinancePartyIbanReadPortV1, UnavailableFinancePartyIbanReadPort>();
        services.AddScoped<IFinanceBankStatementImportServiceV1>(provider =>
        {
            var permissions = provider.GetRequiredService<FinancePermissionPort>();
            var correlation = provider.GetRequiredService<ICorrelationContext>();
            return new FinanceBankStatementImportServiceV1(provider.GetRequiredService<FinanceDbContext>(),
                provider.GetRequiredService<ICompanyContext>(), provider.GetRequiredService<ICurrentUser>(), permissions,
                provider.GetRequiredService<ISettingsBankCsvMappingProviderV1>(),
                provider.GetRequiredService<IFinanceBankStatementDocumentReadV1>(),
                provider.GetRequiredService<IFinancePartyIbanReadPortV1>(),
                provider.GetRequiredService<IAuditEventCollector>(),
                new FinanceAuditEnvelope(FinanceBankStatementPermissionsV1.Import,
                    permissions.RoleCodesOf(FinanceAction.ImportBankStatement),
                    correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent),
                provider.GetRequiredService<TimeProvider>(), provider.GetService<IAuditTransactionWriterV1>(),
                new FinanceAuditEnvelope(FinanceBankStatementPermissionsV1.Match,
                    permissions.RoleCodesOf(FinanceAction.MatchBankStatement),
                    correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent));
        });
        return services;
    }
}

/// <summary>
/// Reads one statement version through the vault's own authorized reads (list on the owner record, then the version
/// content), re-checking the request context after each await. The kind comes from the vault's server-classified content
/// type, never from a file name. Finance reads no vault table.
/// </summary>
public sealed class DocumentsFinanceBankStatementDocumentReadV1(IDocumentService documents, ICompanyContext company,
    ICurrentUser user) : IFinanceBankStatementDocumentReadV1
{
    public async Task<FinanceBankStatementDocumentAnswerV1> ReadAsync(FinanceContext context, Guid importId, Guid documentId,
        int version, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool Same() => context is not null && company.IsResolved && company.CompanyId == context.CompanyId
            && context.CompanyId != Guid.Empty && user.IsAuthenticated && user.UserId == context.ActorId
            && user.CanAccessCompany(context.CompanyId);
        FinanceBankStatementDocumentAnswerV1 Answer(FinanceBankStatementDocumentOutcomeV1 outcome) => new(outcome, null);
        if (!Same()) return Answer(FinanceBankStatementDocumentOutcomeV1.Denied);
        if (importId == Guid.Empty || documentId == Guid.Empty || version < 1)
            return Answer(FinanceBankStatementDocumentOutcomeV1.NotFound);
        try
        {
            var listed = await documents.ListAsync(DocumentOwnerLinkKindsV1.FinanceBankStatementImport, importId, cancellationToken);
            if (!Same()) return Answer(FinanceBankStatementDocumentOutcomeV1.Denied);
            var linked = listed.Items.SingleOrDefault(item => item.Id == documentId);
            if (linked is null || linked.CompanyId != context.CompanyId
                || linked.OwnerKind != DocumentOwnerLinkKindsV1.FinanceBankStatementImport || linked.OwnerId != importId
                || version > linked.VersionNo)
                return Answer(FinanceBankStatementDocumentOutcomeV1.NotFound);
            var content = await documents.GetVersionContentAsync(documentId, version, cancellationToken);
            if (!Same()) return Answer(FinanceBankStatementDocumentOutcomeV1.Denied);
            if (content.DocumentId != documentId) return Answer(FinanceBankStatementDocumentOutcomeV1.Unavailable);
            FinanceBankStatementFormatV1? format = content.ContentType switch
            {
                "text/csv" => FinanceBankStatementFormatV1.Csv,
                "text/plain" => FinanceBankStatementFormatV1.Mt940,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => FinanceBankStatementFormatV1.Xlsx,
                _ => null,
            };
            if (format is null) return Answer(FinanceBankStatementDocumentOutcomeV1.Unavailable);
            return new(FinanceBankStatementDocumentOutcomeV1.Found, new FinanceBankStatementDocumentV1(context.CompanyId,
                content.DocumentId, version, content.Sha256, format.Value, content.Content));
        }
        catch (DocumentServiceException exception)
        {
            if (!Same()) return Answer(FinanceBankStatementDocumentOutcomeV1.Denied);
            return Answer(exception.Status switch
            {
                401 or 403 => FinanceBankStatementDocumentOutcomeV1.Denied,
                404 => FinanceBankStatementDocumentOutcomeV1.NotFound,
                _ => FinanceBankStatementDocumentOutcomeV1.Unavailable,
            });
        }
    }
}
