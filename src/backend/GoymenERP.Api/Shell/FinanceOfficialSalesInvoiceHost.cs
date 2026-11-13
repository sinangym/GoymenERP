using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Shell;

public static class FinanceOfficialSalesInvoiceHostExtensions
{
    public static IServiceCollection AddFinanceOfficialSalesInvoiceHost(this IServiceCollection services)
    {
        services.AddScoped<FinanceOfficialSalesInvoiceAuthority>();
        services.AddScoped<IFinanceOfficialSalesInvoiceAuthorityV1>(provider =>
            provider.GetRequiredService<FinanceOfficialSalesInvoiceAuthority>());
        services.AddScoped<IFinanceOfficialSalesInvoiceDocumentPortV1, FinanceOfficialSalesInvoiceDocumentPort>();
        services.AddScoped<IFinanceOfficialSalesInvoiceServiceV1>(CreateService);
        return services;
    }

    private static FinanceOfficialSalesInvoiceServiceV1 CreateService(IServiceProvider provider)
    {
        var authority = provider.GetRequiredService<FinanceOfficialSalesInvoiceAuthority>();
        var correlation = provider.GetRequiredService<ICorrelationContext>();
        return new FinanceOfficialSalesInvoiceServiceV1(
            provider.GetRequiredService<FinanceDbContext>(),
            provider.GetRequiredService<ICompanyContext>(),
            provider.GetRequiredService<ICurrentUser>(),
            authority,
            provider.GetRequiredService<IFinanceOfficialSalesInvoiceDocumentPortV1>(),
            provider.GetRequiredService<ISettingsMoneyCandidateReader>(),
            provider.GetRequiredService<IAuditEventCollector>(),
            new FinanceOfficialSalesInvoiceAuditEnvelopesV1(
                Envelope(FinanceOfficialSalesInvoicePermissionNamesV1.Record,
                    authority.RoleCodesOf(FinanceOfficialSalesInvoiceAuthorityActionV1.Record), correlation),
                Envelope(FinanceOfficialSalesInvoicePermissionNamesV1.Approve,
                    authority.RoleCodesOf(FinanceOfficialSalesInvoiceAuthorityActionV1.Approve), correlation)),
            provider.GetRequiredService<TimeProvider>());
    }

    private static FinanceAuditEnvelope Envelope(string permission,
        IReadOnlyCollection<string> roleCodes, ICorrelationContext correlation) =>
        new(permission, roleCodes, correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent);
}

/// <summary>
/// Fresh company-wide authority for the official sales invoice record (FNS-D17…FNS-D21, FNS-D26). Read is not a third
/// permission: a holder of Record or Approve may read. An unregistered central name answers Unavailable.
/// </summary>
public sealed class FinanceOfficialSalesInvoiceAuthority(
    IErpAuthorityRevalidationService authority,
    ICompanyContext companyContext,
    ICurrentUser currentUser) : IFinanceOfficialSalesInvoiceAuthorityV1
{
    private readonly Dictionary<FinanceOfficialSalesInvoiceAuthorityActionV1, IReadOnlyCollection<string>> roleCodes = [];

    public IReadOnlyCollection<string> RoleCodesOf(FinanceOfficialSalesInvoiceAuthorityActionV1 action) =>
        new RoleCodesView(this, action);

    public async Task<PermissionDecision> CheckAsync(FinanceContext context,
        FinanceOfficialSalesInvoiceAuthorityActionV1 action, Guid invoiceId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context)) return PermissionDecision.Denied;
        if (action != FinanceOfficialSalesInvoiceAuthorityActionV1.Read && invoiceId == Guid.Empty)
        {
            return PermissionDecision.Denied;
        }

        return action switch
        {
            FinanceOfficialSalesInvoiceAuthorityActionV1.Record =>
                await CheckOneAsync(context, action, FinanceOfficialSalesInvoicePermissionNamesV1.Record,
                    cancellationToken),
            FinanceOfficialSalesInvoiceAuthorityActionV1.Approve =>
                await CheckOneAsync(context, action, FinanceOfficialSalesInvoicePermissionNamesV1.Approve,
                    cancellationToken),
            FinanceOfficialSalesInvoiceAuthorityActionV1.Read => await CheckReadAsync(context, cancellationToken),
            _ => PermissionDecision.Unavailable,
        };
    }

    private async Task<PermissionDecision> CheckReadAsync(FinanceContext context, CancellationToken cancellationToken)
    {
        var record = await CheckOneAsync(context, FinanceOfficialSalesInvoiceAuthorityActionV1.Record,
            FinanceOfficialSalesInvoicePermissionNamesV1.Record, cancellationToken);
        if (record == PermissionDecision.Allowed) return record;
        var approve = await CheckOneAsync(context, FinanceOfficialSalesInvoiceAuthorityActionV1.Approve,
            FinanceOfficialSalesInvoicePermissionNamesV1.Approve, cancellationToken);
        if (approve == PermissionDecision.Allowed) return approve;
        return record == PermissionDecision.Unavailable || approve == PermissionDecision.Unavailable
            ? PermissionDecision.Unavailable
            : PermissionDecision.Denied;
    }

    private async Task<PermissionDecision> CheckOneAsync(FinanceContext context,
        FinanceOfficialSalesInvoiceAuthorityActionV1 action, string permission, CancellationToken cancellationToken)
    {
        if (!ErpPermissions.IsKnown(permission)) return PermissionDecision.Unavailable;
        var scope = await authority.RevalidatePermissionScopeAsync(permission, context.CompanyId, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide || !SameContext(context)) return PermissionDecision.Denied;
        roleCodes[action] = [.. scope.RoleCodes.Order(StringComparer.Ordinal)];
        return PermissionDecision.Allowed;
    }

    private IReadOnlyCollection<string> Current(FinanceOfficialSalesInvoiceAuthorityActionV1 action) =>
        roleCodes.TryGetValue(action, out var codes) ? codes : [];

    private bool SameContext(FinanceContext context) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
        && companyContext.CompanyId == context.CompanyId && currentUser.CanAccessCompany(context.CompanyId);

    private sealed class RoleCodesView(FinanceOfficialSalesInvoiceAuthority owner,
        FinanceOfficialSalesInvoiceAuthorityActionV1 action) : IReadOnlyCollection<string>
    {
        public int Count => owner.Current(action).Count;
        public IEnumerator<string> GetEnumerator() => owner.Current(action).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

/// <summary>
/// FNS-D18 adapter over the Documents vault, answered with the actor's own Documents read authority (Finance never
/// reads document tables). Documents has no Finance owner kind yet, so the PDF must be a clean, downloadable
/// <c>application/pdf</c> linked to the invoice's customer party (<c>counterparty.party</c>). Anything else, or any
/// failure, answers a non-Accepted outcome (fail closed).
/// </summary>
public sealed class FinanceOfficialSalesInvoiceDocumentPort(IDocumentService documents)
    : IFinanceOfficialSalesInvoiceDocumentPortV1
{
    internal const string CustomerOwnerKind = "counterparty.party";
    internal const string PdfContentType = "application/pdf";

    public async Task<FinanceOfficialSalesInvoicePdfCheckV1> CheckAsync(FinanceContext context, Guid documentId,
        Guid customerPartyId, int? versionNo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (documentId == Guid.Empty || customerPartyId == Guid.Empty || versionNo is < 1)
        {
            return Refused(FinanceOfficialSalesInvoicePdfOutcomeV1.NotFound);
        }

        DocumentVersionHistoryResponse history;
        try
        {
            var cursor = versionNo is { } pinned
                ? checked(pinned + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : null;
            history = await documents.ListVersionsAsync(documentId, 1, cursor, cancellationToken);
        }
        catch (DocumentServiceException exception)
        {
            return Refused(exception.Status switch
            {
                401 or 403 => FinanceOfficialSalesInvoicePdfOutcomeV1.Denied,
                404 => FinanceOfficialSalesInvoicePdfOutcomeV1.NotFound,
                _ => FinanceOfficialSalesInvoicePdfOutcomeV1.Unavailable,
            });
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Refused(FinanceOfficialSalesInvoicePdfOutcomeV1.Unavailable);
        }

        if (history is null || history.DocumentId != documentId || history.Versions is not { Count: 1 } versions)
        {
            return Refused(versionNo is null
                ? FinanceOfficialSalesInvoicePdfOutcomeV1.Unavailable
                : FinanceOfficialSalesInvoicePdfOutcomeV1.NotFound);
        }

        var view = versions[0];
        if (view.Id != documentId || view.CompanyId != context.CompanyId
            || versionNo is { } expected && view.VersionNo != expected)
        {
            return Refused(FinanceOfficialSalesInvoicePdfOutcomeV1.Unavailable);
        }
        if (!string.Equals(view.OwnerKind, CustomerOwnerKind, StringComparison.Ordinal)
            || view.OwnerId != customerPartyId)
        {
            return Refused(FinanceOfficialSalesInvoicePdfOutcomeV1.OwnerMismatch);
        }
        if (!string.Equals(view.ContentType, PdfContentType, StringComparison.Ordinal))
        {
            return Refused(FinanceOfficialSalesInvoicePdfOutcomeV1.NotPdf);
        }
        if (!view.Downloadable)
        {
            return Refused(FinanceOfficialSalesInvoicePdfOutcomeV1.NotDownloadable);
        }
        return new(FinanceOfficialSalesInvoicePdfOutcomeV1.Accepted, view.VersionNo, view.Sha256);
    }

    private static FinanceOfficialSalesInvoicePdfCheckV1 Refused(FinanceOfficialSalesInvoicePdfOutcomeV1 outcome) =>
        new(outcome, null, null);
}
