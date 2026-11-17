using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Security;
using GoymenERP.Api.AccountingRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Actual SQL owner/audit/commands. Commercial source and approved account plan are explicitly synthetic owner responses.</summary>
public sealed class AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests
{
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly Guid Maker = Guid.Parse("c0870000-0000-4000-8000-000000000001");
    private static readonly Guid Checker = Guid.Parse("c0870000-0000-4000-8000-000000000002");
    private static readonly Guid Old = Guid.Parse("c0870000-0000-4000-8000-000000000003");
    private static readonly Guid Next = Guid.Parse("c0870000-0000-4000-8000-000000000004");
    private static readonly Guid Counter = Guid.Parse("c0870000-0000-4000-8000-000000000005");
    private static readonly Guid Currency = SettingsCurrencyIdentitiesV1.TurkishLira;
    private static readonly DateTimeOffset At = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [SqlServerFact]
    public async Task A_line_without_a_posted_COGS_component_changes_classification_without_inventing_amounts_or_a_stock_movement()
    {
        await using var run = await Run.Start(); var source = run.Source.Facts; var period = await run.Period();
        await using var scope = run.Open();
        var prepared = await scope.Store.PrepareAsync(source, Request(source, period), Maker, Fence, Metadata(Maker));
        Assert.Equal("Prepared", prepared.Status); Assert.Null(prepared.PostedEvidence);
        var own = await Assert.ThrowsAsync<AccountingStoreException>(() => scope.Store.ApproveAsync(source, prepared.ProposalId, 1, Maker, Fence, Metadata(Maker, true)));
        Assert.Equal(InvoiceLineCogsCorrectionCodesV1.SelfApproval, own.Code);
        var approved = await scope.Store.ApproveAsync(source, prepared.ProposalId, 1, Checker, Fence, Metadata(Checker, true));
        Assert.Equal("Approved", approved.Status); Assert.Null(approved.ReversingCorrectionJournalId); Assert.Null(approved.ReplacementCorrectionJournalId);
        var history = await scope.Store.ReadAsync(source);
        Assert.Equal(1, history.LineVersion); Assert.Equal("NoPostedCogs", history.PostingEvidenceState);
        Assert.Equal(source.Fingerprint, history.Source.Fingerprint); Assert.Single(history.Revisions);
        Assert.DoesNotContain("commercialProofJson", JsonSerializer.Serialize(history, Json), StringComparison.Ordinal);
        Assert.Equal(0, await run.Count("accounting.Journals"));
        Assert.Equal(2, await run.Scalar("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Accounting.InvoiceLineCogsCorrection'"));
    }

    [SqlServerFact]
    public async Task An_exact_line_COGS_component_gets_two_balanced_corrections_with_the_same_amount_and_unchanged_original()
    {
        await using var run = await Run.Start(); var source = run.Source.Facts; var period = await run.Period();
        var originalId = await run.PostComponent(source, period);
        await using var scope = run.Open();
        var prepared = await scope.Store.PrepareAsync(source, Request(source, period), Maker, Fence, Metadata(Maker));
        Assert.Equal("100.01", prepared.PostedEvidence!.Amount);
        var result = await scope.Store.ApproveAsync(source, prepared.ProposalId, 1, Checker, Fence, Metadata(Checker, true));
        Assert.NotNull(result.ReversingCorrectionJournalId); Assert.NotNull(result.ReplacementCorrectionJournalId);
        Assert.Equal(3, await run.Count("accounting.Journals"));
        var original = await scope.Db.JournalLines.AsNoTracking().Where(x => x.JournalId == originalId).OrderBy(x => x.LineNumber).ToListAsync();
        Assert.Equal(100.01m, original[0].Debit); Assert.Equal(Old, original[0].AccountId);
        var replacement = await scope.Db.JournalLines.AsNoTracking().Where(x => x.JournalId == result.ReplacementCorrectionJournalId).OrderBy(x => x.LineNumber).ToListAsync();
        Assert.Equal(Next, replacement[0].AccountId); Assert.Equal(100.01m, replacement[0].Debit);
        Assert.Equal(100.01m, replacement[1].Credit);
        var sourceAfter = await scope.Store.ReadAsync(source);
        Assert.Equal("PostedCogs", sourceAfter.PostingEvidenceState); Assert.Equal(Next, sourceAfter.PostedEvidence!.CogsAccountId);
        Assert.Equal(source.Fingerprint, sourceAfter.PostedEvidence.SourceFingerprint);
    }

    [SqlServerFact]
    public async Task An_invoice_total_journal_without_an_exact_line_component_is_unreadable_and_is_never_wholly_reversed()
    {
        await using var run = await Run.Start(); var source = run.Source.Facts; var period = await run.Period();
        await run.PostComponent(source, period, aggregate: true);
        await using var scope = run.Open();
        Assert.Equal("Unreadable", (await scope.Store.ReadAsync(source)).PostingEvidenceState);
        var exception = await Assert.ThrowsAsync<AccountingStoreException>(() => scope.Store.PrepareAsync(source, Request(source, period), Maker, Fence, Metadata(Maker)));
        Assert.Equal(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid, exception.Code);
        Assert.Equal(1, await run.Count("accounting.Journals")); Assert.Equal(0, await run.Count("accounting.InvoiceLineCogsProposals"));
    }

    [SqlServerFact]
    public async Task Competing_prepared_choices_for_one_line_have_only_one_approved_version_and_other_lines_remain_independent()
    {
        await using var run = await Run.Start(); var source = run.Source.Facts; var period = await run.Period();
        var first = Request(source, period); var second = first with { ProposalId = Guid.NewGuid(), Classification = InvoiceLineCogsClassificationV1.TradeGoods };
        await using (var scope = run.Open())
        { await scope.Store.PrepareAsync(source, first, Maker, Fence, Metadata(Maker)); await scope.Store.PrepareAsync(source, second, Maker, Fence, Metadata(Maker)); }
        async Task<bool> Approve(Guid id)
        {
            await using var scope = run.Open();
            try { await scope.Store.ApproveAsync(source, id, 1, Checker, Fence, Metadata(Checker, true)); return true; }
            catch (Exception exception) when (exception is AccountingVersionConflictException or AccountingStoreException or DbUpdateException
                || exception.GetBaseException() is SqlException { Number: 1205 }) { return false; }
        }
        var results = await Task.WhenAll(Approve(first.ProposalId), Approve(second.ProposalId));
        Assert.Single(results, value => value);
        Assert.Equal(1, await run.Count("accounting.InvoiceLineCogsRevisions"));
        await using var read = run.Open();
        var other = source with { InvoiceLineId = Guid.NewGuid(), Fingerprint = new string('B', 64) };
        Assert.Equal(0, (await read.Store.ReadAsync(other)).LineVersion);
        var foreign = source with { CompanyId = TestCompanyIds.CompanyB };
        await Assert.ThrowsAsync<AccountingStoreException>(() => read.Store.ReadAsync(foreign));
    }

    [SqlServerFact]
    public async Task Source_change_closed_period_and_revoked_fresh_authority_leave_the_prepared_line_and_key_unchanged()
    {
        await using var run = await Run.Start(); var source = run.Source.Facts; var period = await run.Period();
        await using var scope = run.Open();
        var prepared = await scope.Store.PrepareAsync(source, Request(source, period), Maker, Fence, Metadata(Maker));
        var changed = source with { Fingerprint = new string('B', 64) };
        Assert.Equal(InvoiceLineCogsCorrectionCodesV1.SourceChanged, (await Assert.ThrowsAsync<AccountingStoreException>(() =>
            scope.Store.ApproveAsync(changed, prepared.ProposalId, 1, Checker, Fence, Metadata(Checker, true)))).Code);
        var checks = 0;
        Task Revoke(CancellationToken _) { if (++checks == 3) throw new UnauthorizedAccessException(); return Task.CompletedTask; }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scope.Store.ApproveAsync(source, prepared.ProposalId, 1, Checker, Revoke, Metadata(Checker, true)));
        Assert.Equal(0, await run.Count("accounting.InvoiceLineCogsRevisions"));
        Assert.Equal("Prepared", (await scope.Store.FindAsync(prepared.ProposalId))!.Status);
        await AccountingPeriodCarrySqlFixture.CloseAsync(run.Factory, Company, period,
            (await scope.Posting.FindPeriodAsync(period))!.Version, Checker, Metadata(Checker));
        scope.Db.ChangeTracker.Clear();
        Assert.Equal(InvoiceLineCogsCorrectionCodesV1.OpenPeriodRequired, (await Assert.ThrowsAsync<AccountingStoreException>(() =>
            scope.Store.ApproveAsync(source, prepared.ProposalId, 1, Checker, Fence, Metadata(Checker, true)))).Code);
        Assert.Equal(1, await run.Scalar("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Accounting.InvoiceLineCogsCorrection'"));
    }

    [SqlServerFact]
    public async Task An_actual_Audit_append_followed_by_an_Accounting_save_failure_rolls_back_both_and_the_same_key_can_retry()
    {
        await using var run = await Run.Start(); var source = run.Source.Facts; var period = await run.Period();
        await using var scope = run.Open(); var company = new CompanyContext(); company.Set(Company);
        var fault = new FailOneProposalSave();
        await using var db = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString).AddInterceptors(fault).Options, company);
        var store = new InvoiceLineCogsCorrectionStoreV1(db, company, new Plan(), scope.Audit, scope.AuditWriter, TimeProvider.System);
        var request = Request(source, period);
        var metadata = Metadata(Maker) with { Completion = value => new("accounting.invoice-line-cogs.prepare.v1", new string('A', 64),
            Maker.ToString("D"), new string('B', 64), JsonSerializer.Serialize(new AccountingApiResult<InvoiceLineCogsCorrectionViewV1>(
                AccountingApiStatus.Ok, (InvoiceLineCogsCorrectionViewV1)value, false, null), Json), "SENTETIK-line-cogs") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PrepareAsync(source, request, Maker, Fence, metadata));
        Assert.Equal(0, await run.Count("accounting.InvoiceLineCogsProposals"));
        Assert.Equal(0, await run.Scalar("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Accounting.InvoiceLineCogsCorrection'"));
        Assert.Equal(0, await run.Scalar("SELECT COUNT(*) FROM accounting.Commands WHERE CommandName='accounting.invoice-line-cogs.prepare.v1'"));
        Assert.False(db.ChangeTracker.HasChanges()); Assert.False(scope.AuditWriter.HasPendingDrafts);
        Assert.Equal("Prepared", (await store.PrepareAsync(source, request, Maker, Fence, metadata)).Status);
        Assert.Equal(1, await run.Count("accounting.InvoiceLineCogsProposals"));
        Assert.Equal(1, await run.Scalar("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Accounting.InvoiceLineCogsCorrection'"));
        Assert.Equal(1, await run.Scalar("SELECT COUNT(*) FROM accounting.Commands WHERE CommandName='accounting.invoice-line-cogs.prepare.v1'"));
    }

    [SqlServerFact]
    public async Task A_borrowed_transaction_is_refused_before_caller_tracking_or_pending_audit_is_cleared()
    {
        await using var run = await Run.Start(); var period = await run.Period();
        await using var scope = run.Open();
        await using var transaction = await scope.Db.Database.BeginTransactionAsync();
        var row = new InvoiceLineCogsProposalRow { CompanyId = Company, Id = Guid.NewGuid(), InvoiceId = Guid.NewGuid(), InvoiceLineId = Guid.NewGuid(),
            PreparedBy = Maker, PreparedAtUtc = At, SourceJson = "{}", ViewJson = "{}" };
        scope.Db.InvoiceLineCogsProposals.Add(row);
        scope.Audit.Enqueue(new AuditEventDraft(Company, "accounting.synthetic-caller.v1", "Accounting.SyntheticCaller", row.Id.ToString("D"),
            "prepared", null, 1, AuditActorTypes.User, Maker.ToString("D"), AccountingJournalReversalPermissionNamesV1.Propose,
            [], "{}", "{}", "SENTETIK caller-owned audit", "{}", AuditAiInvolvement.None, null,
            "Web", null, null, "SENTETIK-line-cogs", null, "SENTETIK-caller-key"));
        Assert.True(scope.AuditWriter.HasPendingDrafts);
        var denial = await Assert.ThrowsAsync<AccountingStoreException>(() => scope.Store.PrepareAsync(run.Source.Facts,
            Request(run.Source.Facts, period), Maker, Fence, Metadata(Maker)));
        Assert.Equal(InvoiceLineCogsCorrectionCodesV1.SourceUnavailable, denial.Code);
        Assert.Equal(EntityState.Added, scope.Db.Entry(row).State); Assert.Same(transaction, scope.Db.Database.CurrentTransaction);
        Assert.True(scope.AuditWriter.HasPendingDrafts);
        scope.AuditWriter.DiscardPending(); // The caller, rather than the refused operation, owns this cleanup.
        await transaction.RollbackAsync();
    }

    [SqlServerFact]
    public async Task The_invoice_line_COGS_migration_Down_passes_empty_and_refuses_while_history_exists()
    {
        await using var run = await Run.Start();
        var migration = new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AddInvoiceLineCogsCorrections();
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        await run.Execute(guard);
        var period = await run.Period();
        await using (var scope = run.Open())
            await scope.Store.PrepareAsync(run.Source.Facts, Request(run.Source.Facts, period), Maker, Fence, Metadata(Maker));
        var refusal = await Assert.ThrowsAsync<SqlException>(() => run.Execute(guard));
        Assert.Equal(51987, refusal.Number);
        Assert.Equal(1, await run.Count("accounting.InvoiceLineCogsProposals"));
        Assert.Equal(0, await run.Count("accounting.InvoiceLineCogsRevisions"));
    }

    [SqlServerFact]
    public async Task The_actual_Sales_owner_official_lookup_uses_valid_nonempty_keys_and_a_missing_invoice_is_not_a_provider_failure()
    {
        await using var run = await Run.Start(); await using var scope = run.Open();
        var actual = new AccountingInvoiceLineCogsSource(scope.Invoices, scope.CompanyContext, new SyntheticAuthorizedUser());
        var result = await actual.FindAsync(Company, "SENTETIK-no-such-official-document");
        Assert.Equal(FinalizedInvoiceLineSourceOutcomeV1.NotFound, result.Outcome); Assert.Null(result.Selection);
        var foreign = await actual.FindAsync(TestCompanyIds.CompanyB, "SENTETIK-no-such-official-document");
        Assert.Equal(FinalizedInvoiceLineSourceOutcomeV1.Unavailable, foreign.Outcome); Assert.Null(foreign.Selection);
    }

    [SqlServerFact]
    public async Task Official_document_selection_requires_existing_authority_and_rejects_foreign_or_malformed_source_proof_without_prices()
    {
        await using var run = await Run.Start(); var source = run.Source.Facts;
        await run.Grant(TestUsers.CompanyAViewer, AccountingJournalReversalPermissionNamesV1.Propose);
        using var client = run.Factory.CreateClientFor(TestUsers.CompanyAViewer, Company);
        var path = "/api/v1/accounting/invoice-line-cogs/source?officialInvoiceNumber=" + Uri.EscapeDataString(source.OfficialInvoiceNumber);
        using (var response = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("commercialProofJson", body, StringComparison.Ordinal);
            var selection = JsonSerializer.Deserialize<FinalizedInvoiceCogsSelectionV1>(body, Json)!;
            Assert.Equal(source.InvoiceLineId, Assert.Single(selection.Lines).InvoiceLineId);
        }
        var calls = run.Source.Calls;
        using (var malformedQuery = await client.GetAsync(path + "&unexpected=1")) Assert.Equal(HttpStatusCode.BadRequest, malformedQuery.StatusCode);
        Assert.Equal(calls, run.Source.Calls);
        using (var denied = run.Factory.CreateClientFor(TestUsers.CompanyB, Company))
        using (var response = await denied.GetAsync(path)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(calls, run.Source.Calls);
        run.Source.Malformed = true;
        using (var malformed = await client.GetAsync(path)) Assert.Equal(HttpStatusCode.ServiceUnavailable, malformed.StatusCode);
        run.Source.Malformed = false; run.Source.Unavailable = true;
        using (var unavailable = await client.GetAsync(path)) Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
    }

    [SqlServerFact]
    public async Task Actual_HTTP_commands_use_existing_reversal_authority_and_replay_before_a_changed_source_is_read()
    {
        await using var run = await Run.Start(); var source = run.Source.Facts; var period = await run.Period();
        await run.Grant(TestUsers.CompanyA, AccountingJournalReversalPermissionNamesV1.Propose);
        await run.Grant(TestUsers.CompanyAViewer, AccountingJournalReversalPermissionNamesV1.Approve);
        using var maker = run.Factory.CreateClientFor(TestUsers.CompanyA, Company);
        using var checker = run.Factory.CreateClientFor(TestUsers.CompanyAViewer, Company);
        var request = Request(source, period);
        using var prepared = await maker.SendAsync(Command(source, "/preparation", request, "cogs-prepare"));
        Assert.Equal(HttpStatusCode.OK, prepared.StatusCode);
        var first = (await prepared.Content.ReadFromJsonAsync<InvoiceLineCogsCorrectionViewV1>(Json))!;
        run.Source.Unavailable = true;
        using var replay = await maker.SendAsync(Command(source, "/preparation", request, "cogs-prepare"));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.True(replay.Headers.Contains("Idempotency-Replayed"));
        run.Source.Unavailable = false;
        using (var invalid = await checker.SendAsync(Command(source, $"/proposals/{first.ProposalId:D}/approval", new ApproveInvoiceLineCogsCorrectionV1(0), "cogs-approve")))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        Assert.Equal(1, await run.Scalar("SELECT COUNT(*) FROM accounting.Commands WHERE CommandName LIKE 'accounting.invoice-line-cogs.%'"));
        using var approved = await checker.SendAsync(Command(source, $"/proposals/{first.ProposalId:D}/approval", new ApproveInvoiceLineCogsCorrectionV1(1), "cogs-approve"));
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal(1, await run.Count("accounting.InvoiceLineCogsRevisions"));
        Assert.Equal(2, await run.Scalar("SELECT COUNT(*) FROM accounting.Commands WHERE CommandName LIKE 'accounting.invoice-line-cogs.%'"));
        var calls = run.Source.Calls;
        using var forbidden = run.Factory.CreateClientFor(TestUsers.CompanyB, Company);
        using var response = await forbidden.GetAsync(Path(source));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); Assert.Equal(calls, run.Source.Calls);
    }

    private static string Path(FinalizedInvoiceLineSourceV1 source) => $"/api/v1/accounting/invoice-line-cogs/{source.InvoiceId:D}/lines/{source.InvoiceLineId:D}";
    private static HttpRequestMessage Command(FinalizedInvoiceLineSourceV1 source, string suffix, object body, string key)
    { var request = new HttpRequestMessage(HttpMethod.Post, Path(source) + suffix) { Content = JsonContent.Create(body) }; request.Headers.Add("Idempotency-Key", key); return request; }
    private static Task Fence(CancellationToken _) => Task.CompletedTask;
    private static AccountingCommandMetadata Metadata(Guid actor, bool approve = false) => new(actor.ToString("D"),
        approve ? AccountingJournalReversalPermissionNamesV1.Approve : AccountingJournalReversalPermissionNamesV1.Propose,
        [], "SENTETIK-line-cogs", null, Guid.NewGuid().ToString("D"), "Web");
    private static PrepareInvoiceLineCogsCorrectionV1 Request(FinalizedInvoiceLineSourceV1 source, Guid period) => new(Guid.NewGuid(), source.FinalizedVersion,
        0, InvoiceLineCogsClassificationV1.Manufactured, Next, period, new(2026, 11, 17), new(2026, 11, 17), "SENTETIK line-only reclassification");
    private sealed class Source : IAccountingFinalizedInvoiceLineSourceV1, IAccountingInvoiceLineProductReferenceV1
    {
        public FinalizedInvoiceLineSourceV1 Facts { get; } = new(Company, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 1, new(2026, 11, 17), "SENTETIK-invoice", Guid.NewGuid(), "{\"synthetic\":true}", new string('A', 64));
        public bool Unavailable { get; set; }
        public bool Malformed { get; set; }
        public int Calls { get; private set; }
        public Task<FinalizedInvoiceCogsSelectionReadV1> FindAsync(Guid companyId, string officialInvoiceNumber, CancellationToken cancellationToken = default)
        {
            Calls++;
            var available = !Unavailable && companyId == Company && officialInvoiceNumber == Facts.OfficialInvoiceNumber;
            var reference = FinalizedInvoiceLineReferenceV1.From(Facts);
            return Task.FromResult(new FinalizedInvoiceCogsSelectionReadV1(Unavailable ? FinalizedInvoiceLineSourceOutcomeV1.Unavailable
                : available ? FinalizedInvoiceLineSourceOutcomeV1.Available : FinalizedInvoiceLineSourceOutcomeV1.NotFound, companyId,
                available ? new(Company, Facts.InvoiceId, Facts.OfficialInvoiceNumber, Facts.Ettn,
                    [Malformed ? reference with { CompanyId = TestCompanyIds.CompanyB } : reference]) : null));
        }
        public Task<FinalizedInvoiceLineSourceReadV1> GetAsync(Guid companyId, Guid invoiceId, Guid lineId, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(new FinalizedInvoiceLineSourceReadV1(Unavailable ? FinalizedInvoiceLineSourceOutcomeV1.Unavailable : companyId == Company
            && invoiceId == Facts.InvoiceId && lineId == Facts.InvoiceLineId ? FinalizedInvoiceLineSourceOutcomeV1.Available : FinalizedInvoiceLineSourceOutcomeV1.NotFound,
            companyId, invoiceId, lineId, !Unavailable && companyId == Company && invoiceId == Facts.InvoiceId && lineId == Facts.InvoiceLineId ? Facts : null)); }
        public Task<InvoiceLineCogsProductReferenceV1> GetAsync(Guid companyId, Guid productId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InvoiceLineCogsProductReferenceV1(companyId, productId, "NotSet", null, null, null));
    }
    private sealed class Plan : IActiveAccountPlanReaderV1
    {
        public Task<ApprovedAccountingSourceCatalogRecordV1?> ReadActiveAsync(CancellationToken cancellationToken = default)
        {
            var sha = new string('A', 64); var mirror = new AccountPlanMirrorV1(Company, "SENTETIK-plan", sha, Checker, At,
                [new(new(Company, Old), "621", "SENTETIK old"), new(new(Company, Next), "620", "SENTETIK next"), new(new(Company, Counter), "153", "SENTETIK counter")]);
            return Task.FromResult<ApprovedAccountingSourceCatalogRecordV1?>(new(Guid.Parse("c0870000-0000-4000-8000-000000000006"), Company,
                ApprovedAccountingSourceCatalogStatusV1.Active, 2, 1, "SENTETIK-template", "SENTETIK-plan", sha, sha, Maker, At, Checker, At, new(mirror, [])));
        }
    }
    private sealed class Run(string baseline, SqlServerRelationalApiFactory factory, Source source) : IAsyncDisposable
    {
        public SqlServerRelationalApiFactory Factory => factory;
        public Source Source => source;
        public static async Task<Run> Start()
        {
            var baseline = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var source = new Source(); var run = new Run(baseline, new(baseline, services =>
            {
                services.RemoveAll<IAccountingFinalizedInvoiceLineSourceV1>(); services.AddSingleton<IAccountingFinalizedInvoiceLineSourceV1>(source);
                services.RemoveAll<IAccountingInvoiceLineProductReferenceV1>(); services.AddSingleton<IAccountingInvoiceLineProductReferenceV1>(source);
                services.RemoveAll<IActiveAccountPlanReaderV1>(); services.AddSingleton<IActiveAccountPlanReaderV1>(new Plan());
            }), source);
            try
            {
                using var client = factoryClient(run); using var health = await client.GetAsync("/health/live"); health.EnsureSuccessStatusCode();
                await using var scope = run.Open(); await scope.Db.Database.MigrateAsync(); return run;
            }
            catch { await run.DisposeAsync(); throw; }
        }
        private static HttpClient factoryClient(Run run) => run.Factory.CreateClient();
        public Scope Open() => new(factory.Services.CreateAsyncScope());
        public async Task<Guid> Period()
        {
            await using var scope = Open(); var id = Guid.NewGuid();
            var (created, _) = await scope.Posting.ProposePeriodAsync(id, new(2026, 11, 1), new(2026, 11, 30), Maker, Metadata(Maker));
            await scope.Posting.ApprovePeriodAsync(id, created.Version, Checker, Metadata(Checker)); return id;
        }
        public async Task<Guid> PostComponent(FinalizedInvoiceLineSourceV1 source, Guid period, bool aggregate = false)
        {
            await using var scope = Open(); var id = Guid.NewGuid();
            var result = await scope.Posting.ReceiveAsync(new("SENTETIK-existing-cogs", id.ToString("D"), id,
                new(Company, source.InvoiceId, aggregate ? "SENTETIK-aggregate" : InvoiceLineCogsPostingTraceV1.For(source.InvoiceLineId, source.FinalizedVersion)),
                "SENTETIK-line-cogs", Currency, new(new(2026, 11, 17), new(2026, 11, 17), null, At),
                [new("cogs", new AccountRef(Company, Old), PostingSide.Debit, 100.01m), new("counter", new AccountRef(Company, Counter), PostingSide.Credit, 100.01m)]),
                period, Metadata(Maker));
            Assert.Equal(PostingOutcomeKind.Posted, result.Kind); return id;
        }
        public async Task Grant(string subject, string permission)
        {
            await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var external = await db.ExternalIdentities.SingleOrDefaultAsync(x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == subject);
            var actor = external?.UserId ?? Guid.NewGuid();
            if (external is null) { db.Users.Add(new UserAccount(actor, "SENTETIK accounting", null)); db.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, subject)); }
            var role = new RoleDefinition(Guid.NewGuid(), "SENTETIK-" + Guid.NewGuid().ToString("N"), "SENTETIK accounting", true); role.AddPermission(permission);
            var assignment = new RoleAssignment(Guid.NewGuid(), actor, role.Id, Company, DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, Company); db.Roles.Add(role); db.RoleAssignments.Add(assignment); await db.SaveChangesAsync();
        }
        public Task<int> Count(string table) => Scalar("SELECT COUNT(*) FROM " + table); // Only fixed test-owned names.
        public async Task<int> Scalar(string sql)
        { await using var connection = new SqlConnection(factory.DatabaseConnectionString); await connection.OpenAsync(); await using var command = new SqlCommand(sql, connection); return (int)(await command.ExecuteScalarAsync())!; }
        public async Task Execute(string sql)
        { await using var connection = new SqlConnection(factory.DatabaseConnectionString); await connection.OpenAsync(); await using var command = new SqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
        public async ValueTask DisposeAsync()
        {
            await factory.DisposeAsync(); SqlConnection.ClearAllPools(); await using var connection = new SqlConnection(baseline); await connection.OpenAsync();
            await using var command = connection.CreateCommand(); command.CommandText = $"IF DB_ID(N'{factory.DatabaseName}') IS NOT NULL BEGIN ALTER DATABASE [{factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{factory.DatabaseName}]; END;";
            await command.ExecuteNonQueryAsync();
        }
    }
    private sealed class Scope(AsyncServiceScope scope) : IAsyncDisposable
    {
        private readonly ICompanyContext company = Context(scope);
        public ICompanyContext CompanyContext => company;
        public ISalesInvoiceStore Invoices => scope.ServiceProvider.GetRequiredService<ISalesInvoiceStore>();
        public AccountingDbContext Db => scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        public IAuditEventCollector Audit => scope.ServiceProvider.GetRequiredService<IAuditEventCollector>();
        public IAuditTransactionWriterV1 AuditWriter => scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        public AccountingStore Posting => new(Db, company, scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), TimeProvider.System,
            new AccountingAmountScaleVerifier(new AccountingSqlServerRelationalAcceptanceTests.SyntheticMoneyProfiles(Company)));
        public InvoiceLineCogsCorrectionStoreV1 Store => new(Db, company, new Plan(), scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(),
            scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>(), TimeProvider.System);
        private static ICompanyContext Context(AsyncServiceScope scope)
        { var context = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); context.Set(Company); return context; }
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
    private sealed class SyntheticAuthorizedUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string UserId => Maker.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }
    private sealed class FailOneProposalSave : SaveChangesInterceptor
    {
        private bool armed = true;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (armed && data.Context is AccountingDbContext db && db.ChangeTracker.Entries<InvoiceLineCogsProposalRow>().Any(x => x.State == EntityState.Added))
            { armed = false; throw new InvalidOperationException("SENTETIK after actual Audit, before Accounting save"); }
            return ValueTask.FromResult(result);
        }
    }
}
