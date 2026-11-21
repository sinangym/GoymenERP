using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Uses the registered real Settings owner, SQL and Audit writer. Only this direct-store fixture's
/// identity/authority are synthetic; the HTTP host's authentication and permission services stay real.
/// </summary>
internal sealed class PurchaseDocumentNumberSqlTestOwner : IAsyncDisposable
{
    private readonly AsyncServiceScope scope;
    internal ISettingsDocumentNumberSeriesV1 Numbers { get; }
    internal ISettingsDocumentNumberSeriesAdministrationV1 Administration { get; }

    internal PurchaseDocumentNumberSqlTestOwner(IServiceProvider host, Guid company, ICurrentUser actor,
        IErpAuthorityRevalidationService authority)
    {
        scope = host.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);
        var registered = scope.ServiceProvider.GetRequiredService<ISettingsDocumentNumberSeriesV1>();
        var owner = ActivatorUtilities.CreateInstance(scope.ServiceProvider, registered.GetType(), actor, authority);
        Numbers = (ISettingsDocumentNumberSeriesV1)owner;
        Administration = (ISettingsDocumentNumberSeriesAdministrationV1)owner;
    }

    public ValueTask DisposeAsync() => scope.DisposeAsync();

    internal async Task ConfigurePurchaseSeriesAsync(Guid company, string actor)
    {
        var existing = await Administration.GetFormatHistoryAsync(SettingsDocumentSeriesV1.PurchaseOrder);
        if (existing.Versions.Count != 0) return;
        var answer = await Administration.RecordFormatAsync(new(SettingsDocumentSeriesV1.PurchaseOrder, "0", "ALI", 6, "SENTETIK numara biçimi"),
            new SettingsCommandMetadataV1(company, actor, "SENTETIK-number-format", null, SourceChannels.Web, null, null, "SENTETIK-number-format"));
        Assert.Equal(204, answer.Answer.StatusCode);
    }

    internal sealed class FixedActor(Guid company, string actor) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => companyId == company;
    }

    internal sealed class DirectStoreAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(true, true, new HashSet<string> { "SENTETIK" }, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
    }
}
