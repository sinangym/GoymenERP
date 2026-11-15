using System.Reflection;
using GoymenERP.Api.DocumentsRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Documents.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>KLN-D50: the Cari party takes an existing document by link; its owner read is not asked for that answer.</summary>
public sealed class CounterpartyDocumentOwnerLinkPortTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public void The_party_takes_an_existing_document_by_link_KLN_D50()
    {
        var company = new CompanyContext();
        company.Set(Company);
        var user = new MutableUser { UserId = "SENTETIK-actor" };
        user.AllowedCompanyIds.Add(Company);

        var port = new CounterpartyDocumentOwnerLinkPort(company, user, UnusedReads.Create());

        Assert.Equal(DocumentOwnerLinkKindsV1.CounterpartyParty, port.Kind);
        Assert.True(port.AcceptsExistingDocumentLinks);
    }

    /// <summary>A Cari read that must not be called: the opt-in is a declaration, not a read.</summary>
    public class UnusedReads : DispatchProxy
    {
        public static ICounterpartyReadService Create() => DispatchProxy.Create<ICounterpartyReadService, UnusedReads>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("The opt-in must not read the Cari owner.");
    }

    private sealed class MutableUser : ICurrentUser
    {
        public string? UserId { get; set; }
        public bool IsAuthenticated => UserId is not null;
        public HashSet<Guid> AllowedCompanyIds { get; } = [];
        IReadOnlySet<Guid> ICurrentUser.AllowedCompanyIds => AllowedCompanyIds;
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
