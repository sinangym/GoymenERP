using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.FinanceRuntime;

public static class FinanceCashExchangeBookValueRuntime
{
    public static IServiceCollection AddFinanceCashExchangeBookValuesV1(this IServiceCollection services)
    {
        services.AddScoped<ICashExchangeBookValueServiceV1, CashExchangeBookValueServiceV1>();
        services.AddScoped<ICashExchangeBookValueEvidencePortV1, DocumentsCashExchangeBookValueEvidencePortV1>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDocumentOwnerLinkPortV1, FinanceCashExchangeDocumentOwnerLinkPortV1>());
        return services;
    }
}

/// <summary>The real vault read retains its own file/owner authority; it does not trust a user-entered hash.</summary>
public sealed class DocumentsCashExchangeBookValueEvidencePortV1(IDocumentService documents,
    ICompanyContext company, ICurrentUser user) : ICashExchangeBookValueEvidencePortV1
{
    public async Task<ReadOutcome> CheckAsync(FinanceContext context, CashExchangeBookValueEvidenceV1 evidence, CancellationToken ct)
    {
        if (!Same()) return ReadOutcome.Denied;
        if (evidence.DocumentId == Guid.Empty || evidence.VersionNo is < 1 or int.MaxValue) return ReadOutcome.NotFound;
        try
        {
            var result = await documents.ListVersionsAsync(evidence.DocumentId, 1,
                checked(evidence.VersionNo + 1).ToString(CultureInfo.InvariantCulture), ct);
            if (!Same()) return ReadOutcome.Denied;
            if (result.DocumentId != evidence.DocumentId || result.Versions is not { Count: 1 }) return ReadOutcome.NotFound;
            var version = result.Versions[0];
            return version.Id == evidence.DocumentId && version.CompanyId == context.CompanyId
                && version.VersionNo == evidence.VersionNo && version.Downloadable
                && string.Equals(version.Sha256, evidence.Sha256, StringComparison.OrdinalIgnoreCase)
                ? ReadOutcome.Found : ReadOutcome.NotFound;
        }
        catch (DocumentServiceException ex)
        { return !Same() ? ReadOutcome.Denied : ex.Status switch { 401 or 403 => ReadOutcome.Denied, 404 => ReadOutcome.NotFound, _ => ReadOutcome.Unavailable }; }
        catch (Exception) when (!ct.IsCancellationRequested) { return Same() ? ReadOutcome.Unavailable : ReadOutcome.Denied; }
        bool Same() => company.IsResolved && company.CompanyId == context.CompanyId && user.IsAuthenticated
            && user.UserId == context.ActorId && user.CanAccessCompany(context.CompanyId);
    }
}

public sealed class FinanceCashExchangeDocumentOwnerLinkPortV1(IServiceProvider services,
    ICompanyContext company, ICurrentUser user, ICorrelationContext correlation) : IDocumentOwnerLinkPortV1
{
    public string Kind => DocumentOwnerLinkKindsV1.FinanceCashExchange;
    public bool AcceptsExistingDocumentLinks => true;
    public async Task<DocumentOwnerAccessV1> CheckReadableAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        var companyId = company.IsResolved ? company.CompanyId : Guid.Empty;
        var actor = user.UserId;
        if (companyId == Guid.Empty || !user.IsAuthenticated || string.IsNullOrWhiteSpace(actor)) return DocumentOwnerAccessV1.Denied;
        try
        {
            // Resolve only on use: the write service's evidence adapter itself consumes IDocumentService.
            // Capturing the write service in this constructor would make the vault's owner-port collection cyclic.
            var source = services.GetRequiredService<ICashExchangeBookValueServiceV1>();
            var answer = await source.GetAsync(new(companyId, actor, correlation.CorrelationId, correlation.CausationId), ownerId, cancellationToken);
            if (!company.IsResolved || company.CompanyId != companyId || user.UserId != actor || !user.IsAuthenticated || !user.CanAccessCompany(companyId))
                return DocumentOwnerAccessV1.Denied;
            return answer.Outcome switch { ReadOutcome.Found when answer.Resource is { } value && value.CompanyId == companyId && value.Transfer.Id == ownerId => DocumentOwnerAccessV1.Readable,
                ReadOutcome.NotFound => DocumentOwnerAccessV1.NotFound, ReadOutcome.Denied => DocumentOwnerAccessV1.Denied, _ => DocumentOwnerAccessV1.Unavailable };
        }
        catch (OperationCanceledException) { throw; }
        catch { return DocumentOwnerAccessV1.Unavailable; }
    }
}
