using GoymenERP.Api.DocumentsRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>WP12 / FNS-D52: the statement import is the only owner that accepts the statement kinds; outcomes fail closed.</summary>
public sealed class FinanceBankStatementDocumentOwnerLinkPortTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Import = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaab");

    [Fact]
    public void The_import_owner_accepts_only_statement_kinds_and_no_existing_document_link()
    {
        var port = Port(new Imports(_ => ReadOutcome.Found), out _, out _);
        Assert.Equal("finance.bank-statement-import", port.Kind);
        Assert.Equal(DocumentContentPolicy.BankStatementImportKinds, port.AcceptedFileKinds);
        Assert.Equal([DocumentFileKind.Xlsx, DocumentFileKind.Mt940, DocumentFileKind.Csv], port.AcceptedFileKinds);
        Assert.False(port.AcceptsExistingDocumentLinks);
        Assert.False(port.AcceptsGeneratedDocuments);
    }

    [Theory]
    [InlineData(ReadOutcome.Found, DocumentOwnerAccessV1.Readable)]
    [InlineData(ReadOutcome.NotFound, DocumentOwnerAccessV1.NotFound)]
    [InlineData(ReadOutcome.Denied, DocumentOwnerAccessV1.Denied)]
    [InlineData(ReadOutcome.Unavailable, DocumentOwnerAccessV1.Unavailable)]
    public async Task Finance_outcomes_map_fail_closed_with_the_exact_context(ReadOutcome outcome, DocumentOwnerAccessV1 expected)
    {
        var imports = new Imports(_ => outcome);
        var port = Port(imports, out _, out _);
        Assert.Equal(expected, await port.CheckReadableAsync(Import));
        var (context, id) = Assert.Single(imports.Requests);
        Assert.Equal((Company, "SENTETIK-actor", "SENTETIK-correlation", Import), (context.CompanyId, context.ActorId, context.CorrelationId, id));
    }

    [Fact]
    public async Task Empty_id_unresolved_context_drift_and_faults_never_answer_readable()
    {
        var imports = new Imports(_ => ReadOutcome.Found);
        var port = Port(imports, out var company, out var user);
        Assert.Equal(DocumentOwnerAccessV1.Denied, await port.CheckReadableAsync(Guid.Empty));
        user.UserId = null;
        Assert.Equal(DocumentOwnerAccessV1.Denied, await port.CheckReadableAsync(Import));
        Assert.Empty(imports.Requests);

        user.UserId = "SENTETIK-actor";
        var drifting = new Imports(_ => { user.UserId = "SENTETIK-other"; return ReadOutcome.Found; });
        Assert.Equal(DocumentOwnerAccessV1.Denied, await Port(drifting, company, user).CheckReadableAsync(Import));
        user.UserId = "SENTETIK-actor";
        var faulty = new Imports(_ => throw new InvalidOperationException("SENTETIK"));
        Assert.Equal(DocumentOwnerAccessV1.Unavailable, await Port(faulty, company, user).CheckReadableAsync(Import));
    }

    private static IDocumentOwnerLinkPortV1 Port(Imports imports, out MutableCompany company, out MutableUser user)
    {
        company = new MutableCompany();
        company.Set(Company);
        user = new MutableUser { UserId = "SENTETIK-actor" };
        user.AllowedCompanyIds.Add(Company);
        return Port(imports, company, user);
    }

    private static IDocumentOwnerLinkPortV1 Port(Imports imports, MutableCompany company, MutableUser user) =>
        new FinanceBankStatementImportDocumentOwnerLinkPort(company, user, new FixedCorrelation(), imports);

    private sealed class Imports(Func<Guid, ReadOutcome> answer) : IFinanceBankStatementImportServiceV1
    {
        public List<(FinanceContext Context, Guid Id)> Requests { get; } = [];

        public Task<ReadOutcome> CheckDocumentOwnerAsync(FinanceContext context, Guid importId, CancellationToken cancellationToken)
        {
            Requests.Add((context, importId));
            return Task.FromResult(answer(importId));
        }

        public Task<FinanceBankStatementCommandResultV1> ReserveAsync(FinanceContext context, CommandMetadata metadata,
            ReserveFinanceBankStatementImportV1 request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FinanceBankStatementCommandResultV1> ParseAsync(FinanceContext context, CommandMetadata metadata, Guid importId,
            ParseFinanceBankStatementImportV1 request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FinanceBankStatementDetailResultV1> GetAuthorizedAsync(FinanceContext context, Guid importId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FinanceBankStatementListResultV1> ListAuthorizedAsync(FinanceContext context, Guid? bankAccountId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FinanceBankStatementMatchCommandResultV1> ConfirmMatchAsync(FinanceContext context, CommandMetadata metadata,
            Guid importId, ConfirmFinanceBankStatementMatchV1 request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FinanceBankStatementMatchCommandResultV1> ReverseMatchAsync(FinanceContext context, CommandMetadata metadata,
            Guid matchId, ReverseFinanceBankStatementMatchV1 request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "SENTETIK-correlation";
        public string? CausationId => null;
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
