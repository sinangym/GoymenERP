using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Accounting.UnitTests;

public sealed class InvoiceLineCogsCancellationTests
{
    [Theory]
    [InlineData(33.34)]
    [InlineData(-33.34)]
    public void Cancelling_a_proven_share_reverses_only_its_actual_signed_amount_and_preserves_the_aggregate(decimal signed)
    {
        var world = new AccountingWorld(); var original = world.PostOriginal(amount: 100.01m);
        var proof = Proof(original, world, signed);
        var result = InvoiceLineCogsCancellation.ReverseComponent(original, proof, world.OpenPeriod, world.InPeriod, Guid.NewGuid());
        Assert.Equal(JournalKind.Correction, result.Kind); Assert.Equal(original.Id, result.LinkedJournalId);
        Assert.Equal(33.34m, result.Lines[0].Debit); Assert.Equal(33.34m, result.Lines[1].Credit);
        Assert.Equal(world.CreditAccount.AccountId, result.Lines[0].Account.AccountId);
        Assert.Equal(world.DebitAccount.AccountId, result.Lines[1].Account.AccountId);
        Assert.Equal(100.01m, original.Lines[0].Debit); Assert.Equal(JournalKind.Original, original.Kind);
    }

    [Fact]
    public void A_cancellation_cannot_invent_a_zero_journal_change_the_source_account_or_exceed_the_actual_aggregate()
    {
        var world = new AccountingWorld(); var original = world.PostOriginal(amount: 100.01m);
        var proof = Proof(original, world, 33.34m);
        foreach (var invalid in new[] { proof with { SignedAmount = 0 }, proof with { SignedAmount = 100.02m },
            proof with { JournalTotal = 100m }, proof with { CogsAccountId = Guid.NewGuid() }, proof with { CurrencyId = Guid.NewGuid() },
            proof with { CompanyId = Guid.NewGuid() }, proof with { SourceFingerprint = "missing" } })
            Assert.Throws<AccountingDomainException>(() => InvoiceLineCogsCancellation.ReverseComponent(original, invalid,
                world.OpenPeriod, world.InPeriod, Guid.NewGuid()));
        world.OpenPeriod.Close(AccountingWorld.Closer, AccountingWorld.ClosedAt);
        Assert.Throws<AccountingDomainException>(() => InvoiceLineCogsCancellation.ReverseComponent(original, proof,
            world.OpenPeriod, world.InPeriod, Guid.NewGuid()));
    }

    [Fact]
    public void Cancellation_versions_and_economic_claims_are_company_scoped_and_append_only()
    {
        var company = new GoymenERP.BuildingBlocks.Multitenancy.CompanyContext(); company.Set(Guid.NewGuid());
        using var db = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>().UseSqlServer("Server=unused.invalid;Database=unused").Options, company);
        var model = db.GetService<IDesignTimeModel>().Model;
        var versions = model.FindEntityType(typeof(InventoryCostCancellationVersionRow))!;
        var claims = model.FindEntityType(typeof(InventoryCostCancellationClaimRow))!;
        Assert.Single(versions.GetDeclaredQueryFilters()); Assert.Single(claims.GetDeclaredQueryFilters());
        Assert.Equal(AccountingDbContext.InventoryCostCancellationVersionsStateCheck,
            versions.GetCheckConstraints().Single(x => x.Name == "CK_InventoryCostCancellationVersions_State").Sql);
        Assert.Equal(AccountingDbContext.InventoryCostCancellationClaimsProofCheck,
            claims.GetCheckConstraints().Single(x => x.Name == "CK_InventoryCostCancellationClaims_Proof").Sql);
        Assert.Equal(new[] { "CompanyId", "InvoiceId", "InvoiceLineId", "FinalizedVersion", "EconomicKey" }, claims.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Contains(claims.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(InventoryCostCancellationVersionRow));
        var row = new InventoryCostCancellationVersionRow { CompanyId = company.CompanyId, InventoryCostProposalId = Guid.NewGuid(), Version = 1 };
        db.Attach(row); row.ViewJson = "{}"; db.Entry(row).Property(x => x.ViewJson).IsModified = true;
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        var migration = new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AddInventoryCostComponentCancellations();
        Assert.Equal(2, migration.UpOperations.OfType<CreateTableOperation>().Count());
        Assert.Equal(2, migration.UpOperations.OfType<SqlOperation>().Count());
        Assert.All(migration.UpOperations.OfType<SqlOperation>(), operation => {
            Assert.Contains("THROW 51813", operation.Sql); Assert.Contains("THROW 51814", operation.Sql);
        });
        Assert.Contains("THROW 51812", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }

    private static PostedCogsCancellationComponent Proof(Journal source, AccountingWorld world, decimal signed) => new(
        source.CompanyId, source.Id, source.Source.SourceId, source.Source.PostingPurpose, source.CurrencyId, 100.01m, signed,
        signed > 0 ? world.DebitAccount.AccountId : world.CreditAccount.AccountId,
        signed > 0 ? world.CreditAccount.AccountId : world.DebitAccount.AccountId, new string('a', 64), new string('b', 64));
}
