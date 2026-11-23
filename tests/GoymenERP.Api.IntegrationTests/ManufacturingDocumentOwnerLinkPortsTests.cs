using GoymenERP.Api.DocumentsRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// B-26: the Manufacturing plan and order owner kinds answer through the module's own plan/order read only, for the exact
/// context company and actor, fail closed, and accept an existing document.
/// </summary>
public sealed class ManufacturingDocumentOwnerLinkPortsTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherCompany = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Record = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid OtherRecord = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    [Fact]
    public void Plan_and_order_kinds_are_distinct_and_accept_existing_documents()
    {
        var company = new MutableCompany();
        var user = new MutableUser();
        IDocumentOwnerLinkPortV1[] ports =
        [
            new ManufacturingPlanDocumentOwnerLinkPort(company, user, null!),
            new ManufacturingOrderDocumentOwnerLinkPort(company, user, null!),
        ];

        Assert.Equal(["manufacturing.plan", "manufacturing.order"], ports.Select(port => port.Kind));
        Assert.All(ports, port => Assert.True(port.AcceptsExistingDocumentLinks));
    }

    [Fact]
    public async Task The_very_record_of_the_context_company_is_readable()
    {
        var (port, _, _) = Probe((id, _) => Task.FromResult(new ManufacturingOwnerReadV1(ManufacturingApiStatus.Ok, id, Company)));

        Assert.Equal(DocumentOwnerAccessV1.Readable, await port.CheckReadableAsync(Record));
        Assert.Equal(Record, Assert.Single(port.Requests));
    }

    [Fact]
    public async Task An_ok_answer_for_another_record_or_company_or_without_identity_is_unavailable()
    {
        var (otherRecord, _, _) = Probe((_, _) =>
            Task.FromResult(new ManufacturingOwnerReadV1(ManufacturingApiStatus.Ok, OtherRecord, Company)));
        var (otherCompany, _, _) = Probe((id, _) =>
            Task.FromResult(new ManufacturingOwnerReadV1(ManufacturingApiStatus.Ok, id, OtherCompany)));
        var (noIdentity, _, _) = Probe((_, _) =>
            Task.FromResult(new ManufacturingOwnerReadV1(ManufacturingApiStatus.Ok, null, null)));

        Assert.Equal(DocumentOwnerAccessV1.Unavailable, await otherRecord.CheckReadableAsync(Record));
        Assert.Equal(DocumentOwnerAccessV1.Unavailable, await otherCompany.CheckReadableAsync(Record));
        Assert.Equal(DocumentOwnerAccessV1.Unavailable, await noIdentity.CheckReadableAsync(Record));
    }

    [Theory]
    [InlineData(ManufacturingApiStatus.NotFound, DocumentOwnerAccessV1.NotFound)]
    [InlineData(ManufacturingApiStatus.Denied, DocumentOwnerAccessV1.Denied)]
    [InlineData(ManufacturingApiStatus.Conflict, DocumentOwnerAccessV1.Conflict)]
    [InlineData(ManufacturingApiStatus.Unavailable, DocumentOwnerAccessV1.Unavailable)]
    [InlineData(ManufacturingApiStatus.Invalid, DocumentOwnerAccessV1.Unavailable)]
    [InlineData(ManufacturingApiStatus.Unprocessable, DocumentOwnerAccessV1.Unavailable)]
    [InlineData(ManufacturingApiStatus.Created, DocumentOwnerAccessV1.Unavailable)]
    public async Task Manufacturing_read_statuses_are_mapped_fail_closed(ManufacturingApiStatus status, DocumentOwnerAccessV1 expected)
    {
        var (port, _, _) = Probe((id, _) => Task.FromResult(new ManufacturingOwnerReadV1(status, id, Company)));

        Assert.Equal(expected, await port.CheckReadableAsync(Record));
    }

    [Fact]
    public async Task An_empty_id_or_an_unresolved_actor_or_company_is_denied_without_a_read()
    {
        static Task<ManufacturingOwnerReadV1> Ok(Guid id, CancellationToken token) =>
            Task.FromResult(new ManufacturingOwnerReadV1(ManufacturingApiStatus.Ok, id, Company));

        var (emptyId, _, _) = Probe(Ok);
        Assert.Equal(DocumentOwnerAccessV1.Denied, await emptyId.CheckReadableAsync(Guid.Empty));
        Assert.Empty(emptyId.Requests);

        var (anonymous, user, _) = Probe(Ok);
        user.UserId = null;
        Assert.Equal(DocumentOwnerAccessV1.Denied, await anonymous.CheckReadableAsync(Record));
        Assert.Empty(anonymous.Requests);

        var (unresolved, _, company) = Probe(Ok);
        company.Set(Guid.Empty);
        Assert.Equal(DocumentOwnerAccessV1.Denied, await unresolved.CheckReadableAsync(Record));
        Assert.Empty(unresolved.Requests);

        var (noAccess, accessUser, _) = Probe(Ok);
        accessUser.AllowedCompanyIds.Clear();
        Assert.Equal(DocumentOwnerAccessV1.Denied, await noAccess.CheckReadableAsync(Record));
        Assert.Empty(noAccess.Requests);
    }

    [Fact]
    public async Task Actor_or_company_drift_after_the_read_is_denied()
    {
        MutableUser? actor = null;
        var (actorPort, user, _) = Probe((id, _) =>
        {
            actor!.UserId = "SENTETIK-other";
            return Task.FromResult(new ManufacturingOwnerReadV1(ManufacturingApiStatus.Ok, id, Company));
        });
        actor = user;
        Assert.Equal(DocumentOwnerAccessV1.Denied, await actorPort.CheckReadableAsync(Record));

        MutableCompany? company = null;
        var (companyPort, companyUser, companyContext) = Probe((id, _) =>
        {
            company!.Set(OtherCompany);
            return Task.FromResult(new ManufacturingOwnerReadV1(ManufacturingApiStatus.Ok, id, Company));
        });
        company = companyContext;
        companyUser.AllowedCompanyIds.Add(OtherCompany);
        Assert.Equal(DocumentOwnerAccessV1.Denied, await companyPort.CheckReadableAsync(Record));
    }

    [Fact]
    public async Task A_provider_fault_is_unavailable_and_cancellation_is_not_converted()
    {
        var (fault, _, _) = Probe((_, _) => Task.FromException<ManufacturingOwnerReadV1>(new InvalidOperationException("SENTETIK")));
        Assert.Equal(DocumentOwnerAccessV1.Unavailable, await fault.CheckReadableAsync(Record));

        var (cancelled, _, _) = Probe((_, token) => Task.FromCanceled<ManufacturingOwnerReadV1>(token));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.CheckReadableAsync(Record, cancellation.Token));
    }

    private static (ProbePort Port, MutableUser User, MutableCompany Company) Probe(
        Func<Guid, CancellationToken, Task<ManufacturingOwnerReadV1>> answer)
    {
        var company = new MutableCompany();
        company.Set(Company);
        var user = new MutableUser { UserId = "SENTETIK-actor" };
        user.AllowedCompanyIds.Add(Company);
        return (new ProbePort(company, user, answer), user, company);
    }

    private sealed class ProbePort(
        ICompanyContext companyContext,
        ICurrentUser currentUser,
        Func<Guid, CancellationToken, Task<ManufacturingOwnerReadV1>> answer)
        : ManufacturingRecordDocumentOwnerLinkPort(companyContext, currentUser)
    {
        public List<Guid> Requests { get; } = [];

        public override string Kind => "sentetik.manufacturing-record";

        protected override Task<ManufacturingOwnerReadV1> ReadAsync(Guid ownerId, CancellationToken cancellationToken)
        {
            Requests.Add(ownerId);
            return answer(ownerId, cancellationToken);
        }
    }

    private sealed class MutableCompany : ICompanyContext
    {
        public bool IsResolved { get; private set; }
        public Guid CompanyId { get; private set; }
        public void Set(Guid companyId)
        {
            CompanyId = companyId;
            IsResolved = companyId != Guid.Empty;
        }
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
