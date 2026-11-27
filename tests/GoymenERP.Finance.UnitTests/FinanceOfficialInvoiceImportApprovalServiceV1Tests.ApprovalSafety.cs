using System.Data;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class FinanceOfficialInvoiceImportApprovalServiceV1Tests
{
    [Theory]
    [InlineData("missing-writer")] [InlineData("ambient")] [InlineData("dirty")] [InlineData("audit")]
    [InlineData("nonrelational-clean")] [InlineData("nonrelational-audit")]
    public async Task Public_approval_refuses_unowned_entry_and_preserves_caller_work(string boundary)
    {
        var company = CompanyContext();
        var options = new DbContextOptionsBuilder<FinanceDbContext>();
        if (boundary.StartsWith("nonrelational", StringComparison.Ordinal)) options.UseInMemoryDatabase(Guid.NewGuid().ToString());
        else options.UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;");
        await using var db = new FinanceDbContext(options.Options, company);
        var caller = new FinanceOfficialInvoiceImportBatchRow { Id = Batch, CompanyId = Company };
        if (boundary == "dirty") db.OfficialInvoiceImportBatches.Add(caller);
        var writer = new EntryWriter { HasPendingDrafts = boundary is "audit" or "nonrelational-audit" };
        var authority = new EntryAuthority();
        var service = EntryService(db, company, authority, boundary == "missing-writer" ? null : writer);
        using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
        var transaction = Transaction.Current;
        var result = await service.ApproveAsync(Context("checker"), new("approve-key"), new(Batch, "1"), default);
        Assert.Equal(CommandOutcome.Unavailable, result.Outcome); Assert.Null(result.Resource);
        Assert.Equal(1, authority.Calls); Assert.Same(transaction, Transaction.Current);
        Assert.Equal(0, writer.Writes); Assert.Equal(0, writer.Discards);
        Assert.Equal(boundary is "audit" or "nonrelational-audit", writer.HasPendingDrafts);
        Assert.Equal(boundary == "dirty", db.ChangeTracker.HasChanges());
        if (boundary == "dirty") Assert.Same(caller, Assert.Single(db.ChangeTracker.Entries()).Entity);
    }

    [Theory]
    [InlineData("dirty")] [InlineData("audit")]
    public async Task Work_created_during_first_authority_await_is_not_cleaned_by_public_approval(string boundary)
    {
        var company = CompanyContext();
        await using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        var writer = new EntryWriter();
        var caller = new FinanceOfficialInvoiceImportBatchRow { Id = Batch, CompanyId = Company };
        var authority = new EntryAuthority { AfterAwait = () =>
        { if (boundary == "dirty") db.Add(caller); else writer.HasPendingDrafts = true; } };
        var result = await EntryService(db, company, authority, writer)
            .ApproveAsync(Context("checker"), new("approve-key"), new(Batch, "1"), default);
        Assert.Equal(CommandOutcome.Unavailable, result.Outcome); Assert.Equal(1, authority.Calls);
        Assert.Equal(0, writer.Writes); Assert.Equal(0, writer.Discards);
        Assert.Equal(boundary == "dirty", db.ChangeTracker.HasChanges());
        Assert.Equal(boundary == "audit", writer.HasPendingDrafts);
    }

    [Theory]
    [InlineData(PermissionDecision.Denied, CommandOutcome.Denied)]
    [InlineData(PermissionDecision.Unavailable, CommandOutcome.Unavailable)]
    public async Task Public_approval_keeps_native_authority_before_key_validation(PermissionDecision decision, CommandOutcome outcome)
    {
        var company = CompanyContext(); await using var db = Database(company);
        var writer = new EntryWriter { HasPendingDrafts = true };
        var authority = new EntryAuthority { Decision = decision };
        var result = await EntryService(db, company, authority, writer)
            .ApproveAsync(Context("checker"), new(""), new(Batch, "1"), default);
        Assert.Equal(outcome, result.Outcome); Assert.Equal(1, authority.Calls);
        Assert.True(writer.HasPendingDrafts); Assert.Equal(0, writer.Discards); Assert.Equal(0, writer.Writes);
    }

    [Fact]
    public async Task Public_approval_validates_key_before_persistence_without_donating_caller_audit()
    {
        var company = CompanyContext(); await using var db = Database(company);
        var writer = new EntryWriter { HasPendingDrafts = true }; var authority = new EntryAuthority();
        var result = await EntryService(db, company, authority, writer)
            .ApproveAsync(Context("checker"), new(""), new(Batch, "1"), default);
        Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome); Assert.Equal(1, authority.Calls);
        Assert.True(writer.HasPendingDrafts); Assert.Equal(0, writer.Discards); Assert.Equal(0, writer.Writes);
    }

    [Fact]
    public async Task Public_approval_unresolved_company_preserves_denial_without_an_owner_read()
    {
        var company = new CompanyContext(); await using var db = Database(company);
        var authority = new EntryAuthority(); var writer = new EntryWriter();
        var result = await EntryService(db, company, authority, writer)
            .ApproveAsync(Context("checker"), new("approve-key"), new(Batch, "1"), default);
        Assert.Equal(CommandOutcome.Denied, result.Outcome); Assert.Equal(0, authority.Calls);
        Assert.Equal(0, writer.Writes); Assert.Equal(0, writer.Discards);
    }

    [Theory]
    [InlineData("company")] [InlineData("actor")]
    public async Task Public_approval_captures_company_and_actor_across_the_first_authority_await(string drift)
    {
        var company = new MutableCompanyContext(Company); var user = new MutableUser("checker", Company);
        await using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        var writer = new EntryWriter(); var authority = new EntryAuthority { AfterAwait = () =>
        { if (drift == "company") company.Set(ForeignCompany); else user.Set("another-actor"); } };
        var result = await EntryService(db, company, authority, writer, user)
            .ApproveAsync(Context("checker"), new("approve-key"), new(Batch, "1"), default);
        Assert.Equal(CommandOutcome.Denied, result.Outcome); Assert.Equal(1, authority.Calls);
        Assert.Equal(0, writer.Writes); Assert.Equal(0, writer.Discards); Assert.False(db.ChangeTracker.HasChanges());
    }

    private static FinanceOfficialInvoiceImportApprovalServiceV1 EntryService(FinanceDbContext db, ICompanyContext company,
        EntryAuthority authority, IAuditTransactionWriterV1? writer, ICurrentUser? user = null) => new(db, company, user ?? new User("checker", Company),
        new FinanceOfficialInvoiceImportParserV1(), authority, ReadAuthority(), new NullAuditCollector(),
        new(new(FinanceOfficialInvoiceImportPermissionNamesV1.Upload, [], "unit-test"),
            new(FinanceOfficialInvoiceImportPermissionNamesV1.Approve, [], "unit-test")), TimeProvider.System, writer);

    private sealed class EntryAuthority : IFinanceOfficialInvoiceImportApprovalAuthorityV1
    {
        public int Calls; public Action? AfterAwait; public PermissionDecision Decision = PermissionDecision.Allowed;
        public async Task<PermissionDecision> CheckAsync(FinanceContext context,
            FinanceOfficialInvoiceImportAuthorityActionV1 action, Guid id, CancellationToken token)
        { Assert.Equal(FinanceOfficialInvoiceImportAuthorityActionV1.Approve, action); Calls++; await Task.Yield(); AfterAwait?.Invoke(); return Decision; }
    }
    private sealed class EntryWriter : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts { get; set; } public int Writes; public int Discards;
        public Task WritePendingAsync(IDbTransaction tx, CancellationToken token = default)
        { Writes++; throw new InvalidOperationException("Entry must not reach SQL."); }
        public void DiscardPending() { Discards++; HasPendingDrafts = false; }
    }
}
