using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Accounting.UnitTests;

public sealed class InvoiceLineCogsCorrectionTests
{
    [Fact]
    public void A_component_account_change_preserves_the_original_and_posts_two_exact_balanced_corrections()
    {
        var world = new AccountingWorld();
        var original = world.PostOriginal(amount: 100.01m);
        var next = Guid.NewGuid();
        var result = InvoiceLineCogsCorrection.Create(original, 100.01m, world.DebitAccount.AccountId,
            world.CreditAccount.AccountId, next, world.OpenPeriod, world.InPeriod,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(100.01m, original.Lines[0].Debit);
        Assert.Equal(JournalKind.Original, original.Kind);
        Assert.All(new[] { result.Reversing, result.Replacement }, journal =>
        {
            Assert.Equal(JournalKind.Correction, journal.Kind);
            Assert.Equal(original.Id, journal.LinkedJournalId);
            Assert.Equal(original.Source, journal.Source);
            Assert.Equal(original.CurrencyId, journal.CurrencyId);
            Assert.Equal(100.01m, journal.Lines.Sum(x => x.Debit));
            Assert.Equal(100.01m, journal.Lines.Sum(x => x.Credit));
        });
        Assert.Equal(world.DebitAccount.AccountId, result.Reversing.Lines[1].Account.AccountId);
        Assert.Equal(100.01m, result.Reversing.Lines[1].Credit);
        Assert.Equal(next, result.Replacement.Lines[0].Account.AccountId);
        Assert.Equal(100.01m, result.Replacement.Lines[0].Debit);
    }

    [Fact]
    public void A_correction_never_rounds_or_recomputes_the_frozen_component_from_sales_amount_or_quantity()
    {
        var world = new AccountingWorld();
        const decimal frozen = 123456.000123456789m;
        var original = world.PostOriginal(amount: frozen);
        var pair = InvoiceLineCogsCorrection.Create(original, frozen, world.DebitAccount.AccountId,
            world.CreditAccount.AccountId, Guid.NewGuid(), world.OpenPeriod, world.InPeriod,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(frozen, pair.Replacement.Lines[0].Debit);
        Assert.Equal(frozen, pair.Reversing.Lines[0].Debit);
    }

    [Fact]
    public void A_correction_cannot_replace_the_recorded_component_with_an_inferred_amount()
    {
        var world = new AccountingWorld(); var original = world.PostOriginal(amount: 100.01m);
        Assert.Throws<AccountingDomainException>(() => InvoiceLineCogsCorrection.Create(original, 200m,
            world.DebitAccount.AccountId, world.CreditAccount.AccountId, Guid.NewGuid(), world.OpenPeriod, world.InPeriod,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(100.01m, original.Lines[0].Debit);
    }

    [Fact]
    public void A_closed_or_foreign_period_cannot_accept_a_component_correction()
    {
        var world = new AccountingWorld();
        var original = world.PostOriginal();
        var foreign = AccountingWorld.Opened(world.OtherCompany, world.OpenPeriod.StartsOn, world.OpenPeriod.EndsOn);
        Assert.Throws<AccountingDomainException>(() => InvoiceLineCogsCorrection.Create(original, 100,
            world.DebitAccount.AccountId, world.CreditAccount.AccountId, Guid.NewGuid(), foreign, world.InPeriod,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
        world.OpenPeriod.Close(AccountingWorld.Closer, AccountingWorld.ClosedAt);
        Assert.Throws<AccountingDomainException>(() => InvoiceLineCogsCorrection.Create(original, 100,
            world.DebitAccount.AccountId, world.CreditAccount.AccountId, Guid.NewGuid(), world.OpenPeriod, world.InPeriod,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public void Invoice_line_choices_and_approvals_have_company_filters_unique_line_versions_and_append_only_guards()
    {
        var company = new GoymenERP.BuildingBlocks.Multitenancy.CompanyContext(); company.Set(Guid.NewGuid());
        using var db = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>().UseSqlServer("Server=unused.invalid;Database=unused").Options, company);
        var proposals = db.Model.FindEntityType(typeof(InvoiceLineCogsProposalRow))!;
        var revisions = db.Model.FindEntityType(typeof(InvoiceLineCogsRevisionRow))!;
        Assert.Single(proposals.GetDeclaredQueryFilters()); Assert.Single(revisions.GetDeclaredQueryFilters());
        Assert.Equal(new[] { "CompanyId", "InvoiceId", "InvoiceLineId", "Version" }, revisions.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.True(Assert.Single(revisions.GetIndexes(), index => index.Properties.Select(x => x.Name)
            .SequenceEqual(["CompanyId", "ProposalId"])).IsUnique);
        var row = new InvoiceLineCogsRevisionRow { CompanyId = company.CompanyId, InvoiceId = Guid.NewGuid(), InvoiceLineId = Guid.NewGuid(), Version = 1 };
        db.Attach(row); row.ViewJson = "{}"; db.Entry(row).Property(x => x.ViewJson).IsModified = true;
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public void Invoice_line_history_constraints_bind_the_exact_company_invoice_line_and_proposal_and_guard_loss_on_Down()
    {
        var company = new GoymenERP.BuildingBlocks.Multitenancy.CompanyContext(); company.Set(Guid.NewGuid());
        using var db = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>().UseSqlServer("Server=unused.invalid;Database=unused").Options, company);
        var model = db.GetService<IDesignTimeModel>().Model;
        var proposals = model.FindEntityType(typeof(InvoiceLineCogsProposalRow))!;
        var revisions = model.FindEntityType(typeof(InvoiceLineCogsRevisionRow))!;
        Assert.Contains(revisions.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(InvoiceLineCogsProposalRow)
            && key.Properties.Select(x => x.Name).SequenceEqual(["CompanyId", "InvoiceId", "InvoiceLineId", "ProposalId"])
            && key.PrincipalKey.Properties.Select(x => x.Name).SequenceEqual(["CompanyId", "InvoiceId", "InvoiceLineId", "Id"]));
        var proposalChecks = proposals.GetCheckConstraints().ToDictionary(x => x.Name!, x => x.Sql);
        Assert.Equal(AccountingDbContext.InvoiceLineCogsProposalIdentityCheck, proposalChecks["CK_InvoiceLineCogsProposals_Identity"]);
        Assert.Equal(AccountingDbContext.InvoiceLineCogsProposalVersionCheck, proposalChecks["CK_InvoiceLineCogsProposals_Version"]);
        Assert.Equal(AccountingDbContext.InvoiceLineCogsProposalJsonCheck, proposalChecks["CK_InvoiceLineCogsProposals_Json"]);
        Assert.Equal(AccountingDbContext.InvoiceLineCogsProposalUtcCheck, proposalChecks["CK_InvoiceLineCogsProposals_Utc"]);
        var revisionChecks = revisions.GetCheckConstraints().ToDictionary(x => x.Name!, x => x.Sql);
        Assert.Equal(AccountingDbContext.InvoiceLineCogsRevisionIdentityCheck, revisionChecks["CK_InvoiceLineCogsRevisions_Identity"]);
        Assert.Equal(AccountingDbContext.InvoiceLineCogsRevisionVersionCheck, revisionChecks["CK_InvoiceLineCogsRevisions_Version"]);
        Assert.Equal(AccountingDbContext.InvoiceLineCogsRevisionMakerCheckerCheck, revisionChecks["CK_InvoiceLineCogsRevisions_MakerChecker"]);
        Assert.Equal(AccountingDbContext.InvoiceLineCogsRevisionJsonCheck, revisionChecks["CK_InvoiceLineCogsRevisions_Json"]);
        Assert.Equal(AccountingDbContext.InvoiceLineCogsRevisionUtcCheck, revisionChecks["CK_InvoiceLineCogsRevisions_Utc"]);
        var migration = new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AddInvoiceLineCogsCorrections();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Contains("THROW 51987", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
    }
}
