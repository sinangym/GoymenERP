using GoymenERP.Api.DocumentsRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// #66 6860586175: the Document Vault owner kind <c>finance.official-sales-invoice</c> answers through Finance's
/// authorized owner read only, for the exact context company and actor, and fails closed.
/// </summary>
public sealed class FinanceDocumentOwnerLinkPortTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Invoice = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    [Fact]
    public async Task A_reserved_id_of_the_context_company_is_readable_through_the_finance_read()
    {
        var (port, _, _, read) = Port((_, _, _) => Task.FromResult(ReadOutcome.Found));

        Assert.Equal(DocumentOwnerLinkKindsV1.FinanceOfficialSalesInvoice, port.Kind);
        Assert.Equal("finance.official-sales-invoice", port.Kind);
        Assert.Equal(DocumentOwnerAccessV1.Readable, await port.CheckReadableAsync(Invoice));
        var (context, invoiceId) = Assert.Single(read.Requests);
        Assert.Equal(Invoice, invoiceId);
        Assert.Equal(new FinanceContext(Company, "SENTETIK-actor", "SENTETIK-correlation", "SENTETIK-causation"),
            context);
    }

    [Theory]
    [InlineData(ReadOutcome.NotFound, DocumentOwnerAccessV1.NotFound)]
    [InlineData(ReadOutcome.Denied, DocumentOwnerAccessV1.Denied)]
    [InlineData(ReadOutcome.Unavailable, DocumentOwnerAccessV1.Unavailable)]
    public async Task Finance_read_outcomes_are_mapped_fail_closed(ReadOutcome outcome, DocumentOwnerAccessV1 expected)
    {
        var (port, _, _, _) = Port((_, _, _) => Task.FromResult(outcome));

        Assert.Equal(expected, await port.CheckReadableAsync(Invoice));
    }

    [Fact]
    public async Task An_empty_id_or_an_unresolved_actor_or_company_is_denied_without_a_finance_read()
    {
        var (emptyId, _, _, emptyRead) = Port((_, _, _) => Task.FromResult(ReadOutcome.Found));
        Assert.Equal(DocumentOwnerAccessV1.Denied, await emptyId.CheckReadableAsync(Guid.Empty));
        Assert.Empty(emptyRead.Requests);

        var (anonymous, user, _, anonymousRead) = Port((_, _, _) => Task.FromResult(ReadOutcome.Found));
        user.UserId = null;
        Assert.Equal(DocumentOwnerAccessV1.Denied, await anonymous.CheckReadableAsync(Invoice));
        Assert.Empty(anonymousRead.Requests);

        var (unresolved, _, company, unresolvedRead) = Port((_, _, _) => Task.FromResult(ReadOutcome.Found));
        company.Set(Guid.Empty);
        Assert.Equal(DocumentOwnerAccessV1.Denied, await unresolved.CheckReadableAsync(Invoice));
        Assert.Empty(unresolvedRead.Requests);

        var (noAccess, accessUser, _, noAccessRead) = Port((_, _, _) => Task.FromResult(ReadOutcome.Found));
        accessUser.AllowedCompanyIds.Clear();
        Assert.Equal(DocumentOwnerAccessV1.Denied, await noAccess.CheckReadableAsync(Invoice));
        Assert.Empty(noAccessRead.Requests);
    }

    [Fact]
    public async Task Actor_company_or_access_drift_after_the_finance_read_is_denied()
    {
        MutableUser? actor = null;
        var (actorPort, user, _, _) = Port((_, _, _) =>
        {
            actor!.UserId = "SENTETIK-other";
            return Task.FromResult(ReadOutcome.Found);
        });
        actor = user;
        Assert.Equal(DocumentOwnerAccessV1.Denied, await actorPort.CheckReadableAsync(Invoice));

        MutableCompany? company = null;
        var (companyPort, companyUser, companyContext, _) = Port((_, _, _) =>
        {
            company!.Set(Guid.Parse("22222222-2222-4222-8222-222222222222"));
            return Task.FromResult(ReadOutcome.Found);
        });
        company = companyContext;
        companyUser.AllowedCompanyIds.Add(Guid.Parse("22222222-2222-4222-8222-222222222222"));
        Assert.Equal(DocumentOwnerAccessV1.Denied, await companyPort.CheckReadableAsync(Invoice));

        MutableUser? companyActor = null;
        var (accessPort, accessUser, _, _) = Port((_, _, _) =>
        {
            companyActor!.AllowedCompanyIds.Clear();
            return Task.FromResult(ReadOutcome.Found);
        });
        companyActor = accessUser;
        Assert.Equal(DocumentOwnerAccessV1.Denied, await accessPort.CheckReadableAsync(Invoice));
    }

    [Fact]
    public async Task A_provider_fault_is_unavailable_and_cancellation_is_not_converted()
    {
        var (fault, _, _, _) = Port((_, _, _) => Task.FromException<ReadOutcome>(new InvalidOperationException("SENTETIK")));
        Assert.Equal(DocumentOwnerAccessV1.Unavailable, await fault.CheckReadableAsync(Invoice));

        var (cancelled, _, _, _) = Port((_, _, token) => Task.FromCanceled<ReadOutcome>(token));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cancelled.CheckReadableAsync(Invoice, cancellation.Token));
    }

    private static (FinanceDocumentOwnerLinkPort Port, MutableUser User, MutableCompany Company, StubOwnerRead Read)
        Port(Func<FinanceContext, Guid, CancellationToken, Task<ReadOutcome>> answer)
    {
        var company = new MutableCompany();
        company.Set(Company);
        var user = new MutableUser { UserId = "SENTETIK-actor" };
        user.AllowedCompanyIds.Add(Company);
        var correlation = new FixedCorrelation("SENTETIK-correlation", "SENTETIK-causation");
        var read = new StubOwnerRead(answer);
        return (new FinanceDocumentOwnerLinkPort(company, user, correlation, read), user, company, read);
    }

    private sealed class StubOwnerRead(Func<FinanceContext, Guid, CancellationToken, Task<ReadOutcome>> answer)
        : IFinanceOfficialSalesInvoiceOwnerReadV1
    {
        public List<(FinanceContext Context, Guid InvoiceId)> Requests { get; } = [];

        public Task<ReadOutcome> CheckReadableAsync(FinanceContext context, Guid invoiceId,
            CancellationToken cancellationToken)
        {
            Requests.Add((context, invoiceId));
            return answer(context, invoiceId, cancellationToken);
        }
    }

    private sealed class FixedCorrelation(string correlationId, string? causationId) : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => correlationId;
        public string? CausationId => causationId;
        public string SourceChannel => "api";
        public string? ClientIpAddress => null;
        public string? UserAgent => null;

        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress,
            string? userAgent) => throw new NotSupportedException();
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
