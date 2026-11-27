using System.Data;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceBankAccountApprovalSafetyTests
{
    [Theory]
    [InlineData("missing-writer")] [InlineData("ambient")] [InlineData("dirty")] [InlineData("audit")]
    [InlineData("nonrelational-clean")] [InlineData("nonrelational-audit")]
    public async Task Approval_entry_refusal_preserves_caller_state_before_any_native_owner_read_or_cleanup(string boundary)
    {
        var company=new CompanyContext();company.Set(Guid.NewGuid());var user=new User(company.CompanyId);var permissions=new Permissions();
        var options=new DbContextOptionsBuilder<FinanceDbContext>();
        if(boundary.StartsWith("nonrelational",StringComparison.Ordinal))options.UseInMemoryDatabase("SENTETIK-bank-entry-"+Guid.NewGuid());
        else options.UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;");
        await using var db=new FinanceDbContext(options.Options,company);
        var writer=new Writer{HasPendingDrafts=boundary is "audit" or "nonrelational-audit"};
        var row=new BankAccountRow{Id=Guid.NewGuid(),CompanyId=company.CompanyId};if(boundary=="dirty")db.BankAccounts.Add(row);
        using var ambient=boundary=="ambient"?new TransactionScope(TransactionScopeAsyncFlowOption.Enabled):null;
        var service=new FinanceBankAccountService(db,company,user,permissions,new NoCandidates(),new AuditEventCollector(),
            new(new("finance.bank-account.propose",[],"Web"),new("finance.bank-account.approve",[],"Web")),TimeProvider.System,
            boundary=="missing-writer"?null:writer);
        var result=await service.ApproveAsync(new(company.CompanyId,"SENTETIK-checker","SENTETIK",null),new("key"),new(row.Id,"1"),default);
        Assert.Equal(CommandOutcome.Unavailable,result.Outcome);Assert.Null(result.Resource);
        Assert.Equal(1,permissions.Calls); // Existing public authority/key/body preflight remains before the owner boundary.
        Assert.Equal(0,writer.Writes);Assert.Equal(0,writer.Discards);Assert.Equal(boundary is "audit" or "nonrelational-audit",writer.HasPendingDrafts);
        Assert.Equal(boundary=="dirty",db.ChangeTracker.HasChanges());if(boundary=="dirty")Assert.Same(row,Assert.Single(db.ChangeTracker.Entries()).Entity);
    }
    private sealed class User(Guid company):ICurrentUser
    {public bool IsAuthenticated=>true;public string UserId=>"SENTETIK-checker";public IReadOnlySet<Guid> AllowedCompanyIds{get;}=new HashSet<Guid>{company};public bool CanAccessCompany(Guid id)=>id==company;}
    private sealed class Permissions:IFinancePermissionPort
    {public int Calls;public Task<PermissionDecision> CheckAsync(FinanceContext c,FinanceAction a,Guid? id,CancellationToken t){Calls++;return Task.FromResult(PermissionDecision.Allowed);}}
    private sealed class NoCandidates:ISettingsMoneyCandidateReader
    {public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken t=default)=>throw new InvalidOperationException("No owner or Settings read is allowed.");}
    private sealed class Writer:IAuditTransactionWriterV1
    {public bool HasPendingDrafts{get;set;}public int Writes,Discards;public Task WritePendingAsync(IDbTransaction tx,CancellationToken t=default){Writes++;throw new InvalidOperationException();}public void DiscardPending(){Discards++;HasPendingDrafts=false;}}
}
