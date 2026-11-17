using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceCreditLimitPersistenceTests
{
    [Fact]
    public void Bank_group_limit_has_company_scoped_pending_and_approved_revision_slots_and_forward_fix_guard()
    {
        using var db = Context(); var model = db.GetService<IDesignTimeModel>().Model;
        var row = model.FindEntityType(typeof(FinanceCreditLimitRow))!;
        Assert.Single(row.GetDeclaredQueryFilters()); Assert.True(row.FindProperty("Version")!.IsConcurrencyToken);
        Assert.Contains(row.GetIndexes(), x => x.IsUnique && x.GetFilter() == "[Status] = 1" && x.Properties.Select(p => p.Name).SequenceEqual(["CompanyId", "LenderPartyId", "LenderCompanyPartyAccountId", "CreditGroup", "CurrencyId"]));
        Assert.Contains(row.GetIndexes(), x => x.IsUnique && x.GetFilter() == "[Status] = 2" && x.Properties.Last().Name == "Revision");
        Assert.Contains(row.GetCheckConstraints(), x => x.Name == "CK_CreditLimits_Decision" && x.Sql.Contains("[DecidedBy] <> [ProposedBy]", StringComparison.Ordinal));
        Assert.Contains("THROW 51974", Assert.IsType<SqlOperation>(new AddCreditBankGroupLimits().DownOperations[0]).Sql, StringComparison.Ordinal);
    }
    [Fact]
    public async Task Approved_limit_source_cannot_be_overwritten_or_redecided()
    {
        using var db = Context(); var row = new FinanceCreditLimitRow { CompanyId = Company, Id = Guid.NewGuid(), CreditAccountId = Guid.NewGuid(),
            LenderPartyId = Guid.NewGuid(), LenderCompanyPartyAccountId = Guid.NewGuid(), CurrencyId = Guid.NewGuid(), CreditGroup = 1,
            Version = 2, Revision = 1, Status = 2, AmountText = "100", SourceJson = "{}", ProposedBy = "SENTETIK maker", DecidedBy = "SENTETIK checker", DecidedAtUtc = DateTimeOffset.UtcNow };
        db.Attach(row); row.AmountText = "200"; row.Version++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    private static readonly Guid Company = Guid.Parse("f4740000-0000-4000-8000-000000000001");
    private static FinanceDbContext Context()
    { var company = new CompanyContext(); company.Set(Company); return new(new DbContextOptionsBuilder<FinanceDbContext>().UseSqlServer("Server=unused;Database=unused;Trusted_Connection=True;TrustServerCertificate=True").Options, company); }
}
