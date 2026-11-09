using System.Reflection;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// B-04b: the source document admission port through the real service and the real store on one in-memory Accounting
/// database per test, with a synthetic mapping resolver and a synthetic host authority. What SQL Server enforces itself
/// (one save, the unique indexes, the race, the Down guard) is proven by the relational acceptance tests.
/// </summary>
public sealed class AccountingSourceDocumentAdmissionV1Tests
{
    private static readonly Guid Company = Guid.Parse("b0420000-0000-4000-8000-00000000000a");
    private static readonly Guid OtherCompany = Guid.Parse("b0420000-0000-4000-8000-00000000000b");
    private static readonly Guid Invoice = Guid.Parse("b0420000-0000-4000-8000-000000000001");
    private static readonly Guid SourceDocument = Guid.Parse("b0420000-0000-4000-8000-000000000002");
    private static readonly Guid SourceActor = Guid.Parse("b0420000-0000-4000-8000-000000000003");
    private static readonly Guid Party = Guid.Parse("b0420000-0000-4000-8000-000000000004");
    private static readonly Guid Currency = Guid.Parse("b0420000-0000-4000-8000-000000000005");
    private static readonly Guid Receivable = Guid.Parse("b0420000-0000-4000-8000-000000000120");
    private static readonly Guid Revenue = Guid.Parse("b0420000-0000-4000-8000-000000000600");
    private static readonly Guid VatOutput = Guid.Parse("b0420000-0000-4000-8000-000000000391");
    private static readonly DateOnly DocumentDate = new(2026, 11, 8);
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-08T09:00:00Z");

    [Fact]
    public void Every_reason_code_is_a_stable_code_of_at_most_64_characters()
    {
        var codes = typeof(AccountingSourceDocumentAdmissionReasonsV1)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();
        Assert.NotEmpty(codes);
        Assert.All(codes, code =>
        {
            Assert.InRange(code.Length, 1, AccountingSourceDocumentAdmissionAnswerV1.ReasonMaximumLength);
            Assert.Matches("^[a-z_]+$", code);
        });
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task An_in_process_call_without_a_request_correlation_audits_with_a_defined_source_channel()
    {
        var harness = new Harness { ResolveCorrelation = false };
        var answer = await harness.AdmitAsync(Valid());

        Assert.Equal(AccountingSourceDocumentAdmissionOutcomeV1.Admitted, answer.Outcome);
        var audit = Assert.Single(harness.Audit.Events);
        Assert.Equal(SourceChannels.Integration, audit.SourceChannel);
    }

    [Fact]
    public async Task A_mapped_invoice_becomes_one_balanced_draft_voucher_with_audit_and_one_attempt()
    {
        var harness = new Harness();
        var answer = await harness.AdmitAsync(Valid());

        Assert.Equal(AccountingSourceDocumentAdmissionOutcomeV1.Admitted, answer.Outcome);
        Assert.False(answer.IsReplay);
        Assert.NotNull(answer.VoucherDraftId);

        var lines = await harness.LinesAsync();
        // Debit receivable = 1200 + 240 = 1440; credit revenue per line (1000 + 200); credit VAT 240.
        Assert.Equal(4, lines.Count);
        var debit = lines.Single(line => line.Side == AccountingSourceDocumentSidesV1.Debit);
        Assert.Equal((Receivable, 1440m), (debit.AccountId, debit.Amount));
        Assert.Equal([1000m, 200m], lines.Where(line => line.AccountId == Revenue).OrderBy(line => line.LineNumber).Select(line => line.Amount));
        Assert.Equal(240m, lines.Single(line => line.AccountId == VatOutput).Amount);
        Assert.Equal(
            lines.Where(line => line.Side == AccountingSourceDocumentSidesV1.Debit).Sum(line => line.Amount),
            lines.Where(line => line.Side == AccountingSourceDocumentSidesV1.Credit).Sum(line => line.Amount));

        var admission = Assert.Single(await harness.AdmissionsAsync());
        Assert.Equal((int)AccountingSourceDocumentAdmissionStatusV1.Admitted, admission.Status);
        Assert.Equal(1, admission.AttemptCount);
        Assert.Equal(answer.VoucherDraftId, admission.VoucherDraftId);
        Assert.Equal(Company, admission.CompanyId);
        var audit = Assert.Single(harness.Audit.Events);
        Assert.Equal("accounting.source-document-admission.admitted.v1", audit.EventType);
        Assert.Equal(Company, audit.CompanyId);
    }

    [Fact]
    public async Task The_same_key_and_content_replays_the_first_answer_and_never_creates_a_second_voucher()
    {
        var harness = new Harness();
        var first = await harness.AdmitAsync(Valid());
        var second = await harness.AdmitAsync(Valid());

        Assert.Equal(AccountingSourceDocumentAdmissionOutcomeV1.Admitted, second.Outcome);
        Assert.True(second.IsReplay);
        Assert.Equal(first.VoucherDraftId, second.VoucherDraftId);
        Assert.Single(await harness.DraftsAsync());
        Assert.Equal(4, (await harness.LinesAsync()).Count);
        Assert.Single(harness.Audit.Events);
    }

    [Fact]
    public async Task The_same_key_with_other_content_is_a_conflict_and_changes_nothing()
    {
        var harness = new Harness();
        var first = await harness.AdmitAsync(Valid());
        var changed = Valid() with { PartyId = Guid.NewGuid() };
        var answer = await harness.AdmitAsync(changed);

        Assert.Equal(AccountingSourceDocumentAdmissionOutcomeV1.Refused, answer.Outcome);
        Assert.Equal(AccountingSourceDocumentAdmissionReasonsV1.IdempotencyConflict, answer.Reason);
        Assert.False(answer.Retryable);
        Assert.Null(answer.VoucherDraftId);
        Assert.Single(await harness.DraftsAsync());
        Assert.Equal(first.VoucherDraftId, (await harness.DraftsAsync()).Single().Id);
        Assert.Equal(1, (await harness.AdmissionsAsync()).Single().AttemptCount);
    }

    [Fact]
    public async Task A_missing_mapping_waits_fail_closed_and_the_same_key_is_admitted_once_it_is_configured()
    {
        var harness = new Harness { Mappings = { Missing = VatOutputKey } };
        var waiting = await harness.AdmitAsync(Valid());

        Assert.Equal(AccountingSourceDocumentAdmissionOutcomeV1.Refused, waiting.Outcome);
        Assert.Equal(AccountingSourceDocumentAdmissionReasonsV1.MappingPending, waiting.Reason);
        Assert.True(waiting.Retryable);
        Assert.Empty(await harness.DraftsAsync());
        Assert.Empty(await harness.LinesAsync());
        var row = Assert.Single(await harness.AdmissionsAsync());
        Assert.Equal((int)AccountingSourceDocumentAdmissionStatusV1.Waiting, row.Status);

        // Still waiting: the same key is re-driven and the attempt is counted, nothing else changes.
        var again = await harness.AdmitAsync(Valid());
        Assert.Equal(AccountingSourceDocumentAdmissionReasonsV1.MappingPending, again.Reason);
        Assert.Equal(2, (await harness.AdmissionsAsync()).Single().AttemptCount);

        harness.Mappings.Missing = null;
        var admitted = await harness.AdmitAsync(Valid());
        Assert.Equal(AccountingSourceDocumentAdmissionOutcomeV1.Admitted, admitted.Outcome);
        Assert.Single(await harness.DraftsAsync());
        var final = Assert.Single(await harness.AdmissionsAsync());
        Assert.Equal((int)AccountingSourceDocumentAdmissionStatusV1.Admitted, final.Status);
        Assert.Equal(3, final.AttemptCount);
        Assert.Equal(
            ["accounting.source-document-admission.waiting.v1", "accounting.source-document-admission.waiting.v1",
                "accounting.source-document-admission.admitted.v1"],
            harness.Audit.Events.Select(audit => audit.EventType));
    }

    [Theory]
    [InlineData("unmapped")]
    [InlineData("ambiguous")]
    [InlineData("no_active_mapping")]
    [InlineData("not_effective_for_date")]
    [InlineData("account_not_in_active_plan")]
    public async Task Every_mapping_gap_is_a_retryable_waiting_refusal_and_never_a_guessed_account(string gap)
    {
        var harness = new Harness { Mappings = { Gap = gap } };
        var answer = await harness.AdmitAsync(Valid());

        Assert.Equal(AccountingSourceDocumentAdmissionOutcomeV1.Refused, answer.Outcome);
        Assert.True(answer.Retryable);
        Assert.Equal(gap == "ambiguous"
            ? AccountingSourceDocumentAdmissionReasonsV1.MappingAmbiguous
            : AccountingSourceDocumentAdmissionReasonsV1.MappingPending, answer.Reason);
        Assert.Empty(await harness.DraftsAsync());
    }

    [Theory]
    [InlineData("company_not_resolved")]
    [InlineData("account_plan_unavailable")]
    public async Task An_unanswerable_mapping_read_is_unavailable_and_records_nothing(string reason)
    {
        var harness = new Harness { Mappings = { Gap = reason } };
        var answer = await harness.AdmitAsync(Valid());

        Assert.Equal(AccountingSourceDocumentAdmissionOutcomeV1.Unavailable, answer.Outcome);
        Assert.Empty(await harness.AdmissionsAsync());
        Assert.Empty(harness.Audit.Events);
    }

    [Fact]
    public async Task A_query_invalid_mapping_answer_is_a_recorded_terminal_refusal_replayed_for_the_key()
    {
        var harness = new Harness { Mappings = { Gap = "query_invalid" } };
        var first = await harness.AdmitAsync(Valid());
        harness.Mappings.Gap = null;
        var second = await harness.AdmitAsync(Valid());

        Assert.Equal(AccountingSourceDocumentAdmissionReasonsV1.MappingQueryInvalid, first.Reason);
        Assert.False(first.Retryable);
        Assert.True(second.IsReplay);
        Assert.Equal(AccountingSourceDocumentAdmissionReasonsV1.MappingQueryInvalid, second.Reason);
        Assert.Empty(await harness.DraftsAsync());
    }

    [Fact]
    public async Task Mapped_answers_of_two_versions_for_one_voucher_are_unavailable_and_nothing_is_mixed()
    {
        var harness = new Harness { Mappings = { SplitVersions = true } };
        var answer = await harness.AdmitAsync(Valid());

        Assert.Equal(AccountingSourceDocumentAdmissionOutcomeV1.Unavailable, answer.Outcome);
        Assert.Empty(await harness.AdmissionsAsync());
    }

    [Fact]
    public async Task The_withholding_amounts_of_the_producer_are_used_without_any_threshold_and_the_voucher_balances()
    {
        var harness = new Harness();
        // Net 1000, VAT 200, withholding 100 (producer-decided): payable 1100; the VAT credit is VAT - withholding = 100.
        var request = Valid(lines:
        [
            Line(1, "1000", "200", "100", "20"),
        ], net: "1000", vat: "200", withholding: "100", payable: "1100");
        var answer = await harness.AdmitAsync(request);

        Assert.Equal(AccountingSourceDocumentAdmissionOutcomeV1.Admitted, answer.Outcome);
        var lines = await harness.LinesAsync();
        Assert.Equal(1100m, lines.Single(line => line.Side == AccountingSourceDocumentSidesV1.Debit).Amount);
        Assert.Equal(100m, lines.Single(line => line.AccountId == VatOutput).Amount);
        Assert.Equal(1000m, lines.Single(line => line.AccountId == Revenue).Amount);
    }

    [Fact]
    public async Task A_vat_group_with_the_full_vat_withheld_writes_no_zero_line()
    {
        var harness = new Harness();
        var request = Valid(lines: [Line(1, "1000", "200", "200", "20")],
            net: "1000", vat: "200", withholding: "200", payable: "1000");
        var answer = await harness.AdmitAsync(request);

        Assert.Equal(AccountingSourceDocumentAdmissionOutcomeV1.Admitted, answer.Outcome);
        var lines = await harness.LinesAsync();
        Assert.Equal(2, lines.Count);
        Assert.DoesNotContain(lines, line => line.AccountId == VatOutput);
        Assert.All(lines, line => Assert.True(line.Amount > 0m));
    }

    [Fact]
    public async Task A_foreign_currency_invoice_is_booked_in_the_functional_amounts_only()
    {
        var harness = new Harness();
        var request = Valid(lines: [Line(1, "4000", "800", "0", "20")],
            net: "100", vat: "20", withholding: "0", payable: "120",
            functionalNet: "4000", functionalVat: "800", functionalWithholding: "0", functionalPayable: "4800",
            exchangeRate: "40");
        var answer = await harness.AdmitAsync(request);

        Assert.Equal(AccountingSourceDocumentAdmissionOutcomeV1.Admitted, answer.Outcome);
        var lines = await harness.LinesAsync();
        Assert.Equal(4800m, lines.Single(line => line.Side == AccountingSourceDocumentSidesV1.Debit).Amount);
        var draft = Assert.Single(await harness.DraftsAsync());
        Assert.Equal((100m, 4000m), (draft.DocumentNet, draft.FunctionalNet));
        Assert.Equal("40", draft.ExchangeRate);
    }

    [Theory]
    [InlineData("totals", AccountingSourceDocumentAdmissionReasonsV1.TotalsMismatch)]
    [InlineData("payable", AccountingSourceDocumentAdmissionReasonsV1.TotalsMismatch)]
    [InlineData("withholding", AccountingSourceDocumentAdmissionReasonsV1.WithholdingExceedsVat)]
    [InlineData("amount", AccountingSourceDocumentAdmissionReasonsV1.AmountInvalid)]
    [InlineData("negative", AccountingSourceDocumentAdmissionReasonsV1.AmountInvalid)]
    [InlineData("rate-zero", AccountingSourceDocumentAdmissionReasonsV1.ExchangeRateInvalid)]
    [InlineData("tl-mismatch", AccountingSourceDocumentAdmissionReasonsV1.ExchangeRateInvalid)]
    [InlineData("no-lines", AccountingSourceDocumentAdmissionReasonsV1.LineInvalid)]
    [InlineData("duplicate-line", AccountingSourceDocumentAdmissionReasonsV1.LineInvalid)]
    [InlineData("due-before", AccountingSourceDocumentAdmissionReasonsV1.DocumentInvalid)]
    public async Task A_content_fault_is_a_recorded_terminal_refusal_without_a_voucher_and_replays(string fault, string reason)
    {
        var harness = new Harness();
        var request = Faulty(fault);
        var first = await harness.AdmitAsync(request);
        var second = await harness.AdmitAsync(request);

        Assert.Equal(AccountingSourceDocumentAdmissionOutcomeV1.Refused, first.Outcome);
        Assert.Equal(reason, first.Reason);
        Assert.False(first.Retryable);
        Assert.True(second.IsReplay);
        Assert.Equal(reason, second.Reason);
        Assert.Empty(await harness.DraftsAsync());
        var row = Assert.Single(await harness.AdmissionsAsync());
        Assert.Equal((int)AccountingSourceDocumentAdmissionStatusV1.Refused, row.Status);
        Assert.Equal(1, row.AttemptCount);
        Assert.Equal("accounting.source-document-admission.refused.v1", Assert.Single(harness.Audit.Events).EventType);
    }

    [Fact]
    public async Task An_unsupported_kind_or_a_malformed_key_is_refused_without_any_record()
    {
        var harness = new Harness();
        var kind = await harness.AdmitAsync(Valid() with { DocumentKind = "purchase-invoice" });
        var key = await harness.AdmitAsync(Valid() with { IdempotencyKey = "sales-invoice-accounting:" + Invoice.ToString("D") + ":2" });
        var empty = await harness.AdmitAsync(Valid() with { InvoiceId = Guid.Empty });

        Assert.Equal(AccountingSourceDocumentAdmissionReasonsV1.DocumentKindUnsupported, kind.Reason);
        Assert.Equal(AccountingSourceDocumentAdmissionReasonsV1.IdempotencyKeyInvalid, key.Reason);
        Assert.Equal(AccountingSourceDocumentAdmissionReasonsV1.RequestInvalid, empty.Reason);
        Assert.Empty(await harness.AdmissionsAsync());
        Assert.Empty(harness.Audit.Events);
    }

    [Fact]
    public async Task A_missing_host_authority_is_unavailable_and_a_denied_one_is_refused_and_neither_records_anything()
    {
        var unavailable = new Harness { Authority = { Decision = AccountingSourceDocumentAuthorityDecisionV1.Unavailable } };
        var denied = new Harness { Authority = { Decision = AccountingSourceDocumentAuthorityDecisionV1.Denied } };

        Assert.Equal(AccountingSourceDocumentAdmissionOutcomeV1.Unavailable, (await unavailable.AdmitAsync(Valid())).Outcome);
        var refused = await denied.AdmitAsync(Valid());
        Assert.Equal(AccountingSourceDocumentAdmissionReasonsV1.AuthorityDenied, refused.Reason);
        Assert.Empty(await unavailable.AdmissionsAsync());
        Assert.Empty(await denied.AdmissionsAsync());

        var service = new UnavailableAccountingSourceDocumentAdmissionAuthorityV1();
        Assert.Equal(AccountingSourceDocumentAuthorityDecisionV1.Unavailable,
            (await service.CheckAsync(Company, "actor", "sales-invoice", SourceDocument, CancellationToken.None)).Decision);
    }

    [Fact]
    public async Task An_allowed_answer_without_a_permission_code_is_unavailable()
    {
        var harness = new Harness { Authority = { PermissionCode = string.Empty } };
        Assert.Equal(AccountingSourceDocumentAdmissionOutcomeV1.Unavailable, (await harness.AdmitAsync(Valid())).Outcome);
        Assert.Empty(await harness.AdmissionsAsync());
    }

    [Fact]
    public async Task An_authority_lost_between_the_first_fence_and_the_write_writes_nothing()
    {
        var harness = new Harness { Authority = { DenyFromCall = 2 } };
        var answer = await harness.AdmitAsync(Valid());

        Assert.Equal(AccountingSourceDocumentAdmissionReasonsV1.AuthorityDenied, answer.Reason);
        Assert.Empty(await harness.AdmissionsAsync());
        Assert.Empty(await harness.DraftsAsync());
    }

    [Fact]
    public async Task A_request_for_another_company_is_refused_and_a_foreign_company_never_sees_the_voucher()
    {
        var harness = new Harness();
        var foreign = await harness.AdmitAsync(Valid() with { CompanyId = OtherCompany });
        Assert.Equal(AccountingSourceDocumentAdmissionReasonsV1.AuthorityDenied, foreign.Reason);
        Assert.Empty(await harness.AdmissionsAsync());

        Assert.Equal(AccountingSourceDocumentAdmissionOutcomeV1.Admitted, (await harness.AdmitAsync(Valid())).Outcome);

        // The same key in another company on the same database is its own, fresh admission.
        var other = harness.ForCompany(OtherCompany);
        Assert.Empty(await other.AdmissionsAsync());
        var otherAnswer = await other.AdmitAsync(Valid() with { CompanyId = OtherCompany });
        Assert.Equal(AccountingSourceDocumentAdmissionOutcomeV1.Admitted, otherAnswer.Outcome);
        Assert.False(otherAnswer.IsReplay);
        Assert.Single(await harness.DraftsAsync());
        Assert.Single(await other.DraftsAsync());
    }

    [Fact]
    public async Task An_unresolved_company_context_is_unavailable()
    {
        var harness = new Harness { ResolveCompany = false };
        Assert.Equal(AccountingSourceDocumentAdmissionOutcomeV1.Unavailable, (await harness.AdmitAsync(Valid())).Outcome);
    }

    [Fact]
    public async Task The_draft_carries_a_period_only_when_an_open_period_covers_the_date()
    {
        var without = new Harness();
        Assert.Equal(AccountingSourceDocumentAdmissionOutcomeV1.Admitted, (await without.AdmitAsync(Valid())).Outcome);
        // KLN-D15: no open period does not refuse the draft; it waits for a period.
        Assert.Null((await without.DraftsAsync()).Single().PeriodId);

        var open = new Harness();
        var periodId = await open.SeedPeriodAsync(status: 1, DocumentDate.AddDays(-7), DocumentDate.AddDays(20));
        await open.AdmitAsync(Valid());
        Assert.Equal(periodId, (await open.DraftsAsync()).Single().PeriodId);

        var closed = new Harness();
        await closed.SeedPeriodAsync(status: 3, DocumentDate.AddDays(-7), DocumentDate.AddDays(20));
        await closed.AdmitAsync(Valid());
        Assert.Null((await closed.DraftsAsync()).Single().PeriodId);
    }

    private const string VatOutputKey = AccountingPostingAccountMappingKeysV1.VatOutput;

    private static AccountingSourceDocumentLineV1 Line(int number, string net, string vat, string withholding, string rate) =>
        new(number, null, null, false, rate, net, vat, withholding);

    private static AccountingSourceDocumentAdmissionRequestV1 Valid(
        IReadOnlyList<AccountingSourceDocumentLineV1>? lines = null,
        string net = "1200",
        string vat = "240",
        string withholding = "0",
        string payable = "1440",
        string? functionalNet = null,
        string? functionalVat = null,
        string? functionalWithholding = null,
        string? functionalPayable = null,
        string? exchangeRate = null,
        string? key = null) =>
        new(Company, "actor-1", "corr-1", null,
            key ?? AccountingSourceDocumentKindsV1.SalesInvoiceKey(Invoice, 1),
            AccountingSourceDocumentKindsV1.SalesInvoice, SourceDocument, Invoice, 1, SourceActor, Party, Currency,
            exchangeRate, DocumentDate, DocumentDate.AddDays(30),
            new(net, vat, withholding, payable),
            new(functionalNet ?? net, functionalVat ?? vat, functionalWithholding ?? withholding, functionalPayable ?? payable),
            lines ?? [Line(1, "1000", "200", "0", "20"), Line(2, "200", "40", "0", "20")]);

    private static AccountingSourceDocumentAdmissionRequestV1 Faulty(string fault) => fault switch
    {
        "totals" => Valid(net: "1300"),
        "payable" => Valid(payable: "1500"),
        "withholding" => Valid(lines: [Line(1, "1000", "200", "300", "20")], net: "1000", vat: "200", withholding: "300", payable: "900"),
        "amount" => Valid(lines: [Line(1, "ten", "200", "0", "20")]),
        "negative" => Valid(lines: [Line(1, "-1000", "200", "0", "20")]),
        "rate-zero" => Valid(exchangeRate: "0"),
        "tl-mismatch" => Valid(functionalNet: "1300", functionalPayable: "1540"),
        "no-lines" => Valid(lines: []),
        "duplicate-line" => Valid(lines: [Line(1, "1000", "200", "0", "20"), Line(1, "200", "40", "0", "20")]),
        "due-before" => Valid() with { DueDate = DocumentDate.AddDays(-1) },
        _ => throw new ArgumentOutOfRangeException(nameof(fault)),
    };

    private sealed class Harness
    {
        private readonly InMemoryDatabaseRoot root;
        private readonly string database;
        private Guid company = Company;

        public Harness()
            : this(new InMemoryDatabaseRoot(), Guid.NewGuid().ToString("N"))
        {
        }

        private Harness(InMemoryDatabaseRoot root, string database)
        {
            this.root = root;
            this.database = database;
        }

        public bool ResolveCompany { get; set; } = true;

        public bool ResolveCorrelation { get; set; } = true;

        public SyntheticMappings Mappings { get; } = new();

        public SyntheticAuthority Authority { get; } = new();

        public CollectingAudit Audit { get; } = new();

        public Harness ForCompany(Guid companyId) => new(root, database) { company = companyId };

        public async Task<AccountingSourceDocumentAdmissionAnswerV1> AdmitAsync(AccountingSourceDocumentAdmissionRequestV1 request)
        {
            var companyContext = new CompanyContext();
            if (ResolveCompany)
            {
                companyContext.Set(company);
            }

            await using var db = NewContext(companyContext);
            var store = new AccountingSourceDocumentAdmissionStoreV1(db, companyContext, Audit, new FixedTimeProvider(Now));
            var service = new AccountingSourceDocumentAdmissionServiceV1(store, Mappings, Authority, companyContext, new FixedCorrelation(ResolveCorrelation));
            return await service.AdmitAsync(request);
        }

        public Task<List<AccountingSourceDocumentAdmissionRow>> AdmissionsAsync() =>
            ReadAsync(db => db.SourceDocumentAdmissions.AsNoTracking().ToListAsync());

        public Task<List<AccountingSourceDocumentVoucherDraftRow>> DraftsAsync() =>
            ReadAsync(db => db.SourceDocumentVoucherDrafts.AsNoTracking().ToListAsync());

        public Task<List<AccountingSourceDocumentVoucherDraftLineRow>> LinesAsync() =>
            ReadAsync(db => db.SourceDocumentVoucherDraftLines.AsNoTracking().ToListAsync());

        public async Task<Guid> SeedPeriodAsync(int status, DateOnly startsOn, DateOnly endsOn)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            await using var db = NewContext(companyContext);
            var id = Guid.NewGuid();
            db.Periods.Add(new PeriodRow
            {
                Id = id,
                CompanyId = company,
                StartsOn = startsOn,
                EndsOn = endsOn,
                Status = status,
                PreparedByUserId = SourceActor,
                PreparedAtUtc = Now,
                Version = 1,
                CreatedAtUtc = Now,
                UpdatedAtUtc = Now,
            });
            await db.SaveChangesAsync();
            return id;
        }

        private async Task<List<T>> ReadAsync<T>(Func<AccountingDbContext, Task<List<T>>> query)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            await using var db = NewContext(companyContext);
            return await query(db);
        }

        private AccountingDbContext NewContext(CompanyContext companyContext) =>
            new(new DbContextOptionsBuilder<AccountingDbContext>()
                .UseInMemoryDatabase(database, root)
                .ConfigureWarnings(warnings => warnings.Ignore(
                    InMemoryEventId.TransactionIgnoredWarning, CoreEventId.ManyServiceProvidersCreatedWarning))
                .Options, companyContext);
    }

    private sealed class SyntheticMappings : IAccountingPostingAccountMappingResolverV1
    {
        private const long Version = 7;

        /// <summary>A mapping key without any row (unmapped).</summary>
        public string? Missing { get; set; }

        /// <summary>A gap answered for every key: unmapped, ambiguous or an unavailable reason.</summary>
        public string? Gap { get; set; }

        public bool SplitVersions { get; set; }

        public Task<AccountingPostingAccountMappingResolutionV1> ResolveAsync(
            Guid companyId,
            string purpose,
            DateOnly businessDate,
            string mappingKey,
            string? itemType = null,
            string? linePurpose = null,
            bool? isExport = null,
            string? vatRateCode = null,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(AccountingPostingAccountMappingPurposesV1.SalesInvoice, purpose);
            if (Gap is not null)
            {
                return Task.FromResult(Gap switch
                {
                    "unmapped" => AccountingPostingAccountMappingResolutionV1.Unmapped(Version),
                    "ambiguous" => AccountingPostingAccountMappingResolutionV1.Ambiguous(Version),
                    _ => AccountingPostingAccountMappingResolutionV1.Unavailable(Gap),
                });
            }

            if (string.Equals(Missing, mappingKey, StringComparison.Ordinal))
            {
                return Task.FromResult(AccountingPostingAccountMappingResolutionV1.Unmapped(Version));
            }

            var account = mappingKey switch
            {
                AccountingPostingAccountMappingKeysV1.Receivable => Receivable,
                AccountingPostingAccountMappingKeysV1.Revenue => Revenue,
                AccountingPostingAccountMappingKeysV1.VatOutput => VatOutput,
                _ => Guid.Empty,
            };
            return Task.FromResult(account == Guid.Empty
                ? AccountingPostingAccountMappingResolutionV1.Unmapped(Version)
                : AccountingPostingAccountMappingResolutionV1.Mapped(
                    account, SplitVersions && mappingKey == VatOutputKey ? Version + 1 : Version));
        }
    }

    private sealed class SyntheticAuthority : IAccountingSourceDocumentAdmissionAuthorityV1
    {
        private int calls;

        public AccountingSourceDocumentAuthorityDecisionV1 Decision { get; set; } = AccountingSourceDocumentAuthorityDecisionV1.Allowed;

        public string PermissionCode { get; set; } = "synthetic.invoices.finalize";

        public int DenyFromCall { get; set; }

        public Task<AccountingSourceDocumentAuthorityAnswerV1> CheckAsync(
            Guid companyId,
            string actorId,
            string documentKind,
            Guid sourceDocumentId,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref calls);
            var decision = DenyFromCall > 0 && call >= DenyFromCall ? AccountingSourceDocumentAuthorityDecisionV1.Denied : Decision;
            return Task.FromResult(new AccountingSourceDocumentAuthorityAnswerV1(decision, PermissionCode, ["SYNTHETIC-B04B"]));
        }
    }

    private sealed class CollectingAudit : IAuditEventCollector
    {
        public List<AuditEventDraft> Events { get; } = [];

        public void Enqueue(AuditEventDraft auditEvent) => Events.Add(auditEvent);
    }

    private sealed class FixedCorrelation(bool resolved = true) : ICorrelationContext
    {
        public bool IsResolved => resolved;
        public string CorrelationId => "synthetic-b04b";
        public string? CausationId => null;
        public string SourceChannel => "UnitTest";
        public string? ClientIpAddress => null;
        public string? UserAgent => null;

        public void Set(string correlationId, string? causationId, string sourceChannel,
            string? clientIpAddress, string? userAgent) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
