using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>WP15 (DALGA_PLANI_v2 1.16): rule-based incoming invoice duplicate check; warns, never blocks.</summary>
public sealed class PurchaseInvoiceDuplicateCheckTests
{
    private static readonly Guid Company = Guid.Parse("15000000-0000-0000-0000-000000000001");
    private static readonly Guid Supplier = Guid.Parse("15000000-0000-0000-0000-000000000002");
    private static readonly Guid OtherSupplier = Guid.Parse("15000000-0000-0000-0000-000000000003");
    private static readonly Guid Try = Guid.Parse("15000000-0000-0000-0000-000000000004");
    private static readonly Guid Usd = Guid.Parse("15000000-0000-0000-0000-000000000005");
    private static readonly Guid Ettn = Guid.Parse("15000000-0000-0000-0000-0000000000e1");
    private static readonly DateOnly Date = new(2026, 11, 20);

    private static PurchaseInvoiceDuplicateProbe Probe(string number = "GIB2026000000123", Guid? ettn = null, DateOnly? date = null,
        Guid? currency = null, decimal payable = 1180.50m, Guid? supplier = null) =>
        new(supplier ?? Supplier, number, ettn ?? Ettn, date ?? Date, currency ?? Try, payable);

    private static PurchaseInvoiceDuplicateCandidate Candidate(int id, string number = "GIB2026000000999", Guid? ettn = null,
        DateOnly? date = null, Guid? currency = null, decimal? payable = 99m, Guid? supplier = null) =>
        new(Guid.Parse($"15000000-0000-0000-0001-{id:D12}"), supplier ?? Supplier, number,
            ettn ?? Guid.Parse($"15000000-0000-0000-0002-{id:D12}"), date ?? Date.AddDays(-30), currency ?? Try, payable);

    [Fact]
    public void An_equal_ETTN_matches_even_for_another_supplier_and_number()
    {
        var match = Assert.Single(PurchaseInvoiceDuplicateRule.Evaluate(Probe(),
            [Candidate(1, number: "X-1", ettn: Ettn, supplier: OtherSupplier)]));
        Assert.Equal(PurchaseInvoiceDuplicateReason.SameEttn, match.Reason);
        Assert.Equal("purchasing.invoice-duplicate.same-ettn", PurchaseInvoiceDuplicateRule.Code(match.Reason));
    }

    [Fact]
    public void An_empty_ETTN_never_matches_an_empty_ETTN()
    {
        Assert.Empty(PurchaseInvoiceDuplicateRule.Evaluate(Probe(ettn: Guid.Empty, supplier: OtherSupplier),
            [Candidate(1, ettn: Guid.Empty)]));
    }

    [Theory]
    [InlineData("gib2026000000123")]
    [InlineData("GIB 2026 000000123")]
    [InlineData("GIB-2026-000000123")]
    [InlineData(" GIB2026000000123\t")]
    public void The_same_supplier_and_a_number_equal_after_case_space_and_dash_normalization_matches(string stored)
    {
        var match = Assert.Single(PurchaseInvoiceDuplicateRule.Evaluate(Probe(), [Candidate(1, number: stored)]));
        Assert.Equal(PurchaseInvoiceDuplicateReason.SameSupplierAndNumber, match.Reason);
    }

    [Fact]
    public void The_same_number_from_another_supplier_is_not_a_duplicate()
    {
        Assert.Empty(PurchaseInvoiceDuplicateRule.Evaluate(Probe(), [Candidate(1, number: "GIB2026000000123", supplier: OtherSupplier)]));
    }

    [Fact]
    public void A_number_that_differs_by_a_leading_zero_is_not_normalized_away()
    {
        Assert.Empty(PurchaseInvoiceDuplicateRule.Evaluate(Probe(number: "GIB2026000000123"), [Candidate(1, number: "GIB20260000000123")]));
    }

    [Fact]
    public void The_same_supplier_date_currency_and_exact_payable_under_another_number_matches()
    {
        var match = Assert.Single(PurchaseInvoiceDuplicateRule.Evaluate(Probe(),
            [Candidate(1, number: "GIB2026000000124", date: Date, payable: 1180.50m)]));
        Assert.Equal(PurchaseInvoiceDuplicateReason.SameSupplierDateAndAmount, match.Reason);
    }

    [Theory]
    [InlineData(1180.49)]
    [InlineData(1180.51)]
    public void A_payable_one_cent_away_is_not_a_match_because_no_tolerance_is_decided(double payable)
    {
        Assert.Empty(PurchaseInvoiceDuplicateRule.Evaluate(Probe(), [Candidate(1, date: Date, payable: (decimal)payable)]));
    }

    [Fact]
    public void The_same_payable_on_the_next_day_or_in_another_currency_is_not_a_match()
    {
        Assert.Empty(PurchaseInvoiceDuplicateRule.Evaluate(Probe(), [
            Candidate(1, date: Date.AddDays(1), payable: 1180.50m),
            Candidate(2, date: Date, currency: Usd, payable: 1180.50m)]));
    }

    [Fact]
    public void A_legacy_candidate_without_amount_facts_only_takes_part_in_the_ETTN_and_number_rules()
    {
        var legacy = new PurchaseInvoiceDuplicateCandidate(Guid.Parse("15000000-0000-0000-0001-000000000001"), Supplier,
            "GIB2026000000124", Guid.Parse("15000000-0000-0000-0002-000000000001"), null, null, null);
        Assert.Empty(PurchaseInvoiceDuplicateRule.Evaluate(Probe(), [legacy]));
        Assert.Single(PurchaseInvoiceDuplicateRule.Evaluate(Probe(number: "GIB2026000000124"), [legacy]));
    }

    [Fact]
    public void The_probed_invoice_itself_is_excluded_and_each_candidate_matches_once_by_its_strongest_reason()
    {
        var self = Candidate(1, number: "GIB2026000000123", ettn: Ettn, date: Date, payable: 1180.50m);
        var both = Candidate(2, number: "gib2026000000123", date: Date, payable: 1180.50m);
        var amount = Candidate(3, number: "OTHER", date: Date, payable: 1180.50m);
        var matches = PurchaseInvoiceDuplicateRule.Evaluate(Probe(), [amount, both, self, both], self.InvoiceId);
        Assert.Equal([(both.InvoiceId, PurchaseInvoiceDuplicateReason.SameSupplierAndNumber),
                (amount.InvoiceId, PurchaseInvoiceDuplicateReason.SameSupplierDateAndAmount)],
            matches.Select(match => (match.InvoiceId, match.Reason)));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("  ", "")]
    [InlineData(" - – ", "")]
    [InlineData("abc 12-3", "ABC123")]
    public void Number_normalization_is_upper_invariant_without_whitespace_or_dashes(string? input, string expected)
    {
        Assert.Equal(expected, PurchaseInvoiceDuplicateRule.NormalizeNumber(input));
    }

    [Fact]
    public void A_probe_with_an_empty_number_never_matches_on_number()
    {
        Assert.Empty(PurchaseInvoiceDuplicateRule.Evaluate(Probe(number: " - ", ettn: Guid.Empty), [Candidate(1, number: "-")]));
    }

    [Fact]
    public async Task The_check_answers_warn_with_the_owner_link_and_never_blocks()
    {
        var world = new World();
        var match = Candidate(7, number: "gib2026000000123");
        world.Candidates.Add(match);
        var answer = await world.Service().CheckAsync(Request());
        Assert.Equal(PurchaseInvoiceDuplicateBehaviorV1.Warn, answer.Behavior);
        Assert.False(answer.Truncated);
        var warning = Assert.Single(answer.Warnings);
        Assert.Equal((match.InvoiceId, "purchasing.invoice-duplicate.same-supplier-number", "gib2026000000123",
                "/satinalma/faturalar/" + match.InvoiceId.ToString("D")),
            (warning.InvoiceId, warning.Code, warning.OfficialNumber, warning.Link));
        Assert.Equal(2, world.Probes);
    }

    [Fact]
    public async Task More_matches_than_the_page_bound_are_reported_as_truncated()
    {
        var world = new World();
        for (var index = 1; index <= PurchaseInvoiceDuplicateCheck.MaximumWarnings + 1; index++)
            world.Candidates.Add(Candidate(index, number: "N" + index, date: Date, payable: 1180.50m));
        var answer = await world.Service().CheckAsync(Request());
        Assert.Equal(PurchaseInvoiceDuplicateCheck.MaximumWarnings, answer.Warnings.Count);
        Assert.True(answer.Truncated);
    }

    [Fact]
    public async Task Unauthenticated_and_foreign_company_callers_are_refused_before_any_read()
    {
        var anonymous = new World { Authenticated = false };
        Assert.Equal(401, (await Assert.ThrowsAsync<PurchasingServiceException>(() => anonymous.Service().CheckAsync(Request()))).Status);
        var foreign = new World(OtherSupplier);
        Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => foreign.Service().CheckAsync(Request()))).Status);
        Assert.Equal(0, anonymous.Reads + foreign.Reads);
    }

    [Theory]
    [InlineData(PurchaseIncomingInvoicePermissionNamesV1.Import)]
    [InlineData(PurchasingPermissionManifestV1.OrdersPriceRead)]
    public async Task Each_required_permission_missing_is_forbidden_and_nothing_is_read(string missing)
    {
        var world = new World { Denied = missing };
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CheckAsync(Request()));
        Assert.Equal((403, "forbidden"), (refusal.Status, refusal.Code));
        Assert.Equal(0, world.Reads);
    }

    [Fact]
    public async Task Authority_lost_during_the_read_is_forbidden_and_no_warning_leaks()
    {
        var world = new World();
        world.Candidates.Add(Candidate(1, number: "GIB2026000000123"));
        world.OnRead = () => world.Denied = PurchaseIncomingInvoicePermissionNamesV1.Import;
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CheckAsync(Request()));
        Assert.Equal(403, refusal.Status);
    }

    [Fact]
    public async Task A_store_failure_is_service_unavailable_not_an_empty_answer()
    {
        var world = new World { Fail = true };
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CheckAsync(Request()));
        Assert.Equal((503, "service_unavailable"), (refusal.Status, refusal.Code));
    }

    [Fact]
    public async Task Invalid_facts_are_a_validation_failure_naming_each_field()
    {
        var world = new World();
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CheckAsync(
            new PurchaseInvoiceDuplicateCheckRequestV1(Guid.Empty, " X", Guid.Empty, default, Guid.Empty, "1.1234567")));
        Assert.Equal((400, "validation_failed"), (refusal.Status, refusal.Code));
        Assert.Equal(["currencyId", "ettn", "invoiceDate", "officialNumber", "payable", "supplierPartyId"],
            refusal.Errors.Select(error => error.Field).Order(StringComparer.Ordinal));
        Assert.Equal(0, world.Reads);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1,5")]
    [InlineData("1e3")]
    public async Task Non_positive_or_non_invariant_payable_text_is_refused(string payable)
    {
        var world = new World();
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CheckAsync(Request() with { Payable = payable }));
        Assert.Equal("payable", Assert.Single(refusal.Errors).Field);
    }

    [Fact]
    public async Task A_recorded_invoice_check_needs_the_read_permission_and_answers_not_found_for_missing_or_empty_ids()
    {
        var world = new World();
        Assert.Equal(404, (await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CheckRecordedAsync(Guid.Empty))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().CheckRecordedAsync(Guid.Parse("15000000-0000-0000-0001-000000000077")))).Status);
        var denied = new World { Denied = PurchaseIncomingInvoicePermissionNamesV1.Read };
        Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            denied.Service().CheckRecordedAsync(Guid.Parse("15000000-0000-0000-0001-000000000077")))).Status);
    }

    [Fact]
    public void The_duplicate_facts_are_nullable_columns_with_an_all_or_nothing_check_a_supplier_date_index_and_a_pure_DDL_migration()
    {
        using var context = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, ResolvedCompany());
        var invoices = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PurchaseIncomingInvoiceRow))!;
        Assert.True(invoices.FindProperty(nameof(PurchaseIncomingInvoiceRow.InvoiceDate))!.IsNullable);
        Assert.True(invoices.FindProperty(nameof(PurchaseIncomingInvoiceRow.CurrencyId))!.IsNullable);
        var payable = invoices.FindProperty(nameof(PurchaseIncomingInvoiceRow.PayableAmount))!;
        Assert.True(payable.IsNullable);
        Assert.Equal((24, 6), (payable.GetPrecision(), payable.GetScale()));
        Assert.Contains(invoices.GetCheckConstraints(), check => check.Name == "CK_IncomingInvoices_DuplicateFacts"
            && check.Sql == PurchasingDbContext.IncomingInvoiceDuplicateFactsCheck);
        var index = Assert.Single(invoices.GetIndexes(), item => item.GetDatabaseName() == "IX_IncomingInvoices_DuplicateDate");
        Assert.Equal(["CompanyId", "SupplierPartyId", "InvoiceDate"], index.Properties.Select(property => property.Name));
        Assert.False(index.IsUnique);
        // The existing hard refusals stay as they were.
        Assert.Contains(invoices.GetIndexes(), item => item.IsUnique && item.Properties.Select(p => p.Name).SequenceEqual(["CompanyId", "Ettn"]));
        Assert.Contains(invoices.GetIndexes(), item => item.IsUnique
            && item.Properties.Select(p => p.Name).SequenceEqual(["CompanyId", "SupplierPartyId", "OfficialNumber"]));

        var migration = new AddPurchaseIncomingInvoiceDuplicateFacts();
        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        Assert.Empty(migration.DownOperations.OfType<SqlOperation>());
        Assert.Equal(["CurrencyId", "InvoiceDate", "PayableAmount"],
            migration.UpOperations.OfType<AddColumnOperation>().Select(column => column.Name).Order(StringComparer.Ordinal));
        Assert.All(migration.UpOperations.OfType<AddColumnOperation>(), column => Assert.True(column.IsNullable));
    }

    private static CompanyContext ResolvedCompany(Guid? companyId = null)
    {
        var company = new CompanyContext();
        company.Set(companyId ?? Company);
        return company;
    }

    private static PurchaseInvoiceDuplicateCheckRequestV1 Request() =>
        new(Supplier, "GIB2026000000123", Ettn, Date, Try, "1180.50");

    private sealed class World(Guid? companyId = null) : ICurrentUser, IErpAuthorityRevalidationService, IPurchasingInvoiceDuplicateCandidateSourceV1,
        IPurchasingIncomingInvoiceStoreV1
    {
        public CompanyContext Company { get; } = ResolvedCompany(companyId);
        public bool Authenticated { get; set; } = true;
        public string? Denied { get; set; }
        public bool Fail { get; set; }
        public Action? OnRead { get; set; }
        public int Reads { get; private set; }
        public int Probes { get; private set; }
        public List<PurchaseInvoiceDuplicateCandidate> Candidates { get; } = [];

        public PurchaseInvoiceDuplicateCheck Service() => new(Company, this, this, this, this);

        public bool IsAuthenticated => Authenticated;
        public string? UserId => Authenticated ? "SENTETIK-actor" : null;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { PurchaseInvoiceDuplicateCheckTests.Company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            if (permission == PurchaseIncomingInvoicePermissionNamesV1.Import || permission == PurchaseIncomingInvoicePermissionNamesV1.Read)
                Probes++;
            return Task.FromResult(new PermissionScope(permission != Denied, true, new HashSet<string> { "SENTETIK-role" },
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }

        public Task<IReadOnlyList<PurchaseInvoiceDuplicateCandidate>> ListDuplicateCandidatesAsync(PurchaseInvoiceDuplicateProbe probe,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            OnRead?.Invoke();
            if (Fail) throw new InvalidOperationException("SENTETIK store failure");
            return Task.FromResult<IReadOnlyList<PurchaseInvoiceDuplicateCandidate>>(Candidates.ToArray());
        }

        public Task<PurchaseIncomingInvoiceSnapshot?> FindIncomingInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult<PurchaseIncomingInvoiceSnapshot?>(null);
        }

        public Task<PurchasingIncomingInvoiceWriteResult?> FindIncomingInvoiceAttemptAsync(string command, PurchasingWriteEnvelope envelope,
            string requestHash, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PurchasingIncomingInvoiceWriteResult> RecordIncomingInvoiceAsync(PurchaseIncomingInvoice invoice,
            PurchasingWriteEnvelope envelope, string requestHash, Func<CancellationToken, Task> requireStillAuthorized,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PurchasingIncomingInvoiceWriteResult?> MutateIncomingInvoiceAsync(Guid invoiceId, string command,
            PurchasingWriteEnvelope envelope, string requestHash, IReadOnlyList<Guid> orderIdsToLock,
            Func<PurchaseIncomingInvoice, CancellationToken, Task> mutate, Func<CancellationToken, Task> requireStillAuthorized,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PurchasingIncomingInvoiceWriteResult> RecordIncomingInvoiceRefusalAsync(string command, PurchasingWriteEnvelope envelope,
            string requestHash, PurchasingTerminalRefusal refusal, Func<CancellationToken, Task> requireStillAuthorized,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
