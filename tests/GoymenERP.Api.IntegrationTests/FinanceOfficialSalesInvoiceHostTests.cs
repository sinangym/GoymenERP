using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class FinanceOfficialSalesInvoiceHostTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-1111111111a5");
    private static readonly Guid Document = Guid.Parse("22222222-2222-4222-8222-2222222222a5");
    private static readonly Guid Invoice = Guid.Parse("33333333-3333-4333-8333-3333333333a5");
    private const string Actor = "finance-official-sales-actor";
    private static readonly string Sha = new('c', 64);

    [Fact]
    public void Host_registration_resolves_the_service_with_its_scoped_authority_and_document_port()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IErpAuthorityRevalidationService>(new Revalidation());
        services.AddSingleton<ICompanyContext>(new MutableCompanyContext(Company));
        services.AddSingleton<ICurrentUser>(new User(Actor, Company));
        services.AddSingleton<IDocumentService>(new Documents());
        services.AddSingleton<ISettingsMoneyCandidateReader>(new NoCandidates());
        services.AddScoped<IAuditEventCollector>(_ => new NoOpAuditCollector());
        services.AddScoped<ICorrelationContext>(_ =>
        {
            var correlation = new CorrelationContext();
            correlation.Set("finance-official-sales-host-test", null, "test", null, null);
            return correlation;
        });
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<FinanceDbContext>(options =>
            options.UseInMemoryDatabase($"finance-official-sales-host-{Guid.NewGuid():N}"));
        services.AddFinanceOfficialSalesInvoiceHost();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IFinanceOfficialSalesInvoiceServiceV1>());
        Assert.Same(scope.ServiceProvider.GetRequiredService<FinanceOfficialSalesInvoiceAuthority>(),
            scope.ServiceProvider.GetRequiredService<IFinanceOfficialSalesInvoiceAuthorityV1>());
        Assert.IsType<FinanceOfficialSalesInvoiceDocumentPort>(
            scope.ServiceProvider.GetRequiredService<IFinanceOfficialSalesInvoiceDocumentPortV1>());
        Assert.IsType<FinanceOfficialSalesInvoiceOwnerReadV1>(
            scope.ServiceProvider.GetRequiredService<IFinanceOfficialSalesInvoiceOwnerReadV1>());
    }

    [Theory]
    [InlineData(FinanceOfficialSalesInvoiceAuthorityActionV1.Record, FinanceOfficialSalesInvoicePermissionNamesV1.Record)]
    [InlineData(FinanceOfficialSalesInvoiceAuthorityActionV1.Approve,
        FinanceOfficialSalesInvoicePermissionNamesV1.Approve)]
    public async Task Commands_use_their_exact_company_wide_permission_and_keep_audit_roles_live(
        FinanceOfficialSalesInvoiceAuthorityActionV1 action, string permission)
    {
        var revalidation = new Revalidation(Allowed("FINANCE-ROLE"));
        var authority = Create(revalidation);

        var result = await authority.CheckAsync(Context(), action, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(PermissionDecision.Allowed, result);
        Assert.Equal([permission], revalidation.Permissions);
        Assert.Equal(["FINANCE-ROLE"], authority.RoleCodesOf(action));
    }

    [Fact]
    public async Task Read_is_allowed_for_a_holder_of_approve_without_granting_record()
    {
        var revalidation = new Revalidation(PermissionScope.Denied, Allowed("FINANCE-APPROVE"));
        var authority = Create(revalidation);

        var result = await authority.CheckAsync(Context(), FinanceOfficialSalesInvoiceAuthorityActionV1.Read,
            Guid.Empty, CancellationToken.None);
        var record = await authority.CheckAsync(Context(), FinanceOfficialSalesInvoiceAuthorityActionV1.Record,
            Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(PermissionDecision.Allowed, result);
        Assert.Equal(PermissionDecision.Denied, record);
        Assert.Equal(
            [FinanceOfficialSalesInvoicePermissionNamesV1.Record, FinanceOfficialSalesInvoicePermissionNamesV1.Approve,
                FinanceOfficialSalesInvoicePermissionNamesV1.Record],
            revalidation.Permissions);
    }

    [Fact]
    public async Task Narrow_scope_empty_command_target_and_changed_context_fail_closed()
    {
        var revalidation = new Revalidation(new PermissionScope(true, false, new HashSet<string> { "NARROW" },
            new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        var company = new MutableCompanyContext(Company);
        var authority = new FinanceOfficialSalesInvoiceAuthority(revalidation, company, new User(Actor, Company));

        Assert.Equal(PermissionDecision.Denied, await authority.CheckAsync(Context(),
            FinanceOfficialSalesInvoiceAuthorityActionV1.Approve, Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(PermissionDecision.Denied, await authority.CheckAsync(Context(),
            FinanceOfficialSalesInvoiceAuthorityActionV1.Record, Guid.Empty, CancellationToken.None));
        company.Set(Guid.NewGuid());
        Assert.Equal(PermissionDecision.Denied, await authority.CheckAsync(Context(),
            FinanceOfficialSalesInvoiceAuthorityActionV1.Approve, Guid.NewGuid(), CancellationToken.None));
        Assert.Single(revalidation.Permissions);
        Assert.Empty(authority.RoleCodesOf(FinanceOfficialSalesInvoiceAuthorityActionV1.Approve));
    }

    [Fact]
    public async Task Document_port_accepts_a_clean_pdf_owned_by_the_record_id_and_asks_for_the_pinned_version()
    {
        var documents = new Documents(History(View()));
        var port = new FinanceOfficialSalesInvoiceDocumentPort(documents);

        var latest = await port.CheckAsync(Context(), Document, Invoice, null, CancellationToken.None);
        var pinned = await port.CheckAsync(Context(), Document, Invoice, 3, CancellationToken.None);

        Assert.Equal(new FinanceOfficialSalesInvoicePdfCheckV1(FinanceOfficialSalesInvoicePdfOutcomeV1.Accepted, 3, Sha),
            latest);
        Assert.Equal(FinanceOfficialSalesInvoicePdfOutcomeV1.Accepted, pinned.Outcome);
        Assert.Equal([(Document, 1, (string?)null), (Document, 1, "4")], documents.Calls);
    }

    public static TheoryData<string, FinanceOfficialSalesInvoicePdfOutcomeV1> RefusedViews => new()
    {
        { "other-owner-kind", FinanceOfficialSalesInvoicePdfOutcomeV1.OwnerMismatch },
        { "former-customer-owner-kind", FinanceOfficialSalesInvoicePdfOutcomeV1.OwnerMismatch },
        { "other-record-id", FinanceOfficialSalesInvoicePdfOutcomeV1.OwnerMismatch },
        { "not-pdf", FinanceOfficialSalesInvoicePdfOutcomeV1.NotPdf },
        { "not-downloadable", FinanceOfficialSalesInvoicePdfOutcomeV1.NotDownloadable },
        { "foreign-company", FinanceOfficialSalesInvoicePdfOutcomeV1.Unavailable },
        { "other-document", FinanceOfficialSalesInvoicePdfOutcomeV1.Unavailable },
    };

    [Theory]
    [MemberData(nameof(RefusedViews))]
    public async Task Document_port_refuses_anything_but_a_clean_pdf_owned_by_the_record_id(string variant,
        FinanceOfficialSalesInvoicePdfOutcomeV1 expected)
    {
        var view = variant switch
        {
            "other-owner-kind" => View() with { OwnerKind = "sales.order" },
            // #66 6860586175: the customer party is no longer the owner; a PDF of another record id is a mismatch.
            "former-customer-owner-kind" => View() with { OwnerKind = "counterparty.party" },
            "other-record-id" => View() with { OwnerId = Guid.NewGuid() },
            "not-pdf" => View() with { ContentType = "image/png" },
            "not-downloadable" => View() with { Downloadable = false },
            "foreign-company" => View() with { CompanyId = Guid.NewGuid() },
            _ => View() with { Id = Guid.NewGuid() },
        };
        var port = new FinanceOfficialSalesInvoiceDocumentPort(new Documents(History(view)));

        var result = await port.CheckAsync(Context(), Document, Invoice, null, CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.VersionNo);
        Assert.Null(result.Sha256);
    }

    [Theory]
    [InlineData(401, FinanceOfficialSalesInvoicePdfOutcomeV1.Denied)]
    [InlineData(403, FinanceOfficialSalesInvoicePdfOutcomeV1.Denied)]
    [InlineData(404, FinanceOfficialSalesInvoicePdfOutcomeV1.NotFound)]
    [InlineData(503, FinanceOfficialSalesInvoicePdfOutcomeV1.Unavailable)]
    public async Task Document_port_maps_vault_failures_closed(int status, FinanceOfficialSalesInvoicePdfOutcomeV1 expected)
    {
        var port = new FinanceOfficialSalesInvoiceDocumentPort(
            new Documents(failure: new DocumentServiceException(status, "document_failure")));

        var result = await port.CheckAsync(Context(), Document, Invoice, null, CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
    }

    [Fact]
    public async Task Document_port_refuses_a_missing_pinned_version_or_an_unexpected_exception()
    {
        var empty = new FinanceOfficialSalesInvoiceDocumentPort(
            new Documents(new DocumentVersionHistoryResponse(Document, [], null)));
        var wrongVersion = new FinanceOfficialSalesInvoiceDocumentPort(new Documents(History(View() with { VersionNo = 2 })));
        var broken = new FinanceOfficialSalesInvoiceDocumentPort(new Documents(failure: new TimeoutException()));

        Assert.Equal(FinanceOfficialSalesInvoicePdfOutcomeV1.NotFound,
            (await empty.CheckAsync(Context(), Document, Invoice, 3, CancellationToken.None)).Outcome);
        Assert.Equal(FinanceOfficialSalesInvoicePdfOutcomeV1.Unavailable,
            (await empty.CheckAsync(Context(), Document, Invoice, null, CancellationToken.None)).Outcome);
        Assert.Equal(FinanceOfficialSalesInvoicePdfOutcomeV1.Unavailable,
            (await wrongVersion.CheckAsync(Context(), Document, Invoice, 3, CancellationToken.None)).Outcome);
        Assert.Equal(FinanceOfficialSalesInvoicePdfOutcomeV1.Unavailable,
            (await broken.CheckAsync(Context(), Document, Invoice, null, CancellationToken.None)).Outcome);
        Assert.Equal(FinanceOfficialSalesInvoicePdfOutcomeV1.NotFound,
            (await broken.CheckAsync(Context(), Guid.Empty, Invoice, null, CancellationToken.None)).Outcome);
    }

    private static FinanceOfficialSalesInvoiceAuthority Create(Revalidation revalidation) =>
        new(revalidation, new MutableCompanyContext(Company), new User(Actor, Company));

    private static FinanceContext Context() => new(Company, Actor, "finance-official-sales-correlation", null);

    private static PermissionScope Allowed(string role) => new(true, true, new HashSet<string> { role },
        new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

    private static DocumentView View() => new(Document, Company, DocumentOwnerLinkKindsV1.FinanceOfficialSalesInvoice,
        Invoice, 3, "fatura.pdf", "application/pdf", 1024, Sha, "Clean", true, "uploader",
        DateTimeOffset.Parse("2026-11-11T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

    private static DocumentVersionHistoryResponse History(DocumentView view) => new(Document, [view], null);

    private sealed class Documents(DocumentVersionHistoryResponse? history = null, Exception? failure = null)
        : IDocumentService
    {
        public List<(Guid Document, int? PageSize, string? Cursor)> Calls { get; } = [];

        public Task<DocumentVersionHistoryResponse> ListVersionsAsync(Guid documentId, int? pageSize, string? cursor,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((documentId, pageSize, cursor));
            if (failure is not null) throw failure;
            return Task.FromResult(history!);
        }

        public Task<DocumentUploadResponse> UploadAsync(UploadDocumentRequest request, DocumentCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentListResponse> ListAsync(string? ownerKind, Guid ownerId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentContentV1> GetContentAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentVersionWriteResponse> AppendVersionAsync(Guid documentId, AppendDocumentVersionRequest request,
            DocumentCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentContentV1> GetVersionContentAsync(Guid documentId, int versionNo,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoOpAuditCollector : IAuditEventCollector
    {
        public void Enqueue(AuditEventDraft auditEvent) { }
    }

    private sealed class NoCandidates : ISettingsMoneyCandidateReader
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MoneyCurrencyCandidateV1>>([]);
    }

    private sealed class Revalidation(params PermissionScope[] answers) : IErpAuthorityRevalidationService
    {
        private readonly Queue<PermissionScope> answers = new(answers);
        public List<string> Permissions { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Permissions.Add(permission);
            return Task.FromResult(answers.Count == 0 ? PermissionScope.Denied : answers.Dequeue());
        }
    }

    private sealed class MutableCompanyContext(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid value) => CompanyId = value;
    }

    private sealed class User(string actor, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
