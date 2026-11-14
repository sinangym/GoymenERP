using GoymenERP.Api.DocumentsRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// B-26 (FNS-D35/D36): the Finance record owner kinds (manual receipt, manual bank payment, customer instrument) answer
/// through Finance's authorized detail read only, for the exact context company and actor, fail closed, and accept an
/// existing document; the official sales invoice kind does not.
/// </summary>
public sealed class FinanceRecordDocumentOwnerLinkPortsTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherCompany = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Record = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid OtherRecord = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    [Fact]
    public void Finance_record_kinds_are_distinct_and_accept_existing_documents_but_the_invoice_does_not()
    {
        var company = new MutableCompany();
        var user = new MutableUser();
        var correlation = new FixedCorrelation("SENTETIK-correlation", null);
        IDocumentOwnerLinkPortV1[] records =
        [
            new FinanceManualReceiptDocumentOwnerLinkPort(company, user, correlation, null!),
            new FinanceManualBankPaymentDocumentOwnerLinkPort(company, user, correlation, null!),
            new FinanceCustomerInstrumentDocumentOwnerLinkPort(company, user, correlation, null!),
        ];

        Assert.Equal(
            ["finance.manual-receipt", "finance.manual-bank-payment", "finance.customer-instrument"],
            records.Select(port => port.Kind));
        Assert.All(records, port => Assert.True(port.AcceptsExistingDocumentLinks));
        IDocumentOwnerLinkPortV1 invoice = new FinanceDocumentOwnerLinkPort(company, user, correlation, null!);
        Assert.False(invoice.AcceptsExistingDocumentLinks);
    }

    [Fact]
    public async Task The_very_record_of_the_context_company_is_readable_with_the_exact_finance_context()
    {
        var (port, _, _) = Probe((_, id, _) => Task.FromResult(new FinanceOwnerReadV1(ReadOutcome.Found, id, Company)));

        Assert.Equal(DocumentOwnerAccessV1.Readable, await port.CheckReadableAsync(Record));
        var (context, ownerId) = Assert.Single(port.Requests);
        Assert.Equal(Record, ownerId);
        Assert.Equal(new FinanceContext(Company, "SENTETIK-actor", "SENTETIK-correlation", "SENTETIK-causation"), context);
    }

    [Fact]
    public async Task A_found_answer_for_another_record_or_company_or_without_identity_is_unavailable()
    {
        var (otherRecord, _, _) = Probe((_, _, _) => Task.FromResult(new FinanceOwnerReadV1(ReadOutcome.Found, OtherRecord, Company)));
        var (otherCompany, _, _) = Probe((_, id, _) => Task.FromResult(new FinanceOwnerReadV1(ReadOutcome.Found, id, OtherCompany)));
        var (noIdentity, _, _) = Probe((_, _, _) => Task.FromResult(FinanceOwnerReadV1.Of(ReadOutcome.Found)));

        Assert.Equal(DocumentOwnerAccessV1.Unavailable, await otherRecord.CheckReadableAsync(Record));
        Assert.Equal(DocumentOwnerAccessV1.Unavailable, await otherCompany.CheckReadableAsync(Record));
        Assert.Equal(DocumentOwnerAccessV1.Unavailable, await noIdentity.CheckReadableAsync(Record));
    }

    [Theory]
    [InlineData(ReadOutcome.NotFound, DocumentOwnerAccessV1.NotFound)]
    [InlineData(ReadOutcome.Denied, DocumentOwnerAccessV1.Denied)]
    [InlineData(ReadOutcome.Unavailable, DocumentOwnerAccessV1.Unavailable)]
    public async Task Finance_read_outcomes_are_mapped_fail_closed(ReadOutcome outcome, DocumentOwnerAccessV1 expected)
    {
        var (port, _, _) = Probe((_, _, _) => Task.FromResult(FinanceOwnerReadV1.Of(outcome)));

        Assert.Equal(expected, await port.CheckReadableAsync(Record));
    }

    [Fact]
    public async Task An_empty_id_or_an_unresolved_actor_or_company_is_denied_without_a_finance_read()
    {
        static Task<FinanceOwnerReadV1> Found(FinanceContext context, Guid id, CancellationToken token) =>
            Task.FromResult(new FinanceOwnerReadV1(ReadOutcome.Found, id, context.CompanyId));

        var (emptyId, _, _) = Probe(Found);
        Assert.Equal(DocumentOwnerAccessV1.Denied, await emptyId.CheckReadableAsync(Guid.Empty));
        Assert.Empty(emptyId.Requests);

        var (anonymous, user, _) = Probe(Found);
        user.UserId = null;
        Assert.Equal(DocumentOwnerAccessV1.Denied, await anonymous.CheckReadableAsync(Record));
        Assert.Empty(anonymous.Requests);

        var (unresolved, _, company) = Probe(Found);
        company.Set(Guid.Empty);
        Assert.Equal(DocumentOwnerAccessV1.Denied, await unresolved.CheckReadableAsync(Record));
        Assert.Empty(unresolved.Requests);

        var (noAccess, accessUser, _) = Probe(Found);
        accessUser.AllowedCompanyIds.Clear();
        Assert.Equal(DocumentOwnerAccessV1.Denied, await noAccess.CheckReadableAsync(Record));
        Assert.Empty(noAccess.Requests);
    }

    [Fact]
    public async Task Actor_or_company_drift_after_the_finance_read_is_denied()
    {
        MutableUser? actor = null;
        var (actorPort, user, _) = Probe((_, id, _) =>
        {
            actor!.UserId = "SENTETIK-other";
            return Task.FromResult(new FinanceOwnerReadV1(ReadOutcome.Found, id, Company));
        });
        actor = user;
        Assert.Equal(DocumentOwnerAccessV1.Denied, await actorPort.CheckReadableAsync(Record));

        MutableCompany? company = null;
        var (companyPort, companyUser, companyContext) = Probe((_, id, _) =>
        {
            company!.Set(OtherCompany);
            return Task.FromResult(new FinanceOwnerReadV1(ReadOutcome.Found, id, Company));
        });
        company = companyContext;
        companyUser.AllowedCompanyIds.Add(OtherCompany);
        Assert.Equal(DocumentOwnerAccessV1.Denied, await companyPort.CheckReadableAsync(Record));
    }

    [Fact]
    public async Task A_provider_fault_is_unavailable_and_cancellation_is_not_converted()
    {
        var (fault, _, _) = Probe((_, _, _) => Task.FromException<FinanceOwnerReadV1>(new InvalidOperationException("SENTETIK")));
        Assert.Equal(DocumentOwnerAccessV1.Unavailable, await fault.CheckReadableAsync(Record));

        var (cancelled, _, _) = Probe((_, _, token) => Task.FromCanceled<FinanceOwnerReadV1>(token));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.CheckReadableAsync(Record, cancellation.Token));
    }

    private static (ProbePort Port, MutableUser User, MutableCompany Company) Probe(
        Func<FinanceContext, Guid, CancellationToken, Task<FinanceOwnerReadV1>> answer)
    {
        var company = new MutableCompany();
        company.Set(Company);
        var user = new MutableUser { UserId = "SENTETIK-actor" };
        user.AllowedCompanyIds.Add(Company);
        var correlation = new FixedCorrelation("SENTETIK-correlation", "SENTETIK-causation");
        return (new ProbePort(company, user, correlation, answer), user, company);
    }

    private sealed class ProbePort(
        ICompanyContext companyContext,
        ICurrentUser currentUser,
        ICorrelationContext correlation,
        Func<FinanceContext, Guid, CancellationToken, Task<FinanceOwnerReadV1>> answer)
        : FinanceRecordDocumentOwnerLinkPort(companyContext, currentUser, correlation)
    {
        public List<(FinanceContext Context, Guid OwnerId)> Requests { get; } = [];

        public override string Kind => "sentetik.finance-record";

        protected override Task<FinanceOwnerReadV1> ReadAsync(FinanceContext context, Guid ownerId, CancellationToken cancellationToken)
        {
            Requests.Add((context, ownerId));
            return answer(context, ownerId, cancellationToken);
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
