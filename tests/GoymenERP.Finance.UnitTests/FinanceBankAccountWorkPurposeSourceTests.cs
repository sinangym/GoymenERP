using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;
using FixedAuthority = GoymenERP.Finance.UnitTests.FinancePartyCreditLimitWorkInboxSourceV1Tests.FixedAuthority;
using FixedUser = GoymenERP.Finance.UnitTests.FinancePartyCreditLimitWorkInboxSourceV1Tests.FixedUser;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceBankAccountWorkPurposeSourceTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Purpose_reads_safe_pending_and_terminal_view_with_original_maker_without_card_or_master_read_permission(bool terminal)
    {
        var company=new CompanyContext();company.Set(Guid.NewGuid());var other=Guid.NewGuid();
        await using var db=new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase("SENTETIK-bank-purpose-"+Guid.NewGuid()).Options,company);
        var at=new DateTimeOffset(2026,11,27,0,0,0,TimeSpan.Zero);
        var row=new BankAccountRow{Id=Guid.NewGuid(),CompanyId=company.CompanyId,CurrencyId=Guid.NewGuid(),DisplayName="SENTETIK full account name",Status=terminal?2:1,Version=terminal?4:1,
            ProposedBy="SENTETIK-maker",ProposedAtUtc=at,UpdatedAtUtc=at,BankName="SENTETIK secret bank",AccountNumber="SENTETIK secret account",Iban="SENTETIK secret IBAN",MoneyMovementEligible=true};
        db.BankAccounts.Add(row);await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var authority=new FixedAuthority(true,true);var source=new FinanceBankAccountWorkInboxSourceV1(db,new FinanceWorkInboxFence(company,new FixedUser("SENTETIK-checker",[company.CompanyId,other]),authority));
        var answer=await source.ReadDecisionAsync(new(company.CompanyId,"SENTETIK-checker","SENTETIK",null),row.Id,default);Assert.NotNull(answer);
        Assert.Equal(!terminal,answer.Pending);Assert.Equal(row.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),answer.SourceVersion);
        var proof=Assert.IsType<WorkBankAccountProofV1>(answer.BankAccountProof);Assert.Equal((row.ProposedBy,at),(proof.ProposedBy,proof.ProposedAtUtc));Assert.Equal(terminal,proof.IsActive);Assert.Equal(terminal,proof.IsUsableForReceipt);
        Assert.Equal($"/onaylarim/{FinanceBankAccountWorkInboxSourceV1.KindName}/{row.Id:D}",answer.Item.Link);Assert.All(authority.Asked,p=>Assert.Equal(ErpPermissions.FinanceBankAccountApprove,p.Permission));
        var wire=JsonSerializer.Serialize(answer,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        foreach(var secret in new[]{"bankAccountProof",row.BankName,row.AccountNumber,row.Iban})Assert.DoesNotContain(secret,wire);
        Assert.Null(await source.ReadDecisionAsync(new(other,"SENTETIK-checker","SENTETIK",null),row.Id,default));
    }
    [Fact]
    public void Native_approval_receipt_fact_never_changes_the_existing_public_result_wire()
    {
        var company=Guid.NewGuid();var id=Guid.NewGuid();var result=new BankAccountCommandResult(CommandOutcome.Succeeded,new(id,company,Guid.NewGuid(),"SENTETIK",true,false,"2"),false,"SENTETIK",[]);
        var proven=result with{ApprovalReceipt=new(company,id,"finance.bank-account.approve","SENTETIK-checker",DateTimeOffset.UtcNow)};
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);Assert.Equal(JsonSerializer.Serialize(result,options),JsonSerializer.Serialize(proven,options));
        Assert.DoesNotContain("approvalReceipt",JsonSerializer.Serialize(proven,options));
    }
}
