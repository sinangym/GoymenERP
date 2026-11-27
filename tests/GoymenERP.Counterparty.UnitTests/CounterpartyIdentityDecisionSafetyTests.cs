using System.Data;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CounterpartyIdentityDecisionSafetyTests
{
    [Theory]
    [InlineData(true, "missing-writer")]
    [InlineData(false, "missing-writer")]
    [InlineData(true, "nonrelational")]
    [InlineData(false, "nonrelational")]
    [InlineData(true, "dirty")]
    [InlineData(false, "dirty")]
    [InlineData(true, "pending-audit")]
    [InlineData(false, "pending-audit")]
    [InlineData(true, "ambient")]
    [InlineData(false, "ambient")]
    public async Task Native_identity_decision_entry_refusals_preserve_the_callers_state_before_any_authority_or_database_read(bool tax, string boundary)
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        var user = new User(); var audit = new AuditEventCollector(); var writer = new Writer { Pending = boundary == "pending-audit" };
        var options = new DbContextOptionsBuilder<CounterpartyDbContext>();
        if (boundary == "nonrelational") options.UseInMemoryDatabase(Guid.NewGuid().ToString("N"));
        else options.UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;");
        await using var database = new CounterpartyDbContext(options.Options, company);
        Party? caller = null;
        if (boundary == "dirty")
        {
            caller = new Party(Guid.NewGuid(), PartyCode.FromSequence(42), PartyKind.LegalEntity, "SENTETIK caller", null);
            database.Parties.Add(caller);
        }
        using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
        var callerAmbient = Transaction.Current;
        var service = new CounterpartyCommandService(database, user, company, null!, null!, audit, null!, null!,
            NullLogger<CounterpartyCommandService>.Instance, TimeProvider.System,
            identityDecisionAuditWriter: boundary == "missing-writer" ? null : writer);
        var metadata = new CounterpartyCommandMetadata(company.CompanyId, user.UserId, "SENTETIK", null, "Web", null, null, "entry-key");
        var id = Guid.NewGuid(); var request = Guid.NewGuid();
        Task Invoke() => tax ? service.DecideTaxIdentityChangeAsync(id, new("1", request, "approve"), metadata, default)
            : service.DecideLegalNameChangeAsync(id, new("1", request, "reject"), metadata, default);
        await Assert.ThrowsAsync<InvalidOperationException>(Invoke);
        Assert.Equal(0, writer.DiscardCalls); Assert.Equal(0, writer.WriteCalls);
        Assert.Equal(boundary == "pending-audit", writer.HasPendingDrafts);
        Assert.Same(callerAmbient, Transaction.Current);
        if (caller is not null) Assert.Equal(EntityState.Added, database.Entry(caller).State);
        else Assert.Empty(database.ChangeTracker.Entries());
    }
    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true; public string UserId => "SENTETIK-user";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>();
        public bool CanAccessCompany(Guid companyId) => true;
    }
    private sealed class Writer : IAuditTransactionWriterV1
    {
        public bool Pending; public int WriteCalls, DiscardCalls;
        public bool HasPendingDrafts => Pending;
        public Task WritePendingAsync(IDbTransaction transaction, CancellationToken cancellationToken = default)
        { WriteCalls++; throw new InvalidOperationException("Entry refusal cannot flush caller audit."); }
        public void DiscardPending() { DiscardCalls++; Pending = false; }
    }
}
